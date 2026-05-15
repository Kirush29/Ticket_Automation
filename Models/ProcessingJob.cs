namespace TrafficTicketAutomation.Models
{
    public enum JobStatus { Pending, Running, Completed, Failed }

    public class ProcessingJob
    {
        public int Id { get; set; }
        public string ExcelFileName { get; set; } = "";
        public string BillFileName { get; set; } = "";
        public JobStatus Status { get; set; } = JobStatus.Pending;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public ICollection<TicketResult> Results { get; set; } = new List<TicketResult>();
    }
}
