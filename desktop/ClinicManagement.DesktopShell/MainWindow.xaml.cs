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

    public MainWindow()
    {
        InitializeComponent();
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
            await WebView.EnsureCoreWebView2Async();
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

    private void NavigateToServer()
    {
        if (!_config.IsConfigured)
        {
            ShowServerConfig();
            return;
        }

        ShowConnecting();
        // Navigate() (rather than setting Source) forces a fresh request even when the URL is unchanged,
        // so "Réessayer" and "Recharger" actually re-attempt the connection.
        WebView.CoreWebView2.Navigate(_config.BaseUrl);
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

    // ---- View-state switching -------------------------------------------------------------------

    private void ShowWebView()
    {
        WebView.Visibility = Visibility.Visible;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowConnecting()
    {
        ConnectingTarget.Text = _config.BaseUrl;
        ConnectingPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowServerConfig()
    {
        ServerAddressTextBox.Text = _config.IsConfigured ? $"{_config.Host}:{_config.Port}" : string.Empty;
        ServerConfigError.Visibility = Visibility.Collapsed;
        // A first-run user has nowhere to cancel back to; only offer cancel once a server is configured.
        ServerConfigCancelButton.Visibility = _config.IsConfigured ? Visibility.Visible : Visibility.Collapsed;

        ServerConfigPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        UnreachablePanel.Visibility = Visibility.Collapsed;
        ServerAddressTextBox.Focus();
    }

    private void ShowUnreachable(string detail)
    {
        UnreachableDetail.Text = detail;
        UnreachablePanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ServerConfigPanel.Visibility = Visibility.Collapsed;
    }

    // ---- Event handlers -------------------------------------------------------------------------

    private void ChangeServer_Click(object sender, RoutedEventArgs e) => ShowServerConfig();

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (await EnsureWebViewAsync())
        {
            NavigateToServer();
        }
    }

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
}
