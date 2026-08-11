namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What a console account did to a cabinet, as recorded in the console's own access ledger
/// (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <b>Each member arrives with the write that produces it</b>, never ahead of it — the decision
/// <c>PlatformPortfolioSort</c> made in Part 2 for « par date de fin » (progress.md DEV-6): a member nothing can
/// produce is a value the journal can never show and a filter can never match, and the reader has no way to tell
/// « jamais fait » from « pas encore possible ». Parts 5 and 6 add <c>CancelledPeriod</c>, <c>Suspended</c> and
/// <c>Unsuspended</c> on the same terms.</para>
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
    ViewedClinic = 0,

    /// <summary>
    /// A console account recorded a received payment and extended the cabinet's entitlement (Part 4, AC-4.7).
    /// </summary>
    GrantedPeriod = 1
}
