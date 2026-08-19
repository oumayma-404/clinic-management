package com.clinicmanagement.shell

import android.content.Context
import android.net.Uri
import androidx.core.content.edit

/**
 * The clinic server address this shell connects to, persisted so a phone is configured once and reused on every
 * launch (AC-17). Always HTTPS, and never compiled in: one build serves a clinic's own PC on a LAN and a hosted
 * backend on the internet, and baking either in would make the other unreachable.
 *
 * This is the Kotlin port of `desktop/ClinicManagement.DesktopShell/ServerConfig.cs`. The parsing is deliberately
 * **faithful** rather than improved — the two shells must agree on what a typed address means, or the same string
 * reaches two different servers depending on which client the user happens to hold.
 */
data class ServerConfig(val host: String, val port: Int, val portIsExplicit: Boolean = true) {

    /** The absolute HTTPS URL the WebView navigates to, and the origin the bridge is scoped to. */
    val baseUrl: String get() = "https://$host:$port"

    val isConfigured: Boolean get() = host.isNotBlank()

    /**
     * What the address-entry field shows when a server is already configured. The port is omitted while it is
     * still unresolved: offering `:5001` back to someone who typed a hosted domain would invite them to confirm
     * a port that is wrong, and it is not what they typed.
     */
    val displayAddress: String get() = if (portIsExplicit) "$host:$port" else host

    /**
     * The ports to try, in order, when connecting. One entry when the user typed a port — used verbatim, never
     * probed. Otherwise [DEFAULT_PUBLIC_HTTPS_PORT] **before** [DEFAULT_HTTPS_PORT]: a LAN server refuses 443
     * instantly, whereas an internet firewall in front of a hosted server usually *drops* traffic to 5001, so
     * trying the LAN port first would cost a full timeout on every hosted launch.
     */
    val candidatePorts: List<Int>
        get() = if (portIsExplicit) listOf(port) else listOf(DEFAULT_PUBLIC_HTTPS_PORT, DEFAULT_HTTPS_PORT)

    /**
     * The origins the injected bridge may appear on.
     *
     * ⚠️ The 443 pair is not decoration: a page served on 443 reports `https://host` as its origin — the URL
     * spec omits a default port — while [baseUrl] says `https://host:443`. Granting only the latter would leave
     * the bridge silently uninstalled on exactly the deployment that has no other way in.
     */
    val bridgeOrigins: Set<String>
        get() = if (port == DEFAULT_PUBLIC_HTTPS_PORT) setOf("https://$host", baseUrl) else setOf(baseUrl)

    /**
     * The same server on a now-known port. Marked explicit so the probe is a one-time cost per address rather
     * than a delay on every launch.
     */
    fun withResolvedPort(resolved: Int): ServerConfig =
        copy(port = resolved, portIsExplicit = true)

    /** Whether [uri] is a page of *this* server. Scheme, host and port must all match (AC-25). */
    fun isSameOrigin(uri: Uri): Boolean {
        if (!uri.scheme.equals("https", ignoreCase = true)) return false
        if (!host.equals(uri.host, ignoreCase = true)) return false
        // A URL with no port *is* 443 — that is what the scheme means. Reading it as 5001 made every same-origin
        // link on a hosted deployment look external, and sent it out to a Custom Tab.
        val uriPort = if (uri.port == -1) DEFAULT_PUBLIC_HTTPS_PORT else uri.port
        return uriPort == port
    }

    companion object {
        /** Matches the API's own `Hosting:HttpsPort` default — a clinic's own PC on its LAN. */
        const val DEFAULT_HTTPS_PORT = 5001

        /** The port a hosted deployment is reached on over the internet, behind Caddy. */
        const val DEFAULT_PUBLIC_HTTPS_PORT = 443

        fun empty(): ServerConfig = ServerConfig(host = "", port = DEFAULT_HTTPS_PORT)

        /**
         * Parses a user-entered address: a bare host (`192.168.1.10`), `host:port`, or a full URL
         * (`https://clinic-server:5001`).
         *
         * A missing or out-of-range port is left **unresolved** ([portIsExplicit] false) rather than defaulting
         * to 5001, and [ServerProbe] settles it against the real server. Defaulting here is the defect this
         * shape exists to close: it made every hosted deployment — reached on 443 — unreachable unless the user
         * knew to type `:443`, which nobody typing `clinic.example.com` has any reason to do. [port] still
         * carries [DEFAULT_HTTPS_PORT] meanwhile, so nothing reading it before resolution changes behaviour.
         *
         * ⚠️ An IPv6 literal is not handled, exactly as in the desktop shell — `lastIndexOf(':')` would split it
         * in the middle. Left as-is rather than fixed here: a clinic server is reached by hostname or IPv4, and
         * fixing one shell alone would be the two-answers-to-one-question defect this port exists to avoid.
         */
        fun parseAddress(input: String?): ServerConfig {
            var value = (input ?: "").trim()

            for (scheme in arrayOf("https://", "http://")) {
                if (value.startsWith(scheme, ignoreCase = true)) {
                    value = value.substring(scheme.length)
                    break
                }
            }

            val slash = value.indexOf('/')
            if (slash >= 0) {
                value = value.substring(0, slash)
            }

            var host = value
            var port = DEFAULT_HTTPS_PORT
            var explicitPort = false

            val colon = value.lastIndexOf(':')
            if (colon >= 0) {
                host = value.substring(0, colon)
                val parsed = value.substring(colon + 1).toIntOrNull()
                if (parsed != null && parsed in 1..65535) {
                    port = parsed
                    explicitPort = true
                }
            }

            return ServerConfig(host = host.trim(), port = port, portIsExplicit = explicitPort)
        }
    }
}

/**
 * Reads and writes the address in `SharedPreferences` (AC-17). Missing or unreadable values are treated as « not
 * configured » so the first launch shows the address prompt instead of failing.
 */
class ServerConfigStore(context: Context) {

    private val preferences = context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun load(): ServerConfig {
        val host = preferences.getString(KEY_HOST, null).orEmpty()
        val port = preferences.getInt(KEY_PORT, ServerConfig.DEFAULT_HTTPS_PORT)
        if (host.isBlank()) return ServerConfig.empty()
        return ServerConfig(
            host = host,
            port = if (port in 1..65535) port else ServerConfig.DEFAULT_HTTPS_PORT,
            // Absent for an address saved before the port rule existed. Reading that as « not explicit » costs
            // one probe on the next launch and then self-heals; reading it as explicit would keep an install
            // that was silently pinned to 5001 pinned to it for ever.
            portIsExplicit = preferences.getBoolean(KEY_PORT_EXPLICIT, false),
        )
    }

    fun save(config: ServerConfig) {
        // `edit { }` commits with `apply()` — asynchronous, exactly as the chained form did. A blocking `commit()`
        // would be wrong here: this runs on the address screen's click, and the value is re-read only on the next
        // launch.
        preferences.edit {
            putString(KEY_HOST, config.host)
            putInt(KEY_PORT, config.port)
            putBoolean(KEY_PORT_EXPLICIT, config.portIsExplicit)
        }
    }

    private companion object {
        const val PREFERENCES_NAME = "clinic-shell-server"
        const val KEY_HOST = "host"
        const val KEY_PORT = "port"
        const val KEY_PORT_EXPLICIT = "port-explicit"
    }
}
