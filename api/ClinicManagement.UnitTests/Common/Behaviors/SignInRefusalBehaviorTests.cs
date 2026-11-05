using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Platform.Auth;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Behaviors;

/// <summary>
/// sign-in-alerts: a refusal at a credential door reaches the recorder with the address typed — never text that is not
/// one; the steps of an ordinary sign-in, a success and every other request pass through untouched — and recording can
/// never cost the caller their answer.
/// </summary>
public class SignInRefusalBehaviorTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPlatformAccountRepository> _consoleAccounts = new();
    private readonly Mock<ISignInRefusalRecorder> _recorder = new();
    private readonly List<SignInRefusal> _recorded = [];

    public SignInRefusalBehaviorTests()
    {
        _recorder.Setup(r => r.Record(It.IsAny<SignInRefusal>())).Callback<SignInRefusal>(_recorded.Add);
    }

    [Fact]
    public async Task An_Address_With_No_Account_Is_Recorded_In_Full_As_Unknown()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await RunAsync(Login(" Karim.Benali@Gmail.com "), Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials));

        var refusal = Assert.Single(_recorded);
        Assert.Equal(SignInDoor.ClinicSignIn, refusal.Door);
        Assert.Equal(SignInRefusalReason.UnknownEmail, refusal.Reason);
        Assert.Equal("karim.benali@gmail.com", refusal.ShownEmail);
        Assert.Equal("karim.benali@gmail.com", refusal.EmailKey);
        Assert.Null(refusal.AccountLabel);
    }

    [Fact]
    public async Task A_Wrong_Password_On_A_Real_Account_Names_The_Account_And_The_Address()
    {
        _users.Setup(u => u.GetByEmailAsync("sec@clinic.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(ClinicId, User.RoleSecretary, "sec@clinic.com", "HASH", "Sam"));

        await RunAsync(Login("sec@clinic.com"), Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials));

        var refusal = Assert.Single(_recorded);
        Assert.Equal(SignInRefusalReason.WrongSecret, refusal.Reason);
        Assert.Equal("secrétaire", refusal.AccountLabel);
        Assert.Equal("sec@clinic.com", refusal.ShownEmail);
    }

    // The behavior is the only producer of ShownEmail: a password typed into the e-mail box must go through the rule.
    [Fact]
    public async Task A_Password_Typed_Into_The_Email_Box_Is_Not_Written_Down()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await RunAsync(Login("Karim@Dentiste2024"), Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials));

        Assert.Equal(SignInRefusalRules.NotAnAddress, Assert.Single(_recorded).ShownEmail);
    }

    [Fact]
    public async Task A_Locked_Account_Is_Recorded_As_Locked()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(ClinicId, User.RoleDoctor, "doc@clinic.com", "HASH", "Doc"));

        await RunAsync(Login("doc@clinic.com"), Refused<LoginResultDto>(ClinicAuthRefusals.TooManyAttempts));

        Assert.Equal(SignInRefusalReason.Locked, Assert.Single(_recorded).Reason);
    }

    // A switched-off colleague with the right password is not the same thing as somebody waiting for their admin.
    [Fact]
    public async Task A_Deactivated_Account_And_A_Pending_One_Are_Told_Apart()
    {
        var former = User.CreateLocalUser(ClinicId, User.RoleSecretary, "former@clinic.com", "HASH", "Former");
        former.RecordSuccessfulLogin();
        former.Deactivate();
        var waiting = User.CreateSelfRegistered(ClinicId, User.RoleSecretary, "new@clinic.com", "HASH", "New");
        _users.Setup(u => u.GetByEmailAsync("former@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(former);
        _users.Setup(u => u.GetByEmailAsync("new@clinic.com", It.IsAny<CancellationToken>())).ReturnsAsync(waiting);

        await RunAsync(Login("former@clinic.com"), Refused<LoginResultDto>(ClinicAuthRefusals.AccountDisabled));
        await RunAsync(Login("new@clinic.com"), Refused<LoginResultDto>(ClinicAuthRefusals.AccountDisabled));

        Assert.Equal(
            new[] { SignInRefusalReason.Deactivated, SignInRefusalReason.PendingActivation },
            _recorded.Select(r => r.Reason));
    }

    [Fact]
    public async Task Blank_Fields_Are_Recorded_Without_Looking_Anybody_Up()
    {
        await RunAsync(Login("   "), Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials));

        var refusal = Assert.Single(_recorded);
        Assert.Equal(SignInRefusalReason.EmptyFields, refusal.Reason);
        Assert.Equal(SignInRefusalRules.Blank, refusal.ShownEmail);
        _users.Verify(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // « Code requis » is the first half of every ordinary sign-in: recording it would alert on every user, every day.
    [Theory]
    [InlineData(ClinicAuthRefusals.TotpRequired)]
    [InlineData(ClinicAuthRefusals.TotpEnrolmentRequired)]
    [InlineData(ClinicAuthRefusals.RetiredRelayAdminsOnly)]
    public async Task The_Steps_Of_A_Sign_In_Are_Not_Refusals_And_Cost_No_Lookup(string code)
    {
        await RunAsync(Login("doc@clinic.com"), Refused<LoginResultDto>(code));

        Assert.Empty(_recorded);
        _users.Verify(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Successful_Sign_In_Records_Nothing()
    {
        await RunAsync(Login("doc@clinic.com"), Result<LoginResultDto>.Success(new LoginResultDto()));

        Assert.Empty(_recorded);
    }

    [Fact]
    public async Task A_Request_That_Is_Not_A_Credential_Door_Passes_Through()
    {
        await RunAsync(new NotADoor(), Result.Failure("Identifiants invalides.", ClinicAuthRefusals.InvalidCredentials));

        Assert.Empty(_recorded);
        _users.Verify(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Console_Door_Looks_In_The_Console_Accounts_Only()
    {
        _consoleAccounts.Setup(a => a.GetByEmailAsync("vendor@apexa.tn", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformAccount.Create("vendor@apexa.tn", "Vendor", "HASH"));

        await RunAsync(
            new PlatformLoginCommand("Vendor@Apexa.tn", "wrong", null),
            Refused<PlatformSessionDto>(PlatformAuthRefusals.InvalidCredentials));

        var refusal = Assert.Single(_recorded);
        Assert.Equal(SignInDoor.ConsoleSignIn, refusal.Door);
        Assert.Equal(SignInRefusalReason.WrongSecret, refusal.Reason);
        Assert.Equal(SignInRefusalRules.ConsoleAccountLabel, refusal.AccountLabel);
        _users.Verify(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Recorder_That_Throws_Still_Hands_Back_The_Same_Refusal()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _recorder.Setup(r => r.Record(It.IsAny<SignInRefusal>())).Throws(new InvalidOperationException("boom"));
        var refused = Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials);

        var response = await RunAsync(Login("a@b.tn"), refused);

        Assert.Same(refused, response);
    }

    [Fact]
    public async Task A_Lookup_That_Throws_Still_Hands_Back_The_Same_Refusal()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var refused = Refused<LoginResultDto>(ClinicAuthRefusals.InvalidCredentials);

        var response = await RunAsync(Login("a@b.tn"), refused);

        Assert.Same(refused, response);
        Assert.Empty(_recorded);
    }

    private Task<TResponse> RunAsync<TRequest, TResponse>(TRequest request, TResponse response)
        where TRequest : IRequest<TResponse>
    {
        var behavior = new SignInRefusalBehavior<TRequest, TResponse>(
            _users.Object,
            _consoleAccounts.Object,
            _recorder.Object,
            NullLogger<SignInRefusalBehavior<TRequest, TResponse>>.Instance);

        return behavior.Handle(request, _ => Task.FromResult(response), CancellationToken.None);
    }

    private static LoginCommand Login(string email) => new() { Email = email, Password = "secret" };

    private static Result<T> Refused<T>(string code) => Result<T>.Failure("refusé", code);

    private sealed class NotADoor : IRequest<Result>
    {
    }
}
