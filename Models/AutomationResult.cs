namespace TrafficTicketAutomation.Models
{
    public class AutomationResult
    {
        public string RA { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime RentalStart { get; set; }
        public DateTime RentalEnd { get; set; }
        public int RentalDays => (RentalEnd - RentalStart).Days;
        public string ContractPdfPath { get; set; } = "";
        public string? Notes { get; set; }
    }

    public class EmailDraft
    {
        public string To { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public List<string> Attachments { get; set; } = new();
    }
}
