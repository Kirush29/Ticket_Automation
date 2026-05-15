namespace TrafficTicketAutomation.Models
{
    /// <summary>
    /// Written by ASP.NET to Automation/Requests/ — PAD reads this.
    /// </summary>
    public class AutomationRequest
    {
        public int TicketId { get; set; }
        public string Plate { get; set; } = "";
        public string TicketDate { get; set; } = "";   // yyyy-MM-dd
        public decimal Amount { get; set; }
        public string RequestedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// Written by PAD to Automation/Results/ — ASP.NET reads this.
    /// </summary>
    public class AutomationResponse
    {
        public int TicketId { get; set; }
        public bool Success { get; set; }
        public string? RANumber { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? RentalStart { get; set; }   // yyyy-MM-dd
        public string? RentalEnd { get; set; }     // yyyy-MM-dd
        public string? ContractPdfPath { get; set; }
        public string? Notes { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ScreenshotPath { get; set; }
        public string CompletedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
