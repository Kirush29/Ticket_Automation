namespace TrafficTicketAutomation.Models
{
    public class Contract
    {
        public int Id { get; set; }
        public string RA { get; set; } = "";
        public string Plate { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Customer { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Notes { get; set; }
    }
}
