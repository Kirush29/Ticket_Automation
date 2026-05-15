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
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// PadQueueService is singleton — it holds a semaphore to enforce one RentWorks session at a time
builder.Services.AddSingleton<IAutomationService, PadQueueService>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IEmailService, EmailService>();
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
