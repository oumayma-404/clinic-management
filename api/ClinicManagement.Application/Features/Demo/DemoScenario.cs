namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// The demo cabinet's fixed identity — the « Premiers pas » videos' cabinet, so the films and the demo tell one story.
/// Fictional throughout (names from « Friends »), with no real-looking contact details.
/// </summary>
public static class DemoScenario
{
    public const string CabinetName = "Cabinet Dentaire Démo";

    public const string Address = "Avenue des Jasmins";

    public const string City = "Tunis";

    public const string Phone = "71 000 000";

    public const string EmailDomain = "demo.apexa.tn";

    public const string DentistFirstName = "Monica";

    public const string DentistLastName = "Geller";

    // Never « Dr Monica Geller »: the title in a name prints as « Dr Dr » on the documents.
    public const string DentistFullName = "Monica Geller";

    public const string SecondDentistFirstName = "Chandler";

    public const string SecondDentistLastName = "Bing";

    public const string SecondDentistFullName = SecondDentistFirstName + " " + SecondDentistLastName;

    // The stored Doctor.Specialty key; documents print its French label (DoctorSpecialtyLabels).
    public const string DentistSpecialty = "Dentist";

    public const string SecretaryFullName = "Rachel Green";

    /// <summary>A member's address for one build; the stamp keeps it unique beside yesterday's cabinet.</summary>
    public static string EmailFor(string localPart, string stamp) => $"{localPart}+{stamp}@{EmailDomain}";

    /// <summary>Mon–Fri 09:00–18:00 with a lunch break, Saturday morning — what the agenda and the slots follow.</summary>
    public static readonly IReadOnlyList<(DayOfWeek Day, TimeSpan From, TimeSpan To, TimeSpan? BreakFrom, TimeSpan? BreakTo)>
        Hours = new[]
        {
            (DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), (TimeSpan?)new TimeSpan(13, 0, 0), (TimeSpan?)new TimeSpan(14, 0, 0)),
            (DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(13, 0, 0), new TimeSpan(14, 0, 0)),
            (DayOfWeek.Wednesday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(13, 0, 0), new TimeSpan(14, 0, 0)),
            (DayOfWeek.Thursday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(13, 0, 0), new TimeSpan(14, 0, 0)),
            (DayOfWeek.Friday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(13, 0, 0), new TimeSpan(14, 0, 0)),
            (DayOfWeek.Saturday, new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0), null, null),
        };

    public static bool IsWorkingDay(DateTime clinicDate) => Hours.Any(h => h.Day == clinicDate.DayOfWeek);
}
