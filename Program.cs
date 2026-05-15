using Microsoft.EntityFrameworkCore;
using Serilog;
using TrafficTicketAutomation.Data;
using TrafficTicketAutomation.Interfaces;
using TrafficTicketAutomation.Services;
using TrafficTicketAutomation.Services.Automation;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/app-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var provider = builder.Configuration["DatabaseProvider"]?.Trim();
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

    if (provider?.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) == true ||
        connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(connectionString);
    }
});

builder.Services.AddSingleton<IAutomationService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var mode = config["Automation:Mode"]?.Trim();
    if (mode?.Equals("PAD", StringComparison.OrdinalIgnoreCase) == true)
    {
        return new PadQueueService(config, sp.GetRequiredService<ILogger<PadQueueService>>());
    }

    return new RentWorksAutomationService(config, sp.GetRequiredService<ILogger<RentWorksAutomationService>>());
});

builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<IProcessingOrchestrator, ProcessingOrchestrator>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSerilogRequestLogging();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
