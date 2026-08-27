import Foundation
import QuickLook
import UIKit
import WebKit

/**
 The native half of `window.__clinicShell`. `mobile/shared/bridge.md` is the contract itself; this class implements
 it and `injectedScript` is the JavaScript face the web bundle sees.

 ⚠️ **iOS answers through a reply proxy, Android through a callback id, and the contract covers both.**
 `WKScriptMessageHandlerWithReply` hands JavaScript a real `Promise` for free, so there is no pending-request map
 here and no `__clinicShellDeliverIdentityResult` global. The consequence the contract already states: on iOS a
 failure **rejects**, so `lib/download.ts`'s `try/catch` is the thing that speaks — the opposite of Android, where
 an exception out of an `@JavascriptInterface` method is invisible to JavaScript and the shell must toast natively.

 ⚠️ **A `WKUserScript` is not origin-scoped.** Android restricts the wrapper to the configured origin with
 `addDocumentStartJavaScript(script, setOf(origin))`; WebKit has no equivalent and injects into every frame of
 every page. The scope is therefore enforced *inside* the script, against `ServerConfig.bridgeOrigins`, and it is
 load-bearing rather than tidy — the message handler is reachable from any page the web view holds.
 */
final class ShellBridge: NSObject {

    /// The name `WKUserContentController` registers the handler under. Never read by the web bundle directly.
    static let handlerName = "clinicShell"

    /**
     The largest file this shell accepts through `saveFile`, published as `__clinicShell.maxFileBytes`.

     Base64 across the bridge costs ~1.33×, so 25 MB arrives as a ~33 MB string. It equals `lib/download.ts`'s
     documented fallback and Android's ceiling on purpose — the web bundle uses that constant when the bridge does
     not state one, and three different ceilings would refuse different files depending on which side answered.
     */
    static let maxFileBytes = 25 * 1024 * 1024

    private weak var presenter: UIViewController?
    private weak var webView: WKWebView?
    private var previewUrl: URL?

    init(presenter: UIViewController, webView: WKWebView) {
        self.presenter = presenter
        self.webView = webView
    }

    // MARK: - saveFile

    /**
     Hand a file to the OS: write it into the app's temporary directory, then preview or share it.

     The only delivery route that works inside a web view — a `blob:` download has nowhere to go and
     `navigator.share` is unavailable — which is why `lib/download.ts` tries this first and falls back to the
     browser paths only when the bridge is absent.

     `QLPreviewController` is what makes **AC-61** true: a PDF opens in a working viewer, with the system's own
     share and print actions on it. Anything Quick Look cannot render falls to the share sheet rather than to an
     error, because handing the file onwards is still the capability the user asked for.
     */
    private func saveFile(base64: String, filename: String, mimeType: String) throws -> URL {
        guard let bytes = Data(base64Encoded: base64, options: [.ignoreUnknownCharacters]) else {
            throw ShellBridgeError.message(Strings.saveFileFailed)
        }

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("shell-files", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let url = directory.appendingPathComponent(Self.safeFileName(filename))
        try bytes.write(to: url, options: .atomic)
        _ = mimeType
        return url
    }

    private func offer(_ url: URL) {
        guard let presenter else { return }

        if QLPreviewController.canPreview(url as QLPreviewItem) {
            previewUrl = url
            let preview = QLPreviewController()
            preview.dataSource = self
            presenter.present(preview, animated: true)
            return
        }

        let share = UIActivityViewController(activityItems: [url], applicationActivities: nil)
        // Required on iPad: a popover with no anchor raises an exception rather than presenting.
        share.popoverPresentationController?.sourceView = presenter.view
        share.popoverPresentationController?.sourceRect = CGRect(
            x: presenter.view.bounds.midX, y: presenter.view.bounds.midY, width: 0, height: 0
        )
        presenter.present(share, animated: true)
    }

    /**
     Strip any directory part a page supplied. Appending `../../x` escapes the temporary directory, and the name
     crosses the bridge from JavaScript, so it is untrusted input by construction.
     */
    static func safeFileName(_ filename: String) -> String {
        let name = filename
            .components(separatedBy: "/").last?
            .components(separatedBy: "\\").last?
            .replacingOccurrences(of: " ", with: "_")
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return name.isEmpty ? "document" : name
    }

    // MARK: - print

    /**
     Print the page through the OS print service (AC-21).

     What comes out honours the app's `@media print` rules — the rail, the header, the bottom bar, the assistant
     launcher and the toaster are all `print:hidden` — so the document prints as document content, which is what
     makes this worth wiring rather than leaving to a screenshot.
     */
    private func printPage() {
        guard let webView else { return }
        let info = UIPrintInfo.printInfo()
        info.outputType = .general
        info.jobName = [Strings.appName, webView.title].compactMap { $0 }
            .filter { !$0.isEmpty }
            .joined(separator: " — ")

        let controller = UIPrintInteractionController.shared
        controller.printInfo = info
        controller.printFormatter = webView.viewPrintFormatter()
        controller.present(animated: true, completionHandler: nil)
    }
}

// MARK: - The JavaScript face

extension ShellBridge {

