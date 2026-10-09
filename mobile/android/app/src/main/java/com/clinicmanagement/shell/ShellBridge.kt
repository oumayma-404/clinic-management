package com.clinicmanagement.shell

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.Context
import android.net.Uri
import android.print.PrintAttributes
import android.print.PrintManager
import android.util.Base64
import android.util.Log
import android.webkit.JavascriptInterface
import android.webkit.WebView
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.core.net.toUri
import org.json.JSONObject
import java.io.File

/**
 * The native half of `window.__clinicShell`. `mobile/shared/bridge.md` is the contract itself; this class
 * implements it and [injectedScript] is the JavaScript face the web bundle sees.
 *
 * Three native methods (`saveFile`, `print`, `confirmIdentity`) plus a JS-only `onPushToken`, which needs no
 * native counterpart yet because nothing produces a token until Part 6.
 *
 * ⚠️ **Every `@JavascriptInterface` method runs on the WebView's JavaScript thread, not the UI thread.** Anything
 * touching a view, a dialog or the print service must be posted, or it throws from a stack trace that names
 * neither this file nor the page that called it.
 *
 * ⚠️ **A Java exception thrown out of an `@JavascriptInterface` method is not observable in JavaScript** — the
 * WebView logs it and the call simply returns `undefined`. `lib/download.ts` wraps `saveFile` in a `try/catch` and
 * shows a French toast, which on Android can therefore never fire. So the failure message is **native**: this
 * class shows it itself. One message either way, never two and never none.
 */
