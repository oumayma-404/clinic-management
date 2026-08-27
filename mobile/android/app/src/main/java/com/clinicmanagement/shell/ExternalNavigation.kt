package com.clinicmanagement.shell

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.util.Log
import android.webkit.WebResourceRequest
import android.widget.Toast
import androidx.browser.customtabs.CustomTabColorSchemeParams
import androidx.browser.customtabs.CustomTabsIntent
import androidx.core.content.ContextCompat

/**
 * Anything that is not a page of this clinic's server leaves the WebView (AC-25).
 *
 * The load-bearing case is « Connecter Google Agenda »: Google **refuses to serve its sign-in inside a WebView**
 * (`disallowed_useragent`), so without this the one screen that connects a clinic's calendar shows a Google error
 * page and the WebView is stranded on a foreign origin with no way back. Custom Tabs is a real browser — same
 * cookie jar as Chrome, a visible address, the user's own password manager — so the hand-off works and, because
 * the WebView never navigated, it is never stranded.
 *
 * Non-`http(s)` schemes (`mailto:`, `tel:`) go to the system too. A WebView cannot resolve them and the tap would
 * otherwise do nothing.
 */
class ExternalNavigation(private val activity: Activity) {

    /**
     * Whether an external hand-off is in flight, so the activity knows to refresh on the way back.
     *
     * ⚠️ **This is how the return works, and it is weaker than an App Link.** The OAuth callback lands on the
     * clinic's own origin *inside the Custom Tab*, and nothing in the Custom Tabs API reports which URL a tab
     * reached — only an `intent-filter` with a **verified** App Link would make the tab close and hand the
     * navigation back to the app. That needs a fixed, publicly-resolvable domain, which is one of Part 8's four
     * deferred decisions. Until then the shell reloads the page the user came from as soon as it is resumed, so
     * « Connecter Google Agenda » shows the connected state without a manual refresh — the outcome the criterion
     * asks for, reached by resume rather than by redirect.
     */
    var handOffInFlight: Boolean = false
        private set

    /** Call when the hand-off has been accounted for, so an ordinary resume does not reload. */
    fun consumeHandOff(): Boolean {
        val wasInFlight = handOffInFlight
        handOffInFlight = false
        return wasInFlight
    }

    /**
     * `true` when this navigation has been taken over and the WebView must stay where it is.
     *
     * Only **top-level** navigations are intercepted. A cross-origin subframe (an embedded map, a tracking pixel)
     * is the page's business, and opening a browser tab for one would be a tab the user never asked for.
     */
    fun handle(request: WebResourceRequest, config: ServerConfig): Boolean {
        if (!request.isForMainFrame) return false

        val uri = request.url
        val scheme = uri.scheme?.lowercase()

        if (scheme == "https" && config.isSameOrigin(uri)) return false
        if (scheme == "http" || scheme == "https") return openInBrowser(uri)

        // mailto:, tel:, sms:, geo: — a WebView resolves none of them.
        if (!startViewIntent(uri)) {
            Toast.makeText(activity, R.string.external_open_failed, Toast.LENGTH_LONG).show()
        }
        return true
    }

    private fun openInBrowser(uri: Uri): Boolean {
        val colors = CustomTabColorSchemeParams.Builder()
            .setToolbarColor(ContextCompat.getColor(activity, R.color.brand_primary))
            .setNavigationBarColor(Color.WHITE)
            .build()

        val intent = CustomTabsIntent.Builder()
            .setDefaultColorSchemeParams(colors)
            .setShowTitle(true)
            .setUrlBarHidingEnabled(false)
            .build()

        return try {
            intent.launchUrl(activity, uri)
            handOffInFlight = true
            true
        } catch (e: ActivityNotFoundException) {
            // No Custom Tabs provider: try a plain browser before giving up. Either way the answer is `true` —
            // letting the WebView load an origin it must never hold is the worse of the two outcomes.
            Log.w(TAG, "no Custom Tabs provider", e)
            if (!startViewIntent(uri)) {
                Toast.makeText(activity, R.string.external_open_failed, Toast.LENGTH_LONG).show()
            }
            true
        }
    }

    /** `false` when nothing on the device handles the Uri; the caller decides what to say about it. */
    private fun startViewIntent(uri: Uri): Boolean {
        return try {
            activity.startActivity(Intent(Intent.ACTION_VIEW, uri))
            handOffInFlight = true
            true
        } catch (e: ActivityNotFoundException) {
            Log.w(TAG, "nothing handles $uri", e)
            false
        }
    }

    private companion object {
        const val TAG = "ClinicShell"
    }
}
