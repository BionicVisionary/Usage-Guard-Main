[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$TargetScreenDeviceName = '\\.\DISPLAY1',
    [switch]$AllowSinglePrimaryDisplay,
    [switch]$LeaveOpen
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf) -or
    [IO.Path]::GetFileName($ExecutablePath) -cne 'CodexUsageGuard.exe') {
    throw 'ExecutablePath must identify an existing CodexUsageGuard.exe.'
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'OutputDirectory must be a new directory.'
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class UsageGuardQaNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint Type; public INPUTUNION Union; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow, ParameterHigh;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT rect, int size);
    public static bool GetVisibleRect(IntPtr window, out RECT rect)
    {
        return DwmGetWindowAttribute(
            window, 9, out rect, Marshal.SizeOf(typeof(RECT))) == 0;
    }
    public static string Title(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
    public static bool ActivateExact(IntPtr window)
    {
        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
            return true;
        uint owner;
        uint targetThread = GetWindowThreadProcessId(window, out owner);
        uint currentThread = GetCurrentThreadId();
        if (targetThread == 0 || currentThread == 0)
            return false;
        bool attached = false;
        try
        {
            if (targetThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, targetThread, true);
                if (!attached)
                    return false;
            }
            BringWindowToTop(window);
            return SetForegroundWindow(window) && GetForegroundWindow() == window;
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, targetThread, false);
        }
    }
    public static bool SendChord(params ushort[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
            inputs[i] = Key(keys[i], false);
        for (int i = 0; i < keys.Length; i++)
            inputs[keys.Length + i] = Key(keys[keys.Length - 1 - i], true);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
    }
    private static INPUT Key(ushort key, bool up)
    {
        return new INPUT
        {
            Type = 1,
            Union = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = up ? 2U : 0U }
            }
        };
    }
}
'@

$screens = @([Windows.Forms.Screen]::AllScreens)
$target = $screens | Where-Object {
    $_.DeviceName -ceq $TargetScreenDeviceName
} | Select-Object -First 1
if ($null -eq $target -or ($target.Primary -and
        (-not $AllowSinglePrimaryDisplay -or $screens.Count -ne 1))) {
    throw 'The approved target must be non-primary, unless the user explicitly allowed the only connected primary display.'
}
$qaArgument = if ($target.Primary) {
    '--layout-qa-single-display'
} else {
    '--layout-qa-display'
}
$approvedScreenCount = $screens.Count
$approvedPrimary = $target.Primary
$approvedBounds = $target.Bounds.ToString()
$approvedWorkingArea = $target.WorkingArea.ToString()

function Get-CurrentApprovedScreen {
    $currentScreens = @([Windows.Forms.Screen]::AllScreens)
    $matches = @($currentScreens | Where-Object {
        $_.DeviceName -ceq $TargetScreenDeviceName
    })
    if ($matches.Count -ne 1) {
        throw 'Safety stop: the approved display is missing or ambiguous.'
    }
    $current = $matches[0]
    if ($currentScreens.Count -ne $approvedScreenCount -or
        $current.Primary -ne $approvedPrimary -or
        $current.Bounds.ToString() -cne $approvedBounds -or
        $current.WorkingArea.ToString() -cne $approvedWorkingArea -or
        ($current.Primary -and
            (-not $AllowSinglePrimaryDisplay -or $currentScreens.Count -ne 1))) {
        throw 'Safety stop: the approved display state changed.'
    }
    $current
}

function Get-ExactProcessRecord {
    param([int]$ProcessId)
    Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" | Where-Object {
        $_.ExecutablePath -eq $ExecutablePath -and
        $_.CommandLine -like "*$qaArgument*" -and
        $_.CommandLine -like "*$TargetScreenDeviceName*"
    } | Select-Object -First 1
}

function Get-VerifiedContext {
    param([Diagnostics.Process]$Process, [string]$ExpectedTitle)
    $currentTarget = Get-CurrentApprovedScreen
    $Process.Refresh()
    if ($Process.HasExited -or
        [IO.Path]::GetFullPath($Process.MainModule.FileName) -ne $ExecutablePath -or
        $null -eq (Get-ExactProcessRecord -ProcessId $Process.Id)) {
        throw 'Safety stop: the exact opt-in QA process identity changed.'
    }
    $window = $Process.MainWindowHandle
    $owner = [uint32]0
    [UsageGuardQaNative]::GetWindowThreadProcessId($window, [ref]$owner) | Out-Null
    $rect = [UsageGuardQaNative+RECT]::new()
    if ($window -eq [IntPtr]::Zero -or $owner -ne $Process.Id -or
        -not [UsageGuardQaNative]::IsWindowVisible($window) -or
        -not [UsageGuardQaNative]::GetVisibleRect($window, [ref]$rect)) {
        throw 'Safety stop: the visible HWND is not owned by the exact QA PID.'
    }
    $title = [UsageGuardQaNative]::Title($window)
    if (-not $title.StartsWith($ExpectedTitle, [StringComparison]::Ordinal)) {
        throw "Safety stop: unexpected QA title '$title'."
    }
    $area = $currentTarget.WorkingArea
    if ($rect.Left -lt $area.Left -or $rect.Top -lt $area.Top -or
        $rect.Right -gt $area.Right -or $rect.Bottom -gt $area.Bottom) {
        throw 'Safety stop: DWM visible frame is not wholly contained on the approved display.'
    }
    [pscustomobject]@{ Window = $window; Rect = $rect; Title = $title }
}

