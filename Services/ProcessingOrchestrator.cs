using Microsoft.EntityFrameworkCore;
using TrafficTicketAutomation.Data;
using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Models;
using TrafficTicketAutomation.Models.DTOs;

namespace TrafficTicketAutomation.Services
{
    public class ProcessingOrchestrator : IProcessingOrchestrator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IExcelService _excel;
        private readonly IAutomationService _automation;
        private readonly IPdfService _pdf;
        private readonly IEmailService _email;
    private readonly ContractService _contractService;
    private readonly IConfiguration _config;
    private readonly ILogger<ProcessingOrchestrator> _logger;

    public ProcessingOrchestrator(
        IServiceScopeFactory scopeFactory,
        IExcelService excel,
        IAutomationService automation,
        IPdfService pdf,
        IEmailService email,
        ContractService contractService,
        IConfiguration config,
        ILogger<ProcessingOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _excel = excel;
        _automation = automation;
        _pdf = pdf;
        _email = email;
        _contractService = contractService;
        _config = config;
        _logger = logger;
    }

    public async Task<int> StartJobAsync(string excelPath, string billPath)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = new ProcessingJob
        {
            ExcelFileName = Path.GetFileName(excelPath),
            BillFileName = Path.GetFileName(billPath),
            Status = JobStatus.Running
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        _ = Task.Run(() => RunJobAsync(job.Id, excelPath, billPath));
        return job.Id;
    }

    private async Task RunJobAsync(int jobId, string excelPath, string billPath)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.ProcessingJobs.FindAsync(jobId);
            if (job == null) return;

            bool automationEnabled = _automation.IsEnabled;

            if (!automationEnabled)
                _logger.LogWarning("Automation service is disabled or not configured — automation will be skipped.");

            try
            {
                var tickets = _excel.ReadTickets(excelPath);
                var outputDir = Path.Combine(AppContext.BaseDirectory, "Files", "Output");
                Directory.CreateDirectory(outputDir);

                foreach (var ticket in tickets)
                {
                    var result = new TicketResult
                    {
                        JobId = jobId,
                        Plate = ticket.Plate,
                        TicketDate = ticket.Date,
                        Amount = ticket.Amount
                    };

                    // Save early to get the DB-generated Id for PAD request file naming
                    db.TicketResults.Add(result);
                    await db.SaveChangesAsync();

                    try
                    {
                        AutomationResult? automation = null;

                        if (automationEnabled)
                        {
                            // Pass result.Id so PAD request/result files are uniquely named
                            automation = await _automation.ProcessTicketAsync(
                                result.Id, ticket.Plate, ticket.Date, ticket.Amount);
                        }

                        Contract? contractRecord = null;

                        if (automation != null)
                        {
                            result.ContractFound = true;
                            result.RA = automation.RA;
                            result.Customer = automation.Customer;
                            result.CustomerEmail = automation.Email;
                            result.RentalDays = automation.RentalDays;
                            result.ContractPdfPath = automation.ContractPdfPath;

                            contractRecord = new Contract
                            {
                                RA = automation.RA,
                                Plate = ticket.Plate,
                                Start = automation.RentalStart,
                                End = automation.RentalEnd,
                                Customer = automation.Customer,
                                Email = automation.Email,
                                Notes = automation.Notes
                            };

                            db.Contracts.Add(contractRecord);
                        }
                        else
                        {
                            var knownContract = _contractService.FindContract(ticket.Plate, ticket.Date);
                            if (knownContract != null)
                            {
                                result.ContractFound = true;
                                result.RA = knownContract.RA;
                                result.Customer = knownContract.Customer;
                                result.CustomerEmail = knownContract.Email;
                                result.RentalDays = (knownContract.End - knownContract.Start).Days;
                                result.ErrorMessage = automationEnabled
                                    ? "Automation failed, but a matching contract was found in the local database."
                                    : "Automation is disabled; using a matching contract from the local database.";

                                contractRecord = knownContract;
                            }
                            else
                            {
                                result.ContractFound = false;
                                result.ErrorMessage = automationEnabled
                                    ? "Automation returned no result. Check the system logs for details."
                                    : $"Automation is disabled or not configured for mode '{_config["Automation:Mode"] ?? "RentWorks"}'.";
                            }
                        }

                        // Highlight PDF — runs regardless of automation result
                        var highlightedPath = Path.Combine(outputDir,
                            $"highlighted_{result.Id}_{ticket.Plate.Replace(" ", "_")}_{ticket.Date:yyyyMMdd}.pdf");

                        result.FoundInBill = _pdf.HighlightRow(
                            billPath, ticket.Plate, ticket.Date, ticket.Amount, highlightedPath);

                        result.HighlightedBillPath = result.FoundInBill ? highlightedPath : null;

                        // Email draft — if contract information is available
                        if (contractRecord != null)
                        {
                            var draft = _email.GenerateDraft(
                                contractRecord, ticket,
                                result.HighlightedBillPath ?? "",
                                result.ContractPdfPath ?? string.Empty);

                            result.EmailSubject = draft.Subject;
                            result.EmailBody = draft.Body;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed processing ticket for plate {Plate}.", ticket.Plate);
                        result.ErrorMessage = ex.Message;
                    }

                    await db.SaveChangesAsync();
                }

                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("Job {JobId} completed.", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed.", jobId);
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
            }

            await db.SaveChangesAsync();
        }

        public async Task<JobStatusDto?> GetJobStatusAsync(int jobId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.ProcessingJobs.FindAsync(jobId);
            if (job == null) return null;

            var results = await db.TicketResults
                .Where(r => r.JobId == jobId)
                .ToListAsync();

            return new JobStatusDto
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ErrorMessage = job.ErrorMessage,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                Results = results.Select(r => new TicketResultDto
                {
                    Plate = r.Plate,
                    TicketDate = r.TicketDate,
                    Amount = r.Amount,
                    RA = r.RA,
                    Customer = r.Customer,
                    CustomerEmail = r.CustomerEmail,
                    RentalDays = r.RentalDays,
                    FoundInBill = r.FoundInBill,
                    ContractFound = r.ContractFound,
                    HighlightedBillPath = ToRelativeDownloadPath(r.HighlightedBillPath),
                    ContractPdfPath = ToRelativeDownloadPath(r.ContractPdfPath),
                    EmailSubject = r.EmailSubject,
                    EmailBody = r.EmailBody,
                    ErrorMessage = r.ErrorMessage
                }).ToList()
            };
        }

        private static string? ToRelativeDownloadPath(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return null;

            var path = Path.IsPathRooted(fullPath)
                ? Path.GetFullPath(fullPath)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, fullPath));

            var safeBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Files"));
            if (!path.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/');
        }
    }
}
