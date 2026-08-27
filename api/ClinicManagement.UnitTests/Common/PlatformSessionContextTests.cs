using System.Security.Claims;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Reading the acting console account off a request — the real <see cref="PlatformSessionContext"/>, not a mock of
/// its interface.
///
/// <para><b>⚠️ Why this file had to exist.</b> Every other test in the suite mocks
/// <see cref="IPlatformSessionContext"/>, so nothing exercised the claim lookup itself — and it was wrong.
/// <c>GetEmail()</c> asked for the short claim name <c>email</c>, but the JWT bearer handler's inbound claim
/// mapping is on (nothing sets <c>MapInboundClaims = false</c>), so the token's <c>email</c> arrives as the long
/// <see cref="ClaimTypes.Email"/> URI. The lookup found nothing, <c>PlatformAccessEntry</c> stores
/// <c>GetEmail() ?? string.Empty</c>, and so <b>every row of the console's access ledger carried a blank
/// address</b> — 34 of them on the development database, across all five action kinds the console had ever
/// performed — while the column's docstring promised the opposite. Nothing threw and nothing looked wrong.</para>
///
/// <para>⚠️ <b>The fixtures use the MAPPED spelling, which is the point.</b> A test that builds a principal with a
/// short <c>email</c> claim asserts an arrangement production never produces — the same trap Part 7 recorded for
/// <c>PlatformAccountStateMiddleware</c>, whose tests passed for the whole life of the feature by setting
/// <c>context.User</c> by hand. <see cref="Both_Claim_Spellings_Are_Read"/> covers the short form too, so a future
/// change to <c>MapInboundClaims</c> cannot break this in the other direction.</para>
/// </summary>
public class PlatformSessionContextTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string Email = "ops@editeur.tn";

    /// <summary>
    /// A console principal as the JWT handler leaves it: <c>sub</c> mapped to <see cref="ClaimTypes.NameIdentifier"/>
    /// and <c>email</c> mapped to <see cref="ClaimTypes.Email"/>, plus the token-kind claim only the console's own
    /// issuer emits.
    /// </summary>
    private static PlatformSessionContext Context(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Bearer");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext)
            .Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        return new PlatformSessionContext(accessor.Object);
    }

    private static Claim[] ConsoleClaims(bool mapped = true) =>
    [
        new(ClaimTypes.NameIdentifier, AccountId.ToString()),
        new(mapped ? ClaimTypes.Email : "email", Email),
        new(IPlatformSessionContext.TokenKindClaim, IPlatformSessionContext.PlatformTokenKind),
    ];

    /// <summary>
    /// The regression. ⚠️ <b>The address is what makes a ledger row readable years later</b> — after the account
    /// that acted has been deactivated or renamed — which is the entire reason it is denormalised onto the row
    /// rather than joined. A blank one turns « qui a fait cela ? » into a GUID.
    /// </summary>
    [Fact]
    public void The_Mapped_Email_Claim_Is_Read()
    {
        Assert.Equal(Email, Context(ConsoleClaims()).GetEmail());
    }

    // Both spellings, so a later `MapInboundClaims = false` cannot silently blank the column from the other side.
    [Fact]
    public void Both_Claim_Spellings_Are_Read()
    {
        Assert.Equal(Email, Context(ConsoleClaims(mapped: true)).GetEmail());
        Assert.Equal(Email, Context(ConsoleClaims(mapped: false)).GetEmail());
    }

    // The account id, on the same principal — it always worked, and this pins it beside the half that did not.
    [Fact]
    public void The_Account_Id_Is_Read_From_The_Same_Principal()
    {
        Assert.Equal(AccountId, Context(ConsoleClaims()).GetAccountId());
    }

    /// <summary>
    /// ⚠️ A <b>clinic</b> token resolves to nothing here, whatever it carries. The gate is the token-kind claim
    /// only the console's issuer emits — so a clinic session's address can never be recorded as a vendor's, and
    /// <c>AuditActorProvider</c> falls through to the clinic path.
    /// </summary>
    [Fact]
    public void A_Clinic_Principal_Is_Not_A_Console_Account()
    {
        var context = Context(
            new Claim(ClaimTypes.NameIdentifier, "local|3f2504e0-4f89-11d3-9a0c-0305e82c3301"),
            new Claim(ClaimTypes.Email, "dentiste@cabinet.tn"));

        Assert.Null(context.GetEmail());
        Assert.Null(context.GetAccountId());
    }

    // An unauthenticated request is nobody, not a half-resolved somebody.
    [Fact]
    public void An_Unauthenticated_Request_Resolves_To_Nothing()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());
        var context = new PlatformSessionContext(accessor.Object);

        Assert.Null(context.GetEmail());
        Assert.Null(context.GetAccountId());
    }
}
