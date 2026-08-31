using System.Text.Json;

namespace CodexUsageGuard.Core;

public enum WindowShowState
{
    Normal,
    Minimized,
    Maximized,
    Unknown
}

public enum WindowRestorationStatus
{
    Restored,
    Failed,
    NotRequired
}

public interface ISupervisedWindowController
{
    long GetForegroundWindow();

    bool IsWindow(long windowHandle);

    WindowShowState GetShowState(long windowHandle);

    bool TrySetForeground(long windowHandle, TimeSpan timeout);

    bool TryMinimize(long windowHandle, TimeSpan timeout);

    bool TryRestoreShowState(
        long windowHandle,
        WindowShowState showState,
        TimeSpan timeout);
}

public interface IBoundWindowObservationRunner
{
    UsageObservation Observe(long windowHandle);
}

public sealed record SupervisedWindowStateReport(
    UsageObservation Focused,
    UsageObservation Unfocused,
    UsageObservation Minimized,
    bool DistinctOriginalForegroundAvailable,
    WindowRestorationStatus Restoration)
{
    public string ToSanitizedJson() =>
        JsonSerializer.Serialize(this, UsageObservation.JsonOptions);
}

public sealed class SupervisedWindowStateTestRunner(
    ISupervisedWindowController controller,
    IBoundWindowObservationRunner observer,
    IObservationClock clock,
    TimeSpan? transitionTimeout = null)
{
    private readonly TimeSpan _transitionTimeout =
        transitionTimeout ?? TimeSpan.FromSeconds(2);

    public SupervisedWindowStateReport Run(long codexWindowHandle)
    {
        var originalForeground = controller.GetForegroundWindow();
        var originalShowState = controller.GetShowState(codexWindowHandle);
        var hasDistinctOriginalForeground =
            originalForeground != 0 &&
            originalForeground != codexWindowHandle &&
            SafeOperation(() => controller.IsWindow(originalForeground));

        var notRun = UsageObservation.UnavailableAt(
            clock.UtcNow,
            ObservationError.WindowStateTestNotRun);
        var focused = notRun;
        var unfocused = notRun;
        var minimized = notRun;
        var restoration = WindowRestorationStatus.Failed;

        try
        {
            focused = RunAfterTransition(
                () => controller.TrySetForeground(
                    codexWindowHandle,
                    _transitionTimeout),
                codexWindowHandle);

            unfocused = hasDistinctOriginalForeground
                ? RunAfterTransition(
                    () => controller.TrySetForeground(
                        originalForeground,
                        _transitionTimeout),
                    codexWindowHandle)
                : UsageObservation.UnavailableAt(
                    clock.UtcNow,
                    ObservationError.NoDistinctForegroundWindow);

            minimized = RunAfterTransition(
                () => controller.TryMinimize(
                    codexWindowHandle,
                    _transitionTimeout),
                codexWindowHandle);
        }
        finally
        {
            var showStateRestored = SafeOperation(() =>
                controller.TryRestoreShowState(
                    codexWindowHandle,
                    originalShowState,
                    _transitionTimeout));
            var foregroundRestored = originalForeground == 0 ||
                SafeOperation(() =>
                    controller.IsWindow(originalForeground) &&
                    controller.TrySetForeground(
                        originalForeground,
                        _transitionTimeout));

            restoration = showStateRestored && foregroundRestored
                ? WindowRestorationStatus.Restored
                : WindowRestorationStatus.Failed;
        }

        return new SupervisedWindowStateReport(
            focused,
            unfocused,
            minimized,
            hasDistinctOriginalForeground,
            restoration);
    }

    private UsageObservation RunAfterTransition(
        Func<bool> transition,
        long codexWindowHandle)
    {
        if (!SafeOperation(transition))
        {
            return UsageObservation.UnavailableAt(
                clock.UtcNow,
                ObservationError.WindowStateTransitionFailed);
        }

        try
        {
            return observer.Observe(codexWindowHandle);
        }
        catch
        {
            return UsageObservation.ErrorAt(
                clock.UtcNow,
                ObservationError.TestChildFailed);
        }
    }

    private static bool SafeOperation(Func<bool> operation)
    {
        try
        {
            return operation();
        }
        catch
        {
            return false;
        }
    }
}
