namespace ClinicManagement.Domain.Enums;

/// <summary>The scope of an edit/cancel on a recurring series: a single occurrence, this occurrence and all
/// following ones, or the whole series.</summary>
public enum RecurringSeriesScope
{
    Occurrence = 0,
    Following = 1,
    WholeSeries = 2
}
