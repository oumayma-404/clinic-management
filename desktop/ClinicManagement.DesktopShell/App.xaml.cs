using System.Windows;
using Velopack;

namespace ClinicManagement.DesktopShell;

/// <summary>Interaction logic for App.xaml — the WebView2 thin-client shell entry point.</summary>
public partial class App : Application
{
    /// <summary>
    /// Velopack's hook, and it is the first thing this process does that is ours.
    ///
    /// <para>
    /// ⚠️ <b>It is not only an update check — it is how install, update, first-run and uninstall are handled at
    /// all.</b> Velopack re-launches this same executable with its own arguments at those moments, and
    /// <c>Run()</c> recognises them, does the work and exits the process. So it must come before anything that
    /// shows UI or touches the WebView2 runtime: reached later, those runs would flash a window or fail, and an
    /// update would apply only sometimes.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Here rather than in a hand-written <c>Main</c></b>: `App.xaml` is the WPF `ApplicationDefinition`, so
    /// the SDK generates the entry point and a second one is a compile error (CS0017). `OnStartup` is the first
    /// point we own, it runs before any window is constructed, and `Run()` exits the process itself on a hook
    /// invocation — so nothing of ours gets to run on those launches either way.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b><c>SetAutoApplyOnStartup(false)</c>, deliberately — and it was `true` first, which was wrong.</b>
    /// Downloading a newer build in the background asks nothing of anybody. <i>Applying</i> it is a different
    /// act: it replaces the application somebody is using, and deciding that on their behalf is not the same
    /// favour as fetching it for them. So the update waits, and the strip offers « Installer et redémarrer » —
    /// which is what VS Code and Slack do too. The one exception is the version wall, where the app does not
    /// work at all and there is nothing to interrupt.
    /// </para>
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // ⚠️ Nothing above this line, and nothing between it and base.OnStartup that shows UI.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        base.OnStartup(e);
    }
}
