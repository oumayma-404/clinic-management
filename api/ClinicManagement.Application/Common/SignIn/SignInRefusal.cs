namespace ClinicManagement.Application.Common.SignIn;

/// <summary>The six places somebody proves who they are before holding a session.</summary>
public enum SignInDoor
{
    ClinicSignIn,
    ClinicSecondFactorSetup,
    ClinicRecoveryCode,
    ConsoleSignIn,
    ConsoleSecondFactorSetup,
    ConsoleRecoveryCode,
}

/// <summary>Why a door said no, as far as the vendor needs to know it.</summary>
public enum SignInRefusalReason
{
    /// <summary>The e-mail or the password was left blank.</summary>
    EmptyFields,

    /// <summary>No account carries this address.</summary>
    UnknownEmail,

    /// <summary>The account exists; the password or the code was wrong.</summary>
    WrongSecret,

    /// <summary>Too many failed attempts: the account refuses this source for now.</summary>
    Locked,

    /// <summary>The password was right and the account is switched off.</summary>
    Deactivated,

    /// <summary>The password was right and the account still waits for its administrator.</summary>
    PendingActivation,
}

/// <summary>
/// One refused attempt. <see cref="ShownEmail"/> is the only form of the address that is ever written down or sent
/// (<see cref="SignInRefusalRules.ShownEmail"/>); <see cref="EmailKey"/> exists to count repeats in memory and goes
/// nowhere else — it may be a password typed into the wrong box.
/// </summary>
public sealed record SignInRefusal(
    SignInDoor Door,
    SignInRefusalReason Reason,
    string ShownEmail,
    string EmailKey,
    string? AccountLabel,
    DateTime OccurredAtUtc);

public enum SignInAlertKind
{
    /// <summary>Any refusal on the console: only the vendor signs in there.</summary>
    ConsoleRefused,

    /// <summary>One existing account refused again and again.</summary>
    AccountUnderAttack,

    /// <summary>One address refused again and again, whatever the account.</summary>
    AddressGuessing,

    /// <summary>The same address with no account, tried again and again — often somebody who cannot get in.</summary>
    UnknownEmailRepeated,

    /// <summary>A switched-off account presented its right password.</summary>
    DeactivatedAccountTried,
}

/// <summary>What the vendor is told: the refusal that tipped it, where it came from and how many came before it.</summary>
public sealed record SignInAlert(SignInAlertKind Kind, SignInRefusal Refusal, string ClientAddress, int Attempts);

/// <summary>An alert a refusal has made due, before its quiet period is checked.</summary>
public sealed record DueSignInAlert(SignInAlertKind Kind, string QuietKey, TimeSpan Quiet, int Attempts);
