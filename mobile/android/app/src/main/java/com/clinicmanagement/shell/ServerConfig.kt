package com.clinicmanagement.shell

import android.content.Context
import android.net.Uri

/**
 * The clinic server address this shell connects to, persisted so a phone is configured once and reused on every
 * launch (AC-17). Always HTTPS, and never compiled in: one build serves a clinic's own PC on a LAN and a hosted
 * backend on the internet, and baking either in would make the other unreachable.
 *
 * This is the Kotlin port of `desktop/ClinicManagement.DesktopShell/ServerConfig.cs`. The parsing is deliberately
 * **faithful** rather than improved — the two shells must agree on what a typed address means, or the same string
 * reaches two different servers depending on which client the user happens to hold.
 */
data class ServerConfig(val host: String, val port: Int) {

    /** The absolute HTTPS URL the WebView navigates to, and the origin the bridge is scoped to. */
    val baseUrl: String get() = "https://$host:$port"

    val isConfigured: Boolean get() = host.isNotBlank()

    /** What the address-entry field shows when a server is already configured. */
    val displayAddress: String get() = "$host:$port"

    /** Whether [uri] is a page of *this* server. Scheme, host and port must all match (AC-25). */
    fun isSameOrigin(uri: Uri): Boolean {
        if (!uri.scheme.equals("https", ignoreCase = true)) return false
        if (!host.equals(uri.host, ignoreCase = true)) return false
        val uriPort = if (uri.port == -1) DEFAULT_HTTPS_PORT else uri.port
        return uriPort == port
    }

    companion object {
        /** Matches the API's own `Hosting:HttpsPort` default — the single browser-facing Kestrel front door. */
        const val DEFAULT_HTTPS_PORT = 5001

        fun empty(): ServerConfig = ServerConfig(host = "", port = DEFAULT_HTTPS_PORT)

        /**
         * Parses a user-entered address: a bare host (`192.168.1.10`), `host:port`, or a full URL
         * (`https://clinic-server:5001`). A missing or out-of-range port falls back to [DEFAULT_HTTPS_PORT].
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

            val colon = value.lastIndexOf(':')
            if (colon >= 0) {
                host = value.substring(0, colon)
                val parsed = value.substring(colon + 1).toIntOrNull()
                if (parsed != null && parsed in 1..65535) {
                    port = parsed
                }
            }

            return ServerConfig(host = host.trim(), port = port)
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
        )
    }

    fun save(config: ServerConfig) {
        preferences.edit()
            .putString(KEY_HOST, config.host)
            .putInt(KEY_PORT, config.port)
            .apply()
    }

    private companion object {
        const val PREFERENCES_NAME = "clinic-shell-server"
        const val KEY_HOST = "host"
        const val KEY_PORT = "port"
    }
}
