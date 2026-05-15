using TrafficTicketAutomation.Data;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Services
{
    public class ContractService
    {
        private readonly AppDbContext _db;

        public ContractService(AppDbContext db) => _db = db;

        public Contract? FindContract(string plate, DateTime date) =>
            _db.Contracts.FirstOrDefault(c =>
                c.Plate == plate && date >= c.Start && date <= c.End);
    }
}