function Send-VerifiedChord {
    param([Diagnostics.Process]$Process, [string]$ExpectedTitle, [uint16[]]$Keys)
    $context = Get-VerifiedContext -Process $Process -ExpectedTitle $ExpectedTitle
    if (-not [UsageGuardQaNative]::ActivateExact($context.Window)) {
        throw 'Safety stop: Windows refused foreground ownership for the verified QA window.'
    }
    Start-Sleep -Milliseconds 100
    $context = Get-VerifiedContext -Process $Process -ExpectedTitle $ExpectedTitle
    if ([UsageGuardQaNative]::GetForegroundWindow() -ne $context.Window) {
        throw 'Safety stop: the verified QA window is not foreground immediately before input.'
    }
    if (-not [UsageGuardQaNative]::SendChord($Keys)) {
        throw 'Safety stop: the dedicated keyboard chord was not delivered completely.'
    }
}

function Wait-Title {
    param([Diagnostics.Process]$Process, [string]$ExpectedTitle)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 50
        try { return Get-VerifiedContext -Process $Process -ExpectedTitle $ExpectedTitle } catch { }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for '$ExpectedTitle'."
}

function Save-VerifiedWindow {
    param([pscustomobject]$Context, [string]$Path)
    $width = $Context.Rect.Right - $Context.Rect.Left
    $height = $Context.Rect.Bottom - $Context.Rect.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(
                $Context.Rect.Left, $Context.Rect.Top, 0, 0,
                [Drawing.Size]::new($width, $height))
        } finally { $graphics.Dispose() }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

$originalForeground = [UsageGuardQaNative]::GetForegroundWindow()
$process = $null
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
try {
    $process = Start-Process -FilePath $ExecutablePath -ArgumentList @(
        $qaArgument, $TargetScreenDeviceName) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 50
        $process.Refresh()
    } while (($process.MainWindowHandle -eq [IntPtr]::Zero -or -not $process.Responding) -and
        [DateTime]::UtcNow -lt $deadline)

    $codexTitle = 'Usage Guard QA - Codex'
    $claudeTitle = 'Usage Guard QA - Claude'
    $codex = Wait-Title -Process $process -ExpectedTitle $codexTitle
    Send-VerifiedChord -Process $process -ExpectedTitle $codexTitle -Keys @([uint16]0x11, [uint16]0x31)
    $codex = Wait-Title -Process $process -ExpectedTitle $codexTitle
    Start-Sleep -Milliseconds 750
    $codex = Get-VerifiedContext -Process $process -ExpectedTitle $codexTitle
    Save-VerifiedWindow -Context $codex -Path (Join-Path $OutputDirectory 'installed-codex.png')

    Send-VerifiedChord -Process $process -ExpectedTitle $codexTitle -Keys @([uint16]0x11, [uint16]0x32)
    $claude = Wait-Title -Process $process -ExpectedTitle $claudeTitle
    Start-Sleep -Milliseconds 250
    $claude = Get-VerifiedContext -Process $process -ExpectedTitle $claudeTitle
    Save-VerifiedWindow -Context $claude -Path (Join-Path $OutputDirectory 'installed-claude.png')

    $inspection = [pscustomobject]@{
        ProcessId = $process.Id
        ExecutablePath = $ExecutablePath
        Display = $TargetScreenDeviceName
        CodexTitle = $codex.Title
        ClaudeTitle = $claude.Title
        DwmContained = $true
        MouseInputUsed = $false
        AltTabUsed = $false
    } | ConvertTo-Json
    [IO.File]::WriteAllText(
        (Join-Path $OutputDirectory 'inspection.json'),
        $inspection,
        [Text.UTF8Encoding]::new($false))

    if (-not $LeaveOpen) {
        Send-VerifiedChord -Process $process -ExpectedTitle $claudeTitle -Keys @(
            [uint16]0x11, [uint16]0x10, [uint16]0x7B)
        if (-not $process.WaitForExit(15000)) {
            throw 'The exact QA process did not exit after its dedicated shutdown chord.'
        }
    }
}
finally {
    if (-not $LeaveOpen -and $null -ne $process -and -not $process.HasExited) {
        $owned = Get-ExactProcessRecord -ProcessId $process.Id
        if ($null -ne $owned) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
    }
    if ($originalForeground -ne [IntPtr]::Zero -and
        [UsageGuardQaNative]::IsWindow($originalForeground)) {
        [UsageGuardQaNative]::SetForegroundWindow($originalForeground) | Out-Null
    }
}
