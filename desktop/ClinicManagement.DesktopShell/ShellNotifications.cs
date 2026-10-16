using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// A chat message for the person at this PC while the app is not in front (staff-chat AC-4.3, since 1.7): a Windows
/// notification and a flashing taskbar button; a click brings the window forward on that conversation.
///
/// <para>⚠️ <b>The toast is silent</b> — the page already played the chat's own tone, and two sounds for one
/// message is what the whole alert design exists to prevent. ⚠️ Every failure is swallowed: a PC with
/// notifications turned off still has the in-app notice, the tone and the badge.</para>
/// </summary>
public static class ShellNotifications
{
    private const string ConversationArgument = "conversation";

    /// <summary>A click — on a toast raised now, or one left in the action centre before a restart — lands here.</summary>
    public static void Listen(Action<string> open)
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                try
                {
                    var arguments = ToastArguments.Parse(args.Argument);
                    if (arguments.TryGetValue(ConversationArgument, out var conversation)
                        && conversation.Length is > 0 and <= ShellNotifyMessage.MaxConversation)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => open(conversation));
                    }
                }
                catch
                {
                    // A toast from another build, or a malformed argument: nothing to open.
                }
            };
        }
        catch
        {
            // No notification platform on this Windows: the app works without it.
        }
    }

    public static void Show(Window window, ShellNotifyMessage message)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument(ConversationArgument, message.Conversation)
                .AddText(message.Title)
                .AddText(message.Body)
                .AddAudio(new ToastAudio { Silent = true });

            if (message.Urgent)
            {
                // A reminder stays on screen until answered — what an urgent message does in the app (AC-4.5).
                builder.SetToastScenario(ToastScenario.Reminder)
                    .AddButton(new ToastButton().SetContent("Ouvrir").AddArgument(ConversationArgument, message.Conversation))
                    .AddButton(new ToastButtonDismiss("Fermer"));
            }

            builder.Show(toast =>
            {
                // One toast per conversation: the newest replaces the last.
                toast.Tag = TagOf(message.Conversation);
                toast.Group = "staffchat";
            });
        }
        catch
        {
            // Notifications off, or no platform: the flash below still says it.
        }

        Flash(window);
    }

    /// <summary>Brings the window forward, restored if it was minimised.</summary>
    public static void BringForward(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        // Windows refuses a plain Activate() from the background; a topmost round trip is the accepted way past it.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>Removes this build's registration — run by the uninstaller hook, never on an ordinary exit.</summary>
    public static void Unregister()
    {
        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // Nothing registered.
        }
    }

    public static string TagOf(string conversation)
    {
        // Windows caps a toast tag at 64 characters; a conversation key can be longer.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(conversation));
        return Convert.ToHexString(hash, 0, 16);
    }

    private static void Flash(Window window)
    {
        try
        {
            var info = new FlashWindowInfo
            {
                Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Window = new WindowInteropHelper(window).Handle,
                // Caption and taskbar, until the window comes to the foreground.
                Flags = FlashAll | FlashUntilForeground,
                Count = 0,
                Timeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            // Cosmetic: never why a message is missed.
        }
    }

    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
