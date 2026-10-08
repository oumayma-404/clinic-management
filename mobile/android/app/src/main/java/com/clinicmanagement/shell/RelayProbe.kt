package com.clinicmanagement.shell

import android.annotation.SuppressLint
import android.content.Context
import android.net.ConnectivityManager
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.Inet6Address
import java.net.InetAddress
import java.net.URL
import java.security.MessageDigest
import java.security.cert.X509Certificate
import java.util.concurrent.Callable
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * `relayProbe()` (`clinic-pc-copy` AC-6.2, since 1.2.0): while the cloud is locked for a PC de secours that said
 * nothing, does THIS phone reach it on the cabinet's network — and which box is it behind? The cloud counts the
 * answer only from the cabinet's own line and box, so a phone on mobile data is never counted (EC-23).
 *
 * ⚠️ **The PC is recognised by its certificate, never by its address** (D21) — anything answering at that address
 * without the PC's exact certificate is « not the PC ». This pins one certificate for one probe and touches nothing
 * else: the WebView's own TLS is unchanged. ⚠️ Only private addresses are tried — the shell must not become a way to
 * make a phone call arbitrary hosts.
 */
object RelayProbe {
    data class Request(val addresses: List<InetAddress>, val port: Int, val fingerprint: String)

    private const val TIMEOUT_MS = 4000

    /** Null unless every part is what the cloud sends: private addresses (up to eight), a port, a SHA-256 (64 hex). */
    fun parse(json: String?): Request? = try {
        val root = JSONObject(json.orEmpty())
        val port = root.optInt("port", 0)
        val fingerprint = normalizeFingerprint(root.optString("fingerprint", ""))
        val list = root.optJSONArray("addresses") ?: JSONArray()
        val addresses = (0 until list.length())
            .mapNotNull { list.optString(it, "").takeIf { s -> s.isNotBlank() } }
            .mapNotNull { literal -> parseLiteral(literal) }
            .filter { isPrivate(it) }
            .distinct()
            .take(8)
        if (port !in 1..65535 || fingerprint == null || addresses.isEmpty()) null
        else Request(addresses, port, fingerprint)
    } catch (e: Exception) {
        null
    }

    /** An IP literal only — never a host name, which would send this phone to DNS on a page's say-so. */
    private fun parseLiteral(literal: String): InetAddress? {
        val text = literal.trim()
        val looksLikeIp = text.isNotEmpty() && text.all { it.isDigit() || it == '.' || it == ':' || it in 'a'..'f' || it in 'A'..'F' }
        return if (looksLikeIp) runCatching { InetAddress.getByName(text) }.getOrNull() else null
    }

    fun isPrivate(address: InetAddress): Boolean = when (address) {
        is Inet4Address -> address.isSiteLocalAddress || address.isLinkLocalAddress
        is Inet6Address -> address.isLinkLocalAddress || (address.address[0].toInt() and 0xFE) == 0xFC
        else -> false
    }

    fun normalizeFingerprint(fingerprint: String?): String? {
        val hex = fingerprint.orEmpty().filter { it.isDigit() || it.lowercaseChar() in 'a'..'f' }.uppercase()
        return if (hex.length == 64) hex else null
    }

    fun sha256Hex(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02X".format(it) }

    /** This phone's default IPv4 gateways — its box, as the cloud compares it with the PC's. */
    fun gateways(context: Context): List<String> = try {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        val network = connectivity?.activeNetwork
        val properties = network?.let { connectivity.getLinkProperties(it) }
        properties?.routes.orEmpty()
            .filter { it.isDefaultRoute && it.gateway is Inet4Address }
            .mapNotNull { it.gateway?.hostAddress }
            .distinct()
            .take(8)
    } catch (e: Exception) {
        emptyList()
    }

    /** Never throws: a probe that cannot run reached nothing. Blocking — call it off the UI thread. */
    fun run(context: Context, request: Request): JSONObject {
        val pool = Executors.newFixedThreadPool(request.addresses.size)
        val reached = try {
            pool.invokeAll(request.addresses.map { address ->
                Callable { reaches(address, request.port, request.fingerprint) }
            }, (TIMEOUT_MS * 2).toLong(), TimeUnit.MILLISECONDS)
                .any { future -> runCatching { future.get() }.getOrDefault(false) }
        } catch (e: Exception) {
            false
        } finally {
            pool.shutdownNow()
        }
        return JSONObject()
            .put("reached", reached)
            .put("gateways", JSONArray(gateways(context)))
    }

    /**
     * Any HTTP answer over a TLS session with the PC's own certificate is the PC alive — an error status included,
     * since « its server answers » is the fact that matters. A refused, timed-out or foreign-certificate attempt is not.
     */
    // A pin, not a trust-all: it accepts exactly one certificate (the PC's own SHA-256) for this one probe, and the
    // WebView's own TLS never sees it. The PC's certificate is self-issued, so no system validation could apply.
    @SuppressLint("CustomX509TrustManager", "BadHostnameVerifier")
    private fun reaches(address: InetAddress, port: Int, fingerprint: String): Boolean {
        var connection: HttpsURLConnection? = null
        return try {
            val pin = object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) =
                    throw java.security.cert.CertificateException("client certificates are not used")

                override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                    val leaf = chain?.firstOrNull() ?: throw java.security.cert.CertificateException("no certificate")
                    if (sha256Hex(leaf.encoded) != fingerprint) {
                        throw java.security.cert.CertificateException("not the PC de secours")
                    }
                }

                override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            }
            val tls = SSLContext.getInstance("TLS").apply { init(null, arrayOf<TrustManager>(pin), null) }
            val host = if (address is Inet6Address) "[${address.hostAddress}]" else address.hostAddress
            connection = (URL("https://$host:$port/health").openConnection() as HttpsURLConnection).apply {
                sslSocketFactory = tls.socketFactory
                // The certificate is pinned above; its name is whatever the PC's server minted for itself.
                hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
                connectTimeout = TIMEOUT_MS
                readTimeout = TIMEOUT_MS
                instanceFollowRedirects = false
                useCaches = false
            }
            connection.responseCode
            true
        } catch (e: Exception) {
            false
        } finally {
            connection?.disconnect()
        }
    }
}
