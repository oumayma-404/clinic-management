import SafariServices
import UIKit
import WebKit

/**
 Anything that is not a page of this clinic's server leaves the web view (AC-25).

 The load-bearing case is « Connecter Google Agenda »: Google **refuses to serve its sign-in inside a web view**
 (`disallowed_useragent`), so without this the one screen that connects a clinic's calendar shows a Google error
 page and the web view is stranded on a foreign origin with no way back. `SFSafariViewController` is a real
 browser — Safari's own cookie jar, a visible address, the user's password manager — so the hand-off works and,
 because the web view never navigated, it is never stranded.

 Non-`http(s)` schemes (`mailto:`, `tel:`) go to the system too. A web view resolves none of them and the tap would
 otherwise do nothing.
 */
final class ExternalNavigation: NSObject {

    private weak var presenter: UIViewController?

    /**
     Whether an external hand-off is in flight, so the shell knows to refresh on the way back.

     ⚠️ **This is how the return works, and it is weaker than a Universal Link.** The OAuth callback lands on the
     clinic's own origin *inside* the Safari view controller, and nothing reports which URL it reached — only a
     verified Universal Link would close it and hand the navigation back to the app. That needs a fixed, publicly
     resolvable domain serving an `apple-app-site-association` file, which is one of Part 8's four deferred
     decisions. Until then the shell reloads the page the user came from as soon as it is resumed, so « Connecter
     Google Agenda » shows the connected state without a manual refresh — the outcome the criterion asks for,
     reached by resume rather than by redirect. The Android shell carries the identical limitation.
     */
    private(set) var handOffInFlight = false

    init(presenter: UIViewController) {
        self.presenter = presenter
    }

    /// Call when the hand-off has been accounted for, so an ordinary resume does not reload.
    func consumeHandOff() -> Bool {
        let wasInFlight = handOffInFlight
        handOffInFlight = false
        return wasInFlight
    }

    /**
     `true` when this navigation has been taken over and the web view must stay where it is.

     Only **top-level** navigations are intercepted. A cross-origin subframe (an embedded map, a tracking pixel) is
     the page's business, and opening a browser for one would be a browser the user never asked for.
     */
    func handle(_ action: WKNavigationAction, config: ServerConfig) -> Bool {
        guard action.targetFrame?.isMainFrame ?? true else { return false }
        guard let url = action.request.url else { return false }

        let scheme = url.scheme?.lowercased()
        if scheme == "https" && config.isSameOrigin(url) { return false }
        if scheme == "https" || scheme == "http" { return openInBrowser(url) }

        // mailto:, tel:, sms:, maps: — a web view resolves none of them.
        openInSystem(url)
        return true
    }

    private func openInBrowser(_ url: URL) -> Bool {
        // SFSafariViewController refuses anything but http(s), and the caller has already established the scheme.
        guard let presenter else { return true }
        let safari = SFSafariViewController(url: url)
        safari.dismissButtonStyle = .close
        presenter.present(safari, animated: true)
        handOffInFlight = true
        return true
    }

    private func openInSystem(_ url: URL) {
        guard UIApplication.shared.canOpenURL(url) else {
            presenter?.presentShellAlert(message: Strings.externalOpenFailed)
            return
        }
        handOffInFlight = true
        UIApplication.shared.open(url)
    }
}

extension UIViewController {
    /// One French alert, so a failure is never a tap that silently does nothing.
    func presentShellAlert(message: String) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: Strings.close, style: .default))
        present(alert, animated: true)
    }
}
