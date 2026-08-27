using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The thin WebView2 client shell. It stores the clinic server address once (AC-2.2), lets the user
/// change it later without reinstalling (AC-2.3), navigates to the Kestrel HTTPS front door
/// (plan Decision #2 — never the internal Next web port), and renders a friendly, recoverable screen
/// when the server is unreachable instead of a blank page or raw browser error (AC-2.4).
/// </summary>
public partial class MainWindow : Window
{
    private ServerConfig _config = new();
    private bool _coreReady;

    /// <summary>Where to get a newer client, as the server reports it. Empty means no link is configured.</summary>
    private string _downloadUrl = string.Empty;

    /// <summary>The newest release the server knows of — what a dismissal is remembered against.</summary>
    private string _latestKnownVersion = string.Empty;

    /// <summary>
    /// The version whose notice the user hid. Per version rather than a bare bool, so dismissing today's notice
    /// does not silently suppress the next release's — and deliberately not persisted: a reminder once per
    /// session is the point.
    /// </summary>
    private string _noticeDismissedForVersion = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The earliest point at which the window has an HWND, which the DWM chrome attributes need.
    ///
    /// <para>
    /// Windows' own preference is the starting theme and the *only* time it is consulted: no page has loaded, so
    /// there is no web app to ask. From the first navigation on, <see cref="ApplyReportedTheme"/> takes over.
    /// Doing this before <c>Loaded</c> is what keeps a dark-mode PC from flashing a white caption on launch.
    /// </para>
    /// </summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowTheme.Apply(this, WindowTheme.ResolveOsPreference());
        WindowTheme.Reapply(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _config = ServerConfigStore.Load();

        if (!await EnsureWebViewAsync())
        {
            return; // WebView2 runtime missing — the unreachable screen already explains it.
        }

        if (_config.IsConfigured)
        {
            NavigateToServer();
        }
        else
        {
            ShowServerConfig();
        }
    }

