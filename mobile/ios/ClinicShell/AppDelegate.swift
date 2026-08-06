import UIKit

/**
 The entry point, and nothing more. One window, one [ShellViewController].

 No scene manifest and no storyboard, deliberately: this shell has exactly one screen for its whole life, so a
 `UISceneDelegate` would add a lifecycle to reason about and a `.storyboard` would add a binary file that cannot be
 reviewed in a diff — the trap `mobile/shared/bridge.md`'s own history has already been bitten by once.
 */
@main
final class AppDelegate: UIResponder, UIApplicationDelegate {

    var window: UIWindow?

    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        let window = UIWindow(frame: UIScreen.main.bounds)
        window.rootViewController = ShellViewController()
        window.makeKeyAndVisible()
        self.window = window
        return true
    }
}