    /**
     The bridge as the page sees it, installed before the page's own scripts run.

     Four things it does beyond declaring the object:

     1. **Refuses to install off-origin.** WebKit gives no per-origin user script, so this is the scope.
     2. `Object.freeze` — the web-side type declares every member `readonly`, and a page that reassigns `saveFile`
        would be redefining the shell's contract from inside the sandbox.
     3. It is assigned, not `defineProperty`'d non-configurable, so `delete window.__clinicShell` **works** — AC-26
        is verified by deleting it at runtime and checking every affected screen behaves as in a browser, which a
        non-deletable property would make untestable.
     4. It replaces `window.print` with a shim that prefers the bridge **and falls back to the original**. WKWebView
        implements no `window.print()` of its own; with the bridge deleted the shim must do nothing, not throw.
     */
    static func injectedScript(version: String, maxFileBytes: Int, origins: [String]) -> String {
        let quotedVersion = jsonString(version)
        let quotedOrigins = "[" + origins.map(jsonString).joined(separator: ",") + "]"

        return """
        (function () {
          var handlers = window.webkit && window.webkit.messageHandlers;
          var bridge = handlers && handlers.\(handlerName);
          if (!bridge) { return; }
          if (\(quotedOrigins).indexOf(window.location.origin) === -1) { return; }
          var pushListeners = [];
          window.__clinicShell = Object.freeze({
            version: \(quotedVersion),
            platform: "ios",
            maxFileBytes: \(maxFileBytes),
            saveFile: function (base64, filename, mimeType) {
              return bridge.postMessage({ name: "saveFile", base64: base64, filename: filename, mimeType: mimeType });
            },
            print: function () { bridge.postMessage({ name: "print" }); },
            onPushToken: function (listener) {
              if (typeof listener === "function") { pushListeners.push(listener); }
            },
            confirmIdentity: function () {
              return bridge.postMessage({ name: "confirmIdentity" }).then(
                function (outcome) { return outcome; },
                function () { return "unavailable"; }
              );
            }
          });
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
        """
    }

    /// `JSONSerialization` rather than hand-quoting: the version and the origin both come from user configuration.
    private static func jsonString(_ value: String) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: [value], options: []),
              let array = String(data: data, encoding: .utf8) else { return "\"\"" }
        return String(array.dropFirst().dropLast())
    }
}

// MARK: - WKScriptMessageHandlerWithReply

enum ShellBridgeError: Error {
    case message(String)
}

extension ShellBridge: WKScriptMessageHandlerWithReply {

    func userContentController(
        _ userContentController: WKUserContentController,
        didReceive message: WKScriptMessage,
        replyHandler: @escaping (Any?, String?) -> Void
    ) {
        guard let body = message.body as? [String: Any], let name = body["name"] as? String else {
            replyHandler(nil, Strings.saveFileFailed)
            return
        }

        switch name {
        case "saveFile":
            let base64 = body["base64"] as? String ?? ""
            let filename = body["filename"] as? String ?? ""
            let mimeType = body["mimeType"] as? String ?? ""
            do {
                let url = try saveFile(base64: base64, filename: filename, mimeType: mimeType)
                offer(url)
                replyHandler(nil, nil)
            } catch {
                // Rejecting is the contract on iOS: `lib/download.ts` catches it and shows the French toast.
                replyHandler(nil, Strings.saveFileFailed)
            }

        case "print":
            printPage()
            replyHandler(nil, nil)

        case "confirmIdentity":
            BiometricGate.confirm { outcome in replyHandler(outcome, nil) }

        default:
            replyHandler(nil, Strings.saveFileFailed)
        }
    }
}

// MARK: - QLPreviewControllerDataSource

extension ShellBridge: QLPreviewControllerDataSource {

    func numberOfPreviewItems(in controller: QLPreviewController) -> Int {
        previewUrl == nil ? 0 : 1
    }

    func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem {
        (previewUrl ?? URL(fileURLWithPath: "/dev/null")) as QLPreviewItem
    }
}
