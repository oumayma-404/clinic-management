using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Common;
using ClinicManagement.Infrastructure.Persistence.Configurations;

namespace ClinicManagement.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Patient> Patients { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PatientFile> PatientFiles { get; set; }
    public DbSet<PatientFlag> PatientFlags { get; set; }
    public DbSet<RecurringAppointment> RecurringAppointments { get; set; }
    public DbSet<StockItem> StockItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        return result;
    }
}