class ShellBridge(
    private val activity: Activity,
    private val webView: WebView,
    /** The configured server's own page — the only one `relayProbe` answers (it reveals this phone's network). */
    private val isOwnPage: (Uri) -> Boolean = { false },
    /** Part 3: trade a prepared session on the PC de secours and keep it (the activity owns the cookie and the target). */
    private val onRelayPrepare: (RelaySwitch.Prepare, (Boolean) -> Unit) -> Unit = { _, done -> done(false) },
    /** Part 3: the cloud says the cabinet works on the PC — move there if the PC holds. */
    private val onRelaySwitch: ((Boolean) -> Unit) -> Unit = { done -> done(false) },
    /** D23: the cloud's page or the PC de secours's — the only two that may hand over or take a form. */
    private val isClinicPage: (Uri) -> Boolean = isOwnPage,
) {

    /**
     * Hand a file to the OS: write it into app-private cache, then offer to open or share it.
     *
     * The only delivery route that works inside a WebView — a `blob:` download has nowhere to go and
     * `navigator.share` does not exist — which is why `lib/download.ts` tries this first and falls back to the
     * browser paths only when the bridge is absent.
     */
    @JavascriptInterface
    fun saveFile(base64: String?, filename: String?, mimeType: String?) {
        try {
            val bytes = Base64.decode(base64.orEmpty(), Base64.DEFAULT)
            val directory = File(activity.cacheDir, SHARED_DIRECTORY).apply { mkdirs() }
            val file = File(directory, safeFileName(filename))
            file.writeBytes(bytes)

            val uri = FileProvider.getUriForFile(activity, "${BuildConfig.APPLICATION_ID}.files", file)
            val type = mimeType?.takeIf { it.isNotBlank() } ?: "application/octet-stream"
            activity.runOnUiThread { offer(uri, type) }
        } catch (t: Throwable) {
            Log.w(TAG, "saveFile failed", t)
            activity.runOnUiThread { toast(R.string.save_file_failed) }
        }
    }

    /**
     * Print the page through the OS print service (AC-21).
     *
     * Android's WebView has **no** `window.print()` implementation at all, so [injectedScript] routes the page's
     * own `window.print()` here. What comes out honours the app's `@media print` rules — the rail, the header, the
     * bottom bar, the assistant launcher and the toaster are all `print:hidden`, so the document prints as
     * document content, which is what makes this worth wiring rather than leaving to a screenshot.
     */
    @JavascriptInterface
    fun print() {
        activity.runOnUiThread {
            val service = activity.getSystemService(Context.PRINT_SERVICE) as? PrintManager ?: return@runOnUiThread
            val jobName = listOfNotNull(
                activity.getString(R.string.app_name),
                webView.title?.takeIf { it.isNotBlank() },
            ).joinToString(" — ")
            try {
                service.print(jobName, webView.createPrintDocumentAdapter(jobName), PrintAttributes.Builder().build())
            } catch (t: Throwable) {
                Log.w(TAG, "print failed", t)
            }
        }
    }

    /**
     * Ask the OS to confirm the device owner, so a session past the inactivity limit can resume without a
     * password (AC-57…AC-60). [BiometricGate] decides the outcome; this only carries it back.
     *
     * ⚠️ **Asynchronous across a synchronous seam.** An `@JavascriptInterface` method returns a value to
     * JavaScript immediately and cannot hand back a `Promise`, so the wrapper in [injectedScript] parks a
     * resolver under [requestId] and this resolves it through `window.__clinicShellDeliverIdentityResult` —
     * the same shape `onPushToken` uses, and for the same reason.
     */
    @JavascriptInterface
    fun confirmIdentity(requestId: String?) {
        val id = requestId.orEmpty()
        if (id.isEmpty()) return
        activity.runOnUiThread {
            BiometricGate.confirm(activity) { outcome -> deliverIdentityResult(id, outcome) }
        }
    }

    /**
     * AC-6.2 (since 1.2.0): while the cloud is locked for a silent PC de secours, does this phone reach it, and which
     * box is it behind? [RelayProbe] does the work off the UI thread; the answer — or null when the shell cannot
     * answer, and the page then sends no report — comes back through `__clinicShellDeliverRelayResult`.
     */
    @JavascriptInterface
    fun relayProbe(requestId: String?, requestJson: String?) {
        val id = requestId.orEmpty()
        if (!RELAY_REQUEST_ID.matches(id)) return
        val request = RelayProbe.parse(requestJson)
        activity.runOnUiThread {
            val page = webView.url?.let { runCatching { it.toUri() }.getOrNull() }
            if (request == null || page == null || !isOwnPage(page)) {
                deliverRelayResult(id, null)
                return@runOnUiThread
            }
            Thread {
                val answer = runCatching { RelayProbe.run(activity, request) }.getOrNull()
                activity.runOnUiThread { deliverRelayResult(id, answer) }
            }.start()
        }
    }

    /** `relayPrepare` (Part 3, since 1.3.0): only for the configured server's own page; answers `true` / `false`. */
    @JavascriptInterface
    fun relayPrepare(requestId: String?, requestJson: String?) {
        val id = requestId.orEmpty()
        if (!RELAY_REQUEST_ID.matches(id)) return
        val request = RelaySwitch.parsePrepare(requestJson)
        activity.runOnUiThread {
            if (request == null || !onOwnPage()) {
                deliverRelayValue(id, "false")
                return@runOnUiThread
            }
            onRelayPrepare(request) { prepared -> deliverRelayValue(id, prepared.toString()) }
        }
    }

    /** `relaySwitch` (Part 3, since 1.3.0): only for the configured server's own page; answers whether the app moved. */
    @JavascriptInterface
    fun relaySwitch(requestId: String?) {
        val id = requestId.orEmpty()
        if (!RELAY_REQUEST_ID.matches(id)) return
        activity.runOnUiThread {
            if (!onOwnPage()) {
                deliverRelayValue(id, "false")
                return@runOnUiThread
            }
            onRelaySwitch { moved -> deliverRelayValue(id, moved.toString()) }
        }
    }

    /** `carryDraft` (D23): the open form's state, kept in memory for the next clinic page — null or empty forgets it. */
    @JavascriptInterface
    fun carryDraft(draftJson: String?) {
        activity.runOnUiThread {
            if (onClinicPage()) CarriedDraftSlot.carry(draftJson, System.currentTimeMillis())
        }
    }

    /** `takeCarriedDraft` (D23): the held form state, once — then forgotten. */
    @JavascriptInterface
    fun takeCarriedDraft(requestId: String?) {
        val id = requestId.orEmpty()
        if (!RELAY_REQUEST_ID.matches(id)) return
        activity.runOnUiThread {
            val draft = if (onClinicPage()) CarriedDraftSlot.take(System.currentTimeMillis()) else null
            deliverRelayValue(id, draft?.let { JSONObject.quote(it) } ?: "null")
        }
    }

    private fun onClinicPage(): Boolean =
        webView.url?.let { runCatching { it.toUri() }.getOrNull() }?.let(isClinicPage) ?: false

    private fun onOwnPage(): Boolean =
        webView.url?.let { runCatching { it.toUri() }.getOrNull() }?.let(isOwnPage) ?: false

    private fun deliverRelayValue(requestId: String, value: String) {
        activity.runOnUiThread {
            val call = "window.$DELIVER_RELAY_RESULT && window.$DELIVER_RELAY_RESULT(${JSONObject.quote(requestId)}, $value);"
            webView.evaluateJavascript(call, null)
        }
    }

    private fun deliverRelayResult(requestId: String, answer: JSONObject?) {
        val value = answer?.toString() ?: "null"
        val call = "window.$DELIVER_RELAY_RESULT && window.$DELIVER_RELAY_RESULT(${JSONObject.quote(requestId)}, $value);"
        webView.evaluateJavascript(call, null)
    }

    private fun deliverIdentityResult(requestId: String, outcome: String) {
        val call = "window.$DELIVER_IDENTITY_RESULT && window.$DELIVER_IDENTITY_RESULT(" +
            "${JSONObject.quote(requestId)}, ${JSONObject.quote(outcome)});"
        webView.evaluateJavascript(call, null)
    }

    /** `ACTION_VIEW` with `ACTION_SEND` beside it: « ouvrir » is the common case, « partager » the one a dentist
     *  handing a receipt to a patient wants. */
    private fun offer(uri: Uri, mimeType: String) {
        val view = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, mimeType)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        val share = Intent(Intent.ACTION_SEND).apply {
            type = mimeType
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        val chooser = Intent.createChooser(view, activity.getString(R.string.save_file_chooser_title)).apply {
            putExtra(Intent.EXTRA_INITIAL_INTENTS, arrayOf(share))
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        try {
            activity.startActivity(chooser)
        } catch (e: ActivityNotFoundException) {
            Log.w(TAG, "no viewer for $mimeType", e)
            toast(R.string.save_file_no_viewer)
        }
    }

    private fun toast(messageId: Int) {
        Toast.makeText(activity, messageId, Toast.LENGTH_LONG).show()
    }

    companion object {
        private const val TAG = "ClinicShell"

        /** Shared with [FileChooser]: one cache sub-directory, one `file_paths.xml` entry. */
        const val SHARED_DIRECTORY = "shell-files"

        /** The name `addJavascriptInterface` exposes this class under. Never read by the web bundle directly. */
        const val NATIVE_OBJECT = "__clinicShellNative"

        /**
         * Where an async native answer lands. A **separate global**, not a member of `__clinicShell`, exactly as
         * `__clinicShellDeliverPushToken` is: deleting the bridge (AC-26) must not leave a resolver the native
         * side can still call into.
         */
        const val DELIVER_IDENTITY_RESULT = "__clinicShellDeliverIdentityResult"

        /** `relayProbe`'s answer lands here — outside `__clinicShell`, for the same AC-26 reason. */
        const val DELIVER_RELAY_RESULT = "__clinicShellDeliverRelayResult"

        /** A request id the injected script minted: interpolated into JavaScript only after this matched it. */
        private val RELAY_REQUEST_ID = Regex("^r[0-9]{1,10}$")

        /**
         * The largest file this shell accepts through `saveFile`, published as `__clinicShell.maxFileBytes`.
         *
         * Base64 across the bridge costs ~1.33×, so 25 MB arrives as a ~33 MB Java `String` in one allocation.
         * It equals `lib/download.ts`'s documented fallback on purpose — the web bundle uses that constant when
         * the bridge does not state one, and two different ceilings would refuse different files depending on
         * which side answered.
         */
        const val MAX_FILE_BYTES = 25L * 1024 * 1024

        /**
         * Strip any directory part a page supplied. `File(dir, "../../x")` escapes the cache directory, and the
         * name crosses the bridge from JavaScript, so it is untrusted input by construction.
         */
        fun safeFileName(filename: String?): String {
            val name = filename.orEmpty()
                .substringAfterLast('/')
                .substringAfterLast('\\')
                .replace(' ', '_')
                .trim()
            return name.ifBlank { "document" }
        }

        /**
         * The JavaScript face of the bridge, installed before the page's own scripts run.
         *
         * Three things it does beyond declaring the object:
         *
         * 1. `Object.freeze` — the web-side type declares every member `readonly`, and a page that reassigns
         *    `saveFile` would be redefining the shell's contract from inside the sandbox.
         * 2. It is assigned, not `defineProperty`'d non-configurable, so `delete window.__clinicShell` **works** —
         *    AC-26 is verified by deleting it at runtime and checking every affected screen behaves as in a
         *    browser, which a non-deletable property would make untestable.
         * 3. It replaces `window.print` with a shim that prefers the bridge **and falls back to the original**.
         *    That fallback is the AC-26 half people forget: with the bridge deleted, `window.print()` must behave
         *    exactly as it does in a WebView with no shell — i.e. do nothing — rather than throw.
         */
        fun injectedScript(version: String, maxFileBytes: Long): String {
            val quotedVersion = JSONObject.quote(version)
            return """
                (function () {
                  var nativeBridge = window.$NATIVE_OBJECT;
                  if (!nativeBridge) { return; }
                  var pushListeners = [];
                  var pendingIdentity = {};
                  var nextIdentityId = 0;
                  var pendingRelay = {};
                  var nextRelayId = 0;
                  window.__clinicShell = Object.freeze({
                    version: $quotedVersion,
                    platform: "android",
                    maxFileBytes: $maxFileBytes,
                    saveFile: function (base64, filename, mimeType) {
                      nativeBridge.saveFile(base64, filename, mimeType);
                    },
                    print: function () { nativeBridge.print(); },
                    onPushToken: function (listener) {
                      if (typeof listener === "function") { pushListeners.push(listener); }
                    },
                    confirmIdentity: function () {
                      return new Promise(function (resolve) {
                        var id = "id" + (nextIdentityId++);
                        pendingIdentity[id] = resolve;
                        try {
                          nativeBridge.confirmIdentity(id);
                        } catch (e) {
                          // The contract says this never rejects: a bridge that cannot ask is "unavailable",
                          // which the web side already handles as « fall back to the password screen ».
                          delete pendingIdentity[id];
                          resolve("unavailable");
                        }
                      });
                    },
                    relayProbe: function (request) {
                      return new Promise(function (resolve) {
                        var id = "r" + (++nextRelayId);
                        var settled = false;
                        var settle = function (value) {
                          if (settled) { return; }
                          settled = true;
                          delete pendingRelay[id];
                          resolve(value);
                        };
                        pendingRelay[id] = settle;
                        setTimeout(function () { settle(null); }, 20000);
                        try {
                          nativeBridge.relayProbe(id, JSON.stringify({
                            addresses: request && Array.isArray(request.addresses) ? request.addresses : [],
                            port: request && typeof request.port === "number" ? request.port : 0,
                            fingerprint: request && typeof request.fingerprint === "string" ? request.fingerprint : ""
                          }));
                        } catch (e) {
                          settle(null);
                        }
                      });
                    },
                    relayPrepare: function (request) {
                      return new Promise(function (resolve) {
                        var id = "r" + (++nextRelayId);
                        var settled = false;
                        var settle = function (value) {
                          if (settled) { return; }
                          settled = true;
                          delete pendingRelay[id];
                          resolve(value === true);
                        };
                        pendingRelay[id] = settle;
                        setTimeout(function () { settle(false); }, 30000);
                        try {
                          nativeBridge.relayPrepare(id, JSON.stringify({
                            assertion: request && typeof request.assertion === "string" ? request.assertion : "",
                            addresses: request && Array.isArray(request.addresses) ? request.addresses : [],
                            port: request && typeof request.port === "number" ? request.port : 0,
                            fingerprint: request && typeof request.fingerprint === "string" ? request.fingerprint : ""
                          }));
                        } catch (e) {
                          settle(false);
                        }
                      });
                    },
                    carryDraft: function (draft) {
                      try {
                        nativeBridge.carryDraft(typeof draft === "string" ? draft : null);
                      } catch (e) {}
                    },
                    takeCarriedDraft: function () {
                      return new Promise(function (resolve) {
                        var id = "r" + (++nextRelayId);
                        var settled = false;
                        var settle = function (value) {
                          if (settled) { return; }
                          settled = true;
                          delete pendingRelay[id];
                          resolve(typeof value === "string" ? value : null);
                        };
                        pendingRelay[id] = settle;
                        setTimeout(function () { settle(null); }, 5000);
                        try {
                          nativeBridge.takeCarriedDraft(id);
                        } catch (e) {
                          settle(null);
                        }
                      });
                    },
                    relaySwitch: function () {
                      return new Promise(function (resolve) {
                        var id = "r" + (++nextRelayId);
                        var settled = false;
                        var settle = function (value) {
                          if (settled) { return; }
                          settled = true;
                          delete pendingRelay[id];
                          resolve(value === true);
                        };
                        pendingRelay[id] = settle;
                        setTimeout(function () { settle(false); }, 15000);
                        try {
                          nativeBridge.relaySwitch(id);
                        } catch (e) {
                          settle(false);
                        }
                      });
                    }
                  });
                  window.$DELIVER_RELAY_RESULT = function (id, value) {
                    var settle = pendingRelay[id];
                    if (typeof settle === "function") { settle(value); }
                  };
                  window.$DELIVER_IDENTITY_RESULT = function (id, outcome) {
                    var resolve = pendingIdentity[id];
                    if (!resolve) { return; }
                    delete pendingIdentity[id];
                    resolve(outcome);
                  };
                  window.__clinicShellDeliverPushToken = function (token) {
                    for (var i = 0; i < pushListeners.length; i++) {
                      try { pushListeners[i](token); } catch (e) {}
                    }
                  };
                  var fallbackPrint = typeof window.print === "function" ? window.print.bind(window) : function () {};
                  window.print = function () {
                    var shell = window.__clinicShell;
                    if (shell && typeof shell.print === "function") { shell.print(); return; }
                    fallbackPrint();
                  };
                })();
            """.trimIndent()
        }
    }
}
