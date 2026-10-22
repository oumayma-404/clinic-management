package com.clinicmanagement.shell

import android.content.Context
import android.net.Uri
import android.net.http.SslCertificate
import android.os.Build
import androidx.core.content.edit
import org.json.JSONObject
import java.net.Inet6Address
import java.net.InetAddress
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

/**
 * The app follows the PC de secours (`clinic-pc-copy` Part 3, since 1.3.0) — the Android twin of the Windows shell's
 * `RelaySwitch.cs`: while online it holds a session prepared on the PC (D22), and when the cloud cannot be reached and
 * the PC says it holds the cabinet's saves, the WebView moves there — already signed in — and back once the PC lets go.
 *
 * ⚠️ **One holder only.** The session traded here is written straight into the WebView's cookie for the PC's origin and
 * never refreshed by the shell: the PC's refresh chain treats a reused credential as theft. The page brings a fresh
 * ticket each day and the cookie is replaced.
 *
 * ⚠️ The PC is recognised by its certificate (D21), as [RelayProbe] does: only the private addresses the cloud named,
 * and only behind the PC's own certificate.
 */
object RelaySwitch {
    /**
     * Where the PC de secours answers: one address, its port, its certificate — and which relay it is, so the app can
     * find it again by UDP discovery when the box gives it a new address (D21). A target saved before that has no id.
     */
    data class Target(val address: String, val port: Int, val fingerprint: String, val relayId: String? = null) {
        val host: String get() = if (address.contains(':')) "[$address]" else address
        val origin: String get() = "https://$host:$port"
    }

    data class Prepare(val assertion: String, val where: RelayProbe.Request, val relayId: String? = null)

    data class Session(val credential: String, val expiresAt: Date?, val mustChangePassword: Boolean)

    const val SESSION_COOKIE = "__Host-local_session"
    const val MUST_CHANGE_COOKIE = "__Host-local_must_change_password"

    private const val PREFERENCES = "relay_switch"
    private val GUID = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
    private const val TIMEOUT_MS = 5000

    // ---- the page's request ---------------------------------------------------------------------------------

    /** The page's `relayPrepare` payload: the cloud's ticket and where the PC is. Null on anything else. */
    fun parsePrepare(json: String?): Prepare? = try {
        val root = JSONObject(json.orEmpty())
        val assertion = root.optString("assertion", "")
        val relayId = root.optString("relayId", "").takeIf { GUID.matches(it) }?.lowercase()
        if (!assertion.startsWith("ra1.") || assertion.length > 4096) null
        else RelayProbe.parse(json)?.let { Prepare(assertion, it, relayId) }
    } catch (e: Exception) {
        null
    }

    /** The session in the PC's answer (bare, or inside the API's `value` envelope). Null when there is none. */
    fun parseSession(json: String?): Session? = try {
        var root = JSONObject(json.orEmpty())
        root.optJSONObject("value")?.let { root = it }
        val credential = if (root.opt("refreshToken") is String) root.getString("refreshToken") else ""
        if (credential.isEmpty()) null
        else Session(
            credential,
            parseInstant(root.optString("refreshExpiresAt", "")),
            root.optBoolean("mustChangePassword", false),
        )
    } catch (e: Exception) {
        null
    }

    private fun parseInstant(text: String): Date? {
        if (text.isBlank()) return null
        // `2026-11-08T09:00:00Z` or with a fraction / offset — the API writes ISO 8601.
        val trimmed = text.replace(Regex("\\.\\d+"), "").replace("Z", "+0000").replace(Regex("([+-]\\d{2}):(\\d{2})$"), "$1$2")
        return runCatching {
            SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssZ", Locale.ROOT).apply { timeZone = TimeZone.getTimeZone("UTC") }.parse(trimmed)
        }.getOrNull()
    }

    const val NOT_READY_LINE = "Le PC de secours n'est pas encore prêt : il ne peut pas prendre le relais."
    const val OFFER_LINE = "Internet coupé : le cloud ne répond pas. Le cabinet peut continuer sur le PC de secours."

