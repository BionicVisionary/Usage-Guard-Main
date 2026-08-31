Set-StrictMode -Version Latest

if (-not ('UsageGuard.SandboxQa.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UsageGuard.SandboxQa
{
    public sealed class DisplayRecord
    {
        public string DeviceName { get; set; }
        public string StableDeviceId { get; set; }
        public bool Connected { get; set; }
        public bool Primary { get; set; }
        public int WorkingLeft { get; set; }
        public int WorkingTop { get; set; }
        public int WorkingWidth { get; set; }
        public int WorkingHeight { get; set; }
    }

    public sealed class WindowRecord
    {
        public long Hwnd { get; set; }
        public int ProcessId { get; set; }
        public bool Visible { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    public static class NativeMethods
    {
        private const int MonitorInfoPrimary = 1;
        private const int DisplayDeviceActive = 1;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const int SwShowMinNoActivate = 7;
        private const int SwShowNoActivate = 4;
        private const int DwmwaExtendedFrameBounds = 9;

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
        private delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE display, uint flags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        public static DisplayRecord[] EnumerateDisplays()
        {
            var results = new List<DisplayRecord>();
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, hdc, rect, data) =>
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfo(monitor, ref info)) return true;
                var display = new DISPLAY_DEVICE();
                display.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                var hasDevice = EnumDisplayDevices(info.szDevice, 0, ref display, 0);
                results.Add(new DisplayRecord
                {
                    DeviceName = info.szDevice ?? "",
                    StableDeviceId = hasDevice ? (display.DeviceID ?? "") : "",
                    Connected = hasDevice && (display.StateFlags & DisplayDeviceActive) != 0,
                    Primary = (info.dwFlags & MonitorInfoPrimary) != 0,
                    WorkingLeft = info.rcWork.Left,
                    WorkingTop = info.rcWork.Top,
                    WorkingWidth = info.rcWork.Right - info.rcWork.Left,
                    WorkingHeight = info.rcWork.Bottom - info.rcWork.Top
                });
                return true;
            }, IntPtr.Zero)) throw new InvalidOperationException("Display enumeration failed.");
            return results.ToArray();
        }

        public static WindowRecord[] EnumerateWindows()
        {
            var results = new List<WindowRecord>();
            if (!EnumWindows((hwnd, data) =>
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                results.Add(new WindowRecord
                {
                    Hwnd = hwnd.ToInt64(),
                    ProcessId = checked((int)pid),
                    Visible = IsWindowVisible(hwnd)
                });
                return true;
            }, IntPtr.Zero)) throw new InvalidOperationException("Window enumeration failed.");
            return results.ToArray();
        }

        public static int[] GetExtendedFrame(long hwnd)
        {
            RECT rect;
            var result = DwmGetWindowAttribute(new IntPtr(hwnd), DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf(typeof(RECT)));
            if (result != 0) throw new InvalidOperationException("DWM frame query failed: " + result);
            return new[] { rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top };
        }

        public static void Place(long hwnd, int x, int y, int width, int height)
        {
            if (!SetWindowPos(new IntPtr(hwnd), IntPtr.Zero, x, y, width, height,
                SwpNoZOrder | SwpNoActivate | SwpShowWindow))
                throw new InvalidOperationException("Sandbox placement failed.");
        }

        public static void Minimize(long hwnd)
        {
            if (!ShowWindow(new IntPtr(hwnd), SwShowMinNoActivate) && !IsIconic(new IntPtr(hwnd)))
                throw new InvalidOperationException("Sandbox minimize failed.");
        }

        public static void RestoreNoActivate(long hwnd)
        {
            ShowWindow(new IntPtr(hwnd), SwShowNoActivate);
            if (IsIconic(new IntPtr(hwnd)))
                throw new InvalidOperationException("Sandbox restore failed.");
        }

        public static bool IsMinimized(long hwnd)
        {
            return IsIconic(new IntPtr(hwnd));
        }
    }
}
'@
}

function Get-UsageGuardDisplayInventory {
    @([UsageGuard.SandboxQa.NativeMethods]::EnumerateDisplays())
}

