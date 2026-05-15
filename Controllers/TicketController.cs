using Microsoft.AspNetCore.Mvc;
using TrafficTicketAutomation.Interfaces;

namespace TrafficTicketAutomation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TicketController : ControllerBase
    {
        private readonly IProcessingOrchestrator _orchestrator;
        private readonly ILogger<TicketController> _logger;

        public TicketController(IProcessingOrchestrator orchestrator, ILogger<TicketController> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        /// <summary>
        /// Upload Excel + 407 bill PDF. Returns a jobId for polling.
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(50_000_000)] // 50 MB
        public async Task<IActionResult> Upload(IFormFile excelFile, IFormFile billFile)
        {
            if (excelFile == null || billFile == null)
                return BadRequest("Both excelFile and billFile are required.");

            var excelDir = Path.Combine(AppContext.BaseDirectory, "Files", "Excels");
            var billDir = Path.Combine(AppContext.BaseDirectory, "Files", "Bills");
            Directory.CreateDirectory(excelDir);
            Directory.CreateDirectory(billDir);

            var excelPath = Path.Combine(excelDir, $"{Guid.NewGuid()}_{excelFile.FileName}");
            var billPath = Path.Combine(billDir, $"{Guid.NewGuid()}_{billFile.FileName}");

            await using (var fs = System.IO.File.Create(excelPath))
                await excelFile.CopyToAsync(fs);

            await using (var fs = System.IO.File.Create(billPath))
                await billFile.CopyToAsync(fs);

            _logger.LogInformation("Files uploaded: {Excel}, {Bill}.", excelFile.FileName, billFile.FileName);

            var jobId = await _orchestrator.StartJobAsync(excelPath, billPath);
            return Accepted(new { jobId });
        }

        /// <summary>
        /// Poll job status and results.
        /// </summary>
        [HttpGet("job/{jobId:int}")]
        public async Task<IActionResult> GetJob(int jobId)
        {
            var status = await _orchestrator.GetJobStatusAsync(jobId);
            if (status == null) return NotFound();
            return Ok(status);
        }

        /// <summary>
        /// Download a generated output file (highlighted PDF or contract PDF).
        /// Only files inside the Output or Contracts directory are served.
        /// </summary>
        [HttpGet("download")]
        public IActionResult Download([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return BadRequest("Path is required.");

            var allowedBases = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Files", "Output")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Files", "Contracts"))
            };

            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

            if (!allowedBases.Any(b => fullPath.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                return Forbid();

            if (!System.IO.File.Exists(fullPath)) return NotFound();

            var bytes = System.IO.File.ReadAllBytes(fullPath);
            return File(bytes, "application/pdf", Path.GetFileName(fullPath));
        }
    }
}