    /** The PC offers the button ~20 s after its own last answer from the cloud: 25 s is the countdown's estimate. */
    private const val OFFER_EXPECTED_AFTER_MS = 25_000L

    /** Seconds left on the countdown, in steps of 5 s; 0 once it is due. */
    fun secondsLeft(cloudLostAtMs: Long, nowMs: Long): Int {
        val left = OFFER_EXPECTED_AFTER_MS - (nowMs - cloudLostAtMs)
        return if (left <= 0) 0 else Math.ceil(left / 5000.0).toInt() * 5
    }

    /**
     * What the unreachable screen says while the PC answers but does not hold (owner, 2026-10-12): not ready, the offer
     * beside its button, or — until the PC offers it — when it will, never a bare « impossible de joindre ».
     */
    fun waitingLine(armed: Boolean?, canTakeOver: Boolean, secondsLeft: Int): String = when {
        armed == false -> NOT_READY_LINE
        canTakeOver -> OFFER_LINE
        secondsLeft > 0 -> "Internet coupé — le PC de secours sera disponible dans $secondsLeft secondes."
        else -> "Internet coupé — le PC de secours sera disponible dans quelques secondes."
    }

    /**
     * Does the cloud answer at all? Asked when the open page says a request got no reply, so a blip on one request does
     * not throw the app off the page. Any HTTP answer, or a certificate the phone refuses, is « yes ». Blocking.
     */
    fun cloudAnswers(baseUrl: String): Boolean = try {
        val connection = java.net.URL(baseUrl.trimEnd('/') + "/api/auth/mode").openConnection() as java.net.HttpURLConnection
        connection.connectTimeout = 3000
        connection.readTimeout = 5000
        try {
            connection.responseCode
            true
        } finally {
            connection.disconnect()
        }
    } catch (e: javax.net.ssl.SSLException) {
        true
    } catch (e: Exception) {
        false
    }

    /** The PC's word at its last answer: ready for a cut (null: an older PC), and whether it offers the button. */
    @Volatile var lastArmed: Boolean? = null
        private set

    @Volatile var lastCanTakeOver: Boolean = false
        private set

    /** The two cookie strings the PC's own sign-in route writes — `HttpOnly`, `Secure`, `Lax`, its expiry. */
    fun cookies(session: Session): Pair<String, String> {
        val expires = session.expiresAt?.let {
            "; Expires=" + SimpleDateFormat("EEE, dd MMM yyyy HH:mm:ss 'GMT'", Locale.US)
                .apply { timeZone = TimeZone.getTimeZone("GMT") }.format(it)
        }.orEmpty()
        val attributes = "; Path=/; Secure; HttpOnly; SameSite=Lax"
        val sessionCookie = "$SESSION_COOKIE=${session.credential}$attributes$expires"
        val mustChange = if (session.mustChangePassword) {
            "$MUST_CHANGE_COOKIE=1$attributes"
        } else {
            // Cleared: an expiry in the past removes it.
            "$MUST_CHANGE_COOKIE=; Max-Age=0$attributes"
        }
        return sessionCookie to mustChange
    }

    // ---- where the PC is, kept between runs -----------------------------------------------------------------

    fun load(context: Context): Target? {
        val prefs = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        val address = prefs.getString("address", null) ?: return null
        val port = prefs.getInt("port", 0)
        val fingerprint = RelayProbe.normalizeFingerprint(prefs.getString("fingerprint", null)) ?: return null
        return if (port in 1..65535) Target(address, port, fingerprint, prefs.getString("relayId", null)) else null
    }

