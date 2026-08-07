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

    /**
     Whether [port] came from the user (or from a resolution that already happened) rather than from a default.
     False means [candidatePorts] is probed before connecting — see `ServerProbe`.
     */
    let portIsExplicit: Bool

    /// Matches the API's own `Hosting:HttpsPort` default — a clinic's own PC on its LAN.
    static let defaultHttpsPort = 5001

    /// The port a hosted deployment is reached on over the internet, behind Caddy.
    static let defaultPublicHttpsPort = 443

    /// The absolute HTTPS URL the web view navigates to, and the origin the bridge is scoped to.
    var baseUrl: String { "https://\(host):\(port)" }

    var isConfigured: Bool { !host.trimmingCharacters(in: .whitespaces).isEmpty }

    /**
     What the address-entry field shows when a server is already configured. The port is omitted while it is still
     unresolved: offering `:5001` back to someone who typed a hosted domain would invite them to confirm a port
     that is wrong, and it is not what they typed.
     */
    var displayAddress: String { portIsExplicit ? "\(host):\(port)" : host }

    /**
     The ports to try, in order, when connecting. One entry when the user typed a port — used verbatim, never
     probed. Otherwise [defaultPublicHttpsPort] **before** [defaultHttpsPort]: a LAN server refuses 443 instantly,
     whereas an internet firewall in front of a hosted server usually *drops* traffic to 5001, so trying the LAN
     port first would cost a full timeout on every hosted launch.
     */
    var candidatePorts: [Int] {
        portIsExplicit ? [port] : [Self.defaultPublicHttpsPort, Self.defaultHttpsPort]
    }

    /**
     The same server on a now-known port. Marked explicit so the probe is a one-time cost per address rather than
     a delay on every launch.
     */
    func withResolvedPort(_ resolved: Int) -> ServerConfig {
        ServerConfig(host: host, port: resolved, portIsExplicit: true)
    }

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
        port == Self.defaultPublicHttpsPort ? ["https://\(host)", baseUrl] : [baseUrl]
    }

    static func empty() -> ServerConfig {
        ServerConfig(host: "", port: defaultHttpsPort, portIsExplicit: false)
    }

    /**
     Whether `url` is a page of *this* server. Scheme, host and port must all match (AC-25).

     A URL with no port *is* 443 — that is what the scheme means. Reading it as [defaultHttpsPort] made every
     same-origin link on a hosted deployment look external, and sent it out to an `SFSafariViewController`.
     */
    func isSameOrigin(_ url: URL) -> Bool {
        guard url.scheme?.lowercased() == "https" else { return false }
        guard let urlHost = url.host, urlHost.caseInsensitiveCompare(host) == .orderedSame else { return false }
        return (url.port ?? Self.defaultPublicHttpsPort) == port
    }

    /**
     Parses a user-entered address: a bare host (`192.168.1.10`), `host:port`, or a full URL
     (`https://clinic-server:5001`).

     A missing or out-of-range port is left **unresolved** ([portIsExplicit] false) rather than defaulting to
     5001, and `ServerProbe` settles it against the real server. Defaulting here is the defect this shape exists
     to close: it made every hosted deployment — reached on 443 — unreachable unless the user knew to type
     `:443`, which nobody typing `clinic.example.com` has any reason to do. [port] still carries
     [defaultHttpsPort] meanwhile, so nothing reading it before resolution changes behaviour.

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
        var explicitPort = false

        if let colon = value.lastIndex(of: ":") {
            host = String(value[value.startIndex..<colon])
            let tail = String(value[value.index(after: colon)...])
            if let parsed = Int(tail), (1...65535).contains(parsed) {
                port = parsed
                explicitPort = true
            }
        }

        return ServerConfig(
            host: host.trimmingCharacters(in: .whitespaces),
            port: port,
            portIsExplicit: explicitPort
        )
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
    private static let keyPortExplicit = "clinic-shell-server.port-explicit"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> ServerConfig {
        let host = defaults.string(forKey: Self.keyHost) ?? ""
        if host.trimmingCharacters(in: .whitespaces).isEmpty { return .empty() }
        let stored = defaults.integer(forKey: Self.keyPort)
        let port = (1...65535).contains(stored) ? stored : ServerConfig.defaultHttpsPort
        // Absent for an address saved before the port rule existed — `bool(forKey:)` reads a missing key as
        // false. That costs one probe on the next launch and then self-heals; reading it as explicit would keep
        // an install that was silently pinned to 5001 pinned to it for ever.
        return ServerConfig(host: host, port: port, portIsExplicit: defaults.bool(forKey: Self.keyPortExplicit))
    }

    func save(_ config: ServerConfig) {
        defaults.set(config.host, forKey: Self.keyHost)
        defaults.set(config.port, forKey: Self.keyPort)
        defaults.set(config.portIsExplicit, forKey: Self.keyPortExplicit)
    }
}
