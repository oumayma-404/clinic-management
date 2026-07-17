namespace ClinicManagement.Application.DTOs;

public class DashboardStatsDto
{
    public int TodaysAppointments { get; set; }
    public int TotalPatients { get; set; }
    public int UpcomingPending { get; set; }
    public int ThisWeekAppointments { get; set; }
    public int UrgentPatients { get; set; }

    /// <summary>Total collected (encaissé) in the current month, in TND.</summary>
    public decimal MonthlyRevenueCollected { get; set; }
}
