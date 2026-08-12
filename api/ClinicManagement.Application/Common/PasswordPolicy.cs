namespace ClinicManagement.Application.Common;

/// <summary>
/// Single source of truth for the local-account password policy (FR-B2), so the minimum
/// length can't drift between first-run setup, self-registration, and password change.
///
/// <para>⚠️ <b>Every enforcement site is on <i>setting</i> a password, never on checking one</b> — the five are
/// <c>CreateClinicCommand</c>, <c>JoinClinicCommand</c>, <c>SignUpClinicCommand</c>, <c>ChangePasswordCommand</c>
/// and <c>ChangePlatformPasswordCommand</c>. That asymmetry is what lets the floor rise without locking anybody
/// out: an existing password shorter than <see cref="MinLength"/> keeps working until its owner next changes it
/// (<c>hosted-security-hardening</c> FR-1.9, AC-7). A length check on the <i>login</i> path would refuse every
/// account created before the raise, which is a lockout dressed as a policy.</para>
///
/// <para>⚠️ <b>The clients do not restate this number</b> — it is served on <c>GET /api/auth/mode</c> as
/// <c>passwordMinLength</c> and on <c>GET /api/platform/auth/meta</c> for the console, which cannot reach the
/// first (<c>ConsolePortGate</c> refuses anything outside <c>/api/platform</c> on the console listener).
/// <c>PasswordFloorSingleSourceTests</c> scans <c>web/</c> and <c>console/</c> and fails on a re-introduced
/// literal, because a client that states its own floor is a second authority that silently disagrees the day
/// this constant moves.</para>
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
}
