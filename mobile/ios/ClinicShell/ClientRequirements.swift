import Foundation

/**
 What this server requires of a client, read over **native HTTP before the app is loaded** (AC-33, launch half).

 The web bundle already handles the in-session refusal — a 426 reaches `<ClientVersionGate>` and takes the screen —
 but that only works once the bundle is running. A build below the floor must be told so at launch instead of
 loading an app whose every request will be refused.

 ⚠️ **Unreadable means "no floor", never "refuse"** — the same direction the server's own
 `ClientRequirements.IsBelowFloor` and the Android shell both take. An offline phone, a server too old to have the
 route, a malformed body and an unset floor all pass. A shell that refuses to start because a probe failed is a
 worse outcome than any it could prevent, and the unreachable case is diagnosed far better by the load that follows.
 */
enum ClientRequirements {

    private static let path = "/api/meta/client-requirements"
    private static let timeout: TimeInterval = 8

    /// What the probe learned. Both fields are empty when the server said nothing.
    struct Requirements {
        let minimumShellVersion: String
        let storeUrlIos: String
    }

    /// Calls back on the main queue. `nil` when the answer could not be read — see the note on why that must pass.
    static func fetch(baseUrl: String, completion: @escaping (Requirements?) -> Void) {
        guard let url = URL(string: baseUrl + path) else {
            DispatchQueue.main.async { completion(nil) }
            return
        }

        var request = URLRequest(url: url, timeoutInterval: timeout)
        request.httpMethod = "GET"
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        // The header the floor is measured against. Sent here too so the one route exempt from the floor is still
        // asked the same question every other call asks.
        request.setValue(ShellVersion.name, forHTTPHeaderField: "X-Client-Version")

        // ⚠️ Not `URLSession.shared`: it takes no delegate, so it cannot honour a user-installed CA and this
        // probe would fail on every `SelfHostedLan` server — see `ServerTrust`. It fails *soft* (a null result
        // means « no floor »), so the symptom would have been silent rather than loud: the floor check quietly
        // never applying on the one topology it is most likely to matter for.
        probeSession.dataTask(with: request) { data, response, _ in
            let parsed = parse(data: data, response: response)
            DispatchQueue.main.async { completion(parsed) }
        }.resume()
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

    private static func parse(data: Data?, response: URLResponse?) -> Requirements? {
        guard let http = response as? HTTPURLResponse, http.statusCode == 200, let data else { return nil }
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        let stores = root["storeUrls"] as? [String: Any]
        return Requirements(
            minimumShellVersion: root["minimumShellVersion"] as? String ?? "",
            storeUrlIos: stores?["ios"] as? String ?? ""
        )
    }

    /**
     Whether `installed` is older than `floor`. **False for anything unparseable**, mirroring the server's
     `Version.TryParse` pair and the Android shell's `isBelowFloor`, so no two sides can disagree about which builds
     are acceptable.
     */
    static func isBelowFloor(installed: String, floor: String) -> Bool {
        guard let floorParts = parseVersion(floor), let installedParts = parseVersion(installed) else { return false }

        for index in 0..<max(floorParts.count, installedParts.count) {
            let left = index < installedParts.count ? installedParts[index] : 0
            let right = index < floorParts.count ? floorParts[index] : 0
            if left != right { return left < right }
        }
        return false
    }

    /// `1.2.3` → `[1, 2, 3]`. `nil` for anything that is not a dotted run of non-negative integers.
    private static func parseVersion(_ value: String) -> [Int]? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return nil }
        let parts = trimmed.split(separator: ".", omittingEmptySubsequences: false)
        if parts.count > 4 { return nil }

        var numbers: [Int] = []
        for part in parts {
            guard let number = Int(part), number >= 0 else { return nil }
            numbers.append(number)
        }
        return numbers
    }
}

/**
 The shell's own version, read from the bundle so the build and the bridge cannot report different builds (AC-27).

 `MARKETING_VERSION` in `project.yml` is the single source; this is what reaches `window.__clinicShell.version` and
 therefore `X-Client-Version`. ⚠️ A change to the bridge's method set edits `mobile/shared/bridge.md` **and** bumps
 it — one without the other ships a build reporting a capability set it does not have.
 */
enum ShellVersion {
    static let name: String =
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
}
