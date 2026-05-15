using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Geom;
using TrafficTicketAutomation.Interfaces;
using SysPath = System.IO.Path;
using SysFile = System.IO.File;

namespace TrafficTicketAutomation.Services
{
    public class PdfService : IPdfService
    {
        private readonly ILogger<PdfService> _logger;

        public PdfService(ILogger<PdfService> logger) => _logger = logger;

        public bool HighlightRow(string billPath, string plate, DateTime date, decimal amount, string outputPath)
        {
            var safeBase = SysPath.GetFullPath(SysPath.Combine(AppContext.BaseDirectory, "Files"));
            if (!SysPath.GetFullPath(billPath).StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Bill path is outside the allowed directory.");

            using var reader = new PdfReader(billPath);
            using var writer = new PdfWriter(outputPath);
            using var pdf = new PdfDocument(reader, writer);

            var dateStr = date.ToString("yyyy-MM-dd");
            var amountStr = amount.ToString("F2");

            for (int pageNum = 1; pageNum <= pdf.GetNumberOfPages(); pageNum++)
            {
                var page = pdf.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                PdfTextExtractor.GetTextFromPage(page, strategy);

                var chunks = GetTextChunks(page);
                var rowRect = FindRowRectangle(chunks, plate, dateStr, amountStr);

                if (rowRect != null)
                {
                    var highlight = new PdfTextMarkupAnnotation(
                        rowRect,
                        PdfName.Highlight,
                        BuildQuadPoints(rowRect));

                    highlight.SetColor(ColorConstants.YELLOW);
                    highlight.SetContents($"407 Charge: {plate} | {dateStr} | ${amountStr}");
                    page.AddAnnotation(highlight);

                    _logger.LogInformation("Highlighted row for plate {Plate} on page {Page}.", plate, pageNum);
                    return true;
                }
            }

            _logger.LogWarning("No matching row found in bill for plate {Plate} date {Date} amount {Amount}.", plate, dateStr, amountStr);
            return false;
        }

        private static List<(string Text, Rectangle Rect)> GetTextChunks(PdfPage page)
        {
            var listener = new ChunkListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);
            return listener.Chunks;
        }

        private static Rectangle? FindRowRectangle(
            List<(string Text, Rectangle Rect)> chunks,
            string plate, string dateStr, string amountStr)
        {
            // Group chunks by approximate Y position (same row = within 5 units)
            var rows = chunks
                .GroupBy(c => Math.Round(c.Rect.GetY(), 0))
                .OrderByDescending(g => g.Key);

            foreach (var row in rows)
            {
                var rowText = string.Join(" ", row.Select(c => c.Text));
                if (rowText.Contains(plate, StringComparison.OrdinalIgnoreCase) &&
                    rowText.Contains(dateStr) &&
                    rowText.Contains(amountStr))
                {
                    var allRects = row.Select(c => c.Rect).ToList();
                    float x = allRects.Min(r => r.GetX());
                    float y = allRects.Min(r => r.GetY());
                    float right = allRects.Max(r => r.GetX() + r.GetWidth());
                    float top = allRects.Max(r => r.GetY() + r.GetHeight());
                    return new Rectangle(x, y, right - x, top - y);
                }
            }
            return null;
        }

        private static float[] BuildQuadPoints(Rectangle r)
        {
            // iText7 highlight annotation requires quad points (4 corners, bottom-left origin)
            return new float[]
            {
                r.GetLeft(),  r.GetTop(),
                r.GetRight(), r.GetTop(),
                r.GetLeft(),  r.GetBottom(),
                r.GetRight(), r.GetBottom()
            };
        }
    }

    /// <summary>
    /// Custom iText7 listener that collects individual text chunks with their bounding rectangles.
    /// This is needed because LocationTextExtractionStrategy only returns plain text without positions.
    /// </summary>
    internal class ChunkListener : IEventListener
    {
        public List<(string Text, Rectangle Rect)> Chunks { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            var info = (TextRenderInfo)data;
            var text = info.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;
            var baseline = info.GetBaseline();
            var ascent = info.GetAscentLine();
            var rect = new Rectangle(
                baseline.GetStartPoint().Get(0),
                baseline.GetStartPoint().Get(1),
                baseline.GetEndPoint().Get(0) - baseline.GetStartPoint().Get(0),
                ascent.GetStartPoint().Get(1) - baseline.GetStartPoint().Get(1));
            Chunks.Add((text, rect));
        }

        public ICollection<EventType> GetSupportedEvents() =>
            new[] { EventType.RENDER_TEXT };
    }
}
