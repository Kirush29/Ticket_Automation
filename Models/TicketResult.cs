namespace TrafficTicketAutomation.Models
{
    public class TicketResult
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public ProcessingJob Job { get; set; } = null!;

        public string Plate { get; set; } = "";
        public DateTime TicketDate { get; set; }
        public decimal Amount { get; set; }

        public string? RA { get; set; }
        public string? Customer { get; set; }
        public string? CustomerEmail { get; set; }
        public int? RentalDays { get; set; }

        public string? HighlightedBillPath { get; set; }
        public string? ContractPdfPath { get; set; }
        public string? EmailSubject { get; set; }
        public string? EmailBody { get; set; }

        public bool FoundInBill { get; set; }
        public bool ContractFound { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
