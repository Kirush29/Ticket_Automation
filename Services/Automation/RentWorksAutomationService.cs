using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Services.Automation
{
    public class RentWorksAutomationService : IAutomationService, IDisposable
    {
        private readonly IConfiguration _config;
        private readonly ILogger<RentWorksAutomationService> _logger;

        // One RentWorks session at a time
        private static readonly SemaphoreSlim _lock = new(1, 1);

        private UIA3Automation? _automation;
        private Application? _app;
        private bool _disposed;

        private string ExePath => _config["RentWorks:ExePath"]!;
        private string ExeArgs => _config["RentWorks:Args"]!;
        private string Domain => _config["RentWorks:Domain"]!;
        private string Username => _config["RentWorks:Username"]!;
        private string Password => _config["RentWorks:Password"]!;
        private string Branch => _config["RentWorks:Branch"] ?? "";
        private int LoginTimeout => int.TryParse(_config["RentWorks:LoginTimeoutSeconds"], out var v) ? v : 30;
        private int SearchTimeout => int.TryParse(_config["RentWorks:SearchTimeoutSeconds"], out var v) ? v : 60;
        private int ActionTimeout => int.TryParse(_config["RentWorks:ActionTimeoutSeconds"], out var v) ? v : 15;
        private int RetryCount => int.TryParse(_config["PAD:RetryCount"], out var v) ? v : 2;

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_config["RentWorks:ExePath"]) && File.Exists(_config["RentWorks:ExePath"]);

        public RentWorksAutomationService(IConfiguration config, ILogger<RentWorksAutomationService> logger)
        {
            _config = config;
            _logger = logger;
        }

        // ── Public entry point ────────────────────────────────────────────────

        public async Task<AutomationResult?> ProcessTicketAsync(
            int ticketId, string plate, DateTime ticketDate, decimal amount)
        {
            await _lock.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= RetryCount; attempt++)
                {
                    _logger.LogInformation(
                        "RentWorks attempt {A}/{M} — ticket {Id} plate {Plate}",
                        attempt, RetryCount, ticketId, plate);

                    var result = await Task.Run(() =>
                        TryProcessTicket(ticketId, plate, ticketDate, attempt));

                    if (result != null) return result;

                    if (attempt < RetryCount)
                    {
                        _logger.LogWarning("Attempt {A} failed, retrying in 5s…", attempt);
                        await Task.Delay(5000);
                        ResetApp(); // kill & relaunch on retry
                    }
                }

                _logger.LogError("All attempts exhausted for ticket {Id}.", ticketId);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        // ── Core automation flow ──────────────────────────────────────────────

        private AutomationResult? TryProcessTicket(
            int ticketId, string plate, DateTime ticketDate, int attempt)
        {
            try
            {
                EnsureAppRunning();

                var mainWin = GetMainWindow();
                if (mainWin == null)
                {
                    _logger.LogError("Could not find RentWorks main window.");
                    return null;
                }

                // ── Search by plate ──────────────────────────────────────────
                if (!SearchByPlate(mainWin, plate, ticketDate))
                {
                    _logger.LogWarning("Search failed for plate {Plate}.", plate);
                    return null;
                }

                // ── Pick the row whose rental period overlaps the ticket date ─
                var row = FindOverlappingRow(mainWin, ticketDate);
                if (row == null)
                {
                    _logger.LogWarning("No overlapping rental found for plate {Plate} on {Date}.",
                        plate, ticketDate.ToShortDateString());
                    return null;
                }

                // ── Open the rental record ───────────────────────────────────
                row.DoubleClick();
                Thread.Sleep(1500);

                // Dismiss "Note - RA/Res" popup if it appears
                DismissNotePopup();

                // ── Read RA# from the open record ────────────────────────────
                var rentalWin = WaitForWindow("Zoom Car Rental", ActionTimeout);
                if (rentalWin == null)
                {
                    _logger.LogError("Rental record window did not open.");
                    return null;
                }

                string ra = ReadRaNumber(rentalWin);
                if (string.IsNullOrWhiteSpace(ra))
                {
                    _logger.LogError("Could not read RA# from rental record.");
                    return null;
                }

                // ── Read rental dates from the record ────────────────────────
                (DateTime rentalStart, DateTime rentalEnd) = ReadRentalDates(rentalWin);

                // ── Get customer email via Renter Phone Lookup ───────────────
                string email = GetCustomerEmail(rentalWin, ra);

                // ── Read customer name ───────────────────────────────────────
                string customerName = ReadCustomerName(rentalWin);

                // ── Print / save contract (Finish button) ────────────────────
                string contractPath = PrintContract(rentalWin, ticketId);

                // ── Close the rental record ──────────────────────────────────
                CloseRentalRecord(rentalWin);

                _logger.LogInformation(
                    "Ticket {Id} — RA={RA} Customer={Name} Email={Email}",
                    ticketId, ra, customerName, email);

                return new AutomationResult
                {
                    RA = ra,
                    Customer = customerName,
                    Email = email,
                    RentalStart = rentalStart,
                    RentalEnd = rentalEnd,
                    ContractPdfPath = contractPath
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in RentWorks automation attempt {A}.", attempt);
                return null;
            }
        }

        // ── App lifecycle ─────────────────────────────────────────────────────

        private void EnsureAppRunning()
        {
            // If prowcini is already running, attach to it
            var existing = Process.GetProcessesByName("prowc").FirstOrDefault();
            if (existing != null && _app == null)
            {
                _automation ??= new UIA3Automation();
                _app = Application.Attach(existing);
                _logger.LogInformation("Attached to existing RentWorks process.");
                return;
            }

            if (_app != null)
            {
                try
                {
                    if (!_app.HasExited) return; // already running
                }
                catch { /* process gone */ }
            }

            // Launch fresh
            _automation ??= new UIA3Automation();
            _logger.LogInformation("Launching RentWorks…");

            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = ExeArgs,
                UseShellExecute = true
            };

            _app = Application.Launch(psi);

            // Wait for login window
            if (!WaitForLoginWindow(LoginTimeout))
            {
                _logger.LogError("RentWorks login window did not appear.");
                return;
            }

            Login();
        }

        private void ResetApp()
        {
            try { _app?.Kill(); } catch { }
            _app = null;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        private bool WaitForLoginWindow(int timeoutSec)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                var win = TryGetWindow("User Login");
                if (win != null) return true;
                Thread.Sleep(500);
            }
            return false;
        }

        private void Login()
        {
            var loginWin = WaitForWindow("User Login", LoginTimeout);
            if (loginWin == null)
            {
                _logger.LogError("Login window not found.");
                return;
            }

            // The login window has: Domain field, Username field, Password field, OK button
            // Based on PAD recording: first Edit = password field (index 0),
            // second Edit = search/domain field (index 1)
            // We use class-based search to be safe.

            var edits = loginWin.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Edit));

            if (edits.Length >= 1)
            {
                // Domain\Username combined or separate — fill first edit with Domain\Username
                edits[0].AsTextBox().Enter($"{Domain}\\{Username}");
                Thread.Sleep(300);
            }

            if (edits.Length >= 2)
            {
                edits[1].AsTextBox().Enter(Password);
                Thread.Sleep(300);
            }

            // Click OK
            var okBtn = loginWin.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.Button)
                        .And(cf.ByName("OK")));
            okBtn?.AsButton().Invoke();
            Thread.Sleep(2000);

            // Select branch from TreeView if configured
            if (!string.IsNullOrWhiteSpace(Branch))
                SelectBranch(loginWin);

            _logger.LogInformation("RentWorks login submitted.");
        }

        private void SelectBranch(AutomationElement loginWin)
        {
            try
            {
                var tree = loginWin.FindFirstDescendant(
                    cf => cf.ByClassName("TreeView20WndClass"));
                if (tree == null) return;

                // Find the branch item by name and double-click it
                var item = tree.FindFirstDescendant(
                    cf => cf.ByName(Branch));
                item?.DoubleClick();
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Branch selection failed — continuing.");
            }
        }

        // ── Search ────────────────────────────────────────────────────────────

        private bool SearchByPlate(AutomationElement mainWin, string plate, DateTime ticketDate)
        {
            try
            {
                // Find the search Edit field (second Edit in the main window area)
                var edits = mainWin.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Edit));

                if (edits.Length < 2)
                {
                    _logger.LogError("Search fields not found.");
                    return false;
                }

                // Set date range: ticket date ± 90 days to catch all possible rentals
                string fromDate = ticketDate.AddDays(-90).ToString("MM/dd/yyyy");
                string toDate = ticketDate.AddDays(90).ToString("MM/dd/yyyy");

                // From date field
                var dateField = edits[1].AsTextBox();
                dateField.Click();
                Thread.Sleep(200);
                dateField.Enter(fromDate);
                Thread.Sleep(200);

                // Click Search to apply from-date
                ClickSearchButton(mainWin);
                Thread.Sleep(500);

                // To date field (same field, second click sets end date)
                dateField.Click();
                Thread.Sleep(200);
                dateField.Enter(toDate);
                Thread.Sleep(200);

                // Enter plate in the plate search field
                // The plate field appears after date is set
                var plateField = FindPlateField(mainWin);
                if (plateField != null)
                {
                    plateField.Click();
                    Thread.Sleep(200);
                    plateField.AsTextBox().Enter(plate);
                    Thread.Sleep(200);
                }

                // Final search
                ClickSearchButton(mainWin);
                Thread.Sleep(2000);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchByPlate failed.");
                return false;
            }
        }

        private AutomationElement? FindPlateField(AutomationElement mainWin)
        {
            var edits = mainWin.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Edit));
            // Plate field is typically the last/second edit in the search area
            return edits.Length >= 2 ? edits[1] : null;
        }

        private void ClickSearchButton(AutomationElement win)
        {
            var btn = win.FindFirstDescendant(
                cf => cf.ByControlType(ControlType.Button)
                        .And(cf.ByName("Search")));
            btn?.AsButton().Invoke();
        }

        // ── Result grid ───────────────────────────────────────────────────────

        private AutomationElement? FindOverlappingRow(
            AutomationElement mainWin, DateTime ticketDate)
        {
            try
            {
                // XTPReport grid — find all rows
                var grid = mainWin.FindFirstDescendant(
                    cf => cf.ByClassName("XTPReport"));
                if (grid == null)
                {
                    _logger.LogWarning("XTPReport grid not found.");
                    return null;
                }

                var rows = grid.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.DataItem));

                foreach (var row in rows)
                {
                    // Each row's text contains rental start/end dates
                    // Try to parse dates from the row name/value
                    string rowText = row.Name ?? "";
                    if (TryParseRentalDatesFromRow(rowText, out DateTime start, out DateTime end))
                    {
                        if (ticketDate >= start && ticketDate <= end)
                        {
                            _logger.LogInformation(
                                "Found overlapping row: {Start} – {End} for ticket date {Date}",
                                start.ToShortDateString(), end.ToShortDateString(),
                                ticketDate.ToShortDateString());
                            return row;
                        }
                    }
                }

                // Fallback: if only one row, return it
                if (rows.Length == 1)
                {
                    _logger.LogInformation("Single result row — using it.");
                    return rows[0];
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FindOverlappingRow failed.");
                return null;
            }
        }

        private bool TryParseRentalDatesFromRow(
            string rowText, out DateTime start, out DateTime end)
        {
            start = DateTime.MinValue;
            end = DateTime.MinValue;

            // Row text typically contains dates in MM/dd/yyyy format
            // Try to find two dates in the text
            var parts = rowText.Split(new[] { ' ', '\t', '|', '-' },
                StringSplitOptions.RemoveEmptyEntries);

            var dates = parts
                .Where(p => DateTime.TryParse(p, out _))
                .Select(p => DateTime.Parse(p))
                .OrderBy(d => d)
                .ToList();

            if (dates.Count >= 2)
            {
                start = dates[0];
                end = dates[^1];
                return true;
            }

            return false;
        }

        // ── Rental record ─────────────────────────────────────────────────────

        private void DismissNotePopup()
        {
            try
            {
                var noteWin = TryGetWindow("Note - RA/Res");
                if (noteWin == null) return;

                var exitBtn = noteWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Exit")));
                exitBtn?.AsButton().Invoke();
                Thread.Sleep(500);
                _logger.LogInformation("Dismissed Note - RA/Res popup.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DismissNotePopup — no popup or already dismissed.");
            }
        }

        private string ReadRaNumber(AutomationElement rentalWin)
        {
            try
            {
                // RA# field — find Edit or Text with "RA" label nearby
                // Based on PAD: the RA field is a text/edit control in the rental record
                var allEdits = rentalWin.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Edit));

                // RA# is typically the first prominent field in the record
                // Try to find by automation ID or by reading all edits
                foreach (var edit in allEdits)
                {
                    string val = edit.AsTextBox().Text ?? "";
                    // RA numbers are typically 6-digit numeric strings
                    if (val.Length >= 4 && val.All(char.IsDigit))
                        return val.Trim();
                }

                // Fallback: read from window title which often contains RA#
                string title = rentalWin.Name ?? "";
                var match = System.Text.RegularExpressions.Regex
                    .Match(title, @"\d{4,}");
                if (match.Success) return match.Value;

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReadRaNumber failed.");
                return "";
            }
        }

        private (DateTime start, DateTime end) ReadRentalDates(AutomationElement rentalWin)
        {
            try
            {
                var edits = rentalWin.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Edit));

                var dates = edits
                    .Select(e => e.AsTextBox().Text ?? "")
                    .Where(t => DateTime.TryParse(t, out _))
                    .Select(t => DateTime.Parse(t))
                    .OrderBy(d => d)
                    .ToList();

                if (dates.Count >= 2)
                    return (dates[0], dates[^1]);

                return (DateTime.Today, DateTime.Today);
            }
            catch
            {
                return (DateTime.Today, DateTime.Today);
            }
        }

        private string ReadCustomerName(AutomationElement rentalWin)
        {
            try
            {
                // Customer name is typically a text/edit field near the top of the record
                var edits = rentalWin.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Edit));

                foreach (var edit in edits)
                {
                    string val = edit.AsTextBox().Text ?? "";
                    // Name fields contain letters and spaces, not just digits
                    if (val.Length > 3 && val.Any(char.IsLetter) && val.Contains(' '))
                        return val.Trim();
                }

                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        // ── Email lookup ──────────────────────────────────────────────────────

        private string GetCustomerEmail(AutomationElement rentalWin, string ra)
        {
            try
            {
                // Enter RA# in the RA field
                var raField = FindRaInputField(rentalWin);
                if (raField != null)
                {
                    raField.Click();
                    Thread.Sleep(200);
                    raField.AsTextBox().Enter(ra);
                    Thread.Sleep(300);
                }

                // Click "Renter Phone Lookup" button
                var lookupBtn = rentalWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Renter Phone Lookup")));
                lookupBtn?.AsButton().Invoke();
                Thread.Sleep(1500);

                // Handle crash — "Renter Lookup (Not Responding)"
                if (HandleRenterLookupCrash())
                {
                    _logger.LogWarning("Renter Lookup crashed — email not retrieved.");
                    return "";
                }

                // Renter Lookup window opens
                var lookupWin = WaitForWindow("Renter Lookup", ActionTimeout);
                if (lookupWin == null)
                {
                    _logger.LogWarning("Renter Lookup window did not open.");
                    return "";
                }

                // Set dropdown to "Renter's Email"
                var combo = lookupWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.ComboBox));
                if (combo != null)
                {
                    var comboBox = combo.AsComboBox();
                    comboBox.Select("Renter's Email");
                    Thread.Sleep(300);
                }

                // Click Search
                var searchBtn = lookupWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Search")));
                searchBtn?.AsButton().Invoke();
                Thread.Sleep(1500);

                // Read email from results
                string email = ReadEmailFromLookup(lookupWin);

                // Close lookup window
                var closeBtn = lookupWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Close")));
                closeBtn?.AsButton().Invoke();
                Thread.Sleep(500);

                return email;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCustomerEmail failed.");
                return "";
            }
        }

        private AutomationElement? FindRaInputField(AutomationElement rentalWin)
        {
            // RA input field — typically a specific Edit control
            var edits = rentalWin.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Edit));

            // The RA field in the PAD script is filled with a 6-digit number
            // It's usually one of the first edits in the form
            return edits.Length > 0 ? edits[0] : null;
        }

        private bool HandleRenterLookupCrash()
        {
            for (int i = 0; i < 3; i++)
            {
                Thread.Sleep(500);
                var crashWin = TryGetWindow("Renter Lookup (Not Responding)");
                if (crashWin == null) continue;

                _logger.LogWarning("Renter Lookup crashed — closing crash dialog.");

                var closeProgBtn = crashWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Close the program")));
                if (closeProgBtn != null)
                {
                    closeProgBtn.AsButton().Invoke();
                    Thread.Sleep(1000);
                    return true;
                }

                var closeBtn = crashWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Close")));
                closeBtn?.AsButton().Invoke();
                Thread.Sleep(1000);
                return true;
            }
            return false;
        }

        private string ReadEmailFromLookup(AutomationElement lookupWin)
        {
            try
            {
                // Email result is shown in a grid or text field
                var grid = lookupWin.FindFirstDescendant(
                    cf => cf.ByClassName("XTPReport"));

                if (grid != null)
                {
                    var rows = grid.FindAllDescendants(
                        cf => cf.ByControlType(ControlType.DataItem));

                    foreach (var row in rows)
                    {
                        string text = row.Name ?? "";
                        // Basic email validation
                        if (text.Contains('@') && text.Contains('.'))
                            return text.Trim();
                    }
                }

                // Fallback: search all text elements
                var allText = lookupWin.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Edit));

                foreach (var t in allText)
                {
                    string val = t.AsTextBox().Text ?? "";
                    if (val.Contains('@') && val.Contains('.'))
                        return val.Trim();
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        // ── Contract ──────────────────────────────────────────────────────────

        private string PrintContract(AutomationElement rentalWin, int ticketId)
        {
            try
            {
                // Click "Finish" button
                var finishBtn = rentalWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Finish")));

                if (finishBtn == null)
                {
                    _logger.LogWarning("Finish button not found — skipping contract print.");
                    return "";
                }

                finishBtn.AsButton().Invoke();
                Thread.Sleep(2000);

                // Handle print dialog — save to Files/Contracts/
                var contractDir = Path.Combine(
                    AppContext.BaseDirectory, "Files", "Contracts");
                Directory.CreateDirectory(contractDir);

                string contractPath = Path.Combine(
                    contractDir, $"{Guid.NewGuid()}_contract_{ticketId}.pdf");

                // Handle Windows print/save dialog
                var printWin = WaitForWindow("Print", ActionTimeout);
                if (printWin != null)
                {
                    // If a save dialog appears, enter the path
                    var fileNameField = printWin.FindFirstDescendant(
                        cf => cf.ByControlType(ControlType.Edit));
                    if (fileNameField != null)
                    {
                        fileNameField.AsTextBox().Enter(contractPath);
                        Thread.Sleep(300);

                        var saveBtn = printWin.FindFirstDescendant(
                            cf => cf.ByControlType(ControlType.Button)
                                    .And(cf.ByName("Save")));
                        saveBtn?.AsButton().Invoke();
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        // Just click OK/Print
                        var okBtn = printWin.FindFirstDescendant(
                            cf => cf.ByControlType(ControlType.Button)
                                    .And(cf.ByName("OK")));
                        okBtn?.AsButton().Invoke();
                        Thread.Sleep(1000);
                    }
                }

                return File.Exists(contractPath) ? contractPath : "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PrintContract failed.");
                return "";
            }
        }

        // ── Close rental record ───────────────────────────────────────────────

        private void CloseRentalRecord(AutomationElement rentalWin)
        {
            try
            {
                // Look for a Close or Exit button
                var closeBtn = rentalWin.FindFirstDescendant(
                    cf => cf.ByControlType(ControlType.Button)
                            .And(cf.ByName("Close")));
                closeBtn?.AsButton().Invoke();
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CloseRentalRecord — could not close cleanly.");
            }
        }

        // ── Window helpers ────────────────────────────────────────────────────

        private AutomationElement? GetMainWindow()
        {
            if (_app == null || _automation == null) return null;

            try
            {
                // Main window title contains "Zoom Car Rental"
                var deadline = DateTime.Now.AddSeconds(SearchTimeout);
                while (DateTime.Now < deadline)
                {
                    var wins = _automation.GetDesktop()
                        .FindAllChildren(cf => cf.ByControlType(ControlType.Window));

                    var main = wins.FirstOrDefault(w =>
                        (w.Name ?? "").Contains("Zoom Car Rental") ||
                        (w.Name ?? "").Contains("RentWorks") ||
                        (w.Name ?? "").Contains("User Login"));

                    if (main != null) return main;
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMainWindow failed.");
            }

            return null;
        }

        private AutomationElement? WaitForWindow(string titleContains, int timeoutSec)
        {
            if (_automation == null) return null;

            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                var win = TryGetWindow(titleContains);
                if (win != null) return win;
                Thread.Sleep(500);
            }

            _logger.LogWarning("Window '{Title}' did not appear within {Sec}s.",
                titleContains, timeoutSec);
            return null;
        }

        private AutomationElement? TryGetWindow(string titleContains)
        {
            if (_automation == null) return null;

            try
            {
                var wins = _automation.GetDesktop()
                    .FindAllChildren(cf => cf.ByControlType(ControlType.Window));

                return wins.FirstOrDefault(w =>
                    (w.Name ?? "").Contains(titleContains,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _automation?.Dispose();
        }
    }
}
