package com.clinicmanagement.shell

import android.app.AlertDialog
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageInstaller
import android.net.Uri
import android.net.http.SslError
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.View
import android.view.inputmethod.EditorInfo
import android.webkit.CookieManager
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.SslErrorHandler
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
import androidx.core.content.ContextCompat
import androidx.core.content.IntentCompat
import androidx.core.net.toUri
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
    private lateinit var updateStrip: View

    private lateinit var store: ServerConfigStore
    private lateinit var fileChooser: FileChooser
    private lateinit var externalNavigation: ExternalNavigation

    private var config = ServerConfig.empty()
    private var state = ShellState.Connecting
    private var webViewConfigured = false
    private var mainFrameFailed = false
    private var bridgeScript: ScriptHandler? = null
    private var storeUrl = ""

    /**
     * Which listing « Mettre à jour » opens — the app's, or the renderer's.
     *
     * The two update states share one panel because they are the same sentence to the user (« something must be
     * updated before this works »), but they point at different packages, and a button that opens the wrong
     * listing is worse than no button.
     */
    private var updateTargetPackage: String? = null

    /** One thread for the launch probe. A coroutine dependency for a single GET is not worth its version pairing. */
    private val background = Executors.newSingleThreadExecutor()

    /** The last answer from `client-requirements` — the floor, and the update on offer (see [ShellUpdater]). */
    private var requirements: ClientRequirements.Requirements? = null
    private var lastRequirementsReadAt = 0L

    /** « ✕ » hides one VERSION until the next cold start, like the desktop strip — never every future one. */
    private var dismissedUpdateVersion: String? = null

    /** A download or an install is running; both the strip and the wall report it, never start a second. */
    private var updateInFlight = false
    private var updateFromWall = false

    /**
     * What Android's installer says about the APK [ShellUpdater.install] handed it. Registered on this activity,
     * not exported: the status arrives through the PendingIntent this app created, i.e. as this app.
     */
    private val installStatusReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
                // Android's own « Mettre à jour cette application ? » — and, the first time, the
                // « Installer des applis inconnues » screen before it.
                PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                    val confirm = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_INTENT, Intent::class.java)
                    try {
                        if (confirm == null) error("no confirmation intent")
                        startActivity(confirm)
                    } catch (t: Throwable) {
                        Log.w(TAG, "installer confirmation could not be shown", t)
                        failUpdate(R.string.update_install_failed)
                    }
                }
                // Android replaces the app and ends this process; there is nothing left to report.
                PackageInstaller.STATUS_SUCCESS -> Unit
                // The user said no. Not a failure: put the offer back as it was.
                PackageInstaller.STATUS_FAILURE_ABORTED -> restoreUpdateOffer()
                else -> failUpdate(R.string.update_install_failed)
            }
        }
    }

    // ── The PC de secours (clinic-pc-copy Part 3) ──
    /** Where the PC this app prepared a session on answers, or null. */
    private var relayTarget: RelaySwitch.Target? = null
    /** Whether the document on screen is the PC's, and whether the load in flight is aimed at it. */
    private var onPc = false
    private var navigatingToPc = false
    /** Since when the cloud has been unreachable with a PC that does not hold yet (AC-3.3, then AC-3.8). */
    private var cloudLostAtMs: Long? = null
    /** Its own thread: a pinned GET can take five seconds and must never queue behind the launch probe. */
    private val relayWorker = Executors.newSingleThreadExecutor()
    private val relayHandler = Handler(Looper.getMainLooper())
    private val relayTick = Runnable { relayTick() }
    private val offerTick = Runnable { offerTick() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        contentRoot = findViewById(R.id.content_root)
        webView = findViewById(R.id.web_view)
        panelConnecting = findViewById(R.id.panel_connecting)
        panelConfig = findViewById(R.id.panel_config)
        panelUnreachable = findViewById(R.id.panel_unreachable)
        panelUpdate = findViewById(R.id.panel_update)
        updateStrip = findViewById(R.id.update_strip)

        ContextCompat.registerReceiver(
            this,
            installStatusReceiver,
            IntentFilter(ShellUpdater.INSTALL_STATUS_ACTION),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )

        store = ServerConfigStore(this)
        // Both must be built here: `registerForActivityResult` throws once the activity has reached STARTED.
        fileChooser = FileChooser(this)
        externalNavigation = ExternalNavigation(this)

        applyWindowInsets()
        wireControls()
        installBackHandler()

        config = store.load()
        relayTarget = RelaySwitch.load(this)
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
        findViewById<Button>(R.id.unreachable_take_over).setOnClickListener { takeOver() }
        findViewById<Button>(R.id.update_retry).setOnClickListener { startSession() }
        findViewById<Button>(R.id.update_change_server).setOnClickListener { showServerAddress() }
        findViewById<Button>(R.id.update_open_store).setOnClickListener {
            // The app's own wall installs in place when this server publishes an APK; the WebView wall (and an
            // operator who listed a store instead) still opens a listing.
            if (updateTargetPackage == null && ShellUpdater.availableUpdate(requirements) != null) {
                startUpdate(fromWall = true)
            } else {
                openStoreListing()
            }
        }
        findViewById<Button>(R.id.update_strip_install).setOnClickListener { startUpdate(fromWall = false) }
        findViewById<Button>(R.id.update_strip_dismiss).setOnClickListener {
            dismissedUpdateVersion = ShellUpdater.availableUpdate(requirements)?.version
            updateStrip.visibility = View.GONE
        }
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

        // Before anything is loaded, and before the network is touched: a renderer too old for the app's own
        // stylesheet produces a page with NO CSS and no error (see WebViewRequirements). Checked first because
        // it is a local fact needing no server, and because an unstyled screen is indistinguishable from a
        // broken product.
        val webView = WebViewRequirements.read(this)
        if (WebViewRequirements.isBelowFloor(webView)) {
            showWebViewOutdated(webView)
            return
        }

        showConnecting()
        val target = config
        background.execute {
            // An address typed with no port does not yet name a server: 5001 is a clinic's own PC and 443 is a
            // hosted deployment, and only the server can say which this is. Resolved once and persisted, so it
            // costs a probe the first time an address is used and nothing on every launch after it.
            val resolved = ServerProbe.resolve(target)
            val requirements = ClientRequirements.fetch(resolved.baseUrl)
            runOnUiThread {
                if (isFinishing || isDestroyed || target != config) return@runOnUiThread
                if (resolved != config) {
                    config = resolved
                    store.save(resolved)
                    showConnecting() // Re-render: the target line was showing the unresolved port.
                }
                this.requirements = requirements
                lastRequirementsReadAt = SystemClock.elapsedRealtime()
                val floor = requirements?.minimumShellVersion.orEmpty()
                storeUrl = requirements?.storeUrlAndroid.orEmpty()
                if (ClientRequirements.isBelowFloor(BuildConfig.VERSION_NAME, floor)) {
                    showUpdateRequired(floor)
                } else {
                    loadApp()
                    offerUpdate()
                }
            }
        }
    }

    private fun loadApp() {
        configureWebView()
        installBridgeScript()
        mainFrameFailed = false
        navigatingToPc = false
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
        // The renderer's listing is opened by package id (`market://`), the app's by the URL the operator
        // published — a LAN install has no store listing for itself at all, but every device with Play has one
        // for its WebView.
        val target = updateTargetPackage
            ?.let { "market://details?id=$it" }
            ?: storeUrl.trim().ifEmpty { return }

        try {
            startActivity(Intent(Intent.ACTION_VIEW, target.toUri()))
        } catch (t: Throwable) {
            Log.w(TAG, "store listing could not be opened", t)
            // `market://` needs Play installed; the web listing works on any device with a browser.
            val fallback = updateTargetPackage?.let { "https://play.google.com/store/apps/details?id=$it" }
            if (fallback != null) {
                try {
                    startActivity(Intent(Intent.ACTION_VIEW, fallback.toUri()))
                } catch (inner: Throwable) {
                    Log.w(TAG, "web store listing could not be opened either", inner)
                }
            }
        }
    }

    // ── In-app update ─────────────────────────────────────────────────────────────────────────────────────

    /**
     * Show the strip when this server offers a newer APK. Never on the wall (that panel has its own button) and
     * never for a version the user dismissed this session.
     */
    private fun offerUpdate() {
        if (updateInFlight) return
        val update = ShellUpdater.availableUpdate(requirements)
        if (update == null || update.version == dismissedUpdateVersion) {
            updateStrip.visibility = View.GONE
            return
        }
        findViewById<TextView>(R.id.update_strip_text).text = getString(R.string.update_strip_available, update.version)
        setStripButtonsVisible(true)
        updateStrip.visibility = View.VISIBLE
    }

    private fun startUpdate(fromWall: Boolean) {
        if (updateInFlight) return
        val update = ShellUpdater.availableUpdate(requirements) ?: return
        updateInFlight = true
        updateFromWall = fromWall
        reportUpdate(getString(R.string.update_downloading))

        background.execute {
            val apk = ShellUpdater.download(this, update) { percent ->
                val status = if (percent == null) {
                    getString(R.string.update_downloading)
                } else {
                    getString(R.string.update_downloading_percent, percent)
                }
                runOnUiThread { if (updateInFlight) reportUpdate(status) }
            }
            if (apk == null) {
                runOnUiThread { failUpdate(R.string.update_failed) }
                return@execute
            }
            runOnUiThread { reportUpdate(getString(R.string.update_installing)) }
            if (!ShellUpdater.install(this, apk)) {
                runOnUiThread { failUpdate(R.string.update_install_failed) }
            }
        }
    }

    /**
     * The page asked for the APK — the web app's « Mise à jour requise » button, which inside this WebView is a
     * same-origin download with nowhere to go. Re-read the requirements when the launch answer offers nothing:
     * the floor may have been raised since.
     */
    private fun requestUpdateFromPage() {
        if (updateInFlight) return
        if (ShellUpdater.availableUpdate(requirements) != null) {
            startUpdate(fromWall = false)
            return
        }
        val base = config.baseUrl
        background.execute {
            val fresh = ClientRequirements.fetch(base) ?: return@execute
            runOnUiThread {
                if (isFinishing || isDestroyed || base != config.baseUrl) return@runOnUiThread
                requirements = fresh
                lastRequirementsReadAt = SystemClock.elapsedRealtime()
                startUpdate(fromWall = false)
            }
        }
    }

    /** Progress goes where the press happened: under the wall's button, or into the strip. */
    private fun reportUpdate(status: String) {
        if (updateFromWall) {
            findViewById<TextView>(R.id.update_progress).apply {
                text = status
                visibility = View.VISIBLE
            }
            findViewById<Button>(R.id.update_open_store).isEnabled = false
        } else {
            findViewById<TextView>(R.id.update_strip_text).text = status
            setStripButtonsVisible(false)
            updateStrip.visibility = View.VISIBLE
        }
    }

    private fun failUpdate(message: Int) {
        updateInFlight = false
        if (updateFromWall) {
            findViewById<TextView>(R.id.update_progress).apply {
                setText(message)
                visibility = View.VISIBLE
            }
            findViewById<Button>(R.id.update_open_store).isEnabled = true
        } else {
            // The button stays « Mettre à jour », which is now « réessayer ».
            findViewById<TextView>(R.id.update_strip_text).setText(message)
            setStripButtonsVisible(true)
            updateStrip.visibility = View.VISIBLE
        }
    }

    private fun restoreUpdateOffer() {
        updateInFlight = false
        if (updateFromWall) {
            findViewById<TextView>(R.id.update_progress).visibility = View.GONE
            findViewById<Button>(R.id.update_open_store).isEnabled = true
        } else {
            offerUpdate()
        }
    }

    private fun setStripButtonsVisible(visible: Boolean) {
        val visibility = if (visible) View.VISIBLE else View.GONE
        findViewById<Button>(R.id.update_strip_install).visibility = visibility
        findViewById<Button>(R.id.update_strip_dismiss).visibility = visibility
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
        webView.addJavascriptInterface(
            ShellBridge(
                this,
                webView,
                isOwnPage = { page -> config.isSameOrigin(page) },
                onRelayPrepare = { request, done -> prepareRelay(request, done) },
                onRelaySwitch = { done -> switchToPcIfHolding(done) },
                isClinicPage = { page -> config.isSameOrigin(page) || RelaySwitch.isPcOrigin(page, relayTarget) },
            ),
            ShellBridge.NATIVE_OBJECT,
        )

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean =
                externalNavigation.handle(request, config, relayTarget)

            override fun onPageStarted(view: WebView, url: String, favicon: android.graphics.Bitmap?) {
                // The fallback path when `DOCUMENT_START_SCRIPT` is unsupported — see installBridgeScript.
                if (!WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
                    view.evaluateJavascript(bridgeSource(), null)
                }
            }

            override fun onPageFinished(view: WebView, url: String) {
                if (!mainFrameFailed) {
                    followRelayAfterSuccess(url)
                    showWebPage()
                }
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
                followRelayAfterFailure(error.description?.toString().orEmpty())
            }

            /**
             * An untrusted certificate is **refused and reported** (AC-15, AC-74).
             *
             * ⚠️ **This override exists because leaving it out produced a blank white screen** — found on a
             * physical Galaxy S9 before the clinic's CA was installed. The reasoning that omitted it was that
             * the default implementation cancels the load, so the failure would surface as « Impossible de
             * joindre ». It does not: when the SSL handler cancels, `onReceivedError` is **not** raised for the
             * main frame, so `mainFrameFailed` stayed false, `onPageFinished` still fired, and the shell
             * switched to an empty WebView. A white rectangle is the one outcome AC-74 forbids.
             *
             * `handler.cancel()` is kept — the certificate is still refused. What changes is only that the user is
             * told, and told what to do about it.
             *
             * ⚠️ The ONE `proceed()` (since 1.3.0, `clinic-pc-copy` Part 3): the PC de secours's own self-issued
             * certificate, at the address and port this app traded a session on, matched by its DER SHA-256 — the pin
             * the cloud handed over (D21). Any other certificate, anywhere, is still cancelled and reported.
             */
            @android.annotation.SuppressLint("WebViewClientOnReceivedSslError")
            override fun onReceivedSslError(view: WebView, handler: SslErrorHandler, error: SslError) {
                // Part 3: the PC de secours's own certificate, at its own address, and nothing else. Its certificate is
                // self-issued, so this is the only way the WebView can show it — a pin, never « ignore errors ».
                val pc = relayTarget
                val at = runCatching { error.url?.toUri() }.getOrNull()
                if (pc != null && RelaySwitch.isPcOrigin(at, pc) && RelaySwitch.isThePcsCertificate(error.certificate, pc.fingerprint)) {
                    handler.proceed()
                    return
                }
                handler.cancel()
                mainFrameFailed = true
                showUnreachable(getString(R.string.unreachable_certificate))
            }

            // `onReceivedHttpError` is deliberately NOT overridden. An HTTP status means the server answered, and
            // what it answered with is the app's own French error page — which AC-74 requires be *shown* rather
            // than replaced by a shell state. Only a transport failure is the shell's to report.
        }

        // ⚠️ Only the clinic server's own APK. Every other download already goes through the bridge's `saveFile`,
        // and a WebView with no listener drops the rest — which this keeps true for anything that is not an update.
        webView.setDownloadListener { url, _, _, mimeType, _ ->
            val uri = url.toUri()
            val isApk = ShellUpdater.APK_MIME_TYPE.equals(mimeType, ignoreCase = true) ||
                uri.path.orEmpty().endsWith("/client-download/android")
            if (isApk && uri.scheme == "https" && config.isSameOrigin(uri)) requestUpdateFromPage()
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
                // `bridgeOrigins`, not `baseUrl`: a page served on 443 reports `https://host` as its origin — the
                // URL spec omits a default port — so granting only `https://host:443` would leave the bridge
                // silently uninstalled on exactly the deployment that has no other way in.
                WebViewCompat.addDocumentStartJavaScript(webView, bridgeSource(), bridgeOrigins())
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

    /** The cloud's origins, and the PC de secours's once one was prepared — a carried form is taken back there (D23). */
    private fun bridgeOrigins(): Set<String> = config.bridgeOrigins + listOfNotNull(relayTarget?.origin)

    private fun bridgeSource(): String =
        ShellBridge.injectedScript(BuildConfig.VERSION_NAME, ShellBridge.MAX_FILE_BYTES)

    // ── The PC de secours (clinic-pc-copy Part 3) ──────────────────────────────────────────────────────────

    /** `relayPrepare`: trade the ticket on the PC (pinned), write the PC's session cookie, remember where the PC is. */
    private fun prepareRelay(request: RelaySwitch.Prepare, done: (Boolean) -> Unit) {
        relayWorker.execute {
            val traded = RelaySwitch.trade(request)
            runOnUiThread {
                if (traded == null || isDestroyed) {
                    done(false)
                    return@runOnUiThread
                }
                val (target, session) = traded
                val (sessionCookie, mustChange) = RelaySwitch.cookies(session)
                val cookies = CookieManager.getInstance()
                cookies.setCookie(target.origin, sessionCookie)
                cookies.setCookie(target.origin, mustChange)
                cookies.flush()
                RelaySwitch.save(this, target, session.expiresAt)
                val moved = relayTarget?.origin != target.origin
                relayTarget = target
                // A new PC origin needs the bridge too (D23); the page in front is the cloud's, so this changes nothing on screen.
                if (moved) installBridgeScript()
                done(true)
            }
        }
    }

    /** `relaySwitch`: the cloud says the cabinet works on the PC — move there if the PC says it holds. */
    private fun switchToPcIfHolding(done: (Boolean) -> Unit) {
        val known = relayTarget ?: return done(false)
        relayWorker.execute {
            val (pc, holding) = askPc(known)
            // The open page lost the cloud (Wi-Fi off, a request with no reply): if the cloud really does not answer,
            // the cut is shown at once — the countdown, then the button — with no reload (owner, 2026-10-12).
            val cut = holding != true && !RelaySwitch.cloudAnswers(config.baseUrl)
            runOnUiThread {
                if (isDestroyed) return@runOnUiThread done(false)
                if (holding == true) {
                    adoptMovedPc(known, pc)
                    loadPc(pc)
                    done(true)
                } else if (cut) {
                    followRelayAfterFailure(getString(R.string.unreachable_title))
                    done(true)
                } else {
                    done(false)
                }
            }
        }
    }

    /**
     * « Do you hold? », and when the PC no longer answers as itself at the address the app knew, UDP discovery (D21).
     * Blocking — on [relayWorker]. Returns the PC as found (maybe at a new address) and its answer.
     */
    private fun askPc(known: RelaySwitch.Target): Pair<RelaySwitch.Target, Boolean?> {
        val holding = RelaySwitch.holding(known)
        if (holding != null) return known to holding
        val moved = RelayDiscovery.find(this, known) ?: return known to null
        return moved to RelaySwitch.holding(moved)
    }

    /** The PC moved: carry the prepared session to its new origin and keep the new address. On the UI thread. */
    private fun adoptMovedPc(old: RelaySwitch.Target, moved: RelaySwitch.Target) {
        if (old.origin == moved.origin || isDestroyed) return
        val cookies = CookieManager.getInstance()
        val credential = RelaySwitch.cookieValue(cookies.getCookie(old.origin), RelaySwitch.SESSION_COOKIE)
        if (credential != null) {
            val mustChange = RelaySwitch.cookieValue(cookies.getCookie(old.origin), RelaySwitch.MUST_CHANGE_COOKIE) == "1"
            val (sessionCookie, mustChangeCookie) =
                RelaySwitch.cookies(RelaySwitch.Session(credential, RelaySwitch.sessionExpires(this), mustChange))
            cookies.setCookie(moved.origin, sessionCookie)
            cookies.setCookie(moved.origin, mustChangeCookie)
            cookies.flush()
        }
        RelaySwitch.save(this, moved)
        relayTarget = moved
        installBridgeScript()
    }

    private fun loadPc(pc: RelaySwitch.Target) {
        mainFrameFailed = false
        navigatingToPc = true
        webView.loadUrl(pc.origin + "/")
    }

    private fun followRelayAfterSuccess(url: String) {
        onPc = RelaySwitch.isPcOrigin(runCatching { url.toUri() }.getOrNull(), relayTarget)
        navigatingToPc = false
        cloudLostAtMs = null
        scheduleRelayTick(if (onPc) RELAY_BACK_CHECK_MS else null)
    }

    /**
     * The clinic's document did not load. From the PC: back to the cloud. From the cloud: if the PC holds the cabinet's
     * saves, move there (already signed in, D22); if it answers without holding, say it is about to (AC-3.3) or that it
     * was not ready (AC-3.8), and try again every few seconds. With no PC prepared: the panel exactly as before.
     */
    private fun followRelayAfterFailure(reason: String) {
        if (navigatingToPc || onPc) {
            navigatingToPc = false
            onPc = false
            loadApp()
            return
        }
        val pc = relayTarget
        if (pc == null) {
            showUnreachable(reason)
            return
        }
        val now = System.currentTimeMillis()
        val lostAt = cloudLostAtMs ?: now.also { cloudLostAtMs = it }
        showUnreachable(reason)
        relayWorker.execute {
            val (found, holding) = askPc(pc)
            runOnUiThread {
                if (isDestroyed || state != ShellState.Unreachable) return@runOnUiThread
                adoptMovedPc(pc, found)
                if (holding == true) {
                    loadPc(found)
                    return@runOnUiThread
                }
                if (holding == false) {
                    showUnreachable(waitingText())
                    findViewById<Button>(R.id.unreachable_take_over).visibility =
                        if (RelaySwitch.lastCanTakeOver) View.VISIBLE else View.GONE
                    relayHandler.removeCallbacks(offerTick)
                    relayHandler.postDelayed(offerTick, OFFER_POLL_MS)
                }
                scheduleRelayTick(null)
            }
        }
    }

    private fun waitingText(): String = RelaySwitch.waitingLine(
        RelaySwitch.lastArmed, RelaySwitch.lastCanTakeOver,
        cloudLostAtMs?.let { RelaySwitch.secondsLeft(it, System.currentTimeMillis()) } ?: 0,
    )

    /** While the cut panel is up: ask the PC every 3 s, update the countdown and show the button the moment it is offered. */
    private fun offerTick() {
        val pc = relayTarget ?: return
        if (state != ShellState.Unreachable || onPc || cloudLostAtMs == null) return
        relayWorker.execute {
            val holding = RelaySwitch.holding(pc)
            runOnUiThread {
                if (isDestroyed || state != ShellState.Unreachable || onPc) return@runOnUiThread
                val button = findViewById<Button>(R.id.unreachable_take_over)
                if (holding == true) {
                    loadPc(pc)
                    return@runOnUiThread
                }
                if (holding == false && button.isEnabled) {
                    findViewById<TextView>(R.id.unreachable_detail).text = waitingText()
                    button.visibility = if (RelaySwitch.lastCanTakeOver) View.VISIBLE else View.GONE
                }
                relayHandler.postDelayed(offerTick, OFFER_POLL_MS)
            }
        }
    }

    /** One loop: on the PC, « does it still hold? » every 20 s; while the cloud is unreachable, try it again every 10 s. */
    private fun scheduleRelayTick(backCheckMs: Long?) {
        relayHandler.removeCallbacks(relayTick)
        if (relayTarget == null || (backCheckMs == null && cloudLostAtMs == null)) return
        relayHandler.postDelayed(relayTick, backCheckMs ?: RELAY_RETRY_MS)
    }

    private fun relayTick() {
        val pc = relayTarget ?: return
        if (onPc) {
            relayWorker.execute {
                val holding = RelaySwitch.holding(pc)
                runOnUiThread {
                    if (isDestroyed || !onPc) return@runOnUiThread
                    if (holding != true) {
                        onPc = false
                        loadApp()
                    } else {
                        scheduleRelayTick(RELAY_BACK_CHECK_MS)
                    }
                }
            }
        } else if (cloudLostAtMs != null && state == ShellState.Unreachable) {
            loadApp()
        }
    }

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

    /** « Travailler sur le PC de secours » (manual takeover): the PC takes over, then the app moves there. */
    private fun takeOver() {
        val pc = relayTarget ?: return
        val button = findViewById<Button>(R.id.unreachable_take_over)
        button.isEnabled = false
        relayWorker.execute {
            val held = RelaySwitch.takeOver(pc)
            runOnUiThread {
                button.isEnabled = true
                if (isDestroyed) return@runOnUiThread
                if (held) {
                    loadPc(pc)
                } else {
                    findViewById<TextView>(R.id.unreachable_detail).setText(R.string.unreachable_take_over_failed)
                }
            }
        }
    }

    /** Names both the address and the reason (AC-15) — « ça ne marche pas » is not a diagnosis an operator can act on. */
    private fun showUnreachable(reason: String) {
        findViewById<Button>(R.id.unreachable_take_over).visibility = View.GONE
        findViewById<TextView>(R.id.unreachable_detail).text =
            getString(R.string.unreachable_detail, config.baseUrl, reason)
        show(ShellState.Unreachable)
    }

    /**
     * The renderer is too old (see [WebViewRequirements]). Shares the update panel with the app-version state:
     * to the user both are « something must be updated », and the difference is which package the button opens.
     */
    private fun showWebViewOutdated(installed: WebViewRequirements.Installed) {
        findViewById<TextView>(R.id.update_title).setText(R.string.update_webview_title)
        findViewById<TextView>(R.id.update_detail).text = getString(
            R.string.update_webview_detail,
            installed.versionName ?: "?",
            WebViewRequirements.MINIMUM_MAJOR.toString(),
        )
        updateTargetPackage = installed.packageName
        findViewById<Button>(R.id.update_open_store).apply {
            setText(R.string.update_webview_open)
            visibility = if (installed.packageName.isNullOrBlank()) View.GONE else View.VISIBLE
        }
        show(ShellState.UpdateRequired)
    }

    private fun showUpdateRequired(floor: String) {
        findViewById<TextView>(R.id.update_title).setText(R.string.update_title)
        findViewById<Button>(R.id.update_open_store).setText(R.string.update_open_store)
        updateTargetPackage = null
        findViewById<TextView>(R.id.update_detail).text = if (floor.isBlank()) {
            getString(R.string.update_detail_no_floor)
        } else {
            getString(R.string.update_detail, floor, BuildConfig.VERSION_NAME)
        }
        // A button that cannot go anywhere is worse than no button: it installs the APK this server publishes, or
        // opens the listing the operator named — and with neither there is nothing to press.
        val canUpdate = ShellUpdater.availableUpdate(requirements) != null || storeUrl.isNotBlank()
        findViewById<Button>(R.id.update_open_store).apply {
            visibility = if (canUpdate) View.VISIBLE else View.GONE
            isEnabled = !updateInFlight
        }
        if (!updateInFlight) findViewById<TextView>(R.id.update_progress).visibility = View.GONE
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
        recheckForUpdate()
    }

    /**
     * A phone is resumed far more often than it is launched, so the launch read alone would leave a release
     * unseen for days. Re-asked at most every [UPDATE_RECHECK_MS], the desktop shell's own interval.
     */
    private fun recheckForUpdate() {
        if (state != ShellState.WebPage || updateInFlight) return
        if (SystemClock.elapsedRealtime() - lastRequirementsReadAt < UPDATE_RECHECK_MS) return
        lastRequirementsReadAt = SystemClock.elapsedRealtime()
        val base = config.baseUrl
        background.execute {
            val fresh = ClientRequirements.fetch(base) ?: return@execute
            runOnUiThread {
                if (isFinishing || isDestroyed || base != config.baseUrl) return@runOnUiThread
                requirements = fresh
                offerUpdate()
            }
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
        relayHandler.removeCallbacks(relayTick)
        relayWorker.shutdownNow()
        background.shutdownNow()
        removeBridgeScript()
        unregisterReceiver(installStatusReceiver)
        super.onDestroy()
    }

    private companion object {
        const val TAG = "ClinicShell"
        const val UPDATE_RECHECK_MS = 30L * 60 * 1000
        const val RELAY_RETRY_MS = 10_000L
        const val OFFER_POLL_MS = 3_000L
        const val RELAY_BACK_CHECK_MS = 20_000L
    }
}
