using System;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// <c>clinic-pc-copy</c> D23 — the one form state this window holds across a server switch (AC-3.2). The page hands it
/// over as it is typed (<c>carryDraft</c>) and the next clinic page takes it once (<c>takeCarriedDraft</c>).
///
/// <para>⚠️ <b>Memory only, never disk</b>: a fiche is a patient's clinical record, and the window's lifetime is exactly
/// the lifetime a carried form needs.</para>
/// </summary>
public sealed class CarriedDraftSlot
{
    /// <summary>The page caps a draft at this size too; anything larger is not a form somebody typed.</summary>
    public const int MaxChars = 2_000_000;

    /// <summary>Older than this, it is not the form somebody was in the middle of.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    private string? _draft;
    private DateTime _at;

    /// <summary>Replaces what is held; empty forgets it, and so does an oversized one (an older draft is worse than none).</summary>
    public void Carry(string? draft, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(draft) || draft.Length > MaxChars)
        {
            _draft = null;
            return;
        }

        _draft = draft;
        _at = nowUtc;
    }

    /// <summary>Hands the draft over once and forgets it; null when there is none or it is too old.</summary>
    public string? Take(DateTime nowUtc)
    {
        var draft = _draft;
        _draft = null;
        return draft is not null && nowUtc - _at <= MaxAge ? draft : null;
    }
}
