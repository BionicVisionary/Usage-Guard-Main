using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.Windows;

public sealed class WindowsSupervisedWindowController : ISupervisedWindowController
{
    private const int ShowMaximized = 3;
    private const int Minimize = 6;
    private const int Restore = 9;

    public long GetForegroundWindow() => NativeGetForegroundWindow().ToInt64();

    public bool IsWindow(long windowHandle) =>
        NativeIsWindow(new nint(windowHandle));

    public WindowShowState GetShowState(long windowHandle)
    {
        var nativeHandle = new nint(windowHandle);
        if (!NativeIsWindow(nativeHandle))
        {
            return WindowShowState.Unknown;
        }

        if (IsIconic(nativeHandle))
        {
            return WindowShowState.Minimized;
        }

        return IsZoomed(nativeHandle)
            ? WindowShowState.Maximized
            : WindowShowState.Normal;
    }

    public bool TrySetForeground(long windowHandle, TimeSpan timeout)
    {
        var nativeHandle = new nint(windowHandle);
        if (!NativeIsWindow(nativeHandle))
        {
            return false;
        }

        if (NativeGetForegroundWindow() == nativeHandle)
        {
            return true;
        }

        _ = SetForegroundWindow(nativeHandle);
        return WaitUntil(
            () => NativeGetForegroundWindow() == nativeHandle,
            timeout);
    }

    public bool TryMinimize(long windowHandle, TimeSpan timeout)
    {
        var nativeHandle = new nint(windowHandle);
        if (!NativeIsWindow(nativeHandle))
        {
            return false;
        }

        _ = ShowWindowAsync(nativeHandle, Minimize);
        return WaitUntil(() => IsIconic(nativeHandle), timeout);
    }

    public bool TryRestoreShowState(
        long windowHandle,
        WindowShowState showState,
        TimeSpan timeout)
    {
        var nativeHandle = new nint(windowHandle);
        if (!NativeIsWindow(nativeHandle))
        {
            return false;
        }

        var showCommand = showState switch
        {
            WindowShowState.Normal => Restore,
            WindowShowState.Minimized => Minimize,
            WindowShowState.Maximized => ShowMaximized,
            _ => 0
        };
        if (showCommand == 0)
        {
            return false;
        }

        _ = ShowWindowAsync(nativeHandle, showCommand);
        return WaitUntil(() => GetShowState(windowHandle) == showState, timeout);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed <= timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint windowHandle);
}