    /// <summary>Initialises the WebView2 core once. Surfaces a friendly screen if the runtime is missing.</summary>
    private async System.Threading.Tasks.Task<bool> EnsureWebViewAsync()
    {
        if (_coreReady)
        {
            return true;
        }

        try
        {
            // WebView2's DEFAULT user-data folder is created next to the .exe. For an installed app that
            // lives under %ProgramFiles%, a standard (non-admin) user can't write there, so
            // EnsureCoreWebView2Async() fails with E_ACCESSDENIED (0x80070005). Point it at a per-user
            // writable folder instead so the shell works for any user without elevation.
            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClinicManagement", "WebView2");
            System.IO.Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
            WebView.CoreWebView2.ContextMenuRequested += WebView_ContextMenuRequested;
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ThemeReporterScript);

            _coreReady = true;
            return true;
        }
        catch (Exception ex)
        {
            ShowUnreachable(
                "Le composant « Microsoft Edge WebView2 Runtime » n'a pas pu démarrer. " +
                "Vérifiez qu'il est installé sur ce poste.\n\n" + ex.Message);
            return false;
        }
    }

    private async void NavigateToServer()
    {
        if (!_config.IsConfigured)
        {
            ShowServerConfig();
            return;
        }

        ShowConnecting();

        // An address typed with no port does not yet name a server: 5001 is a clinic's own PC and 443 is a hosted
        // deployment, and only the server can say which this is. Resolved once and persisted, so this costs a
        // probe the first time an address is used and nothing on every launch after it.
        if (!_config.PortIsExplicit)
        {
            var resolved = await ServerProbe.ResolveAsync(_config);
            if (!_coreReady)
            {
                return; // The window closed while probing.
            }

            _config = resolved;
            ServerConfigStore.Save(_config);
            ShowConnecting(); // Re-render: the target line was showing the unresolved port.
        }

        // What this server requires of a client, read natively BEFORE the app is loaded — the desktop twin of the
        // mobile shells' launch probe. Until this existed the WPF shell was outside the version floor entirely:
        // it sends no X-Client-Version through a bridge it does not have, and the middleware reads a missing
        // header as "a browser, accept it".
        var requirements = await ClientRequirements.FetchAsync(_config.BaseUrl);
        if (!_coreReady)
        {
            return; // The window closed while probing.
        }

        // Null means the server said nothing readable — offline, an older server with no such route, a malformed
        // body. That must mean "no floor", never "refuse": a shell that will not start because a probe failed is
        // worse than anything it could prevent.
        if (requirements is not null)
        {
            _downloadUrl = requirements.DownloadUrl;

            if (ClientRequirements.IsOlderThan(ClientRequirements.InstalledVersion, requirements.MinimumShellVersion))
            {
                // Below the floor: every /api call would be refused with 426, so loading the app would show a
                // clinic a screen where nothing works. Refuse here instead, with somewhere to go.
                ShowUpdateRequired(requirements.MinimumShellVersion);
                return;
            }

            ShowUpdateNoticeIfNewer(requirements.CurrentShellVersion);
        }

        // Navigate() (rather than setting Source) forces a fresh request even when the URL is unchanged,
        // so "Réessayer" and "Recharger" actually re-attempt the connection.
        WebView.CoreWebView2.Navigate(_config.BaseUrl);
    }

    /// <summary>
    /// A newer build exists but this one still works, so this is a strip and not a wall. Dismissal is remembered
    /// per version: hiding it must not hide the *next* release too.
    /// </summary>
    private void ShowUpdateNoticeIfNewer(string currentShellVersion)
    {
        var installed = ClientRequirements.InstalledVersion;
        _latestKnownVersion = currentShellVersion;
        if (!ClientRequirements.IsOlderThan(installed, currentShellVersion)
            || string.Equals(_noticeDismissedForVersion, currentShellVersion, StringComparison.Ordinal))
        {
            UpdateNoticeBar.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateNoticeText.Text =
            $"Une nouvelle version ({currentShellVersion}) est disponible. Vous utilisez la version {installed}.";
        // No link configured means no button, never a button that goes nowhere.
        UpdateNoticeDownloadButton.Visibility =
            string.IsNullOrWhiteSpace(_downloadUrl) ? Visibility.Collapsed : Visibility.Visible;
        UpdateNoticeBar.Visibility = Visibility.Visible;
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            ShowWebView();
        }
        else
        {
            ShowUnreachable(
                $"Adresse : {_config.BaseUrl}\n" +
                $"Détail : {e.WebErrorStatus}\n\n" +
                "Vérifiez que le serveur est allumé et connecté au réseau, puis réessayez.");
        }
    }

    // ---- Following the web app's theme ----------------------------------------------------------

    /// <summary>
    /// Injected into every top-level document before its own scripts run; reports the resolved theme and every
    /// later change to it.
    ///
    /// <para>
    /// ⚠️ **It watches a class rather than asking once.** The theme is next-themes with <c>attribute="class"</c>,
    /// so the truth is a <c>dark</c> class on <c>&lt;html&gt;</c> — written by a pre-hydration script that has not
    /// necessarily run when this one does, and rewritten whenever the user changes the setting inside the app or
    /// the OS preference moves under <c>system</c>. A single read at navigation-completed would be correct on the
    /// login page and stale for the rest of the session.
    /// </para>
    /// <para>
    /// ⚠️ Top frame only. This script runs in every frame including iframes, and a themeless iframe reporting
    /// « light » would fight the page containing it.
    /// </para>
    /// <para>
    /// Everything is wrapped: a page that somehow breaks this must lose the chrome tint, never its own scripts.
    /// </para>
    /// </summary>
    private const string ThemeReporterScript = """
        (function () {
          try {
            if (window.top !== window) { return; }
            var last = null;
            var report = function () {
              var dark = document.documentElement.classList.contains('dark');
              if (dark === last) { return; }
              last = dark;
              window.chrome.webview.postMessage(dark ? 'theme:dark' : 'theme:light');
            };
            new MutationObserver(report).observe(document.documentElement, {
              attributes: true,
              attributeFilter: ['class'],
            });
            report();
            document.addEventListener('DOMContentLoaded', report);
          } catch (e) { /* chrome tint only — never the page's problem */ }
        })();
        """;

    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // TryGetWebMessageAsString throws if the page posted a non-string (any script on the page may post here).
        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        switch (message)
        {
            case "theme:dark":
                WindowTheme.Apply(this, dark: true);
                break;
            case "theme:light":
                WindowTheme.Apply(this, dark: false);
                break;
        }
    }

    /// <summary>
    /// The two commands that used to be the « Serveur » menu, now on the page's own right-click menu.
    ///
    /// <para>
    /// They are prepended, above a separator: the WebView's stock entries (Retour, Recharger, Inspecter…) are the
    /// page's, and these two are the *shell's*. Burying them under the browser's own list would make the only way
    /// to reach a different server the hardest thing in the window to find.
    /// </para>
    /// </summary>
    private void WebView_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var environment = WebView.CoreWebView2.Environment;

        var reload = environment.CreateContextMenuItem(
            "Recharger", iconStream: null, CoreWebView2ContextMenuItemKind.Command);
        // Not the page's own reload: this re-probes the port and re-reads the server's version floor, which is
        // what makes "Recharger" a fix for a server that has just come back up.
        reload.CustomItemSelected += async (_, _) =>
        {
            if (await EnsureWebViewAsync())
            {
                NavigateToServer();
            }
        };

        var changeServer = environment.CreateContextMenuItem(
            "Changer de serveur…", iconStream: null, CoreWebView2ContextMenuItemKind.Command);
        changeServer.CustomItemSelected += (_, _) => ShowServerConfig();

        var separator = environment.CreateContextMenuItem(
            string.Empty, iconStream: null, CoreWebView2ContextMenuItemKind.Separator);

        e.MenuItems.Insert(0, reload);
        e.MenuItems.Insert(1, changeServer);
        e.MenuItems.Insert(2, separator);
    }

    // ---- View-state switching -------------------------------------------------------------------

    private void ShowWebView()
    {
        WebView.Visibility = Visibility.Visible;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowConnecting()
    {
        ConnectingTarget.Text = _config.BaseUrl;
        ConnectingPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowServerConfig()
    {
        ServerAddressTextBox.Text = _config.IsConfigured ? _config.DisplayAddress : string.Empty;
        ServerConfigError.Visibility = Visibility.Collapsed;
        // A first-run user has nowhere to cancel back to; only offer cancel once a server is configured.
        ServerConfigCancelButton.Visibility = _config.IsConfigured ? Visibility.Visible : Visibility.Collapsed;

        ServerConfigPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
        ServerAddressTextBox.Focus();
    }

    private void ShowUnreachable(string detail)
    {
        UnreachableDetail.Text = detail;
        UnreachablePanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
    }

    // ---- Event handlers -------------------------------------------------------------------------

    private void ChangeServer_Click(object sender, RoutedEventArgs e) => ShowServerConfig();

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (await EnsureWebViewAsync())
        {
            NavigateToServer();
        }
    }

    private void ServerConfigSave_Click(object sender, RoutedEventArgs e) => SaveServerAddress();

    private void ServerAddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveServerAddress();
        }
    }

    private void ServerConfigCancel_Click(object sender, RoutedEventArgs e)
    {
        // Only reachable when a server is already configured (button hidden on first run).
        NavigateToServer();
    }

    private void SaveServerAddress()
    {
        var parsed = ServerConfigStore.ParseAddress(ServerAddressTextBox.Text);
        if (!parsed.IsConfigured)
        {
            ServerConfigError.Text = "Veuillez saisir une adresse de serveur valide.";
            ServerConfigError.Visibility = Visibility.Visible;
            return;
        }

        _config = parsed;
        ServerConfigStore.Save(_config);
        NavigateToServer();
    }

    /// <summary>
    /// This build is below the server's floor. Shown INSTEAD of the app, never over it: loading a page whose
    /// every request returns 426 hands a clinic a screen where nothing works and no reason why.
    /// </summary>
    private void ShowUpdateRequired(string minimumVersion)
    {
        UpdateNoticeBar.Visibility = Visibility.Collapsed; // The wall replaces the strip; both at once is noise.
        UpdateRequiredDetail.Text =
            $"Ce poste utilise la version {ClientRequirements.InstalledVersion}. "
            + $"Le serveur exige au minimum la version {minimumVersion}."
            + Environment.NewLine + Environment.NewLine
            + "Installez la nouvelle version du client pour continuer.";
        UpdateRequiredDownloadButton.Visibility =
            string.IsNullOrWhiteSpace(_downloadUrl) ? Visibility.Collapsed : Visibility.Visible;

        UpdateRequiredPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl))
        {
            return;
        }

        try
        {
            // UseShellExecute so Windows opens it in the default browser rather than trying to execute it.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_downloadUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // A misconfigured URL must not take the shell down — the app behind it still works.
            MessageBox.Show(
                "Le lien de téléchargement n'a pas pu être ouvert." + Environment.NewLine + Environment.NewLine + ex.Message,
                "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DismissUpdateNotice_Click(object sender, RoutedEventArgs e)
    {
        _noticeDismissedForVersion = _latestKnownVersion;
        UpdateNoticeBar.Visibility = Visibility.Collapsed;
    }
}
