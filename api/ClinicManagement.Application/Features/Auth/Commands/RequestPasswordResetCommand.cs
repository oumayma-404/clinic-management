using System.Security.Cryptography;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// « J'ai oublié mon mot de passe » — writes a pending <see cref="PasswordResetRequest"/> and mails a single-use
/// link. Changes <b>nothing</b> about the account: the password is replaced only by
/// <see cref="CompletePasswordResetCommand"/>, by whoever opens that link.
///
/// <para><b>Why this door exists.</b> Every other way back into a local account needs a second human — an
/// administrator, the vendor's console, or somebody with shell access — and the recovery codes are not an
/// exception: <c>RedeemRecoveryCodeCommand</c> verifies the <i>password</i> before it will spend one, deliberately,
/// so a stranger cannot burn an account's codes by guessing. That left the most ordinary failure in the product,
/// one person and one forgotten password, with no path its owner could take alone.</para>
///
/// <para><b>Why it lives under <c>Features/Auth/Commands</c>.</b> <c>RealtimeBroadcastBehavior</c> derives its
/// resource key from the namespace, and <c>Auth</c> is on its excluded list. The same handler under <c>Users</c>
/// would broadcast <c>users</c> to a SignalR group — announcing to a clinic's connected clients that one of their
/// colleagues has forgotten their password.</para>
///
/// <para>⚠️ <b>The second factor is untouched here and in the completion step.</b> Controlling the mailbox is
/// enough to replace a password precisely <i>because</i> TOTP still gates the next sign-in; a reset that also
/// cleared the factor would turn read access to one inbox into full account takeover. The way back for a lost
/// authenticator is a different one, and it is deliberately harder.</para>
/// </summary>
public class RequestPasswordResetCommand : IRequest<Result<PasswordResetRequestedDto>>
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// What the caller is told. One neutral sentence and nothing else — no id, no expiry of a specific row, no echo of
/// the address — so the response is byte-identical whether the address is a live account, a disabled one, or has
/// never existed.
/// </summary>
public class PasswordResetRequestedDto
{
    public string Message { get; set; } = string.Empty;
}

