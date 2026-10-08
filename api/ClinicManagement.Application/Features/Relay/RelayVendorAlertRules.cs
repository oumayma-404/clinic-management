using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// Which PC de secours problems the <b>vendor</b> must hear about right now (<c>clinic-pc-copy</c> AC-9.2, AC-9.4).
/// Pure, like <see cref="RelayAlertRules"/>, and read from the same <see cref="ClinicRelayHealth"/> answer.
///
/// <para>⚠️ <b>Narrower than the admins' bell</b>: a PC off for an hour is the cabinet's business (its admins are told);
/// the vendor is told when nobody has switched it back on for a day. « En retard » keeps the admins' opening-hours rule
/// — counted from the opening, and an episode already open lasts past closing — so the two never disagree about
/// whether a cabinet was late.</para>
/// </summary>
public static class RelayVendorAlertRules
{
    /// <summary>No heartbeat for this long and the vendor is told (AC-9.2).</summary>
    public static readonly TimeSpan UnseenAfter = TimeSpan.FromHours(24);

    /// <summary>How long a restored cloud's gap stays one open incident: one e-mail, not one per minute.</summary>
    public static readonly TimeSpan CloudRestoredFor = TimeSpan.FromHours(24);

    public static IReadOnlyList<RelayIncidentKind> Due(
        ClinicRelay? relay, string? clinicHoursJson, IReadOnlyCollection<RelayIncidentKind> open, DateTime nowUtc)
    {
        if (relay is null)
        {
            return Array.Empty<RelayIncidentKind>();
        }

        if (RelayAlertRules.IsReturnStuck(relay, nowUtc))
        {
            return new[] { RelayIncidentKind.ReturnStuck };
        }

        // AC-9.4: the vendor learns a cabinet's cloud was restored and its PC gave back what was lost — once, that day.
        if (relay.LastGapAtUtc is { } gap && nowUtc - gap < CloudRestoredFor)
        {
            return new[] { RelayIncidentKind.CloudRestored };
        }

        var reading = ClinicRelayHealth.Read(relay, nowUtc);
        return reading.State switch
        {
            ClinicRelayState.Off when nowUtc - (reading.Since ?? nowUtc) >= UnseenAfter => new[] { RelayIncidentKind.Unseen },
            ClinicRelayState.Late when open.Contains(RelayIncidentKind.Late)
                                       || RelayAlertRules.DueInOpeningHours(reading, clinicHoursJson, nowUtc)
                => new[] { RelayIncidentKind.Late },
            ClinicRelayState.Mismatch => new[] { RelayIncidentKind.Mismatch },
            ClinicRelayState.Stopped => new[] { RelayIncidentKind.Stopped },
            _ => Array.Empty<RelayIncidentKind>(),
        };
    }
}

