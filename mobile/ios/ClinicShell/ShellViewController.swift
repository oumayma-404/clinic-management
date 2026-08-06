import UIKit
import WebKit

/**
 The whole iOS shell: one view controller rendering the clinic server's own web bundle full-screen, with the five
 French states of AC-15 in front of it.

 Its siblings are `mobile/android/…/MainActivity.kt` and `desktop/…/MainWindow.xaml.cs`, and the shape is
 deliberately the same — one view with mutually-exclusive panels switched by visibility — including the detail that
 cost the desktop shell a bug: the retry path **reloads**, it never re-assigns an unchanged URL, or « Réessayer »
 does nothing when the address has not changed.

 ⚠️ **Three places where iOS is deliberately *not* a transcription of Android**, each for a stated reason:

 1. **The web view is pinned to the full view, not to the safe area.** Android consumes the insets as padding
    because whether a given WebView build reports the navigation bar through `env(safe-area-inset-*)` is
    version-dependent. WKWebView reports it reliably, and `layout.tsx` already sets `viewportFit: "cover"` — so
    drawing edge-to-edge and letting the app's own `--bottom-inset` do the work is both correct and what makes the
    home indicator area look like part of the app rather than a letterbox.
 2. **No rotation handling exists, because none is needed.** Android must list every configuration in
    `android:configChanges` or the activity is destroyed and the web view with it; iOS never destroys a view
    controller on rotation, so AC-23 holds with no code.
 3. **The « Serveur » actions hang off a device shake, not off a back gesture.** iOS has no system back button, and
    a permanent strip of chrome would contradict AC-13. Back within the app is `allowsBackForwardNavigationGestures`
    (the edge swipe every iOS user already knows); shake is the conflict-free gesture left for the recovery menu,
    and `mobile/ios/README.md` states it where an operator will read it.
 */
final class ShellViewController: UIViewController {

    private enum ShellState { case webPage, connecting, serverAddress, unreachable, updateRequired }

    private var webView: WKWebView!
    private let connectingPanel = ConnectingPanel()
    private let configPanel = ConfigPanel()
    private lazy var unreachablePanel = MessagePanel(
        title: Strings.unreachableTitle,
        buttonTitles: [Strings.unreachableRetry, Strings.unreachableChangeServer]
    )
    private lazy var updatePanel = MessagePanel(
        title: Strings.updateTitle,
        buttonTitles: [Strings.updateOpenStore, Strings.unreachableRetry, Strings.unreachableChangeServer]
    )

    private let store = ServerConfigStore()
    // Held directly rather than reached through `webView.configuration`, which returns a *copy*: the script and
    // the handler must land on the instance the web view is actually using.
    private let userContent = WKUserContentController()
    private var bridge: ShellBridge!
    private var externalNavigation: ExternalNavigation!

    private var config = ServerConfig.empty()
    private var state = ShellState.connecting
    private var mainFrameFailed = false
    private var storeUrl = ""

    // MARK: - Lifecycle

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = ShellColors.surface

        buildWebView()
        layoutPanels()
        wireControls()

        externalNavigation = ExternalNavigation(presenter: self)

        config = store.load()
        if config.isConfigured { startSession() } else { showServerAddress() }

