package com.clinicmanagement.shell

import android.content.Context
import android.util.Log
import androidx.webkit.WebViewCompat

/**
 * Whether this device's **renderer** is new enough to display the app.
 *
 * ⚠️ **The OS version is not the floor — the WebView version is, and it is invisible from the manifest.**
 * `minSdk 26` says nothing useful here: Android System WebView updates through the Play Store independently of
 * the OS, so an Android 10 phone can carry a 2020 renderer for years. Found on a physical Galaxy S9 running
 * WebView **81** (April 2020): the app loaded, every request succeeded, and the screen rendered with **no CSS at
 * all** — correct HTML, unusable page, no error anywhere.
 *
 * The cause is not incidental. `web/` is built with **Tailwind CSS v4**, which requires Chrome 111+: it emits
 * cascade layers (`@layer`), `oklch()` colours and `@property`. A renderer that does not understand `@layer`
 * discards **the whole stylesheet** rather than degrading — so the failure is total and silent, which is why it
 * needs a check rather than a note in a README.
 *
 * ⚠️ **Unreadable means "no floor", never "refuse"** — the same direction [ClientRequirements.isBelowFloor]
 * takes. A device whose WebView package cannot be read, or whose version does not parse, is allowed through: a
 * shell that refuses to start because it could not identify a renderer is a worse outcome than any it prevents.
 */
object WebViewRequirements {

    private const val TAG = "ClinicShell"

    /**
     * Chrome 111 — Tailwind v4's own stated floor, and therefore the app's.
     *
     * It belongs here rather than in `web/` because only the shell can *act* on it: a browser that is too old
     * cannot be told to update itself, while the shell can name the package and open its store listing.
     */
    const val MINIMUM_MAJOR = 111

    /** What the device reports. Any field may be null — see the class note on why that must pass. */
    data class Installed(val packageName: String?, val versionName: String?, val major: Int?)

    fun read(context: Context): Installed {
        val info = try {
            WebViewCompat.getCurrentWebViewPackage(context)
        } catch (t: Throwable) {
            Log.i(TAG, "WebView package unreadable — proceeding with no renderer floor", t)
            null
        }
        val version = info?.versionName
        val installed = Installed(info?.packageName, version, majorOf(version))
        Log.i(TAG, "WebView: ${installed.packageName} ${installed.versionName} (major ${installed.major})")
        return installed
    }

    fun isBelowFloor(installed: Installed): Boolean {
        val major = installed.major ?: return false
        return major < MINIMUM_MAJOR
    }

    /** `81.0.4044.138` → `81`. Null for anything that is not a leading non-negative integer. */
    private fun majorOf(versionName: String?): Int? =
        versionName?.substringBefore('.')?.trim()?.toIntOrNull()?.takeIf { it >= 0 }
}
