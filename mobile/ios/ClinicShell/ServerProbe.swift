import Foundation

/**
 Settles which port a typed address means, when the user did not say.

 The rule is identical in all three clients (desktop, Android, iOS) — see `mobile/CLAUDE.md` § « the port rule ».
 An address with an explicit port is used verbatim and never probed. An address without one is tried against
 [ServerConfig.candidatePorts] in order, and the first port that **answers at all** wins.

 ⚠️ « Answers » deliberately includes a TLS failure. An offline-LAN server presents a certificate signed by a CA
 the phone may not have imported yet, so a handshake rejection is the *expected* outcome of probing a live clinic
 server — treating it as « nothing here » would send every LAN install to the wrong port. What disqualifies a port
 is a transport failure: no route, refused connection, timeout, or a name that does not resolve.

 ⚠️ The candidates are tried **in sequence, not concurrently.** Concurrently would be faster and wrong: with two
 answers in flight the winner is whichever the network happened to return first, so the same address could resolve
 differently on two launches — and a shell that reaches a different server depending on timing is worse than one
 that takes an extra second.
 */
enum ServerProbe {

    /**
     The route asked for. Anonymous and exempt from the client-version floor, so it answers a shell of any age —
     which is what makes it usable as a reachability probe rather than only as a version read.
     */
    private static let path = "/api/meta/client-requirements"

    /**
     Per-candidate budget. Short on purpose: this runs behind the « Connexion… » screen, and the worst case is
     paid once per address, not once per launch.
     */
    private static let timeout: TimeInterval = 4

    /**
     Calls back on the main queue with the config to actually connect with. Returns `config` unchanged when its
     port is already explicit, so the common case costs no network at all.

     When **no** candidate answers, the first candidate is returned rather than nothing: the address is simply
     wrong or the server is off, and that is diagnosed far better by the load that follows — which shows the
     unreachable screen naming the address — than by a second error state of this probe's own.
     */
    static func resolve(_ config: ServerConfig, completion: @escaping (ServerConfig) -> Void) {
        guard !config.portIsExplicit, config.isConfigured else {
            DispatchQueue.main.async { completion(config) }
            return
        }

        tryCandidates(config, remaining: config.candidatePorts, completion: completion)
    }

    private static func tryCandidates(
        _ config: ServerConfig,
        remaining: [Int],
        completion: @escaping (ServerConfig) -> Void
    ) {
        guard let port = remaining.first else {
            let fallback = config.withResolvedPort(config.candidatePorts[0])
            DispatchQueue.main.async { completion(fallback) }
            return
        }

        answers(host: config.host, port: port) { live in
            if live {
                let resolved = config.withResolvedPort(port)
                DispatchQueue.main.async { completion(resolved) }
            } else {
                tryCandidates(config, remaining: Array(remaining.dropFirst()), completion: completion)
            }
        }
    }

    /// Calls back off the main queue — `tryCandidates` owns the hop back.
    private static func answers(host: String, port: Int, completion: @escaping (Bool) -> Void) {
        guard let url = URL(string: "https://\(host):\(port)\(path)") else {
            completion(false)
            return
        }

        var request = URLRequest(url: url, timeoutInterval: timeout)
        request.httpMethod = "GET"
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        // The same trusting session `ClientRequirements` uses, and for the same reason: without it a
        // `SelfHostedLan` server's user-installed CA is not honoured. Here it matters less — a TLS failure counts
        // as an answer anyway — but two probes hitting one server must not disagree about whether it exists.
        probeSession.dataTask(with: request) { _, response, error in
            guard let error = error as NSError? else {
                // Any status is an answer — 200, 404 on a server too old to have the route, even a 502 from a
                // proxy in front of a starting API. All of them prove something is listening on this port.
                completion(response != nil)
                return
            }
            completion(isTlsFailure(error))
        }.resume()
    }

    /**
     Whether `error` means « something is listening and speaking TLS » rather than « nothing is there ».

     Every certificate rejection is a *live* port: the offline-LAN install's normal state before its CA is
     imported. Everything else in `NSURLErrorDomain` — and every error from another domain — is read as dead,
     which is the safe direction: the worst a false « dead » can do is fall through to the next candidate.
     */
    private static func isTlsFailure(_ error: NSError) -> Bool {
        guard error.domain == NSURLErrorDomain else { return false }
        switch error.code {
        case NSURLErrorSecureConnectionFailed,
             NSURLErrorServerCertificateHasBadDate,
             NSURLErrorServerCertificateUntrusted,
             NSURLErrorServerCertificateHasUnknownRoot,
             NSURLErrorServerCertificateNotYetValid,
             NSURLErrorClientCertificateRejected,
             NSURLErrorClientCertificateRequired:
            return true
        default:
            return false
        }
    }

    private static let probeSession: URLSession = {
        URLSession(configuration: .ephemeral, delegate: TrustingSessionDelegate(), delegateQueue: nil)
    }()

    /// Defers the trust decision to the OS, exactly as the web view's own challenge handler does.
    private final class TrustingSessionDelegate: NSObject, URLSessionDelegate {
        func urlSession(
            _ session: URLSession,
            didReceive challenge: URLAuthenticationChallenge,
            completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
        ) {
            guard ServerTrust.isTrustedByTheSystem(challenge),
                  let credential = ServerTrust.credential(for: challenge) else {
                completionHandler(.performDefaultHandling, nil)
                return
            }
            completionHandler(.useCredential, credential)
        }
    }
}
