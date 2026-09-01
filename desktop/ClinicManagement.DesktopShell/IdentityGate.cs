using System;
using System.Threading.Tasks;
using Windows.Security.Credentials.UI;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Windows Hello, behind <c>mobile/shared/bridge.md</c>'s <c>confirmIdentity</c> contract.
///
/// <para><b>What this is for.</b> After the inactivity limit the web bundle either <i>locks</i> — cookie untouched,
/// page still mounted, back to the same open fiche — or signs the user out completely. Which one happens is decided
/// by a single feature detection: <c>typeof window.__clinicShell?.confirmIdentity === "function"</c>. Android has
/// implemented it since Part 7 and therefore locks; this shell had not, so a dentist treating a patient for forty
/// minutes with the app open on the operatory PC was signed out — password, then six digits from a phone across the
/// room. The phone, already protected by its own lock screen, got the gentle treatment; the desktop inside a locked
/// clinic got the harsh one.</para>
///
/// <para>⚠️ <b>It never throws and never rejects — every failure is a value.</b> That is the contract's own rule,
/// and it is load-bearing: the one call site must not fail open, so a shell that cannot answer says
/// <c>"unavailable"</c> and the web bundle falls straight through to the password screen with no error and no dead
/// control. A thrown exception would surface as a rejected promise at a call site written not to expect one.</para>
///
/// <para>⚠️ <b>Availability is asked before verification, and anything but <c>Available</c> is
/// <c>"unavailable"</c>, not a refusal.</b> A machine with no Hello enrolment, a PC where policy disables it, a
/// desktop with no camera or reader — none of those is somebody failing a check, and treating them as
/// <c>"rejected"</c> would burn one of the gate's three attempts for a device that can never succeed.</para>
///
/// <para>⚠️ <b>No password, secret or token is stored or read by any of this (AC-59).</b> The shell asks Windows a
/// yes/no question about the person at the keyboard; the session it resumes is the one already in the WebView's
/// cookie store, which was never cleared.</para>
/// </summary>
public static class IdentityGate
{
    /// <summary>
    /// What the user is told they are confirming. Deliberately says the session is <i>paused</i> rather than
    /// ended — Hello's own dialog is the last thing between a dentist and the fiche they had open, and « votre
    /// session » reads as a sign-in prompt.
    /// </summary>
    private const string Prompt = "Confirmez votre identité pour reprendre votre session.";

    /// <summary>
    /// Asks Windows to confirm the device owner, and maps the answer onto the bridge's four outcomes.
    /// </summary>
    /// <param name="windowHandle">
    /// The shell window. ⚠️ <b>Required, and passing <c>IntPtr.Zero</c> is not a shortcut.</b> In a desktop
    /// (non-packaged) process the parameterless <c>UserConsentVerifier.RequestVerificationAsync</c> has no window
    /// to parent its dialog to and fails; the interop overload that takes an HWND is the supported path, and
    /// without it the prompt either never appears or appears behind the app.
    /// </param>
    public static async Task<string> ConfirmAsync(IntPtr windowHandle)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                // DeviceNotPresent, NotConfiguredForUser, DisabledByPolicy, DeviceBusy — none of them is a person
                // failing a check, so none of them may cost an attempt.
                return "unavailable";
            }

            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(windowHandle, Prompt);

            return result switch
            {
                UserConsentVerificationResult.Verified => "confirmed",

                // The user dismissed the prompt. Counts as one of the gate's three attempts, like a dismissal on
                // Android — it is the only bound on how long a live cookie may sit behind a client-side overlay.
                UserConsentVerificationResult.Canceled => "cancelled",

                // A real refusal: the face or fingerprint did not match, or Windows locked out after its own
                // retries. This is the outcome that should cost an attempt.
                UserConsentVerificationResult.RetriesExhausted => "rejected",

                // The remaining members describe a machine that cannot ask rather than a person who failed.
                _ => "unavailable",
            };
        }
        catch
        {
            // An older Windows, a policy that removed the API, a window handle that has gone. Every one of them
            // means « this machine cannot ask », which is a first-class outcome and never an error.
            return "unavailable";
        }
    }
}
