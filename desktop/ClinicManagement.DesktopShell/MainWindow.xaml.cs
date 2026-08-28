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

    /// <summary>The newest release the server knows of — what a dismissal is remembered against.</summary>
    private string _latestKnownVersion = string.Empty;

    /// <summary>
    /// The update that is downloaded and waiting, or <c>null</c>. Held because applying it is a separate act the
    /// user asks for — see <c>ShellUpdater.ApplyAndRestart</c>.
    /// </summary>
    private Velopack.UpdateInfo? _stagedUpdate;

    /// <summary>
    /// Guards the update path against re-entry — a timer tick landing on a download already in progress, or a
    /// second press of the wall's button.
    ///
    /// <para>⚠️ There is deliberately no download URL and no expected hash held here any more. Velopack derives
    /// the feed from the server address the shell is already using and verifies each package's checksum itself,
    /// so a URL and a digest carried alongside would be a second authority over facts it already owns — and the
    /// kind that goes stale silently.</para>
    /// </summary>
    private bool _updateInProgress;

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
            ShowModeChoice();
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
        // ⚠️ No button: the update is fetched and staged in the background (CheckForUpdate) and applied on the
        // next launch. A « télécharger » control here would be a second, manual mechanism beside the automatic
        // one, and the two would disagree about what has already been downloaded.
        UpdateNoticeDownloadButton.Visibility = Visibility.Collapsed;
        UpdateNoticeBar.Visibility = Visibility.Visible;
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            ShowWebView();
            RunArchiveCopyIfDue();
            StartFileMirrorIfEnabled();
            StartUpdateChecks();
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

        // clinic-archive-auto-copy. Here rather than in the app's own Paramètres because the folder and the
        // schedule are facts about THIS machine, which a web page can neither pick nor know.
        var archiveCopy = environment.CreateContextMenuItem(
            "Copie automatique de l'archive…", iconStream: null, CoreWebView2ContextMenuItemKind.Command);
        archiveCopy.CustomItemSelected += (_, _) => ShowArchiveCopySettings();

        var separator = environment.CreateContextMenuItem(
            string.Empty, iconStream: null, CoreWebView2ContextMenuItemKind.Separator);

        e.MenuItems.Insert(0, reload);
        e.MenuItems.Insert(1, changeServer);
        e.MenuItems.Insert(2, archiveCopy);
        e.MenuItems.Insert(3, separator);
    }

    // ---- Automatic archive copy (clinic-archive-auto-copy) --------------------------------------

    private bool _archiveCopyChecked;

    private void ShowArchiveCopySettings()
    {
        new ArchiveCopyWindow(_config) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// Takes a copy if one is owed, on the first successful load of this session.
    ///
    /// <para>⚠️ <b>Fire-and-forget, and silent on failure by design.</b> It runs behind a clinic's day; a modal
    /// or a toast about a backup would interrupt a consultation for something nobody asked for right then. A
    /// failure is surfaced where it can be acted on — « Copie automatique de l'archive… », which shows the
    /// outcome of a copy run there — and the server keeps nagging through « aucune archive n'est sortie »
    /// until one actually lands, so a silently failing schedule cannot look healthy.</para>
    ///
    /// <para>⚠️ <b>Once per session</b> (`_archiveCopyChecked`): every reload and every in-app navigation raises
    /// `NavigationCompleted`, and re-entering here would start a second multi-gigabyte download over the first.</para>
    /// </summary>
    private void RunArchiveCopyIfDue()
    {
        if (_archiveCopyChecked)
        {
            return;
        }

        _archiveCopyChecked = true;

        var settings = ArchiveCopySettingsStore.Load();
        if (!settings.IsConfigured)
        {
            return; // AC-10 — absent, not broken.
        }

        var newest = ArchiveCopyService.NewestCopyUtc(settings.Folder);
        if (!settings.IsDue(newest, DateTime.UtcNow))
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(() => new ArchiveCopyService(_config, settings).CopyNowAsync());
    }

    // ---- Browsable file mirror (patient-file-mirror) --------------------------------------------

    /// <summary>
    /// How often the mirror looks for new files while the app is open.
    ///
    /// <para>⚠️ <b>Minutes, where the archive's cadence is days</b>, and the difference is the feature: the
    /// archive is a full snapshot rebuilt whole, so taking one hourly would be absurd, while the mirror fetches
    /// only what it does not already have — normally nothing. A doctor who scans a panoramique at 10:00 has it on
    /// this machine before lunch, which the archive alone could never do.</para>
    /// </summary>
    private static readonly TimeSpan MirrorInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How often a running shell re-asks the server whether a newer build exists.
    ///
    /// <para>⚠️ <b>Because a launch-time probe is not enough for a machine nobody restarts.</b> The check used to
    /// happen only on connect — launch, « Recharger », « Réessayer » — so a PC left running Monday to Friday never
    /// noticed a release published on Tuesday. A practice does not reboot its reception machine to find out about
    /// updates.</para>
    ///
    /// <para>⚠️ <b>Thirty minutes, matching the file mirror in this same file — and it started at two hours,
    /// which was wrong for a reason worth recording.</b> The reasoning was « a release happens a few times a year,
    /// so being current within the working day is enough ». True, and it still made the feature feel broken: a
    /// release published three minutes after somebody opened the app was then invisible for the rest of the
    /// morning, which is indistinguishable from an updater that does not work. The check is one small JSON GET
    /// against the clinic's own server; there is nothing to be stingy with.</para>
    /// </summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(30);

    private System.Windows.Threading.DispatcherTimer? _updateTimer;

    /// <summary>Guards a tick starting while a previous check is still in flight on a slow link.</summary>
    private int _updateCheckRunning;

    private System.Windows.Threading.DispatcherTimer? _mirrorTimer;

    /// <summary>Guards against a second run starting while a first is still walking a large cabinet.</summary>
    private int _mirrorRunning;

    /// <summary>
    /// Starts the mirror's own schedule, once per session, on the first successful load.
    ///
    /// <para>⚠️ <b>A repeating timer rather than a once-per-session run</b>, unlike the archive above: the whole
    /// point is to be current within the day, and a machine left open from Monday to Friday would otherwise
    /// mirror once on Monday. It is cheap to repeat — a tick that finds nothing new is one paged read.</para>
    ///
    /// <para>⚠️ Silent, and fire-and-forget, for exactly the reason the archive copy is (see above): it runs
    /// behind a clinic's day. The settings window is where an outcome is shown, on demand.</para>
    /// </summary>
    private void StartFileMirrorIfEnabled()
    {
        if (_mirrorTimer != null)
        {
            return;
        }

        _mirrorTimer = new System.Windows.Threading.DispatcherTimer { Interval = MirrorInterval };
        _mirrorTimer.Tick += (_, _) => RunFileMirror();
        _mirrorTimer.Start();

        RunFileMirror();
    }

    private void RunFileMirror()
    {
        // Re-read every tick rather than capturing at start-up: the user may have ticked the box, changed the
        // folder or pasted a new key in the settings window since, and a captured copy would ignore all three.
        var settings = ArchiveCopySettingsStore.Load();
        if (!settings.IsConfigured || !settings.MirrorFiles)
        {
            return; // AC-9 — absent, not idle.
        }

        // A first mirror of a real cabinet can outlast the interval by hours; letting the timer stack runs would
        // have several passes fetching the same files into the same `.part` paths.
        if (System.Threading.Interlocked.Exchange(ref _mirrorRunning, 1) == 1)
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await new FileMirrorService(_config, settings).MirrorNowAsync();
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _mirrorRunning, 0);
            }
        });
    }

    // ---- View-state switching -------------------------------------------------------------------

    private void ShowWebView()
    {
        WebView.Visibility = Visibility.Visible;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
        ModeChoicePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowConnecting()
    {
        ConnectingTarget.Text = _config.BaseUrl;
        ConnectingPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
        ModeChoicePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The first screen a new install shows, and where « Changer de serveur » returns to. It is a fork, not a
    /// setting: nothing is written until one of the two branches completes.
    /// </summary>
    private void ShowModeChoice()
    {
        ModeChoicePanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
        ChooseHostedButton.Focus();
    }

    private void ShowServerConfig()
    {
        ServerAddressTextBox.Text = _config.IsConfigured ? _config.DisplayAddress : string.Empty;
        ServerConfigError.Visibility = Visibility.Collapsed;
        // Always reachable now: the chooser is behind this panel even on a first run, so there IS somewhere to
        // go back to. Only the word changes -- « Retour » to the fork, « Annuler » to the app already running.
        ServerConfigCancelButton.Visibility = Visibility.Visible;
        ServerConfigCancelButton.Content = _config.IsConfigured ? "Annuler" : "Retour";

        ServerConfigPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        UpdateRequiredPanel.Visibility = Visibility.Collapsed;
        ModeChoicePanel.Visibility = Visibility.Collapsed;
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
        ModeChoicePanel.Visibility = Visibility.Collapsed;
    }

    // ---- Event handlers -------------------------------------------------------------------------

    // Back to the fork, not to the address box: a clinic that moves from its own PC to the hosted plan would
    // otherwise have to be told a hostname to type -- the exact question the chooser exists to avoid.
    private void ChangeServer_Click(object sender, RoutedEventArgs e) => ShowModeChoice();

    /// <summary>« APEXA Cloud » -- the address is ours to know, so it is not asked for.</summary>
    private void ChooseHosted_Click(object sender, RoutedEventArgs e)
    {
        _config = ServerConfig.Hosted();
        ServerConfigStore.Save(_config);
        NavigateToServer();
    }

    /// <summary>« Serveur du cabinet » -- the clinic's own PC, whose address only the clinic knows.</summary>
    private void ChooseLocal_Click(object sender, RoutedEventArgs e) => ShowServerConfig();

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
        if (_config.IsConfigured)
        {
            NavigateToServer();
        }
        else
        {
            ShowModeChoice();
        }
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
        // ⚠️ Always offered now. It used to be hidden unless an operator had configured a download URL — which
        // meant the one screen a stranded shell can act on could come up with no action at all. The feed is the
        // server this shell just spoke to, so there is always something to try, and a failure says so in words.
        UpdateRequiredDownloadButton.Visibility = Visibility.Visible;

        UpdateRequiredPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        ModeChoicePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The wall's own recovery: a shell below the floor updates itself and comes back — no UAC prompt, nothing to
    /// install by hand.
    ///
    /// <para>⚠️ <b>This is the one place a restart is right.</b> Below the floor every <c>/api</c> call returns
    /// 426, so the app behind this screen does not work: there is no consultation to interrupt, and staging for
    /// « the next launch » would strand somebody who has just been told to update. Everywhere else the staged
    /// update waits, silently.</para>
    ///
    /// <para>⚠️ On failure it says so and keeps the button, rather than closing anything: the alternative to a
    /// retry on this screen is an operator visit.</para>
    /// </summary>
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInProgress)
        {
            return;
        }

        _updateInProgress = true;
        UpdateRequiredDownloadButton.IsEnabled = false;
        var original = UpdateRequiredDetail.Text;

        try
        {
            var progress = new Progress<int>(p => UpdateRequiredDetail.Text = $"Téléchargement… {p} %");
            if (await ShellUpdater.DownloadAndRestartAsync(_config.BaseUrl, progress))
            {
                return; // The process is being replaced; nothing after this runs.
            }

            UpdateRequiredDetail.Text =
                "La mise à jour n'a pas pu être récupérée depuis le serveur du cabinet. " +
                "Vérifiez votre connexion, puis réessayez.";
        }
        catch (Exception)
        {
            UpdateRequiredDetail.Text = original;
        }
        finally
        {
            _updateInProgress = false;
            UpdateRequiredDownloadButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Starts the periodic « is there a newer build? » check, once per session, and runs one straight away.
    ///
    /// <para>⚠️ <b>It only ever raises the strip, never the wall.</b> A shell that was above the floor when it
    /// started stays running: taking the app away mid-consultation because an operator moved a number is worse
    /// than anything the floor protects against, and the floor is enforced per request server-side anyway.</para>
    /// </summary>
    private void StartUpdateChecks()
    {
        if (_updateTimer != null)
        {
            return;
        }

        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = UpdateCheckInterval };
        _updateTimer.Tick += (_, _) => CheckForUpdate();
        _updateTimer.Start();

        CheckForUpdate();
    }

    /// <summary>
    /// Fetches and stages a newer build, silently. The only thing this ever puts on screen is the one line saying
    /// a new version is ready — and even that has no action, because there is nothing for anybody to do.
    /// </summary>
    private async void CheckForUpdate()
    {
        if (_updateInProgress || System.Threading.Interlocked.Exchange(ref _updateCheckRunning, 1) == 1)
        {
            return;
        }

        try
        {
            _updateInProgress = true;

            var outcome = await ShellUpdater.CheckAndStageAsync(_config.BaseUrl);
            if (outcome?.StagedVersion is null || !_coreReady)
            {
                return;
            }

            _stagedUpdate = outcome.Info;
            ShowUpdateStaged(outcome.StagedVersion);
        }
        catch (Exception)
        {
            // See ShellUpdater: every failure here means « not today », never a dialog.
        }
        finally
        {
            _updateInProgress = false;
            System.Threading.Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }

    /// <summary>
    /// Says a newer version is downloaded and will be running next time.
    ///
    /// <para>⚠️ <b>No action, and that is the point.</b> Velopack applies a staged update on the next launch
    /// (<c>SetAutoApplyOnStartup</c>, `App.xaml.cs`), so there is nothing to press. A button here would offer to
    /// restart the app in the middle of an appointment to deliver something the user gets for free by closing it
    /// at the end of the day.</para>
    ///
    /// <para>⚠️ The dismissal stays per version, so hiding this cannot suppress the next release's.</para>
    /// </summary>
    private void ShowUpdateStaged(string version)
    {
        _latestKnownVersion = version;
        if (_noticeDismissedForVersion == version)
        {
            return;
        }

        UpdateNoticeText.Text =
            $"La version {version} est prête à être installée. APEXA redémarrera — terminez ce que vous faites, " +
            "puis installez-la quand cela vous convient.";
        UpdateNoticeDownloadButton.Visibility = Visibility.Visible;
        UpdateNoticeBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// « Installer et redémarrer » — the user's decision, not ours.
    ///
    /// <para>⚠️ The download already happened, silently, so this is only the swap-and-restart. It is deliberately
    /// the ONLY thing that replaces a running APEXA outside the version wall: an update applied on somebody's
    /// behalf mid-appointment is the interruption this product avoids everywhere else.</para>
    ///
    /// <para>⚠️ Nothing after <c>ApplyAndRestart</c> runs when it succeeds — the process is replaced. A failure
    /// leaves the strip and the button as they were, because a staged update stays staged and retrying costs
    /// nothing.</para>
    /// </summary>
    private void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_stagedUpdate is null || _updateInProgress)
        {
            return;
        }

        _updateInProgress = true;
        UpdateNoticeDownloadButton.IsEnabled = false;
        UpdateNoticeText.Text = "Installation…";

        if (ShellUpdater.ApplyAndRestart(_stagedUpdate))
        {
            return; // The process is being replaced.
        }

        _updateInProgress = false;
        UpdateNoticeDownloadButton.IsEnabled = true;
        UpdateNoticeText.Text =
            "L'installation n'a pas pu démarrer. La mise à jour reste téléchargée — vous pouvez réessayer.";
    }

    private void DismissUpdateNotice_Click(object sender, RoutedEventArgs e)
    {
        _noticeDismissedForVersion = _latestKnownVersion;
        UpdateNoticeBar.Visibility = Visibility.Collapsed;
    }
}
