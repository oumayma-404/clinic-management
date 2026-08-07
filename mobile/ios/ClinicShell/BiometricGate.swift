import Foundation
import LocalAuthentication

/**
 The OS side of `confirmIdentity` (AC-57…AC-60): ask the platform to confirm the device owner, and answer with one
 of the contract's four outcomes.

 ⚠️ **It cannot fail, only answer.** `mobile/shared/bridge.md` states that `confirmIdentity` never rejects, because
 the one caller — the session lock gate — must not fail open. Every error path below ends in an outcome, and
 `unavailable` is a *first-class* result: the web app falls straight through to the password screen it would have
 shown anyway.

 ⚠️ **`.deviceOwnerAuthentication`, not `.deviceOwnerAuthenticationWithBiometrics`.** AC-57 asks for « a biometric
 **or** device-credential check », and the first policy falls back to the passcode on its own — which is what a
 dentist who has enrolled no Face ID still has. The biometrics-only policy would report `unavailable` for them and
 send them to the password screen for no reason. It is the same decision as Android's
 `BIOMETRIC_STRONG or DEVICE_CREDENTIAL`.

 ⚠️ **Nothing is stored.** This asks the OS a yes/no question about the person holding the phone; the session it
 unlocks is the one already in the web view's cookie store. AC-59 holds by construction, not by care.
 */
enum BiometricGate {

    static let confirmed = "confirmed"
    static let rejected = "rejected"
    static let cancelled = "cancelled"
    static let unavailable = "unavailable"

    /// `completion` is invoked exactly once, on the main queue.
    static func confirm(completion: @escaping (String) -> Void) {
        let context = LAContext()
        // The passcode IS the fallback under this policy, so offering a second one would be a button that leads
        // where the prompt already goes.
        context.localizedFallbackTitle = ""

        var availability: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &availability) else {
            // No passcode set, no biometric hardware, or the class is unavailable: a device that cannot ask.
            DispatchQueue.main.async { completion(unavailable) }
            return
        }

        context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: Strings.identityDescription) { success, error in
            DispatchQueue.main.async {
                completion(success ? confirmed : outcome(for: error))
            }
        }
    }

    /**
     `LAError` codes split by what the *user* should do next rather than by what went wrong. An unrecognised code
     falls to `rejected` — the conservative side, since it advances the web app's attempt counter towards the
     password screen rather than leaving the session paused indefinitely.
     */
    private static func outcome(for error: Error?) -> String {
        guard let code = (error as? LAError)?.code else { return rejected }

        switch code {
        case .userCancel, .appCancel, .systemCancel, .userFallback:
            return cancelled
        case .biometryNotAvailable, .biometryNotEnrolled, .passcodeNotSet:
            return unavailable
        default:
            // .authenticationFailed, .biometryLockout, .invalidContext and anything Apple adds later.
            return rejected
        }
    }
}
