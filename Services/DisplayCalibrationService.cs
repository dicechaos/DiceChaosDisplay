using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DiceChaosDisplay.Services;

public sealed record MonitorDescriptor(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    Int32Rect PhysicalBounds,
    Int32Rect WorkAreaBounds,
    uint EffectiveDpiX,
    uint EffectiveDpiY,
    uint RawEdidDpiX,
    uint RawEdidDpiY)
{
    public double DpiScaleX => EffectiveDpiX / 96.0;
    public double DpiScaleY => EffectiveDpiY / 96.0;

    public string DisplayLabel =>
        $"{FriendlyName} ({PhysicalBounds.Width}×{PhysicalBounds.Height}{(IsPrimary ? ", Primary" : "")})";
}

public static class DisplayCalibrationService
{
    public static IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var friendlyNames = QueryFriendlyMonitorNames();
        var list = new List<MonitorDescriptor>();
        int index = 1;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                uint effX = 96, effY = 96;
                uint rawX = 96, rawY = 96;
                try
                {
                    _ = GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out effX, out effY);
                    _ = GetDpiForMonitor(hMonitor, MonitorDpiType.RawDpi, out rawX, out rawY);
                }
                catch
                {
                    // Fallback to 96 DPI if shcore call fails
                }

                string devName = info.szDevice?.TrimEnd('\0') ?? $"DISPLAY{index}";
                if (!friendlyNames.TryGetValue(devName, out string? friendly) || string.IsNullOrWhiteSpace(friendly))
                {
                    friendly = $"Display {index}";
                }

                bool isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                var phys = new Int32Rect(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top);

                var work = new Int32Rect(
                    info.rcWork.Left,
                    info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top);

                list.Add(new MonitorDescriptor(
                    devName,
                    friendly,
                    isPrimary,
                    phys,
                    work,
                    effX == 0 ? 96 : effX,
                    effY == 0 ? 96 : effY,
                    rawX == 0 ? 96 : rawX,
                    rawY == 0 ? 96 : rawY));

                index++;
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static MonitorDescriptor? SelectTargetTvMonitor(string? preferredDeviceName)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        // Default to first non-primary monitor if present, otherwise primary
        return monitors.FirstOrDefault(m => !m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Positions a borderless WPF window pixel-accurately onto the target monitor in PerMonitorV2 mode.
    /// </summary>
    public static void PositionBorderlessWindowOnMonitor(Window window, MonitorDescriptor monitor)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.EnsureHandle();

        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            monitor.PhysicalBounds.X,
            monitor.PhysicalBounds.Y,
            monitor.PhysicalBounds.Width,
            monitor.PhysicalBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Positions a resizable floating preview window on the target monitor when sharing the DM's own display,
    /// avoiding a fullscreen borderless trap on single-monitor setups.
    /// </summary>
    public static void PositionFloatingPreviewWindowOnMonitor(Window window, MonitorDescriptor? monitor)
    {
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.CanResize;

        if (monitor is null)
        {
            window.Width = 960;
            window.Height = 540;
            return;
        }

        double scaleX = Math.Max(0.25, monitor.DpiScaleX);
        double scaleY = Math.Max(0.25, monitor.DpiScaleY);

        int workW = Math.Max(640, monitor.WorkAreaBounds.Width);
        int workH = Math.Max(400, monitor.WorkAreaBounds.Height);

        int targetPxW = Math.Clamp((int)Math.Round(workW * 0.56), Math.Min(640, workW), workW - 40);
        int targetPxH = Math.Clamp((int)Math.Round(targetPxW * 9.0 / 16.0), Math.Min(380, workH), (int)Math.Round(workH * 0.74));

        int posX = monitor.WorkAreaBounds.X + Math.Max(20, (workW - targetPxW) / 2 + (int)Math.Round(workW * 0.04));
        int posY = monitor.WorkAreaBounds.Y + Math.Max(20, (workH - targetPxH) / 2 + (int)Math.Round(workH * 0.03));

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            SetWindowPos(
                helper.Handle,
                IntPtr.Zero,
                posX,
                posY,
                targetPxW,
                targetPxH,
                SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        else
        {
            window.Width = targetPxW / scaleX;
            window.Height = targetPxH / scaleY;
            window.Left = posX / scaleX;
            window.Top = posY / scaleY;
        }
    }

    /// <summary>
    /// Returns the GDI DeviceName (e.g. \\.\DISPLAY1) of the monitor currently hosting the given WPF window.
    /// </summary>
    public static string? GetMonitorDeviceNameForWindow(Window? window)
    {
        if (window is not null)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
                    if (GetMonitorInfoW(hMon, ref info) && !string.IsNullOrWhiteSpace(info.szDevice))
                    {
                        return info.szDevice.TrimEnd('\0');
                    }
                }
            }
        }

        var monitors = GetMonitors();
        return (monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault())?.DeviceName;
    }

    /// <summary>
    /// Determines whether the target TV monitor is the exact same physical display hosting the DM Studio window.
    /// </summary>
    public static bool IsSameMonitorAsWindow(Window? dmWindow, MonitorDescriptor? targetMonitor)
    {
        if (targetMonitor is null)
            return GetMonitors().Count <= 1;

        string? dmDevice = GetMonitorDeviceNameForWindow(dmWindow);
        if (!string.IsNullOrWhiteSpace(dmDevice))
        {
            return string.Equals(dmDevice, targetMonitor.DeviceName, StringComparison.OrdinalIgnoreCase);
        }

        return targetMonitor.IsPrimary && GetMonitors().Count <= 1;
    }

    /// <summary>
    /// Estimates the WPF DIP viewport dimensions of a monitor so the DM canvas TV VIEW guide matches the real TV even while the TV window is OFF.
    /// </summary>
    public static Size GetEstimatedDipViewportSize(MonitorDescriptor monitor)
    {
        double w = Math.Max(320.0, monitor.PhysicalBounds.Width / Math.Max(0.25, monitor.DpiScaleX));
        double h = Math.Max(200.0, monitor.PhysicalBounds.Height / Math.Max(0.25, monitor.DpiScaleY));
        return new Size(w, h);
    }

    /// <summary>
    /// Calculates physical screen Pixels-Per-Inch (PPI) from pixel resolution and diagonal size in inches.
    /// </summary>
    public static double CalculatePhysicalPpi(int physicalWidthPx, int physicalHeightPx, double diagonalInches)
    {
        if (diagonalInches <= 0.1 || physicalWidthPx <= 0 || physicalHeightPx <= 0)
            return 96.0;

        double diagonalPx = Math.Sqrt((double)physicalWidthPx * physicalWidthPx + (double)physicalHeightPx * physicalHeightPx);
        return diagonalPx / diagonalInches;
    }

    /// <summary>
    /// Computes the exact WPF camera Zoom factor (DIPs per map pixel) so that 1 map grid cell equals
    /// exactly 1.0 real physical inch on the target monitor glass, accounting for Windows DPI scaling.
    /// </summary>
    public static double CalculateOneInchZoom(
        double physicalMonitorPpi,
        double monitorEffectiveDpi,
        double mapPixelsPerCell)
    {
        if (mapPixelsPerCell <= 0.001) return 1.0;
        double dpiScale = Math.Max(0.25, monitorEffectiveDpi / 96.0);
        double dipsPerRealInch = physicalMonitorPpi / dpiScale;
        return Math.Clamp(dipsPerRealInch / mapPixelsPerCell, 0.02, 20.0);
    }

    /// <summary>
    /// Computes how many DIPs correspond to 1 real physical inch on a monitor.
    /// </summary>
    public static double CalculateDipsPerInch(double physicalMonitorPpi, double monitorEffectiveDpi)
    {
        double dpiScale = Math.Max(0.25, monitorEffectiveDpi / 96.0);
        return physicalMonitorPpi / dpiScale;
    }

    private static Dictionary<string, string> QueryFriendlyMonitorNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return result;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return result;

            for (int i = 0; i < pathCount; i++)
            {
                var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id
                    }
                };

                var targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref sourceName) == 0 &&
                    DisplayConfigGetDeviceInfo(ref targetName) == 0)
                {
                    string gdiName = sourceName.viewGdiDeviceName?.TrimEnd('\0') ?? "";
                    string friendly = targetName.monitorFriendlyDeviceName?.TrimEnd('\0') ?? "";
                    if (!string.IsNullOrWhiteSpace(gdiName) && !string.IsNullOrWhiteSpace(friendly))
                    {
                        result[gdiName] = friendly;
                    }
                }
            }
        }
        catch
        {
            // Safe fallback if DisplayConfig APIs unavailable
        }
        return result;
    }

    #region Win32 Interop

    private const int MONITORINFOF_PRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
        AngularDpi = 1,
        RawDpi = 2
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    #endregion
}
