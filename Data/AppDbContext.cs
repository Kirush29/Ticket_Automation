using Microsoft.EntityFrameworkCore;
using TrafficTicketAutomation.Models;

namespace TrafficTicketAutomation.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
        public DbSet<TicketResult> TicketResults => Set<TicketResult>();
        public DbSet<Contract> Contracts => Set<Contract>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TicketResult>()
                .HasOne(r => r.Job)
                .WithMany(j => j.Results)
                .HasForeignKey(r => r.JobId);

            modelBuilder.Entity<TicketResult>()
                .Property(r => r.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Contract>()
                .HasIndex(c => c.Plate);

            modelBuilder.Entity<ProcessingJob>()
                .Property(j => j.Status)
                .HasConversion<string>();
        }
    }
}
