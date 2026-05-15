using System.Diagnostics;
using System.Text.Json;
using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Services.Automation
{
    public class PadQueueService : IAutomationService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<PadQueueService> _logger;

        // One job at a time — PAD is a desktop session, not a parallel worker
        private static readonly SemaphoreSlim _lock = new(1, 1);

        private string RequestsDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Requests");
        private string ProcessingDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Processing");
        private string CompletedDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Completed");
        private string FailedDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Failed");
        private string ResultsDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Results");
        private string ScreenshotsDir => Path.Combine(_config["PAD:AutomationFolder"]!, "Screenshots");

        public PadQueueService(IConfiguration config, ILogger<PadQueueService> logger)
        {
            _config = config;
            _logger = logger;
            EnsureFolders();
        }

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_config["PAD:PadExePath"]) && File.Exists(_config["PAD:PadExePath"]);

        public async Task<AutomationResult?> ProcessTicketAsync(int ticketId, string plate, DateTime ticketDate, decimal amount)
        {
            // Only one RentWorks session at a time
            await _lock.WaitAsync();
            try
            {
                int retryCount = int.TryParse(_config["PAD:RetryCount"], out var r) ? r : 2;

                for (int attempt = 1; attempt <= retryCount; attempt++)
                {
                    _logger.LogInformation("PAD attempt {Attempt}/{Max} for ticket {Id} plate {Plate}.",
                        attempt, retryCount, ticketId, plate);

                    var result = await RunSingleAttemptAsync(ticketId, plate, ticketDate, amount, attempt);

                    if (result != null) return result;

                    if (attempt < retryCount)
                    {
                        _logger.LogWarning("Attempt {Attempt} failed. Retrying in 5s...", attempt);
                        await Task.Delay(5000);
                    }
                }

                _logger.LogError("All PAD attempts exhausted for ticket {Id}.", ticketId);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<AutomationResult?> RunSingleAttemptAsync(
            int ticketId, string plate, DateTime ticketDate, decimal amount, int attempt)
        {
            var fileName = $"ticket_{ticketId}_attempt{attempt}";
            var requestPath = Path.Combine(RequestsDir, $"{fileName}.json");
            var resultPath = Path.Combine(ResultsDir, $"{fileName}.json");
            var failedPath = Path.Combine(FailedDir, $"{fileName}.json");

            // Clean up any leftover files from previous attempts
            CleanFile(resultPath);
            CleanFile(failedPath);

            // --- WRITE REQUEST FILE ---
            var request = new AutomationRequest
            {
                TicketId = ticketId,
                Plate = plate,
                TicketDate = ticketDate.ToString("yyyy-MM-dd"),
                Amount = amount
            };

            await File.WriteAllTextAsync(requestPath,
                JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));

            _logger.LogInformation("Request file written: {Path}", requestPath);

            // --- LAUNCH PAD FLOW ---
            bool launched = LaunchPadFlow(requestPath);
            if (!launched)
            {
                _logger.LogError("Failed to launch PAD flow.");
                return null;
            }

            // --- POLL FOR RESULT ---
            int timeoutSeconds = int.TryParse(_config["PAD:ResultTimeoutSeconds"], out var t) ? t : 120;
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);

            while (DateTime.Now < deadline)
            {
                await Task.Delay(2000);

                // PAD writes to Results/ on success
                if (File.Exists(resultPath))
                {
                    var json = await File.ReadAllTextAsync(resultPath);
                    var response = JsonSerializer.Deserialize<AutomationResponse>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (response == null) return null;

                    // Archive the request
                    MoveFile(requestPath, Path.Combine(CompletedDir, Path.GetFileName(requestPath)));

                    if (!response.Success)
                    {
                        _logger.LogWarning("PAD reported failure for ticket {Id}: {Msg}",
                            ticketId, response.ErrorMessage);
                        MoveFile(resultPath, Path.Combine(FailedDir, Path.GetFileName(resultPath)));
                        return null;
                    }

                    _logger.LogInformation("PAD completed ticket {Id} — RA={RA}", ticketId, response.RANumber);

                    return new AutomationResult
                    {
                        RA = response.RANumber ?? "",
                        Customer = response.CustomerName ?? "",
                        Email = response.CustomerEmail ?? "",
                        RentalStart = DateTime.TryParse(response.RentalStart, out var s) ? s : ticketDate,
                        RentalEnd = DateTime.TryParse(response.RentalEnd, out var e) ? e : ticketDate,
                        ContractPdfPath = response.ContractPdfPath ?? "",
                        Notes = response.Notes
                    };
                }

                // PAD writes to Failed/ on error
                if (File.Exists(failedPath))
                {
                    var json = await File.ReadAllTextAsync(failedPath);
                    var response = JsonSerializer.Deserialize<AutomationResponse>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    _logger.LogWarning("PAD failed ticket {Id}: {Msg}", ticketId, response?.ErrorMessage);
                    MoveFile(requestPath, Path.Combine(FailedDir, Path.GetFileName(requestPath)));
                    return null;
                }
            }

            // Timeout — move request to failed, take screenshot for debugging
            _logger.LogError("PAD timed out after {Sec}s for ticket {Id}.", timeoutSeconds, ticketId);
            MoveFile(requestPath, Path.Combine(FailedDir, $"TIMEOUT_{Path.GetFileName(requestPath)}"));
            TakeTimeoutScreenshot(ticketId, attempt);
            return null;
        }

        private bool LaunchPadFlow(string requestPath)
        {
            try
            {
                var padExe = _config["PAD:PadExePath"];
                var flowName = _config["PAD:FlowName"];

                // PAD Console Host launches a named flow and exits when done
                // The request file path is passed as an environment variable
                // so PAD can read it at the start of the flow
                var psi = new ProcessStartInfo
                {
                    FileName = padExe,
                    Arguments = $"/flow \"{flowName}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                // Pass request file path via environment variable — PAD reads this
                psi.EnvironmentVariables["PAD_REQUEST_FILE"] = requestPath;
                psi.EnvironmentVariables["PAD_RESULTS_DIR"] = ResultsDir;
                psi.EnvironmentVariables["PAD_FAILED_DIR"] = FailedDir;
                psi.EnvironmentVariables["PAD_SCREENSHOTS_DIR"] = ScreenshotsDir;

                Process.Start(psi);
                _logger.LogInformation("PAD flow '{Flow}' launched.", flowName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch PAD.");
                return false;
            }
        }

        private void TakeTimeoutScreenshot(int ticketId, int attempt)
        {
            try
            {
                // Uses PowerShell to capture screen — no extra dependency needed
                var screenshotPath = Path.Combine(ScreenshotsDir,
                    $"timeout_ticket{ticketId}_attempt{attempt}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                                $"$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; " +
                                $"$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height); " +
                                $"$g = [System.Drawing.Graphics]::FromImage($bmp); " +
                                $"$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size); " +
                                $"$bmp.Save('{screenshotPath}')\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                _logger.LogInformation("Timeout screenshot saved: {Path}", screenshotPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not take timeout screenshot.");
            }
        }

        private void EnsureFolders()
        {
            foreach (var dir in new[] { RequestsDir, ProcessingDir, CompletedDir, FailedDir, ResultsDir, ScreenshotsDir })
                Directory.CreateDirectory(dir);
        }

        private static void CleanFile(string path) { if (File.Exists(path)) File.Delete(path); }

        private static void MoveFile(string src, string dst)
        {
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }
    }
}
