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
 * port is a transport failure: no route, refused connection, a name that does not resolve, or a timeout **while
 * still connecting**.
 *
 * ⚠️ **A timeout while waiting for the answer is the second thing that counts as answering**, and it is the hosted
 * deployment's normal state rather than an edge case. A managed host that suspends an idle service accepts the TCP
 * connection at its edge immediately and only *then* wakes the application, so the connect succeeds in
 * milliseconds while the first response takes ten seconds or more — measured at **13.4 s** against the live Render
 * front end. Reading that as « nothing on 443 » disqualified the only port a hosted install has, fell through to
 * the LAN candidate, found nothing there either, and left the resolution to the not-found fallback below. The
 * question this probe asks is « is something listening on this port? », and a completed connection has already
 * answered it — so the phase the timeout happened in is what decides, not the timeout itself.
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
     *
     * Both stay short, including the read: a slow answer no longer needs to be *waited* for, because the connect
     * having succeeded is already the whole verdict. Raising the read budget to outlast a cold start would make
     * every genuinely dead candidate cost that same budget, which is the trade the 443-before-5001 ordering exists
     * to avoid.
     */
    private const val CONNECT_TIMEOUT_MS = 4_000
    private const val READ_TIMEOUT_MS = 4_000

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
        // Which phase a timeout lands in is the whole verdict, and `SocketTimeoutException` does not say. So the
        // connect is made explicitly and the fact recorded: anything that times out after this is true was waiting
        // on a server that had already accepted it.
        var connected = false
        return try {
            connection = (URL("https://$host:$port$PATH").openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = CONNECT_TIMEOUT_MS
                readTimeout = READ_TIMEOUT_MS
                setRequestProperty("Accept", "application/json")
            }
            connection.connect()
            connected = true
            // Any status is an answer — 200, 404 on a server too old to have the route, even a 502 from a proxy
            // in front of a starting API. All of them prove something is listening on this port.
            connection.responseCode
            true
        } catch (e: SSLException) {
            // A certificate the phone does not trust — the offline-LAN install's normal state before its CA is
            // imported. Something is listening and speaking TLS, which is all this probe asks. Thrown out of
            // `connect()`, so `connected` is still false and this branch is what has to answer.
            true
        } catch (e: UnknownHostException) {
            false
        } catch (e: SocketTimeoutException) {
            // Connected then silent ⇒ listening but slow (a suspended hosted service waking up) ⇒ answers.
            // Timed out before connecting ⇒ nothing reachable on this port ⇒ does not answer.
            connected
        } catch (e: IOException) {
            false
        } finally {
            connection?.disconnect()
        }
    }
}
