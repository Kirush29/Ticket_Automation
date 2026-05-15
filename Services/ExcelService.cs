using OfficeOpenXml;
using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Services
{
    public class ExcelService : IExcelService
    {
        private readonly ILogger<ExcelService> _logger;

        public ExcelService(ILogger<ExcelService> logger)
        {
            _logger = logger;
            ExcelPackage.License.SetNonCommercialOrganization("TrafficTicketAutomation");
        }

        public List<Ticket> ReadTickets(string filePath)
        {
            var safeBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Files", "Excels"));
            var safePath = Path.GetFullPath(filePath);
            if (!safePath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("File path is outside the allowed directory.");

            var file = new FileInfo(safePath);
            if (!file.Exists) throw new FileNotFoundException("Excel file not found.", safePath);

            var tickets = new List<Ticket>();
            using var package = new ExcelPackage(file);
            var sheet = package.Workbook.Worksheets[0];

            if (sheet.Dimension == null) return tickets;

            for (int i = 2; i <= sheet.Dimension.Rows; i++)
            {
                var plate = sheet.Cells[i, 1].Text?.Trim();
                if (string.IsNullOrWhiteSpace(plate)) continue;

                if (!DateTime.TryParse(sheet.Cells[i, 2].Text, out var date))
                {
                    _logger.LogWarning("Row {Row}: invalid date '{Val}', skipping.", i, sheet.Cells[i, 2].Text);
                    continue;
                }

                if (!decimal.TryParse(sheet.Cells[i, 3].Text, out var amount))
                {
                    _logger.LogWarning("Row {Row}: invalid amount '{Val}', skipping.", i, sheet.Cells[i, 3].Text);
                    continue;
                }

                tickets.Add(new Ticket
                {
                    Plate = plate,
                    Date = date,
                    Amount = amount,
                    Description = sheet.Cells[i, 4].Text?.Trim()
                });
            }

            _logger.LogInformation("Read {Count} tickets from {File}.", tickets.Count, file.Name);
            return tickets;
        }
    }
}
