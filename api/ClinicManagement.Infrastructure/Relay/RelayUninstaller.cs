namespace ClinicManagement.Infrastructure.Relay;

public enum RelayUninstallOutcome
{
    /// <summary>Nothing to tell: this PC was never paired with a cabinet.</summary>
    NotPaired,
    /// <summary>The cloud recorded the uninstall; the copy stays on this PC, as asked.</summary>
    Recorded,
    /// <summary>The cloud recorded the uninstall, the copy is gone, and the cloud recorded that too.</summary>
    RecordedAndErased,
    /// <summary>The copy is gone, but the cloud could not be told so.</summary>
    ErasedButNotReported,
    /// <summary>The cloud did not answer: nothing was erased, since this PC may hold the cabinet's last copy.</summary>
    CloudNotTold,
    /// <summary>The copy stopped because the cloud went back in time, so this PC holds newer records: kept.</summary>
    KeptNewerCopy,
}

/// <summary>
/// ⚠️ <see cref="ExitCode"/> is a contract with the installer's uninstall step (<c>clinic-setup.iss</c>,
/// <c>UninstallSentence</c>), which words each code itself — the printed sentence arrives in the console's code page.
/// 0 done · 1 cannot run · 2 cloud not told · 3 copy newer than the cloud · 4 erased, cloud not told · 5 erase failed,
/// nothing erased · 6 rows erased, some files not.
/// </summary>
public sealed record RelayUninstallResult(RelayUninstallOutcome Outcome, int FilesDeleted, string Sentence)
{
    public int ExitCode => Outcome switch
    {
        RelayUninstallOutcome.NotPaired or RelayUninstallOutcome.Recorded or RelayUninstallOutcome.RecordedAndErased => 0,
        RelayUninstallOutcome.CloudNotTold => 2,
        RelayUninstallOutcome.KeptNewerCopy => 3,
        RelayUninstallOutcome.ErasedButNotReported => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, null),
    };
}

/// <summary>
/// What uninstalling a PC de secours does (<c>clinic-pc-copy</c> AC-8.3): it counts as retiring it, and « Effacer aussi
/// la copie du cabinet ? » erases it.
///
/// <para>⚠️ <b>The cloud is told FIRST, and the copy is erased only once it has answered.</b> A cloud that does not answer
/// may be a cloud that is lost — the very case where the vendor promotes this PC — and an uninstaller that erased then
/// would destroy the cabinet's last copy. Likewise a PC whose copy stopped because the cloud went back in time holds
/// records the cloud does not have, and is never erased here.</para>
///
/// <para>Run with the API service stopped (the installer's order), so the copy loop cannot race this file's writes.</para>
/// </summary>
public sealed class RelayUninstaller
{
    private const int EraseReportAttempts = 3;

    private readonly IRelayCloudClient? _cloud;
    private readonly RelayFollowerStateStore _state;
    private readonly Func<CancellationToken, Task<int>> _erase;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<bool> _holdsUnreturnedWork;

    /// <param name="cloud">Null when this PC holds no pairing credentials.</param>
    /// <param name="erase">The local erase (<see cref="RelayLocalEraser"/>); it records <c>ErasedAtUtc</c> itself.</param>
    public RelayUninstaller(
        IRelayCloudClient? cloud,
        RelayFollowerStateStore state,
        Func<CancellationToken, Task<int>> erase,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<bool>? holdsUnreturnedWork = null)
    {
        _cloud = cloud;
        _state = state;
        _erase = erase;
        _delay = delay ?? Task.Delay;
        _holdsUnreturnedWork = holdsUnreturnedWork ?? (() => false);
    }

    public async Task<RelayUninstallResult> UninstallAsync(bool eraseCopy, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (_cloud is null)
        {
            return new(RelayUninstallOutcome.NotPaired, 0, "Ce PC n'était jumelé à aucun cabinet : rien à prévenir.");
        }

        // « Unknown » means the cloud answered and holds no such PC — it is up, so it is not the cabinet's last copy.
        var told = await _cloud.ReportUninstalledAsync(cancellationToken);
        if (told.Status is not (RelayCallStatus.Ok or RelayCallStatus.Released))
        {
            return new(RelayUninstallOutcome.CloudNotTold, 0, eraseCopy
                ? "Le cloud n'a pas pu être prévenu, donc la copie n'a pas été effacée : sans le cloud, ce PC est peut-être la "
                  + "seule copie du cabinet. Retirez ce PC depuis « Paramètres → PC de secours » sur le cloud."
                : "Le cloud n'a pas pu être prévenu. Retirez ce PC depuis « Paramètres → PC de secours » sur le cloud.");
        }

        var state = _state.Load();
        _state.Save(state with { Released = true, ReleasedAtUtc = state.ReleasedAtUtc ?? nowUtc });

        if (!eraseCopy)
        {
            return new(RelayUninstallOutcome.Recorded, 0,
                "Le cloud a noté la désinstallation. La copie du cabinet reste sur ce PC.");
        }

        if (state.StoppedReason is not null)
        {
            return new(RelayUninstallOutcome.KeptNewerCopy, 0,
                "La copie n'a pas été effacée : ce PC a arrêté la copie parce que le cloud était revenu en arrière, il contient "
                + "donc des données plus récentes que le cloud. Contactez la personne qui a installé votre logiciel.");
        }

        if (_holdsUnreturnedWork())
        {
            return new(RelayUninstallOutcome.KeptNewerCopy, 0,
                "La copie n'a pas été effacée : ce PC garde du travail enregistré pendant une coupure d'internet qui n'est "
                + "jamais arrivé dans le cloud. Contactez la personne qui a installé votre logiciel.");
        }

        var files = state.ErasedAtUtc is null ? await _erase(cancellationToken) : 0;

        for (var attempt = 1; attempt <= EraseReportAttempts; attempt++)
        {
            var reported = await _cloud.ReportErasedAsync(cancellationToken);
            if (reported.Status is RelayCallStatus.Ok or RelayCallStatus.Released)
            {
                _state.Save(_state.Load() with { ErasureReported = true });
                return new(RelayUninstallOutcome.RecordedAndErased, files,
                    "Copie du cabinet effacée de ce PC, et le cloud l'a noté.");
            }

            if (attempt < EraseReportAttempts)
            {
                await _delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return new(RelayUninstallOutcome.ErasedButNotReported, files,
            "La copie du cabinet est effacée de ce PC, mais le cloud n'a pas pu le noter.");
    }
}
