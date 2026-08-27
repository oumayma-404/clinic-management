using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>What <c>platform-account create</c> has to print, and nothing the caller could get any other way.</summary>
/// <param name="Account">The created (or re-secreted) account.</param>
/// <param name="TemporaryPassword">The one-time password, null when the operation did not set one.</param>
/// <param name="EnrolmentSecret">The base32 TOTP secret, in the form an authenticator accepts. Null when unchanged.</param>
public record PlatformAccountProvisioned(
    PlatformAccount Account,
    string? TemporaryPassword,
    string? EnrolmentSecret);

/// <summary>
/// The four console-account operations, shared by the bootstrap verb's four switches — create, deactivate, reset
/// the second factor and reset the password (AC-8.1, AC-8.2, AC-8.5).
///
/// <para><b>It sits in Application rather than in the verb</b> for the same reason
/// <c>LocalClinicProvisioning</c> does: the verb has no mediator and no HTTP context, and this is the layer the
/// unit tests can reach. <b>There is deliberately no MediatR command over it</b> — a request-reachable path to
/// creating a console account is precisely what AC-8.5 forbids, and a handler nobody may call is one attribute
/// away from being callable.</para>
///
/// <para>⚠️ <b>Every operation returns the secret exactly once, to a console the operator is looking at.</b>
/// Nothing persists it in readable form: the account stores it encrypted and the verb prints it. If the operator
/// loses the printout before enrolling, the answer is to run <c>--reset-totp</c> again, not to recover it.</para>
/// </summary>
public static class PlatformAccountProvisioning
{
    /// <summary>
    /// Creates a console account, minting a one-time password and an enrolment secret.
    ///
    /// <para>Refuses an address that already has an account: two rows for one address is a state the unique index
    /// would reject anyway, and the operator's next question is « which one am I signing into? ».</para>
    /// </summary>
    public static async Task<Result<PlatformAccountProvisioned>> CreateAsync(
        string email,
        string fullName,
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        ITotpService totp,
        IPlatformSecretProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        var normalised = EmailNormalization.Normalize(email);

        if (await accounts.GetByEmailAsync(normalised, cancellationToken) is not null)
        {
            return Result<PlatformAccountProvisioned>.Failure(
                $"Un compte console existe déjà pour {normalised}.");
        }

        var temporaryPassword = auth.GenerateTemporaryPassword();
        var account = PlatformAccount.Create(normalised, fullName, auth.HashPassword(temporaryPassword));

        var secret = totp.GenerateSecret();
        account.IssueTotpSecret(protector.Protect(secret));

        await accounts.AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PlatformAccountProvisioned>.Success(
            new PlatformAccountProvisioned(account, temporaryPassword, secret));
    }

    /// <summary>
    /// Deactivates an account. Its live sessions die on their <b>next</b> request, because
    /// <see cref="PlatformAccount.Deactivate"/> bumps the token version that
    /// <c>PlatformAccountStateMiddleware</c> compares (AC-1.6).
    /// </summary>
    public static async Task<Result<PlatformAccountProvisioned>> DeactivateAsync(
        string email,
        IPlatformAccountRepository accounts,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByEmailAsync(
            EmailNormalization.Normalize(email), cancellationToken);

        if (account is null)
        {
            return Result<PlatformAccountProvisioned>.Failure(
                $"Aucun compte console pour {EmailNormalization.Normalize(email)}.");
        }

        account.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PlatformAccountProvisioned>.Success(
            new PlatformAccountProvisioned(account, null, null));
    }

    /// <summary>
    /// Re-issues the enrolment secret for an account that has lost its authenticator (AC-8.2).
    ///
    /// <para>⚠️ It <b>invalidates the old secret and every recovery code</b> and revokes live sessions — that is
    /// <see cref="PlatformAccount.IssueTotpSecret"/>'s contract, and the reason « re-issue » is not additive:
    /// leaving the previous authenticator working would mean a lost phone stays a valid second factor for ever.</para>
    /// </summary>
    public static async Task<Result<PlatformAccountProvisioned>> ResetTotpAsync(
        string email,
        IPlatformAccountRepository accounts,
        ITotpService totp,
        IPlatformSecretProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByEmailAsync(
            EmailNormalization.Normalize(email), cancellationToken);

        if (account is null)
        {
            return Result<PlatformAccountProvisioned>.Failure(
                $"Aucun compte console pour {EmailNormalization.Normalize(email)}.");
        }

        var secret = totp.GenerateSecret();
        account.IssueTotpSecret(protector.Protect(secret));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PlatformAccountProvisioned>.Success(
            new PlatformAccountProvisioned(account, null, secret));
    }

    /// <summary>
    /// Mints a fresh one-time password for an account whose own is forgotten — the way back for the vendor's own
    /// credential, which had none.
    ///
    /// <para><b>⚠️ Why it has to exist.</b> <c>ChangePlatformPasswordCommand</c> requires the current password, and
    /// the console recovery code proves the second factor rather than the first. So a console account whose password
    /// was forgotten had exactly one remedy: deactivate it and create another — which discards that account's
    /// enrolled authenticator, its recovery codes and its journal identity to fix a forgotten string. The three
    /// operations beside this one covered every credential except the one an operator is most likely to lose.</para>
    ///
    /// <para>⚠️ <b>The second factor is deliberately untouched</b>, the same split the clinic-side console reset
    /// makes: whoever gets this password still needs the authenticator, and re-issuing both in one command would
    /// collapse two independent proofs into one invocation. An operator who has lost both runs both verbs, and
    /// there is a line for each in the journal.</para>
    ///
    /// <para>⚠️ <see cref="PlatformAccount.SetPassword"/> is given <c>mustChangePassword: true</c> and bumps
    /// <c>TokenVersion</c>: the printed credential is a handover token, not a password, and every session opened
    /// under the forgotten one ends. It also clears the lockout, so an operator who locked themselves out guessing
    /// can use the new one at once.</para>
    /// </summary>
    public static async Task<Result<PlatformAccountProvisioned>> ResetPasswordAsync(
        string email,
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByEmailAsync(
            EmailNormalization.Normalize(email), cancellationToken);

        if (account is null)
        {
            return Result<PlatformAccountProvisioned>.Failure(
                $"Aucun compte console pour {EmailNormalization.Normalize(email)}.");
        }

        var temporaryPassword = auth.GenerateTemporaryPassword();
        account.SetPassword(auth.HashPassword(temporaryPassword), mustChangePassword: true);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // No enrolment secret: the authenticator this account already holds keeps working, which is the whole point
        // of resetting only the password.
        return Result<PlatformAccountProvisioned>.Success(
            new PlatformAccountProvisioned(account, temporaryPassword, null));
    }
}
