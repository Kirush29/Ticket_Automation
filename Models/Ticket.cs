namespace TrafficTicketAutomation.Models
{
    public class Ticket
    {
        public int Id { get; set; }
        public string Plate { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