function Get-UsageGuardTopLevelWindows {
    @([UsageGuard.SandboxQa.NativeMethods]::EnumerateWindows())
}

function Get-UsageGuardSandboxClientSnapshot {
    $All = @(Get-CimInstance Win32_Process)
    $ByPid = @{}
    foreach ($Process in $All) { $ByPid[[int]$Process.ProcessId] = $Process }
    @($All | Where-Object { $_.Name -ceq 'WindowsSandboxClient.exe' } | ForEach-Object {
        $Ancestors = New-Object System.Collections.Generic.List[int]
        $Parent = [int]$_.ParentProcessId
        for ($Depth = 0; $Depth -lt 16 -and $Parent -ne 0; $Depth++) {
            $Ancestors.Add($Parent)
            if (-not $ByPid.ContainsKey($Parent)) { break }
            $Parent = [int]$ByPid[$Parent].ParentProcessId
        }
        [pscustomobject]@{
            ProcessId = [int]$_.ProcessId
            ExecutablePath = [string]$_.ExecutablePath
            CreatedAtUtc = ([DateTimeOffset]$_.CreationDate).ToUniversalTime()
            AncestorProcessIds = $Ancestors.ToArray()
        }
    })
}

function Assert-UsageGuardFrameContained {
    param([long]$Hwnd, $Display)
    $Frame = [UsageGuard.SandboxQa.NativeMethods]::GetExtendedFrame($Hwnd)
    if ($Frame[0] -lt [int]$Display.WorkingLeft -or
        $Frame[1] -lt [int]$Display.WorkingTop -or
        $Frame[0] + $Frame[2] -gt [int]$Display.WorkingLeft + [int]$Display.WorkingWidth -or
        $Frame[1] + $Frame[3] -gt [int]$Display.WorkingTop + [int]$Display.WorkingHeight) {
        throw 'The Sandbox client is not fully contained on the approved display.'
    }
    $Frame
}

function Save-UsageGuardSandboxClientCapture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][long]$Hwnd,
        [Parameter(Mandatory = $true)][string]$ExpectedClientPath,
        [Parameter(Mandatory = $true)]$ApprovedDisplay,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )
    $Process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"
    if ($null -eq $Process -or
        -not ([IO.Path]::GetFullPath([string]$Process.ExecutablePath)).Equals(
            [IO.Path]::GetFullPath($ExpectedClientPath),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The exact Sandbox client process is no longer owned.'
    }
    $Window = @(Get-UsageGuardTopLevelWindows | Where-Object {
        [int]$_.ProcessId -eq $ProcessId -and [int64]$_.Hwnd -eq $Hwnd -and $_.Visible
    })
    if ($Window.Count -ne 1 -or
        [UsageGuard.SandboxQa.NativeMethods]::IsMinimized($Hwnd)) {
        throw 'The exact Sandbox client window is unavailable or minimized.'
    }
    $Frame = Assert-UsageGuardFrameContained -Hwnd $Hwnd -Display $ApprovedDisplay
    Add-Type -AssemblyName System.Drawing
    $Bitmap = [Drawing.Bitmap]::new($Frame[2], $Frame[3])
    $Graphics = [Drawing.Graphics]::FromImage($Bitmap)
    $Hdc = $Graphics.GetHdc()
    try {
        if (-not [UsageGuard.SandboxQa.NativeMethods]::PrintWindow(
                [IntPtr]$Hwnd,
                $Hdc,
                2)) {
            throw 'Exact Sandbox client capture failed.'
        }
    }
    finally {
        $Graphics.ReleaseHdc($Hdc)
        $Graphics.Dispose()
    }
    try {
        $Bitmap.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $Bitmap.Dispose()
    }
}

Export-ModuleMember -Function @(
    'Get-UsageGuardDisplayInventory',
    'Get-UsageGuardTopLevelWindows',
    'Get-UsageGuardSandboxClientSnapshot',
    'Assert-UsageGuardFrameContained',
    'Save-UsageGuardSandboxClientCapture'
)
