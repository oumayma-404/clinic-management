package com.clinicmanagement.shell

import android.util.Log
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * What this server requires of a client, read over **native HTTP before the app is loaded** (AC-33, launch half).
 *
 * The web bundle already handles the in-session refusal — a 426 reaches `<ClientVersionGate>` and takes the screen
 * — but that only works once the bundle is running. A build below the floor must be told so at launch instead of
 * loading an app whose every request will be refused.
 *
 * ⚠️ **Unreadable means "no floor", never "refuse"** — the same direction the server's own
 * `ClientRequirements.IsBelowFloor` takes, and for the same reason: this setting's failure mode has to be « nothing
 * is refused ». An offline phone, a server too old to have the route (which is AC-74's native half — an absent
 * route degrades to nothing happening), a malformed body and an unset floor all pass. A shell that refuses to
 * start because a probe failed is a worse outcome than any it could prevent, and the unreachable case is diagnosed
 * far better by the load that follows than by this probe.
 */
object ClientRequirements {

    private const val TAG = "ClinicShell"
    private const val PATH = "/api/meta/client-requirements"
    private const val TIMEOUT_MS = 8_000

    /** What the probe learned. `minimumShellVersion` and `storeUrlAndroid` are blank when the server said nothing. */
    data class Requirements(val minimumShellVersion: String, val storeUrlAndroid: String)

    /** `null` when the answer could not be read at all — see the class note on why that must pass. */
    fun fetch(baseUrl: String): Requirements? {
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL(baseUrl + PATH).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = TIMEOUT_MS
                readTimeout = TIMEOUT_MS
                instanceFollowRedirects = true
                setRequestProperty("Accept", "application/json")
                // The header the floor is measured against. Sent here too so the one route exempt from the floor
                // is still asked the same question every other call asks.
                setRequestProperty("X-Client-Version", BuildConfig.VERSION_NAME)
            }
            if (connection.responseCode != HttpURLConnection.HTTP_OK) return null

            val body = connection.inputStream.bufferedReader().use { it.readText() }
            val json = JSONObject(body)
            Requirements(
                minimumShellVersion = json.optString("minimumShellVersion").orEmpty(),
                storeUrlAndroid = json.optJSONObject("storeUrls")?.optString("android").orEmpty(),
            )
        } catch (t: Throwable) {
            Log.i(TAG, "Client requirements unreadable — proceeding with no floor: ${t.javaClass.simpleName}")
            null
        } finally {
            connection?.disconnect()
        }
    }

    /**
     * Whether [installed] is older than [floor]. **False for anything unparseable**, mirroring the server's
     * `Version.TryParse` pair so the two sides cannot disagree about which builds are acceptable.
     */
    fun isBelowFloor(installed: String, floor: String): Boolean {
        val floorParts = parseVersion(floor) ?: return false
        val installedParts = parseVersion(installed) ?: return false

        for (index in 0 until maxOf(floorParts.size, installedParts.size)) {
            val left = installedParts.getOrElse(index) { 0 }
            val right = floorParts.getOrElse(index) { 0 }
            if (left != right) return left < right
        }
        return false
    }

    /** `1.2.3` → `[1, 2, 3]`. `null` for anything that is not a dotted run of non-negative integers. */
    private fun parseVersion(value: String): List<Int>? {
        val trimmed = value.trim()
        if (trimmed.isEmpty()) return null
        val parts = trimmed.split('.')
        if (parts.size > 4) return null
        return parts.map { part -> part.toIntOrNull()?.takeIf { it >= 0 } ?: return null }
    }
}
