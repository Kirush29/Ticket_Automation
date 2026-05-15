namespace TrafficTicketAutomation.Models.DTOs
{
    public class ProcessRequest
    {
        public string ExcelPath { get; set; } = "";
        public string BillPath { get; set; } = "";
    }

    public class TicketResultDto
    {
        public string Plate { get; set; } = "";
        public DateTime TicketDate { get; set; }
        public decimal Amount { get; set; }
        public string? RA { get; set; }
        public string? Customer { get; set; }
        public string? CustomerEmail { get; set; }
        public int? RentalDays { get; set; }
        public bool FoundInBill { get; set; }
        public bool ContractFound { get; set; }
        public string? HighlightedBillPath { get; set; }
        public string? ContractPdfPath { get; set; }
        public string? EmailSubject { get; set; }
        public string? EmailBody { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class JobStatusDto
    {
        public int JobId { get; set; }
        public string Status { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<TicketResultDto> Results { get; set; } = new();
    }
}