public class RequestPasswordResetCommandHandler
    : IRequestHandler<RequestPasswordResetCommand, Result<PasswordResetRequestedDto>>
{
    /// <summary>
    /// The one sentence every outcome of a well-formed request gets. It says « si un compte existe » rather than
    /// « nous avons envoyé » on purpose: it has to stay true in the branches where nothing was sent, and a
    /// sentence that is a lie in one branch is a sentence somebody will later "fix" into an enumeration oracle.
    /// </summary>
    private const string NeutralAcknowledgement =
        "Si un compte existe pour cette adresse, un lien de réinitialisation vient de lui être envoyé. "
        + "Le lien est valable 1 heure.";

    /// <summary>
    /// Refusal for a deployment that cannot complete a reset at all. It names nothing internal: an
    /// unauthenticated caller learning this backend's configuration schema, and that its mail transport is down,
    /// is a fingerprint and an abuse window. The operator's version of the same fact goes to the log.
    /// </summary>
    private const string ServiceUnavailable =
        "La réinitialisation du mot de passe est momentanément indisponible. Réessayez plus tard.";

    /// <summary>Lets the controller answer 503 rather than 400 — nothing about the request was malformed.</summary>
    public const string UnavailableCode = "password_reset_unavailable";

    /// <summary>How long a spent row is kept before the opportunistic purge drops it.</summary>
    private static readonly TimeSpan ConsumedRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// The minimum gap between two reset emails to one account. Without it a caller can re-send on every request
    /// the rate limiter allows — and that limiter partitions on the <i>submitted</i> address, which the caller
    /// chooses, so it is no bound at all on mail aimed at one victim's mailbox. Two minutes, matching the signup
    /// path's own cooldown.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    private readonly IPasswordResetRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<RequestPasswordResetCommandHandler> _logger;

    public RequestPasswordResetCommandHandler(
        IPasswordResetRequestRepository requests,
        IUserRepository users,
        ITransactionalEmailSender emailSender,
        IUnitOfWork unitOfWork,
        IPublicAppUrlProvider appUrl,
        ILogger<RequestPasswordResetCommandHandler> logger)
    {
        _requests = requests;
        _users = users;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _appUrl = appUrl;
        _logger = logger;
    }

    public async Task<Result<PasswordResetRequestedDto>> Handle(
        RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Result<PasswordResetRequestedDto>.Failure("L'email est requis.");
            }

            // Shared with the signup door — see `EmailAddressInput` for the display-name form this refuses and
            // why storing the raw string was exploitable.
            var refusal = EmailAddressInput.Read(request.Email, out var canonicalEmail);
            if (refusal != null)
            {
                return Result<PasswordResetRequestedDto>.Failure(refusal);
            }

            // Before anything is written, and both are equally fatal and silent afterwards: no mail host sends
            // nothing, and no FrontendUrl points every link at the recipient's own machine. This is the refusal
            // for a transport that is *unconfigured* — a configured one having a bad minute must stay silent, for
            // the reason `SendResetAsync` states.
            if (!_emailSender.IsConfigured || !_appUrl.IsConfigured)
            {
                _logger.LogError(
                    "Password reset is unreachable: mail configured={MailConfigured}, "
                    + "FrontendUrl configured={UrlConfigured}.",
                    _emailSender.IsConfigured, _appUrl.IsConfigured);

                return Result<PasswordResetRequestedDto>.Failure(ServiceUnavailable, UnavailableCode);
            }

            var email = PasswordResetRequest.NormalizeEmail(canonicalEmail);
            var nowUtc = DateTime.UtcNow;

            // The table only grows when somebody asks for a reset, so this is the write that owes the trim. It
            // commits on its own and is bounded — see the repository for the 409 that staging it here produced.
            var purged = await _requests.PurgeSpentAsync(nowUtc, ConsumedRetention, cancellationToken);
            if (purged > 0)
            {
                _logger.LogInformation("Purged {Count} spent password-reset request(s).", purged);
            }

            var user = await _users.GetByEmailAsync(email, cancellationToken);

            // ⚠️ Every ineligible branch answers exactly as the happy path does, and each is a real case rather
            // than defensive padding: no such address, an Auth0-backed account with no password to replace, an
            // account awaiting an administrator's activation, and a deactivated one. Telling them apart is the
            // whole of account enumeration.
            //
            // ⚠️ What this does NOT claim: the branches are not indistinguishable by *stopwatch*. A live account
            // writes a row and waits on an SMTP round-trip; an unknown address returns here. Closing that would
            // mean either faking a send or deferring the real one to a queue, and this product has no queue that
            // is not clinic-keyed (see `ITransactionalEmailSender`'s own remarks on why). The bound on probing is
            // therefore the `AnonymousAuthPolicy` rate limiter, not this shape — said plainly so nobody later
            // reads the identical sentences as a guarantee they are not.
            if (user is null || !user.IsLocalAccount() || !user.IsActive)
            {
                return Acknowledged();
            }

            var existing = await _requests.GetByUserIdAsync(user.Id, cancellationToken);

            return existing is not null && existing.IsUsable(nowUtc)
                ? await RotateAsync(existing, user, nowUtc, cancellationToken)
                : await IssueAsync(existing, user, email, nowUtc, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two requests for one account in the same tick: the loser losing is correct, but a distinguishable
            // failure would re-open the enumeration oracle — and the winner's email really was sent.
            _logger.LogInformation(ex, "Concurrent password-reset request for one account; the first won.");
            return Acknowledged();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Password-reset request failed.");
            return Result<PasswordResetRequestedDto>.Failure(
                "La demande n'a pas pu aboutir. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// A second request while the previous link is <b>still live</b>.
    ///
    /// <para>⚠️ <b>The row is rotated only after the send succeeded</b>, the reverse of <see cref="IssueAsync"/>'s
    /// ordering — and for a reason that only applies here: a failure has something to fall back on, the link
    /// already sitting in the person's inbox, and rotating first would kill it to replace it with one that never
    /// arrived. The signup path arrived at the same split.</para>
    /// </summary>
    private async Task<Result<PasswordResetRequestedDto>> RotateAsync(
        PasswordResetRequest existing, User user, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (nowUtc - existing.LastIssuedAtUtc() < ResendCooldown)
        {
            return Acknowledged();
        }

        var rawToken = GenerateToken();
        if (!await SendResetAsync(existing, user, rawToken, cancellationToken))
        {
            return Acknowledged();
        }

        existing.Rearm(PasswordResetRequest.HashToken(rawToken), nowUtc);
        existing.RecordEmailSendAttempt();
        await _requests.UpdateAsync(existing, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Acknowledged();
    }

    /// <summary>
    /// A first request, or one replacing a row that can no longer do anything (expired or already spent). The row
    /// is committed <b>before</b> the send, so a mail failure leaves a row the next request re-arms rather than a
    /// live link with nothing behind it.
    /// </summary>
    private async Task<Result<PasswordResetRequestedDto>> IssueAsync(
        PasswordResetRequest? existing,
        User user,
        string email,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawToken = GenerateToken();
        var tokenHash = PasswordResetRequest.HashToken(rawToken);

        PasswordResetRequest row;
        if (existing is null)
        {
            row = PasswordResetRequest.Create(user.Id, email, tokenHash, nowUtc);
            row.RecordEmailSendAttempt();
            await _requests.AddAsync(row, cancellationToken);
        }
        else
        {
            row = existing;
            row.Rearm(tokenHash, nowUtc);
            row.RecordEmailSendAttempt();
            await _requests.UpdateAsync(row, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await SendResetAsync(row, user, rawToken, cancellationToken);

        return Acknowledged();
    }

    /// <summary>
    /// ⚠️ A failed send is reported to the <b>log</b> and never to the caller. A refusal here would be a clean
    /// enumeration oracle: during any mail outage a real account would get « l'e-mail n'a pas pu être envoyé »
    /// while an unknown address got the neutral sentence, with no timing needed to tell them apart. The loud
    /// refusal is for a transport that is <i>unconfigured</i>, checked before anything is written.
    /// </summary>
    private async Task<bool> SendResetAsync(
        PasswordResetRequest row, User user, string rawToken, CancellationToken cancellationToken)
    {
        var sent = await _emailSender.SendAsync(
            row.Email, EmailSubject, BuildEmailBody(user.FullName, rawToken), cancellationToken);

        if (sent.Outcome == TransactionalEmailOutcome.Sent)
        {
            return true;
        }

        _logger.LogWarning(
            "Password-reset email could not be sent for request {RequestId} (attempt {Attempts}): {Outcome} {Reason}",
            row.Id, row.EmailSendAttempts, sent.Outcome, sent.Error);

        return false;
    }

    private static Result<PasswordResetRequestedDto> Acknowledged() =>
        Result<PasswordResetRequestedDto>.Success(
            new PasswordResetRequestedDto { Message = NeutralAcknowledgement });

    /// <summary>PostgreSQL 23505 — matched on the type name, following <c>UnitOfWork.IsExclusionViolation</c>.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner.GetType().FullName != "Npgsql.PostgresException")
            {
                continue;
            }

            if (inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 32 bytes from the OS CSPRNG, URL-safe. <c>Random</c> is not an option: this is the only thing between an
    /// inbox and the right to replace an account's password.
    /// </summary>
    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string EmailSubject = "Réinitialisation de votre mot de passe";

    private string BuildEmailBody(string? fullName, string rawToken) =>
        $"""
        {EmailGreeting.For(fullName)}

        Une réinitialisation de mot de passe vient d'être demandée pour votre compte. Pour choisir un
        nouveau mot de passe, ouvrez le lien ci-dessous :

        {BuildResetLink(rawToken)}

        Ce lien est valable 1 heure et ne peut servir qu'une seule fois. Votre mot de passe actuel reste
        valable jusqu'à ce que vous en choisissiez un nouveau.

        Si vous n'êtes pas à l'origine de cette demande, ignorez simplement ce message : rien n'a été
        modifié. Votre code de vérification à six chiffres reste par ailleurs exigé à la connexion.
        """;

    /// <summary>
    /// Built from <see cref="IPublicAppUrlProvider"/>, i.e. from <c>FrontendUrl</c> — so no host is compiled in and
    /// one deployment's link never points at another's front door.
    ///
    /// <para>⚠️ The token rides in the <b>fragment</b>, not the query string, exactly as the signup link does. A
    /// fragment is never sent to the server, so this live single-use credential stays out of the reverse proxy's
    /// access log, out of every intermediate hop and out of the Next server's own request log — all of which
    /// outlive by a long way the hour the token is bounded by.</para>
    /// </summary>
    private string BuildResetLink(string rawToken) =>
        $"{_appUrl.BaseUrl}/reinitialiser-mot-de-passe#token={Uri.EscapeDataString(rawToken)}";
}
