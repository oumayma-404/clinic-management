using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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
        ClinicRelayState.ClockWrong => "clock-wrong",
        ClinicRelayState.InCharge => "in-charge",
        ClinicRelayState.Returning => "returning",
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
            ClinicRelayState.InCharge => $"Le cabinet travaille sur ce PC depuis {Moment(reading.Since, nowUtc)}",
            ClinicRelayState.Returning => "Retour au cloud en cours",
            ClinicRelayState.DiskNearlyFull =>
                $"Il reste {Math.Max(0, (relay?.DiskFreeBytes ?? 0) / (1024L * 1024 * 1024))} Go sur {label}",
            ClinicRelayState.Mismatch => "La copie ne correspond pas au cloud — réparation en cours",
            ClinicRelayState.Retired when relay?.RetiredReason == ClinicRelayRetirement.LostOrStolen =>
                $"Déclaré perdu ou volé le {ClinicClock.ToClinicLocal(reading.Since ?? nowUtc).ToString("dd/MM", French)}",
            ClinicRelayState.Retired when relay is { UninstalledAtUtc: not null, ErasedAtUtc: { } erasedOnUninstall } =>
                $"PC désinstallé, copie effacée le {ClinicClock.ToClinicLocal(erasedOnUninstall).ToString("dd/MM", French)}",
            // « peut être » is the honest word: the box was unticked, or the erase was never confirmed to the cloud.
            ClinicRelayState.Retired when relay?.UninstalledAtUtc is { } uninstalled =>
                $"PC désinstallé le {ClinicClock.ToClinicLocal(uninstalled).ToString("dd/MM", French)} : la copie du cabinet peut être restée sur ce PC",
            ClinicRelayState.Retired when relay?.ErasedAtUtc is { } erased =>
                $"Copie effacée du PC le {ClinicClock.ToClinicLocal(erased).ToString("dd/MM", French)}",
            ClinicRelayState.Retired =>
                $"Copie arrêtée le {ClinicClock.ToClinicLocal(reading.Since ?? nowUtc).ToString("dd/MM", French)}",
            ClinicRelayState.Stopped when relay?.CutOverruledAtUtc is not null =>
                $"Copie arrêtée depuis {Moment(reading.Since, nowUtc)} : le cloud a repris la main pendant la coupure, {label} garde ce que le cabinet y a enregistré",
            ClinicRelayState.Stopped =>
                $"Copie arrêtée depuis {Moment(reading.Since, nowUtc)} : le cloud est revenu à un état antérieur, {label} garde les données les plus récentes",
            ClinicRelayState.ClockWrong =>
                $"L'horloge de {label} est fausse et il ne peut pas la remettre à l'heure : il ne prendra pas le relais",
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.State, null),
        };
    }

    /// <summary>Why the cabinet's saves are refused on the cloud right now — the card's line above « Reprendre la main ».</summary>
    public static string Lock(bool pcHolding, DateTime lockedSinceUtc, DateTime nowUtc) =>
        pcHolding
            ? $"Le cabinet travaille sur le PC de secours depuis {Moment(lockedSinceUtc, nowUtc)} : ici, le cloud est en lecture seule."
            : $"Le PC de secours ne répond plus depuis {Moment(lockedSinceUtc, nowUtc)} : le cloud refuse les enregistrements du cabinet.";

    /// <summary>AC-7.1's warning before « Reprendre la main » — and before a retire or a loss while the cabinet is locked (AC-8.6).</summary>
    public static string ReclaimWarning(string? label, DateTime lockedSinceUtc, DateTime nowUtc) =>
        $"Ce que le cabinet a enregistré sur {label ?? "le PC de secours"} depuis {Moment(lockedSinceUtc, nowUtc)} ne partira pas "
        + "dans le cloud : il faudra le saisir à nouveau, et les numéros de notes émis sur ce PC seront en double. "
        + "Appelez le cabinet avant de continuer.";

    /// <summary>What the PC de secours says about itself on its own « Paramètres » (AC-8.1).</summary>
    public static string Local(bool retired, DateTime? retiredAtUtc) =>
        !retired
            ? "Ce PC est le PC de secours du cabinet : sa copie suit le cloud, et l'on n'y enregistre rien."
            : retiredAtUtc is { } at
                ? $"Copie arrêtée le {ClinicClock.ToClinicLocal(at).ToString("dd/MM", French)} : ce PC ne suit plus le cabinet. "
                  + "Seuls les administrateurs peuvent encore l'ouvrir."
                : "Copie arrêtée : ce PC ne suit plus le cabinet. Seuls les administrateurs peuvent encore l'ouvrir.";

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
        ClinicRelayState.ClockWrong => "Horloge fausse",
        ClinicRelayState.InCharge => $"En relève depuis {Moment(reading.Since, nowUtc)}",
        ClinicRelayState.Returning => "Retour au cloud",
        _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.State, null),
    };

    /// <summary>
    /// Who took the cabinet's saves back from a silent PC, and when — said on the card until that PC is heard from again
    /// (AC-6.2's devices, or US-7's « Reprendre la main »). Null when nothing was taken back since the PC last spoke.
    /// </summary>
    public static string? Reclaimed(ClinicRelay? relay, DateTime nowUtc)
    {
        if (relay?.ReclaimedAtUtc is not { } at || relay.LastSeenAtUtc is { } seen && seen > at)
        {
            return null;
        }

        var when = Moment(at, nowUtc);
        return relay.ReclaimedByUserId == ClinicRelay.ReclaimedByDevices
            ? $"Repris par les appareils du cabinet à {when} : ils joignaient le cloud, plus le PC de secours."
            : $"Un administrateur a repris la main à {when}.";
    }

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

    /// <summary>D20b: how far a clock was off — « 45 s », « 3 min », « 1 h 05 », « 2 j ».</summary>
    public static string ClockOffset(int seconds)
    {
        var d = TimeSpan.FromSeconds(Math.Abs(seconds));
        return d.TotalDays >= 1 ? $"{(int)d.TotalDays} j"
            : d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00}"
            : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min"
            : $"{(int)d.TotalSeconds} s";
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
