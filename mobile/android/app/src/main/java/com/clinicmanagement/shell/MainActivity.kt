package com.clinicmanagement.shell

import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.view.inputmethod.EditorInfo
import android.webkit.CookieManager
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import androidx.webkit.ScriptHandler
import java.util.concurrent.Executors

/**
 * The whole Android shell: one activity rendering the clinic server's own web bundle full-screen, with the five
 * French states of AC-15 in front of it.
 *
 * Its Windows sibling is `desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs`, and the shape is deliberately
 * the same — one view with mutually-exclusive panels switched by visibility — including the detail that cost that
 * shell a bug: the retry path calls **`reload()`**, never a re-assigned URL, or « Réessayer » does nothing when the
 * address has not changed.
 */
class MainActivity : ComponentActivity() {

    private enum class ShellState { WebPage, Connecting, ServerAddress, Unreachable, UpdateRequired }

    private lateinit var contentRoot: FrameLayout
    private lateinit var webView: WebView
    private lateinit var panelConnecting: View
    private lateinit var panelConfig: View
    private lateinit var panelUnreachable: View
    private lateinit var panelUpdate: View

    private lateinit var store: ServerConfigStore
    private lateinit var fileChooser: FileChooser
    private lateinit var externalNavigation: ExternalNavigation

    private var config = ServerConfig.empty()
    private var state = ShellState.Connecting
    private var webViewConfigured = false
    private var mainFrameFailed = false
    private var bridgeScript: ScriptHandler? = null
    private var storeUrl = ""

    /** One thread for the launch probe. A coroutine dependency for a single GET is not worth its version pairing. */
    private val background = Executors.newSingleThreadExecutor()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        contentRoot = findViewById(R.id.content_root)
        webView = findViewById(R.id.web_view)
        panelConnecting = findViewById(R.id.panel_connecting)
        panelConfig = findViewById(R.id.panel_config)
        panelUnreachable = findViewById(R.id.panel_unreachable)
        panelUpdate = findViewById(R.id.panel_update)

        store = ServerConfigStore(this)
        // Both must be built here: `registerForActivityResult` throws once the activity has reached STARTED.
        fileChooser = FileChooser(this)
        externalNavigation = ExternalNavigation(this)

        applyWindowInsets()
        wireControls()
        installBackHandler()

