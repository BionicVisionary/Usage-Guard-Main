using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.Windows;

public sealed record BoundTargetResponse(
    UsageObservation Observation,
    long? WindowHandle)
{
    public string ToSanitizedJson() =>
        JsonSerializer.Serialize(this, UsageObservation.JsonOptions);

    public static BoundTargetResponse? FromSanitizedJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BoundTargetResponse>(
                json,
                UsageObservation.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class TimedChildProcessObservationRunner(
    IObservationClock clock,
    TimeSpan? childTimeout = null) : IBoundWindowObservationRunner
{
    private readonly TimeSpan _childTimeout =
        childTimeout ?? TimeSpan.FromSeconds(7);

    public UsageObservation Observe(long windowHandle)
    {
        var childResult = RunChild(
            "--probe-bound-window",
            windowHandle.ToString(CultureInfo.InvariantCulture));
        if (childResult.TimedOut)
        {
            return UsageObservation.UnavailableAt(
                clock.UtcNow,
                ObservationError.TestChildTimedOut);
        }

        return childResult.Output is null
            ? UsageObservation.ErrorAt(
                clock.UtcNow,
                ObservationError.TestChildFailed)
            : UsageObservation.FromSanitizedJson(childResult.Output) ??
              UsageObservation.ErrorAt(
                  clock.UtcNow,
                  ObservationError.TestChildFailed);
    }

    public BoundTargetResponse Locate()
    {
        var childResult = RunChild("--locate-bound-window");
        if (childResult.TimedOut)
        {
            return new BoundTargetResponse(
                UsageObservation.UnavailableAt(
                    clock.UtcNow,
                    ObservationError.TestChildTimedOut),
                null);
        }

        return childResult.Output is null
            ? new BoundTargetResponse(
                UsageObservation.ErrorAt(
                    clock.UtcNow,
                    ObservationError.TestChildFailed),
                null)
            : BoundTargetResponse.FromSanitizedJson(childResult.Output) ??
              new BoundTargetResponse(
                  UsageObservation.ErrorAt(
                      clock.UtcNow,
                      ObservationError.TestChildFailed),
                  null);
    }

    private ChildProcessResult RunChild(params string[] arguments)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ChildProcessResult(null, false);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var child = new Process { StartInfo = startInfo };
        try
        {
            if (!child.Start())
            {
                return new ChildProcessResult(null, false);
            }

            var standardOutput = child.StandardOutput.ReadToEndAsync();
            var standardError = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(checked((int)_childTimeout.TotalMilliseconds)))
            {
                StopOwnTimedOutChild(child);
                return new ChildProcessResult(null, true);
            }

            _ = standardError.GetAwaiter().GetResult();
            return new ChildProcessResult(
                standardOutput.GetAwaiter().GetResult().Trim(),
                false);
        }
        catch
        {
            StopOwnTimedOutChild(child);
            return new ChildProcessResult(null, false);
        }
    }

    private static void StopOwnTimedOutChild(Process child)
    {
        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                _ = child.WaitForExit(2_000);
            }
        }
        catch
        {
            // The parent still fails closed; this never targets a Codex process.
        }
    }

    private readonly record struct ChildProcessResult(
        string? Output,
        bool TimedOut);
}

public static class SupervisedWindowStateTestMode
{
    public static SupervisedWindowStateReport Run()
    {
        var clock = new SystemObservationClock();
        var childRunner = new TimedChildProcessObservationRunner(clock);
        var located = childRunner.Locate();
        if (located.Observation.Status != ObservationStatus.Available ||
            located.WindowHandle is null)
        {
            var notRun = UsageObservation.UnavailableAt(
                clock.UtcNow,
                ObservationError.WindowStateTestNotRun);
            return new SupervisedWindowStateReport(
                located.Observation,
                notRun,
                notRun,
                false,
                WindowRestorationStatus.NotRequired);
        }

        var runner = new SupervisedWindowStateTestRunner(
            new WindowsSupervisedWindowController(),
            childRunner,
            clock);
        return runner.Run(located.WindowHandle.Value);
    }
}
