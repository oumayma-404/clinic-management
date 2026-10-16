package com.clinicmanagement.shell

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.core.content.edit
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage

/**
 * Phone notifications (staff-chat Part 7): the two channels, the Firebase token, and what the page is told.
 *
 * ⚠️ **Nothing here touches Firebase when the build carries no `google-services.json`** (`BuildConfig.PUSH_CONFIGURED`
 * false): `FirebaseMessaging.getInstance()` throws on an app Firebase never initialised, and an absent config is a
 * supported build, not a broken one.
 *
 * ⚠️ **The server sends a `notification` payload, so Android draws the banner itself** while the app is in the
 * background or the screen is off — this code builds no notification. In the foreground [ShellMessagingService]
 * receives the message and does nothing, because the page on screen already rang.
 */
object ShellPush {
    /** The ids the server's `FcmPushSender` names — renaming one here silently routes its banners to « Divers ». */
    const val MESSAGES_CHANNEL = "messages"
    const val AGENDA_CHANNEL = "agenda"

    private const val TAG = "ClinicShell"
    private const val PREFS = "shell-push"
    private const val ASKED = "permission-asked"

    @Volatile
    private var onToken: ((String) -> Unit)? = null

    val isConfigured: Boolean get() = BuildConfig.PUSH_CONFIGURED

    /** Both channels, created at every start — creating one that exists is a no-op that keeps the user's own choices. */
    fun createChannels(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        manager.createNotificationChannel(
            NotificationChannel(MESSAGES_CHANNEL, context.getString(R.string.push_channel_messages), NotificationManager.IMPORTANCE_HIGH),
        )
        manager.createNotificationChannel(
            NotificationChannel(AGENDA_CHANNEL, context.getString(R.string.push_channel_agenda), NotificationManager.IMPORTANCE_DEFAULT),
        )
    }

    /** Ask Firebase for this install's token; [deliver] receives it now and again whenever Firebase rotates it. */
    fun requestToken(deliver: (String) -> Unit) {
        if (!isConfigured) return
        onToken = deliver
        try {
            FirebaseMessaging.getInstance().token
                .addOnSuccessListener { token -> if (!token.isNullOrBlank()) deliver(token) }
                .addOnFailureListener { error -> Log.w(TAG, "push token unavailable", error) }
        } catch (t: Throwable) {
            Log.w(TAG, "Firebase not initialised", t)
        }
    }

    /** Called by [ShellMessagingService] when Firebase replaces the token. */
    internal fun tokenRotated(token: String) {
        onToken?.invoke(token)
    }

    /** The page lost its WebView (activity destroyed): stop handing tokens to it. */
    fun forget() {
        onToken = null
    }

    /** `granted` · `default` (Android 13+, never asked) · `denied` · `unavailable` (this build cannot receive any). */
    fun state(context: Context): String = when {
        !isConfigured -> "unavailable"
        NotificationManagerCompat.from(context).areNotificationsEnabled() -> "granted"
        needsRuntimePermission(context) && !wasAsked(context) -> "default"
        else -> "denied"
    }

    /** True when Android 13+ still has to show its permission prompt for this app. */
    fun needsRuntimePermission(context: Context): Boolean =
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED

    fun wasAsked(context: Context): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(ASKED, false)

    fun markAsked(context: Context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit { putBoolean(ASKED, true) }
    }
}

/** Firebase's entry point. Only the token matters here — the banner itself is drawn by Android (see [ShellPush]). */
class ShellMessagingService : FirebaseMessagingService() {
    override fun onNewToken(token: String) {
        ShellPush.tokenRotated(token)
    }

    // In the foreground the page on screen already alerted; a second, system banner would be noise.
    override fun onMessageReceived(message: RemoteMessage) = Unit
}
