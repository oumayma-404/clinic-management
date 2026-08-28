using System;
using System.Windows;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Where the automatic copy is set up (<c>clinic-archive-auto-copy</c>).
///
/// <para>⚠️ <b>A window rather than a sixth view state in <c>MainWindow</c>.</b> Those five are mutually exclusive
/// states of *reaching the server*; this is a setting, reached deliberately from the right-click menu, and folding
/// it in would mean the app could not be on screen behind it.</para>
/// </summary>
public partial class ArchiveCopyWindow : Window
{
    private readonly ServerConfig _server;
    private ArchiveCopySettings _settings;

    public ArchiveCopyWindow(ServerConfig server)
    {
        InitializeComponent();

        _server = server;
        _settings = ArchiveCopySettingsStore.Load();

        FolderTextBox.Text = _settings.Folder;
        GrantTextBox.Text = _settings.GrantSecret;
        EveryDaysTextBox.Text = _settings.EveryDays.ToString();
        KeepTextBox.Text = _settings.KeepCopies.ToString();

        FolderTextBox.TextChanged += (_, _) => ReportEncryption();
        ReportEncryption();
    }

    /// <summary>AC-8 — three answers, and « indéterminé » is one of them rather than an assumption.</summary>
    private void ReportEncryption()
    {
        var folder = FolderTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            EncryptionText.Text = "Choisissez un dossier pour vérifier le chiffrement du disque.";
            return;
        }

        EncryptionText.Text = ArchiveCopyService.IsDriveEncrypted(folder) switch
        {
            true => "Ce disque est protégé par BitLocker : en cas de vol, les copies restent illisibles.",
            false => "Ce disque n'est PAS protégé par BitLocker. Quiconque récupère ce poste peut lire les copies. "
                     + "Activez BitLocker sur ce disque.",
            _ => "Le chiffrement de ce disque n'a pas pu être vérifié (BitLocker demande des droits "
                 + "administrateur). Vérifiez-le vous-même avant de laisser des copies s'accumuler.",
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        // The WPF-native folder picker (.NET 8). No WinForms reference, no Vista COM dialog by hand.
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Dossier de destination des copies",
            InitialDirectory = FolderTextBox.Text?.Trim() ?? "",
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderTextBox.Text = dialog.FolderName;
        }
    }

    /// <summary>Reads the form, or null with the reason already on screen.</summary>
    private ArchiveCopySettings? Collect()
    {
        var folder = FolderTextBox.Text?.Trim() ?? "";
        var grant = GrantTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(grant))
        {
            Report("Indiquez un dossier et collez la clé du poste.");
            return null;
        }

        if (!int.TryParse(EveryDaysTextBox.Text, out var days) || days < 1)
        {
            Report("« Une copie tous les » doit être un nombre de jours d'au moins 1.");
            return null;
        }

        if (!int.TryParse(KeepTextBox.Text, out var keep) || keep < 1)
        {
            Report("« Copies conservées » doit être au moins 1.");
            return null;
        }

        return new ArchiveCopySettings
        {
            Folder = folder,
            GrantSecret = grant,
            EveryDays = days,
            KeepCopies = keep,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var collected = Collect();
        if (collected == null)
        {
            return;
        }

        try
        {
            ArchiveCopySettingsStore.Save(collected);
            _settings = collected;
            Report("Enregistré. La copie se fera automatiquement quand elle sera due.");
        }
        catch (Exception ex)
        {
            Report($"Les réglages n'ont pas pu être enregistrés : {ex.Message}");
        }
    }

    /// <summary>
    /// Runs one copy now, so setting this up is verifiable on the spot rather than a week later.
    /// ⚠️ Saves first — a « Copier maintenant » that used unsaved values would test something the schedule will not do.
    /// </summary>
    private async void CopyNow_Click(object sender, RoutedEventArgs e)
    {
        var collected = Collect();
        if (collected == null)
        {
            return;
        }

        CopyNowButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        Report("Copie en cours… cela peut prendre plusieurs minutes.");

        try
        {
            ArchiveCopySettingsStore.Save(collected);
            _settings = collected;

            var outcome = await new ArchiveCopyService(_server, _settings).CopyNowAsync();
            Report(outcome.Message);
        }
        finally
        {
            CopyNowButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
    }

    private void Report(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