        config = store.load()
        if (config.isConfigured) startSession() else showServerAddress()
    }

    /**
     * Keep the page inside the safe area (AC-22).
     *
     * An app targeting SDK 35 draws edge-to-edge on Android 15 whether it asks to or not, and
     * `setDecorFitsSystemWindows(true)` no longer opts out. Consuming the insets as padding reproduces the
     * behaviour every version had: the WebView occupies only the usable area, so the app's own
     * `--bottom-inset` (bar height + `env(safe-area-inset-bottom)`) clears the gesture bar because the viewport
     * ends above it — rather than depending on whether this WebView build reports the navigation bar through
     * `env()`, which is version-dependent and untestable from here.
     *
     * The IME inset is folded into the same bottom padding, so the keyboard shrinks the viewport and the app's
     * `dvh`-sized sheets keep their sticky footers reachable.
     */
    private fun applyWindowInsets() {
        ViewCompat.setOnApplyWindowInsetsListener(contentRoot) { view, insets ->
            val bars = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout(),
            )
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            view.setPadding(bars.left, bars.top, bars.right, maxOf(bars.bottom, ime.bottom))
            WindowInsetsCompat.CONSUMED
        }
    }

    private fun wireControls() {
        findViewById<Button>(R.id.config_save).setOnClickListener { saveServerAddress() }
        findViewById<Button>(R.id.config_cancel).setOnClickListener { startSession() }
        findViewById<EditText>(R.id.config_address).setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_GO) {
                saveServerAddress()
                true
            } else {
                false
            }
        }

        findViewById<Button>(R.id.unreachable_retry).setOnClickListener { startSession() }
        findViewById<Button>(R.id.unreachable_change_server).setOnClickListener { showServerAddress() }
        findViewById<Button>(R.id.update_retry).setOnClickListener { startSession() }
        findViewById<Button>(R.id.update_change_server).setOnClickListener { showServerAddress() }
        findViewById<Button>(R.id.update_open_store).setOnClickListener { openStoreListing() }
    }

    /**
     * The back gesture navigates **within** the app (AC-24), and at the root it opens the « Serveur » actions
     * rather than closing the app outright.
     *
     * That is also where « Recharger » and « Changer de serveur… » live. The desktop shell keeps them in a menu
     * bar; here a permanent strip of chrome would contradict AC-13, so they hang off the one gesture that is
     * already free at the root — which additionally turns an accidental back-out into a deliberate « Quitter ».
     */
    private fun installBackHandler() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when {
                    state == ShellState.WebPage && webView.canGoBack() -> webView.goBack()
                    state == ShellState.WebPage -> showServerMenu()
                    // Backing out of the address screen or an error state returns to the configured server.
                    config.isConfigured -> startSession()
                    else -> finish()
                }
            }
        })
    }

    private fun showServerMenu() {
        val actions = arrayOf(
            getString(R.string.menu_reload),
            getString(R.string.menu_change_server),
            getString(R.string.menu_quit),
        )
        AlertDialog.Builder(this)
            .setTitle(R.string.menu_title)
            .setItems(actions) { _, index ->
                when (index) {
                    0 -> startSession()
                    1 -> showServerAddress()
                    else -> finish()
                }
            }
            .setNegativeButton(R.string.config_cancel, null)
            .show()
    }

    // ── Session ───────────────────────────────────────────────────────────────────────────────────────────

    /**
     * Ask the server what it requires, then load the app.
     *
     * The floor is read over **native HTTP before anything is loaded** (AC-33): a build below it never reaches the
     * app, so no session is opened and no request is made that the server would refuse. The WebView is inflated
     * with the layout, but it is neither configured nor sent a URL until the check passes.
     */
    private fun startSession() {
        if (!config.isConfigured) {
            showServerAddress()
            return
        }

        showConnecting()
        val target = config
        background.execute {
            val requirements = ClientRequirements.fetch(target.baseUrl)
            runOnUiThread {
                if (isFinishing || isDestroyed || target != config) return@runOnUiThread
                val floor = requirements?.minimumShellVersion.orEmpty()
                storeUrl = requirements?.storeUrlAndroid.orEmpty()
                if (ClientRequirements.isBelowFloor(BuildConfig.VERSION_NAME, floor)) {
                    showUpdateRequired(floor)
                } else {
                    loadApp()
                }
            }
        }
    }

    private fun loadApp() {
        configureWebView()
        installBridgeScript()
        mainFrameFailed = false
        // `loadUrl` on the same address re-requests it, which is what makes « Réessayer » and « Recharger »
        // re-attempt an unchanged server (`MainWindow.xaml.cs:85-87`'s Navigate(), not Source=).
        webView.loadUrl(config.baseUrl)
    }

    private fun saveServerAddress() {
        val entered = findViewById<EditText>(R.id.config_address).text?.toString()
        val parsed = ServerConfig.parseAddress(entered)
        val error = findViewById<TextView>(R.id.config_error)

        if (!parsed.isConfigured) {
            error.setText(R.string.config_invalid)
            error.visibility = View.VISIBLE
            return
        }

        error.visibility = View.GONE
        config = parsed
        store.save(config)
        // A new origin means a new bridge scope: drop the old document-start script so it cannot be injected
        // into a server it was not granted to.
        removeBridgeScript()
        startSession()
    }

    private fun openStoreListing() {
        val target = storeUrl.trim()
        if (target.isEmpty()) return
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(target)))
        } catch (t: Throwable) {
            Log.w(TAG, "store listing could not be opened", t)
        }
    }

    // ── WebView ───────────────────────────────────────────────────────────────────────────────────────────

    // The shell exists to run the clinic's own web app; JavaScript is the whole point, and the origin it may be
    // enabled for is the one the user configured — enforced by the network config, the mixed-content mode and
    // ExternalNavigation together rather than by withholding the setting.
    @android.annotation.SuppressLint("SetJavaScriptEnabled")
    private fun configureWebView() {
        if (webViewConfigured) return
        webViewConfigured = true

        webView.settings.apply {
            javaScriptEnabled = true
            // Without DOM storage the app's three `localStorage` keys and its sessionStorage key all fail
            // silently: the theme, the sidebar preference and the period selector stop persisting.
            domStorageEnabled = true
            // HTTPS or nothing. It pairs with `cleartextTrafficPermitted="false"` — this closes the
            // subresources, the network config closes the top-level navigation.
            mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
            // The page is served over https and needs no local file access of its own.
            allowFileAccess = false
            javaScriptCanOpenWindowsAutomatically = false
            setSupportMultipleWindows(false)
            useWideViewPort = true
            loadWithOverviewMode = false
            // Pinch-zoom stays available (the 200 % zoom rule) without the deprecated on-screen +/− buttons
            // painting over the app.
            setSupportZoom(true)
            builtInZoomControls = true
            displayZoomControls = false
        }

        CookieManager.getInstance().apply {
            // The session lives in the `local_session` HttpOnly cookie, so this is what makes AC-14 possible:
            // WebView's cookie store is persistent by default, and `flush()` in onPause commits it before the
            // process can be killed.
            setAcceptCookie(true)
            setAcceptThirdPartyCookies(webView, false)
        }

        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG)
        webView.addJavascriptInterface(ShellBridge(this, webView), ShellBridge.NATIVE_OBJECT)

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean =
                externalNavigation.handle(request, config)

            override fun onPageStarted(view: WebView, url: String, favicon: android.graphics.Bitmap?) {
                // The fallback path when `DOCUMENT_START_SCRIPT` is unsupported — see installBridgeScript.
                if (!WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
                    view.evaluateJavascript(bridgeSource(), null)
                }
            }

            override fun onPageFinished(view: WebView, url: String) {
                if (!mainFrameFailed) showWebPage()
            }

            override fun onReceivedError(
                view: WebView,
                request: WebResourceRequest,
                error: WebResourceError,
            ) {
                // Main frame only: a failed subresource is the page's business, and replacing the whole app with
                // an error screen because one image 404'd is the blank-app failure AC-74 forbids.
                if (!request.isForMainFrame) return
                mainFrameFailed = true
                showUnreachable(error.description?.toString().orEmpty())
            }

            // `onReceivedHttpError` is deliberately NOT overridden. An HTTP status means the server answered, and
            // what it answered with is the app's own French error page — which AC-74 requires be *shown* rather
            // than replaced by a shell state. Only a transport failure is the shell's to report.
            // `onReceivedSslError` is deliberately NOT overridden either: the default cancels the load, so an
            // untrusted certificate becomes « Impossible de joindre » instead of a silently accepted MITM.
        }

        webView.webChromeClient = object : WebChromeClient() {
            override fun onShowFileChooser(
                view: WebView,
                filePathCallback: ValueCallback<Array<Uri>>,
                params: FileChooserParams,
            ): Boolean = fileChooser.show(filePathCallback, params)
        }
    }

    /**
     * Install the bridge **before the page's own scripts run**, scoped to this server's origin.
     *
     * `addDocumentStartJavaScript` is the only API that guarantees the ordering, and `client.ts` reads
     * `window.__clinicShell?.version` when it builds its very first request header — a bridge that arrives late
     * is a first call with no `X-Client-Version` on it. The origin rule matters as much as the timing: the
     * `@JavascriptInterface` object is reachable from any page the WebView holds, so restricting *this* wrapper
     * to the configured origin is what keeps `window.__clinicShell` from appearing on a foreign one.
     */
    private fun installBridgeScript() {
        removeBridgeScript()

        if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            bridgeScript = try {
                WebViewCompat.addDocumentStartJavaScript(webView, bridgeSource(), setOf(config.baseUrl))
            } catch (t: Throwable) {
                Log.w(TAG, "document-start script rejected — falling back to page-start injection", t)
                null
            }
        } else {
            // Falls back to onPageStarted injection, which runs after document start. Next's bundle executes far
            // later than that, so the bridge is in place before any API call — but it is not *guaranteed*, which
            // is why the supported path is preferred rather than this being the only path.
            Log.i(TAG, "DOCUMENT_START_SCRIPT unsupported — injecting the bridge at page start instead")
        }
    }

    /** Non-null only where the feature is supported, but the check is repeated so the requirement is local. */
    private fun removeBridgeScript() {
        val script = bridgeScript ?: return
        bridgeScript = null
        if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            script.remove()
        }
    }

    private fun bridgeSource(): String =
        ShellBridge.injectedScript(BuildConfig.VERSION_NAME, ShellBridge.MAX_FILE_BYTES)

    // ── State switching ───────────────────────────────────────────────────────────────────────────────────

    private fun show(next: ShellState) {
        state = next
        webView.visibility = if (next == ShellState.WebPage) View.VISIBLE else View.GONE
        panelConnecting.visibility = if (next == ShellState.Connecting) View.VISIBLE else View.GONE
        panelConfig.visibility = if (next == ShellState.ServerAddress) View.VISIBLE else View.GONE
        panelUnreachable.visibility = if (next == ShellState.Unreachable) View.VISIBLE else View.GONE
        panelUpdate.visibility = if (next == ShellState.UpdateRequired) View.VISIBLE else View.GONE
    }

    private fun showWebPage() = show(ShellState.WebPage)

    private fun showConnecting() {
        findViewById<TextView>(R.id.connecting_target).text = config.baseUrl
        show(ShellState.Connecting)
    }

    private fun showServerAddress() {
        findViewById<EditText>(R.id.config_address).setText(
            if (config.isConfigured) config.displayAddress else "",
        )
        findViewById<TextView>(R.id.config_error).visibility = View.GONE
        // A first-run user has nowhere to cancel back to, so the button only exists once a server is configured.
        findViewById<Button>(R.id.config_cancel).visibility =
            if (config.isConfigured) View.VISIBLE else View.GONE
        show(ShellState.ServerAddress)
        findViewById<EditText>(R.id.config_address).requestFocus()
    }

    /** Names both the address and the reason (AC-15) — « ça ne marche pas » is not a diagnosis an operator can act on. */
    private fun showUnreachable(reason: String) {
        findViewById<TextView>(R.id.unreachable_detail).text =
            getString(R.string.unreachable_detail, config.baseUrl, reason)
        show(ShellState.Unreachable)
    }

    private fun showUpdateRequired(floor: String) {
        findViewById<TextView>(R.id.update_detail).text = if (floor.isBlank()) {
            getString(R.string.update_detail_no_floor)
        } else {
            getString(R.string.update_detail, floor, BuildConfig.VERSION_NAME)
        }
        // A button that cannot go anywhere is worse than no button: the operator publishes the listing URL, and
        // a LAN install has no store at all.
        findViewById<Button>(R.id.update_open_store).visibility =
            if (storeUrl.isBlank()) View.GONE else View.VISIBLE
        show(ShellState.UpdateRequired)
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────────────────

    override fun onResume() {
        super.onResume()
        // Coming back from a Custom Tab: the state the user left to change (a connected Google calendar) is on the
        // server now, so the page they came from has to be re-read. See ExternalNavigation.handOffInFlight.
        if (externalNavigation.consumeHandOff() && state == ShellState.WebPage) {
            webView.reload()
        }
    }

    override fun onPause() {
        super.onPause()
        // Commit the session cookie now rather than hoping the process lives long enough for WebView's own
        // periodic flush — a phone kills a backgrounded app whenever it likes, and AC-14 is « still signed in
        // after a cold start ».
        CookieManager.getInstance().flush()
    }

    override fun onDestroy() {
        background.shutdownNow()
        removeBridgeScript()
        super.onDestroy()
    }

    private companion object {
        const val TAG = "ClinicShell"
    }
}
