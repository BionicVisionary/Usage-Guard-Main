using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using CodexUsageGuard.Core;
using Microsoft.Win32.SafeHandles;

namespace CodexUsageGuard.Windows;

public sealed class WindowsCodexAccessibilityProbe : IAccessibilityProbe
{
    private const int MaximumScopeElements = 512;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Condition WeeklyUsageLabelCondition =
        new PropertyCondition(
            AutomationElement.NameProperty,
            RemainingUsageParser.WeeklyUsageLimitLabel,
            PropertyConditionFlags.IgnoreCase);

    public AccessibilityProbeResult Capture() => CaptureBound().ProbeResult;

    public BoundAccessibilityProbeResult CaptureBound()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WithoutBoundWindow(AccessibilityProbeState.AccessibilityUnavailable);
        }

        var codexProcessIds = GetCodexDesktopProcessIds();
        if (codexProcessIds.Count == 0)
        {
            return WithoutBoundWindow(AccessibilityProbeState.CodexNotRunning);
        }

        var visibleWindows = GetVisibleTopLevelWindows(codexProcessIds);
        if (visibleWindows.Count == 0)
        {
            return WithoutBoundWindow(AccessibilityProbeState.CodexWindowNotVisible);
        }

        return CaptureFromWindows(visibleWindows);
    }

    public AccessibilityProbeResult CaptureBoundWindow(long windowHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AccessibilityProbeResult.WithoutViews(
                AccessibilityProbeState.AccessibilityUnavailable);
        }

        var nativeWindowHandle = new nint(windowHandle);
        var codexProcessIds = GetCodexDesktopProcessIds();
        if (!IsWindow(nativeWindowHandle) ||
            !IsWindowVisible(nativeWindowHandle) ||
            !IsOwnedByCodex(nativeWindowHandle, codexProcessIds))
        {
            return AccessibilityProbeResult.WithoutViews(
                AccessibilityProbeState.CodexWindowNotVisible);
        }

        return CaptureFromWindows(new[] { nativeWindowHandle }).ProbeResult;
    }

    private static BoundAccessibilityProbeResult CaptureFromWindows(
        IReadOnlyList<nint> windowHandles)
    {

        var candidates = new List<UsageViewCandidate>();
        var accessibilityFailed = false;

        foreach (var windowHandle in windowHandles)
        {
            try
            {
                var window = AutomationElement.FromHandle(windowHandle);
                var markers = window.FindAll(
                    TreeScope.Subtree,
                    WeeklyUsageLabelCondition);

                foreach (AutomationElement marker in markers)
                {
                    if (marker.Current.IsOffscreen)
                    {
                        continue;
                    }

                    candidates.Add(CaptureWeeklyUsageCandidate(
                        marker,
                        window,
                        windowHandle));
                }
            }
            catch (ScopeLimitExceededException)
            {
                return WithoutBoundWindow(AccessibilityProbeState.ScopeTooLarge);
            }
            catch (Exception ex) when (IsAccessibilityFailure(ex))
            {
                accessibilityFailed = true;
            }
        }

        if (accessibilityFailed)
        {
            return WithoutBoundWindow(
                AccessibilityProbeState.AccessibilityUnavailable);
        }

        if (candidates.Count > 0)
        {
            var selected = UsageViewCandidateSelector.SelectMostSpecific(candidates);
            var result = AccessibilityProbeResult.WithViews(
                selected.Select(candidate => candidate.Snapshot).ToArray());
            long? boundWindowHandle = selected.Count == 1
                ? selected[0].SourceWindowHandle
                : null;
            return new BoundAccessibilityProbeResult(result, boundWindowHandle);
        }

        return WithoutBoundWindow(
            AccessibilityProbeState.WeeklyUsageLabelNotVisible);
    }

    private static BoundAccessibilityProbeResult WithoutBoundWindow(
        AccessibilityProbeState state) => new(
            AccessibilityProbeResult.WithoutViews(state),
            null);

    private static HashSet<int> GetCodexDesktopProcessIds()
    {
        var processIds = new HashSet<int>();
        foreach (var processName in new[] { "Codex", "ChatGPT" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var packageFamilyName = GetPackageFamilyName(process.Id);
                        if (packageFamilyName is not null &&
                            CodexDesktopIdentity.IsExpected(
                                process.ProcessName,
                                packageFamilyName))
                        {
                            processIds.Add(process.Id);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited during passive enumeration.
                    }
                }
            }
        }

        return processIds;
    }

    private static string? GetPackageFamilyName(int processId)
    {
        using var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            false,
            processId);
        if (processHandle.IsInvalid)
        {
            return null;
        }

        uint requiredLength = 0;
        var result = GetPackageFamilyName(processHandle, ref requiredLength, null);
        if (result != ErrorInsufficientBuffer ||
            requiredLength == 0 ||
            requiredLength > 256)
        {
            return null;
        }

        var packageFamilyName = new StringBuilder(checked((int)requiredLength));
        result = GetPackageFamilyName(
            processHandle,
            ref requiredLength,
            packageFamilyName);

        return result == 0 ? packageFamilyName.ToString() : null;
    }

    private static IReadOnlyList<nint> GetVisibleTopLevelWindows(
        IReadOnlySet<int> codexProcessIds)
    {
        var handles = new List<nint>();

        EnumWindows(
            (windowHandle, parameter) =>
            {
                _ = parameter;
                if (!IsWindowVisible(windowHandle))
                {
                    return true;
                }

                GetWindowThreadProcessId(windowHandle, out var processId);
                if (codexProcessIds.Contains(unchecked((int)processId)))
                {
                    handles.Add(windowHandle);
                }

                return true;
            },
            0);

        return handles;
    }

    private static bool IsOwnedByCodex(
        nint windowHandle,
        IReadOnlySet<int> codexProcessIds)
    {
        GetWindowThreadProcessId(windowHandle, out var processId);
        return codexProcessIds.Contains(unchecked((int)processId));
    }

    private static UsageViewCandidate CaptureWeeklyUsageCandidate(
        AutomationElement marker,
        AutomationElement window,
        nint sourceWindowHandle)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = marker;
        var visitedScopes = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var scopeIdentity = GetRuntimeIdentity(current);
            if (!visitedScopes.Add(scopeIdentity))
            {
                throw new InvalidOperationException(
                    "UI Automation returned a cyclic ancestor structure.");
            }

            var names = ReadVisibleNames(current, walker);
            var hasOtherUsageLimitLabel = names.Any(name =>
                RemainingUsageParser.IsUsageLimitLabel(name) &&
                !RemainingUsageParser.IsWeeklyUsageLimitLabel(name));
            if (!hasOtherUsageLimitLabel &&
                RemainingUsageParser.ExtractDistinctPercentages(names).Count > 0)
            {
                return CreateCandidate(
                    marker,
                    current,
                    window,
                    sourceWindowHandle,
                    names,
                    walker);
            }

            if (Automation.Compare(current, window))
            {
                break;
            }

            var parent = walker.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return CreateCandidate(
            marker,
            marker,
            window,
            sourceWindowHandle,
            Array.Empty<string>(),
            walker);
    }

    private static UsageViewCandidate CreateCandidate(
        AutomationElement marker,
        AutomationElement scope,
        AutomationElement window,
        nint sourceWindowHandle,
        IReadOnlyList<string> names,
        TreeWalker walker)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var current = walker.GetParent(scope);
        var visited = 0;

        while (current is not null)
        {
            visited++;
            if (visited > MaximumScopeElements)
            {
                throw new ScopeLimitExceededException();
            }

            ancestors.Add(GetRuntimeIdentity(current));
            if (Automation.Compare(current, window))
            {
                break;
            }

            current = walker.GetParent(current);
        }

        return new UsageViewCandidate(
            sourceWindowHandle.ToInt64(),
            GetRuntimeIdentity(marker),
            GetRuntimeIdentity(scope),
            ancestors,
            new UsageViewSnapshot(names));
    }

    private static string GetRuntimeIdentity(AutomationElement element)
    {
        var runtimeId = element.GetRuntimeId();
        if (runtimeId is null || runtimeId.Length == 0)
        {
            throw new InvalidOperationException(
                "UI Automation element has no runtime identity.");
        }

        return string.Join(
            '.',
            runtimeId.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static IReadOnlyList<string> ReadVisibleNames(
        AutomationElement root,
        TreeWalker walker)
    {
        var names = new List<string>();
        var pending = new Stack<AutomationElement>();
        pending.Push(root);
        var visited = 0;

        while (pending.Count > 0)
        {
            var element = pending.Pop();
            visited++;
            if (visited > MaximumScopeElements)
            {
                throw new ScopeLimitExceededException();
            }

            if (!element.Current.IsOffscreen)
            {
                var name = element.Current.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            var children = new List<AutomationElement>();
            for (var child = walker.GetFirstChild(element);
                 child is not null;
                 child = walker.GetNextSibling(child))
            {
                children.Add(child);
            }

            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }

        return names;
    }

    private static bool IsAccessibilityFailure(Exception exception) =>
        exception is ElementNotAvailableException or
            InvalidOperationException or
            COMException or
            UnauthorizedAccessException;

    private sealed class ScopeLimitExceededException : Exception;

    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(
        SafeProcessHandle processHandle,
        ref uint packageFamilyNameLength,
        StringBuilder? packageFamilyName);
}