    /** Keeps where the PC is — and, when a session was just traded, when it ends (for a move after discovery, D21). */
    fun save(context: Context, target: Target, sessionExpires: Date? = null) {
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).edit {
            putString("address", target.address)
            putInt("port", target.port)
            putString("fingerprint", target.fingerprint)
            if (target.relayId != null) putString("relayId", target.relayId) else remove("relayId")
            if (sessionExpires != null) putLong("sessionExpires", sessionExpires.time)
        }
    }

    /** When the prepared session ends, as traded — null when unknown. */
    fun sessionExpires(context: Context): Date? =
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).getLong("sessionExpires", 0L)
            .takeIf { it > 0 }?.let { Date(it) }

    /** The value of [name] in a `CookieManager.getCookie` string (`a=1; b=2`), or null. */
    fun cookieValue(cookies: String?, name: String): String? =
        cookies.orEmpty().split(';').map { it.trim() }
            .firstOrNull { it.startsWith("$name=") }?.substringAfter('=')?.takeIf { it.isNotEmpty() }

    /** True when [uri] is the PC de secours's own origin — host and port, like the server's. */
    fun isPcOrigin(uri: Uri?, target: Target?): Boolean {
        if (uri == null || target == null || !uri.scheme.equals("https", ignoreCase = true)) return false
        val host = uri.host?.trim('[', ']') ?: return false
        return host.equals(target.address, ignoreCase = true) && uri.port == target.port
    }

    /** Whether the certificate WebView refused is exactly the PC's own. */
    fun isThePcsCertificate(certificate: SslCertificate?, fingerprint: String): Boolean {
        val der = certificate?.let { derOf(it) } ?: return false
        return RelayProbe.sha256Hex(der) == fingerprint
    }

    private fun derOf(certificate: SslCertificate): ByteArray? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            runCatching { certificate.x509Certificate?.encoded }.getOrNull()
        } else {
            // Before API 29 the DER is only reachable through the saved state, under this key (unchanged since API 1).
            runCatching { SslCertificate.saveState(certificate).getByteArray("x509-certificate") }.getOrNull()
        }

    // ---- the PC, through its pinned certificate ---------------------------------------------------------------

    /** Trades the ticket on the first address that answers with the PC's certificate. Blocking; never throws. */
    fun trade(request: Prepare): Pair<Target, Session>? {
        for (address in request.where.addresses) {
            val target = Target(literal(address), request.where.port, request.where.fingerprint, request.relayId)
            val session = runCatching {
                val connection = RelayProbe.pinnedConnection("${target.origin}/api/auth/relay-session", target.fingerprint, TIMEOUT_MS)
                try {
                    connection.requestMethod = "POST"
                    connection.doOutput = true
                    connection.setRequestProperty("Content-Type", "application/json")
                    connection.outputStream.use {
                        it.write(JSONObject().put("assertion", request.assertion).toString().toByteArray(Charsets.UTF_8))
                    }
                    if (connection.responseCode in 200..299) {
                        parseSession(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() })
                    } else {
                        null
                    }
                } finally {
                    connection.disconnect()
                }
            }.getOrNull()
            if (session != null) return target to session
        }
        return null
    }

    /** Whether the PC holds the cabinet's saves: true / false, or null when it cannot be reached as itself. Blocking. */
    fun holding(target: Target): Boolean? = runCatching {
        val connection = RelayProbe.pinnedConnection("${target.origin}/api/relay/local/holding", target.fingerprint, TIMEOUT_MS)
        try {
            if (connection.responseCode !in 200..299) {
                null
            } else {
                val json = JSONObject(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() })
                lastArmed = if (json.has("armed") && !json.isNull("armed")) json.optBoolean("armed") else null
                lastCanTakeOver = json.optBoolean("canTakeOver", false)
                json.optBoolean("holding", false)
            }
        } finally {
            connection.disconnect()
        }
    }.getOrNull()

    /** « Travailler sur le PC de secours »: true once the PC holds the cabinet's saves. Blocking; never throws. */
    fun takeOver(target: Target): Boolean = runCatching {
        // The PC waits for its own takeover (up to 10 s) before it answers.
        val connection = RelayProbe.pinnedConnection("${target.origin}/api/relay/local/take-over", target.fingerprint, 15_000)
        try {
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json")
            connection.outputStream.use { it.write("{}".toByteArray(Charsets.UTF_8)) }
            connection.responseCode in 200..299 &&
                JSONObject(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }).optBoolean("holding", false)
        } finally {
            connection.disconnect()
        }
    }.getOrDefault(false)

    private fun literal(address: InetAddress): String =
        (address.hostAddress ?: "").let { if (address is Inet6Address) it.substringBefore('%') else it }
}
