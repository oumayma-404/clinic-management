namespace ClinicManagement.Application.Common;

/// <summary>
/// Short-lived proof that a user re-authenticated just now, for an action that demands it
/// (<c>hosted-security-hardening</c> FR-1.8, FR-4.3).
///
/// <para><b>⚠️ Registered <c>AddSingleton</c>, and the lifetime is load-bearing.</b> A confirmation is minted by
/// one request and consumed by <i>another</i> — that is what a step-up is. Under a scoped or transient
/// registration each request builds a fresh store, the confirmation is never found, and <b>every</b> guarded
/// action refuses with a French « mot de passe incorrect » that is not incorrect. That failure is silent and
/// indistinguishable from the feature working, which is why the lifetime is stated here rather than left to the
/// registration site to remember.</para>
///
/// <para>⚠️ <b>Single-use per action.</b> A confirmation is spent when it is consumed, so one re-authentication
/// does not silently authorise a second export ten minutes later.</para>
///
/// <para>⚠️ <b>An ABSOLUTE expiry, never sliding</b> — it is what makes both the confirmation and the failure
/// counter expire with no sweep and no background job (the OAuth <c>state</c> cache's precedent). A sliding
/// window would keep a confirmation alive precisely while somebody kept trying to use it.</para>
///
/// <para>⚠️ <b>Stated residual: the store is instance-local.</b> There is one <c>api</c> service with no
/// replicas today, so this is correct — but <c>MigrationLock</c> exists precisely because two containers *can*
/// come up together, and scaling past one instance requires a shared store first. Recorded here and in
/// <c>deploy/README.md</c> beside the registration.</para>
/// </summary>
public interface IStepUpConfirmations
{
    /// <summary>Records a fresh confirmation for <paramref name="userId"/> and returns its opaque token.</summary>
    string Issue(string userId, string action);

    /// <summary>
    /// Spends a confirmation if it matches an unexpired one for this user <b>and this action</b>, and reports
    /// whether it did. A confirmation minted for one action does not authorise another.
    /// </summary>
    bool Consume(string userId, string action, string token);

    /// <summary>
    /// Records a failed step-up attempt and reports whether the caller has now run out.
    ///
    /// <para>⚠️ <b>Its own counter, never the login lockout.</b> Three wrong attempts at a step-up must not lock
    /// the account out of the product: the user is already signed in, doing ordinary work, and turning a
    /// mistyped confirmation into « ce compte est temporairement bloqué » would be a self-inflicted outage on
    /// the person who was working correctly.</para>
    /// </summary>
    bool RecordFailureAndCheckExhausted(string userId);

    /// <summary>Clears the failure counter after a success.</summary>
    void ClearFailures(string userId);

    /// <summary>How many attempts a step-up gets before it refuses, for the screen to state up front.</summary>
    int MaxAttempts { get; }
}
