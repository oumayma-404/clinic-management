namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// One of a patient's <b>additional</b> phone numbers — the second line a practice has for them.
///
/// <para><b>Why the primary number is NOT one of these.</b> <see cref="Entities.Patient.PhoneNumber"/> stays
/// exactly where it was: it is what the reminder engine dispatches to, what the duplicate index folds, what
/// the CSV import and export carry, and what ~200 files read. Turning it into « the first row of a list »
/// would have moved all of that at once for no clinical gain — a practice still has one number it calls
/// first. So this is an addition, never a replacement, and « quel numéro appelle-t-on ? » keeps one answer.
/// </para>
///
/// <para>⚠️ <b>A number and nothing else.</b> A « libellé » field (Mobile / Domicile / Époux) was built and
/// withdrawn before it shipped: the ask is a second number, and a second box beside every number is a second
/// thing to fill in on the commonest form in the product. If it comes back it comes back as a column too —
/// a field served by the API and reachable from no screen is the shape
/// <c>features/cnam-ui-withdrawal</c> exists to warn about.</para>
///
/// <para>⚠️ <see cref="E164"/> is non-nullable here, unlike <see cref="PhoneNumber.E164"/>. That column is
/// nullable because it was added to rows that already existed and re-derives for them. This table has no such
/// rows — it is new, every row is written through the constructor below, and the constructor refuses a number
/// no country can parse. So there is no fallback to write, and nothing here restates
/// <c>PersistedE164 ?? ToE164(Value)</c>.</para>
///
/// <para>⚠️ <b>Not a <see cref="ValueObject"/>.</b> EF maps this as an owned collection with its own key, and
/// the order the practice entered them in is part of the record.</para>
/// </summary>
public sealed class PatientPhone
{
    /// <summary>The number exactly as a human typed it. ⚠️ Never rewritten — <see cref="E164"/> is the dialable form.</summary>
    public string Value { get; private set; }

    /// <summary>
    /// The dialable form, resolved with the region the writer supplied. Never null: the constructor refuses a
    /// number that has none, so every surface can dial a row of this table without a fallback.
    /// </summary>
    public string E164 { get; private set; }

    /// <summary>The order the practice put them in — the list is read back exactly as it was entered.</summary>
    public int SortOrder { get; private set; }

    private PatientPhone()
    {
        // For EF Core.
        Value = string.Empty;
        E164 = string.Empty;
    }

    /// <param name="value">The number as typed. Stored verbatim.</param>
    /// <param name="region">
    /// The country to read <paramref name="value"/> as when it carries no country code — the form's country
    /// selector, exactly as for <see cref="PhoneNumber"/>. ⚠️ This is the only chance to record it.
    /// </param>
    /// <param name="sortOrder">Position in the patient's list.</param>
    /// <exception cref="ArgumentException">
    /// When <paramref name="value"/> is blank, or when no country can parse it. <b>Callers validate first</b>
    /// with <see cref="PhoneNumber.IsDeliverable"/> and return the French refusal — the same two-step the
    /// primary number already follows, so a bad number produces a sentence about the number rather than a 500.
    /// </exception>
    public PatientPhone(string value, string? region = null, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Phone number cannot be empty", nameof(value));
        }

        // The ONE normalisation rule, the same one the primary number and the reminder engine go through.
        E164 = PhoneNumber.ToE164(value, region)
            ?? throw new ArgumentException($"Phone number '{value}' is not dialable", nameof(value));

        Value = value.Trim();
        SortOrder = sortOrder;
    }
}
