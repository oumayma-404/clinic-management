package com.clinicmanagement.shell

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import android.util.Log
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/**
 * The sideloaded APK's way of updating itself (`android-in-app-update`) — the Android twin of the desktop shell's
 * `ShellUpdater.cs`. It downloads the APK the cabinet's own server publishes, checks its SHA-256, and hands it to
 * Android's installer, which asks the user « Mettre à jour cette application ? ».
 *
 * ⚠️ **Android always asks.** A sideloaded app cannot install anything silently; what this removes is the browser,
 * the Downloads folder and the file manager — not the system's own confirmation. The first update also meets
 * Android's « Installer des applis inconnues » screen for APEXA, once, and Android resumes the install after it.
 *
 * ⚠️ **Every failure is silent at the call site's discretion** — offline, a server with no APK, a truncated
 * download all mean « not today », exactly as on the desktop. Only the hash mismatch is worth naming, and even
 * that is reported as a failed download: a user cannot act on « the bytes differ ».
 *
 * ⚠️ **The same signing key, always.** Android refuses an update signed by another key, whatever this class does.
 */
object ShellUpdater {

    private const val TAG = "ClinicShell"
    private const val CONNECT_TIMEOUT_MS = 15_000
    private const val READ_TIMEOUT_MS = 30_000

    /** A shell APK is a few megabytes; anything near this is not one, and must not fill the cache. */
    private const val MAX_APK_BYTES = 150L * 1024 * 1024

    /** What the server serves the APK as (`MetaController.AndroidPackageContentType`). */
    const val APK_MIME_TYPE = "application/vnd.android.package-archive"

    /** The broadcast PackageInstaller reports back on — package-scoped, received by MainActivity only. */
    const val INSTALL_STATUS_ACTION = "com.clinicmanagement.shell.INSTALL_STATUS"

    data class Available(val version: String, val downloadUrl: String, val sha256: String)

    /** A newer release this shell can fetch, or `null` — none published, no URL, or this build is already it. */
    fun availableUpdate(requirements: ClientRequirements.Requirements?): Available? {
        val current = requirements?.currentVersion?.trim().orEmpty()
        val url = requirements?.downloadUrl?.trim().orEmpty()
        if (current.isEmpty() || !url.startsWith("https://")) return null
        if (!ClientRequirements.isBelowFloor(BuildConfig.VERSION_NAME, current)) return null
        return Available(current, url, requirements?.sha256?.trim().orEmpty())
    }

    /**
     * Downloads [update] into the app's cache and returns the file, or `null` on any failure. **Blocking** — call it
     * off the UI thread. [onProgress] receives 0–100, or `null` when the server sent no length.
     *
     * The bytes go to a `.part` file and are hashed as they stream; only a file whose hash matches is renamed into
     * place, so a half-written or tampered APK is never handed to the installer.
     */
    fun download(context: Context, update: Available, onProgress: (Int?) -> Unit): File? {
        val folder = File(context.cacheDir, "updates").apply { mkdirs() }
        // One update at a time: an older APK left behind is dead weight in a cache the OS may clear anyway.
        folder.listFiles()?.forEach { it.delete() }

        val part = File(folder, "APEXA-${update.version}.apk.part")
        val apk = File(folder, "APEXA-${update.version}.apk")
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL(update.downloadUrl).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = CONNECT_TIMEOUT_MS
                readTimeout = READ_TIMEOUT_MS
                instanceFollowRedirects = true
                setRequestProperty("X-Client-Version", BuildConfig.VERSION_NAME)
                setRequestProperty("X-Client-Platform", ClientRequirements.PLATFORM)
            }
            if (connection.responseCode != HttpURLConnection.HTTP_OK) {
                Log.i(TAG, "update download refused: HTTP ${connection.responseCode}")
                return null
            }

            val length = connection.contentLengthLong
            if (length > MAX_APK_BYTES) return null

            val digest = MessageDigest.getInstance("SHA-256")
            var received = 0L
            var lastPercent = -1
            connection.inputStream.use { input ->
                part.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        received += read
                        if (received > MAX_APK_BYTES) return null
                        digest.update(buffer, 0, read)
                        output.write(buffer, 0, read)

                        val percent = if (length > 0) ((received * 100) / length).toInt() else null
                        if (percent == null || percent != lastPercent) {
                            lastPercent = percent ?: lastPercent
                            onProgress(percent)
                        }
                    }
                }
            }

            if (length > 0 && received != length) return null

            // An empty hash means the server could not state one; Android still re-checks the signature.
            val actual = digest.digest().joinToString("") { "%02X".format(it) }
            if (update.sha256.isNotEmpty() && !actual.equals(update.sha256, ignoreCase = true)) {
                Log.w(TAG, "update refused: SHA-256 does not match what the server published")
                return null
            }

            if (!part.renameTo(apk)) return null
            apk
        } catch (t: Throwable) {
            Log.i(TAG, "update download failed: ${t.javaClass.simpleName}")
            null
        } finally {
            connection?.disconnect()
            part.delete()
        }
    }

    /**
     * Hands [apk] to Android's installer. **Blocking** for the copy — call it off the UI thread. The outcome arrives
     * as a broadcast on [INSTALL_STATUS_ACTION]: `STATUS_PENDING_USER_ACTION` carries the confirmation screen to
     * start, and on success Android replaces this app and ends its process.
     */
    fun install(context: Context, apk: File): Boolean = try {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
        }
        val sessionId = installer.createSession(params)
        installer.openSession(sessionId).use { session ->
            apk.inputStream().use { input ->
                session.openWrite("apexa.apk", 0, apk.length()).use { output ->
                    input.copyTo(output)
                    session.fsync(output)
                }
            }

            // Mutable from API 31: the installer writes the status and the confirmation intent INTO this intent.
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or
                (if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0)
            val status = PendingIntent.getBroadcast(
                context,
                sessionId,
                Intent(INSTALL_STATUS_ACTION).setPackage(context.packageName),
                flags,
            )
            session.commit(status.intentSender)
        }
        true
    } catch (t: Throwable) {
        Log.w(TAG, "update could not be handed to the installer", t)
        false
    }
}
