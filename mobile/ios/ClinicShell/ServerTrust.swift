import Foundation
import Security

/**
 Evaluates a server's certificate **through the OS**, so a CA the user has installed and trusted is honoured.

 ⚠️ **This is not "accept anything", and the distinction is the whole point.** `SecTrustEvaluateWithError` runs
 Apple's own chain validation, which includes roots the user has installed *and* enabled under « Réglages →
 Général → Informations → Réglages de confiance de certificat ». A certificate the user has not trusted still
 fails here, exactly as it would in Safari. `.performDefaultHandling` — never `.useCredential` on a failure —
 is what keeps that true.

 It exists because **ATS ignores user-installed roots** while Safari does not. Measured on an iPhone with the
 clinic's CA installed and fully trusted: Safari loaded the app over HTTPS cleanly, and `WKWebView` in this
 shell refused the identical URL with « le certificat de ce serveur n'est pas valide ». Since `SelfHostedLan` is
 *defined* by a self-signed certificate on a LAN address, without this the topology is unreachable from iOS —
 not degraded, unreachable.

 The Android counterpart is one line of XML (`network_security_config.xml` trusting user CAs). iOS needs the
 `NSAllowsLocalNetworking` exemption in `Info.plist` **and** this, because the first relaxes the transport
 policy and only the second answers « is this certificate one the user chose to trust? ».
 */
enum ServerTrust {

    /**
     `true` when the OS — with the user's own trust settings applied — considers this server's chain valid.

     Callers must reject on `false` by deferring to default handling, so an untrusted certificate produces the
     shell's « Impossible de joindre » state rather than a silent connection.
     */
    static func isTrustedByTheSystem(_ challenge: URLAuthenticationChallenge) -> Bool {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust else { return false }

        // The hostname must still match a SAN: `SecTrustEvaluateWithError` checks the policy the trust object
        // was created with, which for a URL session includes the host. A certificate valid for another address
        // is refused here, which is what caught the container-generated certificate naming the wrong IP.
        var error: CFError?
        let valid = SecTrustEvaluateWithError(trust, &error)
        if !valid {
            let reason = (error as Error?)?.localizedDescription ?? "unknown"
            NSLog("ClinicShell: server trust rejected by the OS — \(reason)")
        }
        return valid
    }

    /// The credential to present once [isTrustedByTheSystem] has said yes. Never build one otherwise.
    static func credential(for challenge: URLAuthenticationChallenge) -> URLCredential? {
        guard let trust = challenge.protectionSpace.serverTrust else { return nil }
        return URLCredential(trust: trust)
    }
}
