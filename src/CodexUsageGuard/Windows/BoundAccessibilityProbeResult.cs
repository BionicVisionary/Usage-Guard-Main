using CodexUsageGuard.Core;

namespace CodexUsageGuard.Windows;

public sealed record BoundAccessibilityProbeResult(
    AccessibilityProbeResult ProbeResult,
    long? WindowHandle);
