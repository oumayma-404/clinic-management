package com.clinicmanagement.shell

import android.app.Activity
import android.hardware.biometrics.BiometricManager
import android.hardware.biometrics.BiometricPrompt
import android.os.Build
import android.os.CancellationSignal
import android.util.Log
import androidx.annotation.RequiresApi

/**
 * The OS side of `confirmIdentity` (AC-57…AC-60): ask the platform to confirm the device owner, and answer with
 * one of the contract's four outcomes.
 *
 * ⚠️ **It cannot fail, only answer.** `mobile/shared/bridge.md` states that `confirmIdentity` never rejects,
 * because the one caller — the session lock gate — must not fail open. Every error path below therefore ends in
 * an outcome, and the `unavailable` one is a *first-class* result: the web app falls straight through to the
 * password screen it would have shown anyway.
 *
 * ⚠️ **The framework prompt, deliberately, and only from API 28.** `androidx.biometric` would add a dependency,
 * force [MainActivity] to become a `FragmentActivity`, and below API 28 render its own fingerprint dialog against
 * AppCompat theme attributes this shell's framework theme does not carry. An API 26–27 device gets `unavailable`
 * and the ordinary password screen instead — the outcome AC-60 already specifies for a device that cannot ask.
 *
 * ⚠️ **Nothing is stored.** This asks the OS a yes/no question about the person holding the phone; the session it
 * unlocks is the one already in the WebView's cookie store. AC-59 holds by construction, not by care.
 */
object BiometricGate {

    const val CONFIRMED = "confirmed"
    const val REJECTED = "rejected"
    const val CANCELLED = "cancelled"
    const val UNAVAILABLE = "unavailable"

    /** Must be called on the UI thread. [onOutcome] is invoked exactly once, also on the UI thread. */
    fun confirm(activity: Activity, onOutcome: (String) -> Unit) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) {
            onOutcome(UNAVAILABLE)
            return
        }
        try {
            prompt(activity, once(onOutcome))
        } catch (t: Throwable) {
            // A prompt that could not even be shown is a device that cannot ask, not a refusal of the user.
            Log.w(TAG, "identity prompt could not be shown", t)
            onOutcome(UNAVAILABLE)
        }
    }

    @RequiresApi(Build.VERSION_CODES.P)
    private fun prompt(activity: Activity, onOutcome: (String) -> Unit) {
        val builder = BiometricPrompt.Builder(activity)
            .setTitle(activity.getString(R.string.identity_title))
            .setDescription(activity.getString(R.string.identity_description))

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            // The PIN, the schéma and the password are what a dentist who has enrolled no fingerprint still has,
            // and AC-57 asks for « a biometric **or** device-credential check ». Combining the two is only
            // expressible from API 30; with DEVICE_CREDENTIAL allowed the prompt supplies its own cancel action
            // and setting a negative button alongside it throws.
            builder.setAllowedAuthenticators(
                BiometricManager.Authenticators.BIOMETRIC_STRONG or
                    BiometricManager.Authenticators.DEVICE_CREDENTIAL,
            )
        } else {
            builder.setNegativeButton(
                activity.getString(R.string.identity_cancel),
                activity.mainExecutor,
            ) { _, _ -> onOutcome(CANCELLED) }
        }

        builder.build().authenticate(
            CancellationSignal(),
            activity.mainExecutor,
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                    onOutcome(CONFIRMED)
                }

                override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                    onOutcome(outcomeFor(errorCode))
                }

                // `onAuthenticationFailed` is deliberately not overridden: a non-matching finger does NOT end the
                // prompt — the OS says « réessayez » and keeps it up. Reporting it would spend one of the web
                // side's three attempts on a retry the user has not finished making.
            },
        )
    }

    /**
     * The framework's `BIOMETRIC_ERROR_*` codes, split by what the *user* should do next rather than by what went
     * wrong. Only the three groups matter to the contract, so an unrecognised code falls to `rejected` — the
     * conservative side, since it advances the attempt counter towards the password screen.
     */
    @RequiresApi(Build.VERSION_CODES.P)
    private fun outcomeFor(errorCode: Int): String = when (errorCode) {
        BiometricPrompt.BIOMETRIC_ERROR_USER_CANCELED,
        BiometricPrompt.BIOMETRIC_ERROR_CANCELED,
        ERROR_NEGATIVE_BUTTON,
        -> CANCELLED

        BiometricPrompt.BIOMETRIC_ERROR_NO_BIOMETRICS,
        BiometricPrompt.BIOMETRIC_ERROR_NO_DEVICE_CREDENTIAL,
        BiometricPrompt.BIOMETRIC_ERROR_HW_NOT_PRESENT,
        BiometricPrompt.BIOMETRIC_ERROR_HW_UNAVAILABLE,
        -> UNAVAILABLE

        else -> REJECTED
    }

    /** The prompt can report both a negative-button press and an error for one dismissal. The gate must not. */
    private fun once(onOutcome: (String) -> Unit): (String) -> Unit {
        var delivered = false
        return { outcome ->
            if (!delivered) {
                delivered = true
                onOutcome(outcome)
            }
        }
    }

    /**
     * « the user pressed the negative button ». The framework's own `BiometricPrompt` does not expose this one
     * publicly — only `androidx.biometric` does, at the same stable value — and it arrives *alongside* the
     * negative-button listener above, which [once] is there to absorb. Mapped anyway, so the outcome does not
     * depend on which of the two the platform delivers first.
     */
    private const val ERROR_NEGATIVE_BUTTON = 13

    private const val TAG = "ClinicShell"
}
