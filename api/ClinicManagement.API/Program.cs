using ClinicManagement.Application;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Application layer
builder.Services.AddApplication();

// Add Infrastructure layer
builder.Services.AddInfrastructure(builder.Configuration);

// Add Hangfire for background jobs
var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(hangfireConnectionString));
builder.Services.AddHangfireServer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();

// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();
}

// Schedule background jobs
RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.NotificationJob>(
    "process-notifications",
    job => job.ProcessPendingNotifications(),
    Cron.Minutely);

RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.AISummaryJob>(
    "generate-ai-summaries",
    job => job.GenerateSummariesForUpcomingAppointments(),
    Cron.Minutely);

RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.GoogleCalendarSyncJob>(
    "sync-google-calendar",
    job => job.SyncFromGoogleCalendar(),
    Cron.Minutely); // Changed to every minute for testing - change back to Cron.Hourly for production

app.Run();

// Simple authorization filter for Hangfire (in production, use proper authentication)
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // In production, implement proper authorization
        return true;
    }
}
