package com.clinicmanagement.shell

import android.content.Context
import android.net.ConnectivityManager
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.SocketTimeoutException

/**
 * `clinic-pc-copy` D21 / FR-7 — the PC de secours found again after the box gave it a new address: a broadcast « who is
 * the PC de secours of relay X? » on the cabinet's Wi-Fi, the PC's answer, then a pinned request to the new address.
 * The Windows shell's `RelayDiscovery.cs`, line for line; the server's half is `Infrastructure/Relay/RelayDiscovery.cs`.
 *
 * ⚠️ An answer is a hint, never trust: only its addresses are taken, the port and certificate must be the ones this app
 * already holds, and the address is accepted only once the PC answers through that certificate.
 */
object RelayDiscovery {
    const val PORT = 47950
    const val REQUEST_PREFIX = "APEXA-RELAY-DISCOVER/1 "
    private const val LISTEN_MS = 1500

    fun buildRequest(relayId: String): ByteArray = (REQUEST_PREFIX + relayId.lowercase()).toByteArray(Charsets.US_ASCII)

    /** The private addresses an answer offers for [known] — same relay, port and certificate only; empty otherwise. */
    fun addressesFrom(json: String, known: RelaySwitch.Target): List<String> = try {
        val root = JSONObject(json)
        val relayId = known.relayId
        val list = root.optJSONArray("addresses")
        if (relayId == null || !root.optString("relayId").equals(relayId, ignoreCase = true) ||
            root.optInt("port", 0) != known.port ||
            RelayProbe.normalizeFingerprint(root.optString("fingerprint")) != known.fingerprint || list == null
        ) {
            emptyList()
        } else {
            (0 until list.length())
                .mapNotNull { list.optString(it).takeIf { s -> s.isNotBlank() } }
                .filter { literal -> runCatching { RelayProbe.isPrivate(InetAddress.getByName(literal)) }.getOrDefault(false) }
                .distinct()
                .take(8)
        }
    } catch (e: Exception) {
        emptyList()
    }

    /** Broadcasts, collects answers, returns the PC at the first address reachable through its pinned certificate. Blocking. */
    fun find(context: Context, known: RelaySwitch.Target): RelaySwitch.Target? {
        val relayId = known.relayId ?: return null
        val candidates = mutableListOf<String>()
        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = LISTEN_MS
                val request = buildRequest(relayId)
                for (destination in broadcastAddresses(context)) {
                    runCatching { socket.send(DatagramPacket(request, request.size, destination, PORT)) }
                }
                val deadline = System.currentTimeMillis() + LISTEN_MS
                val buffer = ByteArray(2048)
                while (System.currentTimeMillis() < deadline) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        socket.receive(packet)
                        candidates += addressesFrom(String(packet.data, 0, packet.length, Charsets.UTF_8), known)
                    } catch (e: SocketTimeoutException) {
                        break
                    }
                }
            }
        } catch (e: Exception) {
            return null
        }
        return candidates.distinct()
            .map { known.copy(address = it) }
            .firstOrNull { RelaySwitch.holding(it) != null }
    }

    /** The limited broadcast plus the Wi-Fi network's directed broadcast (some boxes drop the first). */
    private fun broadcastAddresses(context: Context): List<InetAddress> {
        val found = mutableListOf(InetAddress.getByName("255.255.255.255"))
        runCatching {
            val connectivity = context.getSystemService(ConnectivityManager::class.java)
            val properties = connectivity?.activeNetwork?.let { connectivity.getLinkProperties(it) }
            for (link in properties?.linkAddresses.orEmpty()) {
                val address = link.address as? Inet4Address ?: continue
                val bytes = address.address
                val prefix = link.prefixLength
                val mask = if (prefix == 0) 0 else -1 shl (32 - prefix)
                val ip = ((bytes[0].toInt() and 0xFF) shl 24) or ((bytes[1].toInt() and 0xFF) shl 16) or
                    ((bytes[2].toInt() and 0xFF) shl 8) or (bytes[3].toInt() and 0xFF)
                val broadcast = ip or mask.inv()
                found += InetAddress.getByAddress(
                    byteArrayOf((broadcast ushr 24).toByte(), (broadcast ushr 16).toByte(), (broadcast ushr 8).toByte(), broadcast.toByte()),
                )
            }
        }
        return found.distinct()
    }
}