        NotificationCenter.default.addObserver(
            self, selector: #selector(applicationDidBecomeActive),
            name: UIApplication.didBecomeActiveNotification, object: nil
        )
    }

    /// Shake is the recovery menu's trigger — see the class note on why it is not a title bar.
    override var canBecomeFirstResponder: Bool { true }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        becomeFirstResponder()
    }

    override func motionEnded(_ motion: UIEvent.EventSubtype, with event: UIEvent?) {
        guard motion == .motionShake, state == .webPage else { return }
        showServerMenu()
    }

    @objc private func applicationDidBecomeActive() {
        // Coming back from a Safari view controller: the state the user left to change (a connected Google
        // calendar) is on the server now, so the page they came from has to be re-read.
        if externalNavigation.consumeHandOff() && state == .webPage {
            webView.reload()
        }
    }

    // MARK: - Web view

    private func buildWebView() {
        let configuration = WKWebViewConfiguration()
        // The session lives in the `local_session` HttpOnly cookie, so a *persistent* store is what makes AC-14
        // possible: still signed in after the app is killed and cold-started.
        configuration.websiteDataStore = .default()
        configuration.allowsInlineMediaPlayback = true
        configuration.userContentController = userContent

        webView = WKWebView(frame: .zero, configuration: configuration)
        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.navigationDelegate = self
        webView.uiDelegate = self
        // The edge swipe is iOS's back gesture, and it is what AC-24 asks for on this platform.
        webView.allowsBackForwardNavigationGestures = true
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.isOpaque = false
        webView.backgroundColor = ShellColors.surface

        view.addSubview(webView)
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            webView.topAnchor.constraint(equalTo: view.topAnchor),
            webView.bottomAnchor.constraint(equalTo: view.bottomAnchor),
        ])

        bridge = ShellBridge(presenter: self, webView: webView)
    }

    /**
     Install the bridge **before the page's own scripts run**, scoped to this server's origin.

     `.atDocumentStart` is what guarantees the ordering, and `client.ts` reads `window.__clinicShell?.version` when
     it builds its very first request header — a bridge that arrives late is a first call with no
     `X-Client-Version` on it. The origin rule matters as much as the timing, and on iOS it is enforced *inside*
     the script: see `ShellBridge`'s note on `WKUserScript` having no origin argument.
     */
    private func installBridgeScript() {
        // A new origin means a new bridge scope: drop the old script so it cannot be injected into a server it
        // was not granted to.
        userContent.removeAllUserScripts()
        userContent.removeScriptMessageHandler(forName: ShellBridge.handlerName)

        let source = ShellBridge.injectedScript(
            version: ShellVersion.name,
            maxFileBytes: ShellBridge.maxFileBytes,
            origins: config.bridgeOrigins
        )
        userContent.addUserScript(
            WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: true)
        )
        userContent.addScriptMessageHandler(bridge, contentWorld: .page, name: ShellBridge.handlerName)
    }

    // MARK: - Session

    /**
     Ask the server what it requires, then load the app.

     The floor is read over **native HTTP before anything is loaded** (AC-33): a build below it never reaches the
     app, so no session is opened and no request is made that the server would refuse.
     */
    private func startSession() {
        guard config.isConfigured else {
            showServerAddress()
            return
        }

        showConnecting()
        let target = config
        ClientRequirements.fetch(baseUrl: target.baseUrl) { [weak self] requirements in
            guard let self, self.config == target else { return }
            let floor = requirements?.minimumShellVersion ?? ""
            self.storeUrl = requirements?.storeUrlIos ?? ""
            if ClientRequirements.isBelowFloor(installed: ShellVersion.name, floor: floor) {
                self.showUpdateRequired(floor: floor)
            } else {
                self.loadApp()
            }
        }
    }

    private func loadApp() {
        installBridgeScript()
        mainFrameFailed = false
        guard let url = URL(string: config.baseUrl) else {
            showUnreachable(reason: Strings.configInvalid)
            return
        }
        webView.load(URLRequest(url: url))
    }

    private func saveServerAddress() {
        let parsed = ServerConfig.parseAddress(configPanel.addressField.text)
        guard parsed.isConfigured else {
            configPanel.errorLabel.isHidden = false
            return
        }

        configPanel.errorLabel.isHidden = true
        configPanel.addressField.resignFirstResponder()
        config = parsed
        store.save(config)
        startSession()
    }

    private func openStoreListing() {
        let target = storeUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !target.isEmpty, let url = URL(string: target) else { return }
        UIApplication.shared.open(url)
    }

    private func showServerMenu() {
        let sheet = UIAlertController(title: Strings.menuTitle, message: nil, preferredStyle: .actionSheet)
        sheet.addAction(UIAlertAction(title: Strings.menuReload, style: .default) { [weak self] _ in
            self?.startSession()
        })
        sheet.addAction(UIAlertAction(title: Strings.menuChangeServer, style: .default) { [weak self] _ in
            self?.showServerAddress()
        })
        sheet.addAction(UIAlertAction(title: Strings.configCancel, style: .cancel))
        // Required on iPad, where an action sheet is a popover and one with no anchor raises rather than presents.
        sheet.popoverPresentationController?.sourceView = view
        sheet.popoverPresentationController?.sourceRect =
            CGRect(x: view.bounds.midX, y: view.bounds.midY, width: 0, height: 0)
        present(sheet, animated: true)
    }

    // MARK: - State switching

    private func layoutPanels() {
        for panel in [connectingPanel, configPanel, unreachablePanel, updatePanel] as [UIView] {
            panel.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(panel)
            NSLayoutConstraint.activate([
                panel.leadingAnchor.constraint(equalTo: view.leadingAnchor),
                panel.trailingAnchor.constraint(equalTo: view.trailingAnchor),
                panel.topAnchor.constraint(equalTo: view.topAnchor),
                panel.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            ])
        }
    }

    private func wireControls() {
        configPanel.saveButton.addTarget(self, action: #selector(onSaveAddress), for: .touchUpInside)
        configPanel.cancelButton.addTarget(self, action: #selector(onCancelAddress), for: .touchUpInside)
        configPanel.addressField.delegate = self

        unreachablePanel.buttons[0].addTarget(self, action: #selector(onRetry), for: .touchUpInside)
        unreachablePanel.buttons[1].addTarget(self, action: #selector(onChangeServer), for: .touchUpInside)

        updatePanel.buttons[0].addTarget(self, action: #selector(onOpenStore), for: .touchUpInside)
        updatePanel.buttons[1].addTarget(self, action: #selector(onRetry), for: .touchUpInside)
        updatePanel.buttons[2].addTarget(self, action: #selector(onChangeServer), for: .touchUpInside)
    }

    @objc private func onSaveAddress() { saveServerAddress() }
    @objc private func onCancelAddress() { startSession() }
    @objc private func onRetry() { startSession() }
    @objc private func onChangeServer() { showServerAddress() }
    @objc private func onOpenStore() { openStoreListing() }

    private func show(_ next: ShellState) {
        state = next
        webView.isHidden = next != .webPage
        connectingPanel.isHidden = next != .connecting
        configPanel.isHidden = next != .serverAddress
        unreachablePanel.isHidden = next != .unreachable
        updatePanel.isHidden = next != .updateRequired
    }

    private func showWebPage() { show(.webPage) }

    private func showConnecting() {
        connectingPanel.targetLabel.text = config.baseUrl
        show(.connecting)
    }

    private func showServerAddress() {
        configPanel.addressField.text = config.isConfigured ? config.displayAddress : ""
        configPanel.errorLabel.isHidden = true
        // A first-run user has nowhere to cancel back to, so the button only exists once a server is configured.
        configPanel.cancelButton.isHidden = !config.isConfigured
        show(.serverAddress)
        configPanel.addressField.becomeFirstResponder()
    }

    /// Names both the address and the reason (AC-15) — « ça ne marche pas » is not a diagnosis an operator can act on.
    private func showUnreachable(reason: String) {
        unreachablePanel.detailLabel.text = Strings.unreachableDetail(address: config.baseUrl, reason: reason)
        show(.unreachable)
    }

    private func showUpdateRequired(floor: String) {
        updatePanel.detailLabel.text = floor.isEmpty
            ? Strings.updateDetailNoFloor
            : Strings.updateDetail(floor: floor, installed: ShellVersion.name)
        // A button that cannot go anywhere is worse than no button: the operator publishes the listing URL, and a
        // LAN install has no store at all.
        updatePanel.buttons[0].isHidden = storeUrl.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        show(.updateRequired)
    }
}

// MARK: - WKNavigationDelegate

extension ShellViewController: WKNavigationDelegate {

    func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationAction: WKNavigationAction,
        decisionHandler: @escaping (WKNavigationActionPolicy) -> Void
    ) {
        decisionHandler(externalNavigation.handle(navigationAction, config: config) ? .cancel : .allow)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        if !mainFrameFailed { showWebPage() }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        reportMainFrameFailure(error)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        reportMainFrameFailure(error)
    }

    private func reportMainFrameFailure(_ error: Error) {
        // A cancelled load is the app navigating away from itself, not a server that is down.
        if (error as NSError).code == NSURLErrorCancelled { return }
        mainFrameFailed = true
        showUnreachable(reason: error.localizedDescription)
    }

    // `webView(_:decidePolicyFor navigationResponse:)` is deliberately NOT implemented to turn a status into a
    // shell state. An HTTP status means the server answered, and what it answered with is the app's own French
    // error page — which AC-74 requires be *shown* rather than replaced.
    //
    // `didReceiveAuthenticationChallenge` is deliberately NOT implemented either: the default rejects an untrusted
    // certificate, so a self-signed one becomes « Impossible de joindre » rather than a silently accepted MITM.
    // The offline-LAN install is reached by installing the clinic's CA on the device and trusting it in Settings —
    // the iOS counterpart of Android's `network_security_config.xml`, and the reason no ATS exception is declared.
}

// MARK: - WKUIDelegate

extension ShellViewController: WKUIDelegate {

    /// `target="_blank"` has no window to open into here; route it through the same rule every other link takes.
    func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        guard navigationAction.targetFrame == nil else { return nil }
        // Off-origin leaves for Safari; a same-origin `target="_blank"` would otherwise be a tap that does
        // nothing at all, so it loads here instead.
        if !externalNavigation.handle(navigationAction, config: config) {
            webView.load(navigationAction.request)
        }
        return nil
    }
}

// MARK: - UITextFieldDelegate

extension ShellViewController: UITextFieldDelegate {

    func textFieldShouldReturn(_ textField: UITextField) -> Bool {
        saveServerAddress()
        return true
    }
}
