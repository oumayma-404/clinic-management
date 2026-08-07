package com.clinicmanagement.shell

import android.util.Log
import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URL
import java.net.UnknownHostException
import javax.net.ssl.SSLException

/**
 * Settles which port a typed address means, when the user did not say.
 *
 * The rule is identical in all three clients (desktop, Android, iOS) — see `mobile/CLAUDE.md` § « the port rule ».
 * An address with an explicit port is used verbatim and never probed. An address without one is tried against
 * [ServerConfig.candidatePorts] in order, and the first port that **answers at all** wins.
 *
 * ⚠️ « Answers » deliberately includes a TLS failure. An offline-LAN server presents a certificate signed by a CA
 * the phone may not have imported yet, so a handshake rejection is the *expected* outcome of probing a live clinic
 * server — treating it as « nothing here » would send every LAN install to the wrong port. What disqualifies a
 * port is a transport failure: no route, refused connection, timeout, or a name that does not resolve.
 *
 * ⚠️ Blocking I/O, exactly like [ClientRequirements.fetch], and called from the same background executor. It runs
 * *before* that fetch because the fetch needs a base URL, and until this has run there is no base URL to give it.
 */
object ServerProbe {

    private const val TAG = "ClinicShell"

    /**
     * The route asked for. Anonymous and exempt from the client-version floor, so it answers a shell of any age —
     * which is what makes it usable as a reachability probe rather than only as a version read.
     */
    private const val PATH = "/api/meta/client-requirements"

    /**
     * Per-candidate budget. Short on purpose: this runs behind the « Connexion… » screen, and the worst case is
     * paid once per address, not once per launch.
     */
    private const val TIMEOUT_MS = 4_000

    /**
     * The config to actually connect with. Returns [config] unchanged when its port is already explicit, so the
     * common case costs no network at all.
     *
     * When **no** candidate answers, the first candidate is returned rather than nothing: the address is simply
     * wrong or the server is off, and that is diagnosed far better by the load that follows — which shows the
     * unreachable screen naming the address — than by a second error state of this probe's own.
     */
    fun resolve(config: ServerConfig): ServerConfig {
        if (config.portIsExplicit || !config.isConfigured) return config

        for (port in config.candidatePorts) {
            if (answers(config.host, port)) {
                Log.i(TAG, "Server address resolved to port $port")
                return config.withResolvedPort(port)
            }
        }

        return config.withResolvedPort(config.candidatePorts.first())
    }

    private fun answers(host: String, port: Int): Boolean {
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL("https://$host:$port$PATH").openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = TIMEOUT_MS
                readTimeout = TIMEOUT_MS
                setRequestProperty("Accept", "application/json")
            }
            // Any status is an answer — 200, 404 on a server too old to have the route, even a 502 from a proxy
            // in front of a starting API. All of them prove something is listening on this port.
            connection.responseCode
            true
        } catch (e: SSLException) {
            // A certificate the phone does not trust — the offline-LAN install's normal state before its CA is
            // imported. Something is listening and speaking TLS, which is all this probe asks.
            true
        } catch (e: UnknownHostException) {
            false
        } catch (e: SocketTimeoutException) {
            false
        } catch (e: IOException) {
            false
        } finally {
            connection?.disconnect()
        }
    }
}
