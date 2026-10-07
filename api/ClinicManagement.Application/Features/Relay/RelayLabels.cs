using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>The FR-2 sentence for each state, in the clinic's own clock. Words and an icon, never a colour alone.</summary>
public static class RelayLabels
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Key(ClinicRelayState state) => state switch
    {
        ClinicRelayState.None => "none",
        ClinicRelayState.Installing => "installing",
        ClinicRelayState.InstallFailed => "install-failed",
        ClinicRelayState.Abandoned => "abandoned",
        ClinicRelayState.Ready => "ready",
        ClinicRelayState.Late => "late",
        ClinicRelayState.Off => "off",
        ClinicRelayState.Updating => "updating",
        ClinicRelayState.DiskNearlyFull => "disk-nearly-full",
        ClinicRelayState.Mismatch => "mismatch",
        ClinicRelayState.Retired => "retired",
        ClinicRelayState.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Sentence(ClinicRelayHealthReading reading, ClinicRelay? relay, DateTime nowUtc)
    {
        var label = relay?.Label ?? "PC de secours";
        return reading.State switch
        {
            ClinicRelayState.None => "Aucun PC de secours",
            ClinicRelayState.Installing => $"Copie en cours ({reading.SeedPercent ?? 0} %)",
            ClinicRelayState.InstallFailed => $"Échec de l'installation : {relay?.LastError}",
            ClinicRelayState.Abandoned => $"Installation abandonnée sur {label}",
            ClinicRelayState.Ready => Fresh(reading.Since, nowUtc),
            ClinicRelayState.Late => $"Copie en retard de {Duration(nowUtc - (reading.Since ?? nowUtc))}",
            ClinicRelayState.Off => $"Éteint depuis {Moment(reading.Since, nowUtc)}",
            ClinicRelayState.Updating => "Mise à jour du PC de secours en cours",
            ClinicRelayState.DiskNearlyFull =>
                $"Il reste {Math.Max(0, (relay?.DiskFreeBytes ?? 0) / (1024L * 1024 * 1024))} Go sur {label}",
            ClinicRelayState.Mismatch => "La copie ne correspond pas au cloud — réparation en cours",
            ClinicRelayState.Retired =>
                $"Copie arrêtée le {ClinicClock.ToClinicLocal(reading.Since ?? nowUtc).ToString("dd/MM", French)}",
            ClinicRelayState.Stopped =>
                $"Copie arrêtée depuis {Moment(reading.Since, nowUtc)} : le cloud est revenu à un état antérieur, {label} garde les données les plus récentes",
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.State, null),
        };
    }

    /// <summary>
    /// The vendor console's column (AC-9.1): short, and without the PC's name — a cabinet's machine names are not
    /// the console's business, and the column is read down a list of every cabinet.
    /// </summary>
    public static string Short(ClinicRelayHealthReading reading, DateTime nowUtc) => reading.State switch
    {
        ClinicRelayState.None => "Aucun",
        ClinicRelayState.Installing => $"Installation ({reading.SeedPercent ?? 0} %)",
        ClinicRelayState.InstallFailed => "Échec de l'installation",
        ClinicRelayState.Abandoned => "Installation abandonnée",
        ClinicRelayState.Ready => "Prêt",
        ClinicRelayState.Late => $"En retard de {Duration(nowUtc - (reading.Since ?? nowUtc))}",
        ClinicRelayState.Off => $"Éteint depuis {Moment(reading.Since, nowUtc)}",
        ClinicRelayState.Updating => "Mise à jour",
        ClinicRelayState.DiskNearlyFull => "Disque presque plein",
        ClinicRelayState.Mismatch => "Ne correspond pas",
        ClinicRelayState.Retired => "Retiré",
        ClinicRelayState.Stopped => $"Copie arrêtée depuis {Moment(reading.Since, nowUtc)}",
        _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.State, null),
    };

    /// <summary>Under a minute: « il y a 3 s »; otherwise the time (FR-2, the spec's freshness call).</summary>
    private static string Fresh(DateTime? since, DateTime nowUtc)
    {
        if (since is null)
        {
            return "Copie à jour";
        }

        var age = nowUtc - since.Value;
        return age < TimeSpan.FromMinutes(1)
            ? $"Copie à jour il y a {Math.Max(0, (int)age.TotalSeconds)} s"
            : $"Copie de {ClinicClock.ToClinicLocal(since.Value).ToString("HH:mm", French)}";
    }

    /// <summary>« 08:12 » today, « 06/10 à 08:12 » otherwise — the bell says the same as the card.</summary>
    public static string Moment(DateTime? since, DateTime nowUtc)
    {
        if (since is null)
        {
            return "un moment";
        }

        var local = ClinicClock.ToClinicLocal(since.Value);
        return local.Date == ClinicClock.ToClinicLocal(nowUtc).Date
            ? local.ToString("HH:mm", French)
            : local.ToString("dd/MM 'à' HH:mm", French);
    }

    private static string Duration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes:00}"
            : $"{Math.Max(1, (int)span.TotalMinutes)} min";
}
