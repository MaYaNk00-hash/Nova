using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Nova.Core;

namespace Nova.UI;

public sealed class TrayManager : IDisposable
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 1024;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly IslandWindow _islandWindow;
    private IntPtr _hwnd;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconAdded = false;
    private HwndSource? _hwndSource;

    public ContextMenu? TrayContextMenu { get; set; }

    public TrayManager(IslandWindow islandWindow)
    {
        _islandWindow = islandWindow;
    }

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        CreateTrayIcon();
        AppLogger.Info("TrayManager initialized and tray icon added.");
    }

    private void CreateTrayIcon()
    {
        try
        {
            _hIcon = CreateNativeOrbIcon();

            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "NOVA — Desktop Companion"
            };

            _isIconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to create tray icon", ex);
        }
    }

    private static IntPtr CreateNativeOrbIcon()
    {
        const int width = 16;
        const int height = 16;
        uint[] pixels = new uint[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - 7.5;
                double dy = y - 7.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= 3.0)
                {
                    pixels[y * width + x] = 0xFF64D2FF; // Cyan bright core
                }
                else if (dist <= 6.5)
                {
                    pixels[y * width + x] = 0xFF38BDF8; // Cyan accent ring
                }
                else if (dist <= 7.5)
                {
                    pixels[y * width + x] = 0xFF0E1117; // Dark bezel
                }
                else
                {
                    pixels[y * width + x] = 0x00000000; // Transparent
                }
            }
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        IntPtr hbmColor = IntPtr.Zero;
        IntPtr hbmMask = IntPtr.Zero;

        try
        {
            hbmColor = CreateBitmap(width, height, 1, 32, handle.AddrOfPinnedObject());
            byte[] maskBytes = new byte[(width * height) / 8];
            var maskHandle = GCHandle.Alloc(maskBytes, GCHandleType.Pinned);
            try
            {
                hbmMask = CreateBitmap(width, height, 1, 1, maskHandle.AddrOfPinnedObject());
            }
            finally
            {
                maskHandle.Free();
            }

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor
            };

            var hIcon = CreateIconIndirect(ref iconInfo);
            if (hIcon != IntPtr.Zero)
            {
                return hIcon;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to create custom orb icon: {ex.Message}");
        }
        finally
        {
            handle.Free();
            if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
            if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
        }

        // Fallback to standard application icon (IDI_APPLICATION = 32512)
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONUP)
            {
                OnTrayLeftClick();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP)
            {
                OnTrayRightClick();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void OnTrayLeftClick()
    {
        AppLogger.Debug("Tray icon left-clicked.");
        if (_islandWindow.CurrentState == IslandState.Hidden)
        {
            _islandWindow.TransitionTo(IslandState.Compact);
        }
        else if (_islandWindow.CurrentState == IslandState.Compact)
        {
            _islandWindow.TransitionTo(IslandState.Expanded);
        }
        else
        {
            _islandWindow.TransitionTo(IslandState.Compact);
        }
    }

    private void OnTrayRightClick()
    {
        AppLogger.Debug("Tray icon right-clicked.");
        if (TrayContextMenu != null)
        {
            SetForegroundWindow(_hwnd);
            TrayContextMenu.IsOpen = true;
        }
    }

    public void Dispose()
    {
        if (_isIconAdded && _hwnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1001
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _isIconAdded = false;
            AppLogger.Info("Tray icon removed.");
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
