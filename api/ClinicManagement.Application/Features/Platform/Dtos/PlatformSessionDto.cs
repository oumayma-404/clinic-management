namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// A console session, as the spec's 200 on sign-in states it.
///
/// <para><see cref="RecoveryCodesRemaining"/> is populated only on the recovery path, where the spec asks for it:
/// a code was just spent, and the number left is the only warning an operator gets before the last one goes.
/// Null on an ordinary sign-in rather than the real count, deliberately — reporting it on every login would put a
/// standing invitation to use recovery codes on the screen that does not need them.</para>
/// </summary>
public record PlatformSessionDto(string Token, DateTime ExpiresAt, int? RecoveryCodesRemaining = null);

/// <summary>
/// The enrolment response — <b>the only time the recovery codes exist outside the operator's own notes</b>
/// (AC-1.3a). They are hashed on the way into the database, so this response cannot be reproduced.
/// </summary>
public record PlatformEnrolmentDto(IReadOnlyList<string> RecoveryCodes);
