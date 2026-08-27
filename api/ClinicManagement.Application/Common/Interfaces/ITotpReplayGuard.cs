namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Refuses a one-time code that has already been spent — RFC 6238 § 5.2: <i>"the verifier MUST NOT accept the
/// second attempt of the OTP after the successful validation has been issued for the first OTP"</i>.
///
/// <para>⚠️ <b>Without this the second factor is not one.</b> A TOTP code stays valid for its whole 30-second
/// step (and, here, one step either side — see <c>ITotpService.VerifyCode</c>), so the <i>same</i> code was
/// accepted twice: two <c>POST /api/auth/login</c> calls with the same <c>totpCode</c> both returned 200 and a
/// valid token. The product makes the factor <b>mandatory</b> for administrators, so what is weakened is
/// precisely the guarantee it exists to provide — a code read over a shoulder, or captured once anywhere between
/// the phone and the server, is replayable for the rest of its window.</para>
///
/// <para><b>Keyed on the pair, not on the code.</b> A spent code is spent <i>for that account</i>: two
/// administrators may legitimately hold the same six digits at the same instant, and keying on the digits alone
/// would let one lock the other out of their own window.</para>
///
/// <para><b>The counter, not the code either.</b> The verifier accepts a window of steps, so a code is really a
/// claim about a <i>time step</i>; remembering the digits would let the same step be re-spent by the neighbouring
/// code. See the implementation for how the step is derived without a second HMAC.</para>
/// </summary>
public interface ITotpReplayGuard
{
    /// <summary>
    /// Claims <paramref name="code"/> for <paramref name="userId"/>. True when this is its first presentation —
    /// the caller may proceed. False when it has already been spent, and the caller must refuse it exactly as it
    /// refuses a wrong code, so a replay cannot be used to learn that the code was otherwise valid.
    /// </summary>
    /// <remarks>
    /// Call it only <b>after</b> the code has verified: claiming first would let a wrong guess burn the real
    /// code's one use, which is a denial of service against the account's own owner.
    /// </remarks>
    bool TryConsume(string userId, string code);
}
