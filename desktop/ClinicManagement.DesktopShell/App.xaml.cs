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
    /// ⚠️ <b><c>SetAutoApplyOnStartup(true)</c> is what makes the flow silent.</b> A staged update is applied on
    /// the next launch with nobody asked anything — the behaviour every desktop application has. The shell
    /// therefore never closes itself mid-consultation to finish an update, which is the one thing a « restart
    /// now » prompt cannot avoid asking for.
    /// </para>
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // ⚠️ Nothing above this line, and nothing between it and base.OnStartup that shows UI.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true)
            .Run();

        base.OnStartup(e);
    }
}
