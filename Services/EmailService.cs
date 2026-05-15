using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Services
{
    public class EmailService : IEmailService
    {
        public EmailDraft GenerateDraft(Contract contract, Ticket ticket, string highlightedBillPath, string contractPdfPath)
        {
            var rentalDays = (contract.End - contract.Start).Days;

            var attachmentLines = new List<string>();
            var attachments = new List<string>();

            if (!string.IsNullOrWhiteSpace(highlightedBillPath))
            {
                attachmentLines.Add("  1. Highlighted 407 ETR bill showing your specific charge");
                attachments.Add(highlightedBillPath);
            }

            if (!string.IsNullOrWhiteSpace(contractPdfPath))
            {
                attachmentLines.Add("  2. Original printed rental contract");
                attachments.Add(contractPdfPath);
            }

            var attachmentSection = attachmentLines.Count > 0
                ? "Please find attached:\n" + string.Join("\n", attachmentLines)
                : "No attachments are available at this time.";

            var body = $@"Dear {contract.Customer},

Please be advised that a 407 ETR charge has been recorded for the vehicle rented under your agreement.

Rental Agreement Details:
  RA Number     : {contract.RA}
  Vehicle Plate : {ticket.Plate}
  Rental Period : {contract.Start:MMMM dd, yyyy} – {contract.End:MMMM dd, yyyy} ({rentalDays} day(s))

407 Charge Details:
  Charge Date   : {ticket.Date:MMMM dd, yyyy}
  Description   : {ticket.Description ?? "407 ETR Toll Charge"}
  Amount        : ${ticket.Amount:F2}

{attachmentSection}

If you have any questions, please contact us.

Thank you,
Vehicle Rental Team";

            return new EmailDraft
            {
                To = contract.Email,
                Subject = $"RA#{contract.RA} – 407 ETR Charge Notice",
                Body = body,
                Attachments = attachments
            };
        }
    }
}
