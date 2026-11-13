using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.Portable;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// Notices a card or a camera being plugged in (<c>features/camera-photo-import</c>, phase 2): a new volume holding
/// <c>DCIM</c> (Windows' <c>WM_DEVICECHANGE</c>, broadcast to every top-level window), or a portable device (a
/// <c>DeviceWatcher</c>). <see cref="Arrived"/> is raised on the UI thread.
///
/// <para>⚠️ <b>Only what arrives while it watches.</b> A device already connected when the app starts is not
/// « plugged in » — « Chercher l'appareil » on the import screen finds it — so the watcher ignores the initial
/// enumeration, and a card left in a reader does not raise a notice at every launch.</para>
///
/// <para>⚠️ <b>It copies nothing.</b> An arrival only raises a notice; photos are listed and copied when somebody
/// presses « Importer ». A phone charged on this PC exposes a <c>DCIM</c> too, and its owner's photos must not be
/// copied because it was plugged in.</para>
/// </summary>
public sealed class DeviceArrivalWatcher : IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDevtypVolume = 0x0002;

    /// <summary>A volume announces itself before its file system answers.</summary>
    private static readonly TimeSpan VolumeSettle = TimeSpan.FromMilliseconds(1500);

    private readonly Dispatcher _dispatcher;
    private HwndSource? _window;
    private DeviceWatcher? _devices;
    private volatile bool _devicesEnumerated;

    public DeviceArrivalWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event Action<ICameraSource>? Arrived;

    public bool IsRunning => _window != null;

    public void Start(Window window)
    {
        if (_window != null)
        {
            return;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            _window = handle == IntPtr.Zero ? null : HwndSource.FromHwnd(handle);
            _window?.AddHook(WndProc);
        }
        catch
        {
            _window = null;
        }

        try
        {
            _devicesEnumerated = false;
            _devices = DeviceInformation.CreateWatcher(StorageDevice.GetDeviceSelector());
            _devices.Added += OnDeviceAdded;

            // A watcher with no Updated/Removed handler may never raise Added on some Windows builds.
            _devices.Updated += (_, _) => { };
            _devices.Removed += (_, _) => { };
            _devices.EnumerationCompleted += (_, _) => _devicesEnumerated = true;
            _devices.Start();
        }
        catch
        {
            // No portable-device layer on this Windows: cards still arrive through the window message.
            _devices = null;
        }
    }

    public void Stop()
    {
        try
        {
            _window?.RemoveHook(WndProc);
        }
        catch
        {
            // The window is closing.
        }

        _window = null;

        if (_devices != null)
        {
            try
            {
                _devices.Added -= OnDeviceAdded;
                if (_devices.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    _devices.Stop();
                }
            }
            catch
            {
                // Already stopped.
            }

            _devices = null;
        }
    }

    public void Dispose() => Stop();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmDeviceChange || wParam.ToInt64() != DbtDeviceArrival || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            // DEV_BROADCAST_HDR { size, devicetype, reserved } then DEV_BROADCAST_VOLUME's unit mask.
            if (Marshal.ReadInt32(lParam, 4) != DbtDevtypVolume)
            {
                return IntPtr.Zero;
            }

            var mask = Marshal.ReadInt32(lParam, 12);
            for (var drive = 0; drive < 26; drive++)
            {
                if ((mask & (1 << drive)) != 0)
                {
                    _ = OnVolumeArrivedAsync((char)('A' + drive));
                }
            }
        }
        catch
        {
            // A malformed broadcast is somebody else's problem.
        }

        return IntPtr.Zero;
    }

    private async Task OnVolumeArrivedAsync(char letter)
    {
        try
        {
            await Task.Delay(VolumeSettle);
            var card = await Task.Run(() => CameraSources.CardAt($"{letter}:\\"));
            if (card != null)
            {
                Arrived?.Invoke(card);
            }
        }
        catch
        {
            // Not a card.
        }
    }

    private async void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
    {
        if (!_devicesEnumerated)
        {
            return;
        }

        try
        {
            var camera = await CameraSources.FromPortableDeviceAsync(device.Id, device.Name);
            if (camera != null)
            {
                await _dispatcher.InvokeAsync(() => Arrived?.Invoke(camera));
            }
        }
        catch
        {
            // Not a camera.
        }
    }
}
