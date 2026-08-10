namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What a console account did to a cabinet, as recorded in the console's own access ledger
/// (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <b>One member, deliberately.</b> The plan lists five — the four writes of Parts 4–6 alongside this —
/// and each of those arrives with the write that produces it. It is the decision <c>PlatformPortfolioSort</c>
/// already made in Part 2 for « par date de fin » (progress.md DEV-6): a member nothing can produce is a value the
/// journal can never show and a filter can never match, and the reader has no way to tell « jamais fait » from
/// « pas encore possible ». Part 4 adds <c>GrantedPeriod</c> in the same commit as the grant.</para>
/// </summary>
public enum PlatformAccessAction
{
    /// <summary>
    /// A console account opened one cabinet's detail.
    ///
    /// <para>⚠️ Listing cabinets is deliberately <b>not</b> recorded (AC-3.5): one list read touches every
    /// cabinet, so a row per cabinet per page load would drown every reading anyone actually wants — including
    /// this one.</para>
    /// </summary>
    ViewedClinic = 0
}
