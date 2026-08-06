import Foundation

/**
 The clinic server address this shell connects to, persisted so a phone is configured once and reused on every
 launch (AC-17). Always HTTPS, and never compiled in: one build serves a clinic's own PC on a LAN and a hosted
 backend on the internet, and baking either in would make the other unreachable.

 This is the Swift port of `mobile/android/…/ServerConfig.kt`, itself a port of
 `desktop/ClinicManagement.DesktopShell/ServerConfig.cs`. The parsing is deliberately **faithful** rather than
 improved — three clients must agree on what a typed address means, or the same string reaches different servers
 depending on which one the user happens to hold. Where a quirk is carried across it is named as such below.
 */
struct ServerConfig: Equatable {

    let host: String
    let port: Int

    /// Matches the API's own `Hosting:HttpsPort` default — the single browser-facing Kestrel front door.
    static let defaultHttpsPort = 5001

    /// The absolute HTTPS URL the web view navigates to, and the origin the bridge is scoped to.
    var baseUrl: String { "https://\(host):\(port)" }

    var isConfigured: Bool { !host.trimmingCharacters(in: .whitespaces).isEmpty }

    /// What the address-entry field shows when a server is already configured.
    var displayAddress: String { "\(host):\(port)" }

    /**
     The origins the injected bridge may appear on.

     ⚠️ **iOS has no per-origin user script.** Android scopes the wrapper with
     `addDocumentStartJavaScript(script, setOf(origin))`; `WKUserScript` takes no such argument and runs in every
     frame of every page the web view loads. The scope therefore has to be enforced *inside* the script, which is
     why this list exists and is injected into it.

     The 443 entry is not decoration: `window.location.origin` omits the default port, so a server reached on 443
     reports `https://host` while [baseUrl] says `https://host:443`, and a strict comparison would silently leave
     the bridge uninstalled on exactly the deployment that has no other way in.
     */
    var bridgeOrigins: [String] {
        port == 443 ? ["https://\(host)", "https://\(host):443"] : [baseUrl]
    }

    static func empty() -> ServerConfig {
        ServerConfig(host: "", port: defaultHttpsPort)
    }

    /**
     Whether `url` is a page of *this* server. Scheme, host and port must all match (AC-25).

     ⚠️ Carries the Android quirk deliberately: a URL with no explicit port is read as [defaultHttpsPort] rather
     than as 443. See `mobile/ios/README.md` § « Le défaut de port », which records it as a real defect of all
     three clients and why it is not fixed in one of them alone.
     */
    func isSameOrigin(_ url: URL) -> Bool {
        guard url.scheme?.lowercased() == "https" else { return false }
        guard let urlHost = url.host, urlHost.caseInsensitiveCompare(host) == .orderedSame else { return false }
        return (url.port ?? Self.defaultHttpsPort) == port
    }

    /**
     Parses a user-entered address: a bare host (`192.168.1.10`), `host:port`, or a full URL
     (`https://clinic-server:5001`). A missing or out-of-range port falls back to [defaultHttpsPort].

     ⚠️ An IPv6 literal is not handled, exactly as in the other two shells — searching for the last `:` would split
     it in the middle. Left as-is rather than fixed here: fixing one client alone is the two-answers-to-one-question
     defect this port exists to avoid.
     */
    static func parseAddress(_ input: String?) -> ServerConfig {
        var value = (input ?? "").trimmingCharacters(in: .whitespacesAndNewlines)

        for scheme in ["https://", "http://"] where value.lowercased().hasPrefix(scheme) {
            value = String(value.dropFirst(scheme.count))
            break
        }

        if let slash = value.firstIndex(of: "/") {
            value = String(value[value.startIndex..<slash])
        }

        var host = value
        var port = defaultHttpsPort

        if let colon = value.lastIndex(of: ":") {
            host = String(value[value.startIndex..<colon])
            let tail = String(value[value.index(after: colon)...])
            if let parsed = Int(tail), (1...65535).contains(parsed) {
                port = parsed
            }
        }

        return ServerConfig(host: host.trimmingCharacters(in: .whitespaces), port: port)
    }
}

/**
 Reads and writes the address in `UserDefaults` (AC-17) — the counterpart of Android's `SharedPreferences`. Missing
 or unreadable values are treated as « not configured » so the first launch shows the address prompt instead of
 failing.
 */
struct ServerConfigStore {

    private let defaults: UserDefaults
    private static let keyHost = "clinic-shell-server.host"
    private static let keyPort = "clinic-shell-server.port"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> ServerConfig {
        let host = defaults.string(forKey: Self.keyHost) ?? ""
        if host.trimmingCharacters(in: .whitespaces).isEmpty { return .empty() }
        let stored = defaults.integer(forKey: Self.keyPort)
        let port = (1...65535).contains(stored) ? stored : ServerConfig.defaultHttpsPort
        return ServerConfig(host: host, port: port)
    }

    func save(_ config: ServerConfig) {
        defaults.set(config.host, forKey: Self.keyHost)
        defaults.set(config.port, forKey: Self.keyPort)
    }
}
