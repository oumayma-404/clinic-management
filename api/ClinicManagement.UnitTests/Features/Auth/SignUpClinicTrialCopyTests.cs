using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// AC-1.3, the e-mail half: « the signup form <b>and the verification e-mail</b> both state the free trial before
/// the visitor submits anything ».
///
/// <para><b>Why the number is asserted against the policy rather than against « 30 ».</b> The trial's length is
/// operator configuration (<c>Subscription:TrialDays</c>) and <see cref="ISubscriptionPolicy.TrialDays"/> is its
/// one authority. A literal in the e-mail body would be a second one — a promise no code keeps the day an operator
/// changes the setting — and this product's own landing copy has already drifted from it once
/// (« Essai accompagné — 2 semaines »). So the load-bearing test here is
/// <see cref="The_email_quotes_the_configured_trial_length_not_a_literal"/>: the two default-value rows below
/// would pass just as happily on a hardcoded sentence.</para>
/// </summary>
public class SignUpClinicTrialCopyTests
{
    private const string Recipient = "dr.benali@cabinet.tn";

    [Fact]
    public async Task The_verification_email_states_the_free_trial_and_that_no_card_is_needed()
    {
        var body = await CapturedEmailBody(trialDays: 30);

        Assert.Contains("30 jours d'essai gratuit", body);
        Assert.Contains("sans carte bancaire", body);
    }

    /// <summary>
    /// ⚠️ The one row that fails on a hardcoded sentence. 14 is a value no default produces, so a body still
    /// reading « 30 jours » here is exactly the second-authority defect this test exists for.
    /// </summary>
    [Fact]
    public async Task The_email_quotes_the_configured_trial_length_not_a_literal()
    {
        var body = await CapturedEmailBody(trialDays: 14);

        Assert.Contains("14 jours d'essai gratuit", body);
        Assert.DoesNotContain("30 jours", body);
    }

    /// <summary>
    /// A deployment where nothing expires must not be made to advertise a trial. Unreachable through the HTTP door
    /// today — public signup and enforced subscriptions are both <c>HostedMultiTenant</c>-only — but the handler
    /// must not be the thing holding that true, since the two capabilities are independent by construction.
    /// </summary>
    [Fact]
    public async Task No_trial_is_promised_where_subscriptions_are_not_enforced()
    {
        var body = await CapturedEmailBody(trialDays: 30, requiresSubscription: false);

        Assert.DoesNotContain("essai gratuit", body);
        Assert.DoesNotContain("carte bancaire", body);
        // The rest of the e-mail is unaffected — this adds a sentence, it does not rewrite the message.
        Assert.Contains("/signup/verifier#token=", body);
    }

    /// <summary>
    /// Runs a first-submission signup end to end against mocks and returns the body actually handed to the sender
    /// — not a re-render of it, so a sentence composed but never sent would fail these.
    /// </summary>
    private static async Task<string> CapturedEmailBody(int trialDays, bool requiresSubscription = true)
    {
        var signups = new Mock<IClinicSignupRepository>();
        signups.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicSignup?)null);
        signups.Setup(r => r.PurgeSpentAsync(
                It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var localAuth = new Mock<ILocalAuthService>();
        localAuth.Setup(s => s.HashPassword(It.IsAny<string>())).Returns("hashed");

        var captured = string.Empty;
        var sender = new Mock<ITransactionalEmailSender>();
        sender.SetupGet(s => s.IsConfigured).Returns(true);
        sender.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, body, _) => captured = body)
            .ReturnsAsync(TransactionalEmailResult.Sent);

        var appUrl = new Mock<IPublicAppUrlProvider>();
        appUrl.SetupGet(u => u.IsConfigured).Returns(true);
        appUrl.SetupGet(u => u.BaseUrl).Returns("https://cabinet.example.tn");

        var policy = new Mock<ISubscriptionPolicy>();
        policy.SetupGet(p => p.RequiresSubscription).Returns(requiresSubscription);
        policy.SetupGet(p => p.TrialDays).Returns(trialDays);

        var handler = new SignUpClinicCommandHandler(
            signups.Object,
            users.Object,
            localAuth.Object,
            sender.Object,
            new Mock<IUnitOfWork>().Object,
            appUrl.Object,
            policy.Object,
            new Mock<ILogger<SignUpClinicCommandHandler>>().Object);

        var result = await handler.Handle(
            new SignUpClinicCommand
            {
                ClinicName = "Cabinet Benali",
                FullName = "Dr Benali",
                Email = Recipient,
                Password = "correct horse battery staple"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(string.Empty, captured);
        return captured;
    }
}
