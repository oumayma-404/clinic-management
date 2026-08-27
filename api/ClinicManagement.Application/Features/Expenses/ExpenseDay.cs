using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Expenses;

/// <summary>
/// The one rule about a dépense's date: it is a <b>day in the cabinet's calendar</b>, and it is required.
///
/// <para>⚠️ <b>Two defects, one seam.</b></para>
///
/// <para><b>The day belonged to whoever typed it, not to the cabinet.</b> The form sent
/// <c>new Date("2026-08-20T00:00:00").toISOString()</c> — midnight in the <i>workstation's</i> zone. From
/// Africa/Tunis that is <c>2026-08-19T23:00Z</c> and files on the 20th ✓; from Asia/Dubai it is
/// <c>2026-08-19T20:00Z</c> and files on the <b>19th</b> ✗. The caisse's read side was fixed for exactly this and
/// the write side was not, so a dépense could be entered on one day and reported on another with nothing on any
/// screen to explain it. The client now sends the bare <c>yyyy-MM-dd</c> the user picked and this resolves it.</para>
///
/// <para><b>And it was not required.</b> <c>DateTime</c> is not nullable, so an omitted key bound to
/// <c>default</c> and PostgreSQL stored <c>-infinity</c> — a row belonging to no caisse period, ever, invisible to
/// every money read and to the person who entered it. The parameter is nullable now and an absent value is a
/// French 400 carrying <c>expense_date_required</c>.</para>
/// </summary>
public static class ExpenseDay
{
    /// <summary>
    /// The furthest ahead a dépense may be dated: the end of next year, in the cabinet's calendar.
    ///
    /// <para>⚠️ Not tidiness — <c>expenseDate: 2099-01-01</c> was accepted and stored. A dépense dated seventy
    /// years out is invisible to every caisse period a practice will ever open, so the money is gone from every
    /// total with no error and nothing to notice. Next year rather than today, because a deliberate advance
    /// payment (an annual insurance premium, a lease) is a real thing a cabinet records.</para>
    /// </summary>
    public static DateTime LatestAllowed => new(ClinicClock.ClinicToday().Year + 1, 12, 31);

    public const string TooFarAhead =
        "La date de la dépense est trop lointaine. Vérifiez l'année saisie.";

    /// <summary>Column length for the catégorie (<c>varchar(100)</c>), stated so the handler can refuse in French.</summary>
    public const int CategoryMaxLength = 100;

    public const string CategoryTooLong = "La catégorie ne peut pas dépasser 100 caractères.";

    /// <summary>Column length for the description (<c>varchar(1000)</c>).</summary>
    public const int DescriptionMaxLength = 1000;

    public const string DescriptionTooLong = "La description ne peut pas dépasser 1000 caractères.";

    /// <summary>
    /// An amount that rounds away to nothing at the column's <c>decimal(18,3)</c> scale.
    ///
    /// <para>⚠️ <c>amount: 0.0004</c> passed the <c>&gt; 0</c> check and was stored as <b>0.000</b>: a dépense of
    /// zero dinars, which is not what anybody typed and not something the form can show. The check has to run at
    /// the scale the column keeps, not at the scale the request arrives in.</para>
    /// </summary>
    public const string AmountRoundsToZero =
        "Le montant est trop petit : la plus petite valeur enregistrable est 0,001 DT.";

    /// <summary>The French refusal for a category, description or amount the column cannot hold, or null.</summary>
    public static string? RefuseFields(string? category, string? description, decimal amount)
    {
        if (category?.Trim().Length > CategoryMaxLength)
        {
            return CategoryTooLong;
        }

        if (description?.Trim().Length > DescriptionMaxLength)
        {
            return DescriptionTooLong;
        }

        return decimal.Round(amount, 3, MidpointRounding.AwayFromZero) <= 0 ? AmountRoundsToZero : null;
    }

    /// <summary>The code the client branches on. See <c>ApiErrorCode</c> on the frontend side.</summary>
    public const string RequiredCode = "expense_date_required";

    public const string Required = "Une date est requise pour cette dépense.";

    /// <summary>
    /// The instant to store for a submitted date, or null when none was submitted.
    ///
    /// <para>⚠️ <b>The <c>Kind</c> is the discriminator, and both branches are needed.</b> A bare
    /// <c>"2026-08-20"</c> binds as <c>Unspecified</c> and already <i>is</i> a clinic-local day, so it is taken at
    /// face value — this is the shape the form sends. An instant (<c>Kind.Utc</c>, from an older client, an import
    /// or the lab-order posting) is converted to the cabinet's zone first, because <c>.Date</c> on
    /// <c>2026-08-19T23:00Z</c> is the 19th while the Tunisian day is the 20th — the same off-by-one from the
    /// other direction.</para>
    /// </summary>
    public static DateTime? Resolve(DateTime? submitted)
    {
        if (submitted is not { } value || value == default)
        {
            return null;
        }

        var day = value.Kind == DateTimeKind.Unspecified
            ? value.Date
            : ClinicClock.ToClinicLocal(value).Date;

        return ClinicClock.StartOfLocalDayUtc(day);
    }

    /// <summary>
    /// The French refusal for a submitted date that is beyond <see cref="LatestAllowed"/>, or null. Called with the
    /// RESOLVED day so the ceiling is compared in the cabinet's calendar, not the caller's.
    /// </summary>
    public static string? RefuseDay(DateTime resolvedUtc) =>
        ClinicClock.ToClinicLocal(resolvedUtc).Date > LatestAllowed ? TooFarAhead : null;
}