/// <summary>
/// What the vendor's e-mail says (<c>clinic-pc-copy</c> AC-9.2) — through the alert channel shared with
/// <c>server-loss-recovery</c> Part 3 (<c>VendorAlertRecipients</c>). Public so the wording is tested.
/// </summary>
public static class RelayVendorAlertEmail
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>What an e-mail calls a cabinet whose name is blank — real data has them, and « —  : » names nobody.</summary>
    public const string UnnamedClinic = "Cabinet sans nom";

    /// <summary>The subject names the cabinet and the problem, so an inbox list reads as the alarm.</summary>
    public static string Subject(string? clinicName, RelayIncidentKind kind) =>
        $"PC de secours — {NameOf(clinicName)} : {Problem(kind)}";

    private static string NameOf(string? clinicName) =>
        string.IsNullOrWhiteSpace(clinicName) ? UnnamedClinic : clinicName.Trim();

    public static string Problem(RelayIncidentKind kind) => kind switch
    {
        RelayIncidentKind.Unseen => "injoignable depuis 24 h",
        RelayIncidentKind.Late => "copie en retard",
        RelayIncidentKind.Mismatch => "copie qui ne correspond pas",
        RelayIncidentKind.Stopped => "copie arrêtée",
        RelayIncidentKind.ReturnStuck => "retour au cloud bloqué",
        RelayIncidentKind.CloudRestored => "cloud restauré, données rendues par le PC",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <param name="clinicId">Always printed: a name can be blank or shared by two cabinets, the id is what opens the
    /// cabinet's page in the console (<c>/cabinets/{id}</c>).</param>
    public static EmailContent Compose(
        string? clinicName, Guid clinicId, RelayIncidentKind kind, DateTime? sinceUtc, DateTime nowUtc)
    {
        clinicName = NameOf(clinicName);
        var (intro, outro) = kind switch
        {
            RelayIncidentKind.Unseen => (
                $"Le PC de secours de {clinicName} ne donne plus de nouvelles depuis plus de 24 heures. Si internet "
                + "tombe au cabinet, personne ne pourra y travailler.",
                "Appelez le cabinet : le PC est sans doute éteint, débranché ou hors du réseau."),
            RelayIncidentKind.Late => (
                $"La copie du PC de secours de {clinicName} est en retard depuis plus de 15 minutes, pendant les "
                + "heures d'ouverture. Il ne pourrait pas prendre le relais avec les dernières données.",
                "Si le retard dure, vérifiez la connexion du PC et son journal (Paramètres → PC de secours du cabinet)."),
            RelayIncidentKind.Mismatch => (
                $"La copie du PC de secours de {clinicName} ne correspond pas au cloud, et elle n'a pas pu se "
                + "réparer seule.",
                "Ne retirez pas ce PC tant que l'écart n'est pas compris : il peut détenir ce que le cloud n'a pas."),
            RelayIncidentKind.Stopped => (
                $"Le cloud est revenu à un état antérieur à la copie du PC de secours de {clinicName} "
                + "(restauration ?). Le PC a arrêté de copier pour ne rien perdre : il garde les données les plus récentes.",
                "Ne retirez pas et n'effacez pas ce PC : il détient ce que le cloud a perdu."),
            RelayIncidentKind.ReturnStuck => (
                $"Le PC de secours de {clinicName} n'arrive pas à rendre au cloud le travail fait pendant une coupure "
                + "d'internet, depuis plus de 15 minutes. Le cabinet continue de travailler sur ce PC.",
                "Ne retirez pas et n'effacez pas ce PC : il détient le travail de la coupure. Consultez son journal."),
            RelayIncidentKind.CloudRestored => (
                $"Le cloud de {clinicName} a été restauré depuis une sauvegarde plus ancienne que la copie de son PC de "
                + "secours. Le PC a renvoyé au cloud les enregistrements que la restauration avait perdus.",
                "Ce que le cloud avait modifié depuis sa restauration est gardé et listé dans « Retours du PC de "
                + "secours » pour les administrateurs du cabinet."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return new EmailContent
        {
            Title = Subject(clinicName, kind),
            Preheader = kind == RelayIncidentKind.CloudRestored
                ? $"Le cloud de {clinicName} a récupéré les données de son PC de secours."
                : $"Le PC de secours de {clinicName} ne peut pas prendre le relais.",
            Intro = [intro, "Vous ne recevrez ce message qu'une fois par problème ; la console éditeur montre l'état à jour."],
            Details =
            [
                new EmailDetail("Cabinet", clinicName),
                new EmailDetail("Identifiant du cabinet", clinicId.ToString("D")),
                new EmailDetail("Problème", Problem(kind)),
                new EmailDetail("Depuis", At(sinceUtc ?? nowUtc)),
                new EmailDetail("Vérifié le", At(nowUtc)),
            ],
            Outro = [outro],
        };
    }

    private static string At(DateTime utc) =>
        ClinicClock.ToClinicLocal(utc).ToString("dd/MM/yyyy 'à' HH:mm", French);
}
