using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Interfaces
{
    public interface IExcelService
    {
        List<Ticket> ReadTickets(string filePath);
    }

    public interface IPdfService
    {
        bool HighlightRow(string billPath, string plate, DateTime date, decimal amount, string outputPath);
    }

    public interface IAutomationService
    {
        /// <summary>
        /// Writes a request file, launches PAD flow, waits for result file.
        /// Returns null if PAD times out or reports failure.
        /// </summary>
        Task<AutomationResult?> ProcessTicketAsync(int ticketId, string plate, DateTime ticketDate, decimal amount);

        /// <summary>
        /// True when the configured automation provider has the required config and executable available.
        /// </summary>
        bool IsEnabled { get; }
    }

    public interface IEmailService
    {
        EmailDraft GenerateDraft(Contract contract, Ticket ticket, string highlightedBillPath, string contractPdfPath);
    }

    public interface IProcessingOrchestrator
    {
        Task<int> StartJobAsync(string excelPath, string billPath);
        Task<Models.DTOs.JobStatusDto?> GetJobStatusAsync(int jobId);
    }
}
