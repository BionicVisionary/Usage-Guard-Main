using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text.Json;
using CodexUsageGuard.AppServer;
using CodexUsageGuard.Core;
using CodexUsageGuard.Monitoring;
using CodexUsageGuard.Providers;
using CodexUsageGuard.Windows;

namespace CodexUsageGuard.Tests;

internal static class UiEvidenceRenderer
{
    private static readonly DateTimeOffset EvidenceTime =
        new(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);

    public static int Render(string outputDirectory)
    {
        try
        {
            var root = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(root);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var results = new List<UiRenderResult>();
            foreach (var scenario in new[]
            {
                new UiScenario("codex-normal-dark", new Size(660, 780), WindowsTheme.Dark, 0),
                new UiScenario("codex-minimum-dark", new Size(560, 620), WindowsTheme.Dark, 0),
                new UiScenario("codex-normal-light", new Size(660, 780), WindowsTheme.Light, 0),
                new UiScenario("codex-minimum-light", new Size(560, 620), WindowsTheme.Light, 0),
                new UiScenario("claude-normal-dark", new Size(660, 780), WindowsTheme.Dark, 1),
                new UiScenario("claude-minimum-light", new Size(560, 620), WindowsTheme.Light, 1)
            })
            {
                results.Add(RenderScenario(root, scenario));
            }
            results.Add(RenderInstructionsScenario(
                root,
                "instructions-codex-dark",
                new Size(760, 680),
                WindowsTheme.Dark,
                1));
            results.Add(RenderInstructionsScenario(
                root,
                "instructions-claude-minimum-light",
                new Size(620, 520),
                WindowsTheme.Light,
                2));

            var report = new UiEvidenceReport(
                SchemaVersion: 1,
                RenderedAtUtc: DateTimeOffset.UtcNow,
                Dpi: 96,
                ProductionSimulationUsed: false,
                AppServerInvoked: false,
                Scenarios: results);
            File.WriteAllText(
                Path.Combine(root, "ui-evidence.json"),
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(
                $"PASS rendered {results.Count} actual-form UI evidence scenarios");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"UI evidence render failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static UiRenderResult RenderInstructionsScenario(
        string root,
        string name,
        Size size,
        ThemePalette palette,
        int tabIndex)
    {
        using var form = new InstructionsForm(palette)
        {
            Size = size,
            MinimumSize = size,
            MaximumSize = size,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30_000, -30_000),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.SelectedIndex = tabIndex;
        form.PerformLayout();
        foreach (var control in Descendants(form))
        {
            control.PerformLayout();
        }
        var selected = tabs.SelectedTab ??
            throw new InvalidOperationException("Instruction tab is unavailable.");
        var primaryAction = Descendants(selected)
            .OfType<Button>()
            .First(control => control.Text.StartsWith("Copy ", StringComparison.Ordinal));
        primaryAction.Focus();
        Application.DoEvents();
        var tabStops = Descendants(selected)
            .Where(control => control.TabStop && control is Button or TextBox)
            .Select(control => new UiTabStop(
                control.GetType().Name,
                control.AccessibleName ?? string.Empty,
                control.TabIndex,
                control.Enabled,
                control.Visible))
            .ToArray();
        if (tabStops.Any(item => string.IsNullOrWhiteSpace(item.AccessibleName)))
        {
            throw new InvalidOperationException(
                "An instruction keyboard target has no accessible name.");
        }

        var path = Path.Combine(root, name + ".png");
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            bitmap.SetResolution(96, 96);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(path, ImageFormat.Png);
        }

        return new UiRenderResult(
            name,
            form.Width,
            form.Height,
            HorizontalScrollVisible: false,
            VerticalScrollVisible: true,
            ScrollExerciseMilliseconds: 0,
            primaryAction.Focused,
            tabStops);
    }

    private static UiRenderResult RenderScenario(
        string root,
        UiScenario scenario)
    {
        var storage = new EvidenceStorage();
        var monitor = new UsageMonitor(
            new NeverInvokedSource(),
            storage,
            new EvidenceClock());
        try
        {
            var startup = new StartupRegistration(
                new EvidenceStartupStore(),
                @"C:\Evidence\CodexUsageGuard.exe");
            var providerStorage = new ProviderCatalogStorage(
                Path.Combine(root, "catalog", scenario.Name));
            providerStorage.Save(new ProviderCatalogSettings(
                ProviderCatalogSettings.CurrentSchemaVersion,
                [
                    ProviderCatalogSettings.DefaultCodex,
                    ProviderCatalogSettings.DefaultClaudeCode
                ]));
            using var form = new MainForm(
                monitor,
                startup,
                startHidden: false,
                scenario.Palette,
                showTrayIcon: false,
                providerStorage: providerStorage,
                providerDiscovery: new EvidenceProviderDiscovery())
            {
                Size = scenario.Size,
                MinimumSize = scenario.Size,
                MaximumSize = scenario.Size,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-30_000, -30_000),
                ShowInTaskbar = false
            };

            form.Show();
            Application.DoEvents();
            var providerTabs = Descendants(form).OfType<TabControl>().Single();
            providerTabs.SelectedIndex = scenario.ProviderTabIndex;
            form.PerformLayout();
            foreach (var control in Descendants(form))
            {
                control.PerformLayout();
            }

            var selectedPage = providerTabs.SelectedTab ??
                throw new InvalidOperationException("Provider tab is unavailable.");
            Control primaryAction = Descendants(selectedPage)
                .OfType<Button>()
                .Single(control => control.Text == "Check now");
            primaryAction.Focus();
            Application.DoEvents();

            var scrollPanel = Descendants(selectedPage)
                .OfType<Panel>()
                .Single(control => control.AutoScroll);
            var scrollExercise = Stopwatch.StartNew();
            var maximumScroll = Math.Max(
                0,
                scrollPanel.VerticalScroll.Maximum -
                scrollPanel.VerticalScroll.LargeChange + 1);
            for (var index = 0; index < 120; index++)
            {
                var position = maximumScroll == 0
                    ? 0
                    : index * maximumScroll / 119;
                scrollPanel.AutoScrollPosition = new Point(0, position);
                scrollPanel.Update();
            }
            scrollPanel.AutoScrollPosition = Point.Empty;
            scrollPanel.Update();
            scrollExercise.Stop();
            if (scrollExercise.Elapsed > TimeSpan.FromSeconds(2))
            {
                throw new InvalidOperationException(
                    $"Scroll/repaint exercise exceeded two seconds: {scenario.Name}.");
            }
            var tabStops = Descendants(selectedPage)
                .Where(control => control.TabStop &&
                    control is Button or CheckBox or NumericUpDown)
                .Select(control => new UiTabStop(
                    control.GetType().Name,
                    control.AccessibleName ?? string.Empty,
                    control.TabIndex,
                    control.Enabled,
                    control.Visible))
                .ToArray();
            ValidateTabStops(tabStops, scenario.ProviderTabIndex);

            var path = Path.Combine(root, scenario.Name + ".png");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                bitmap.SetResolution(96, 96);
                form.DrawToBitmap(
                    bitmap,
                    new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path, ImageFormat.Png);
            }

            return new UiRenderResult(
                scenario.Name,
                form.Width,
                form.Height,
                scrollPanel.HorizontalScroll.Visible,
                scrollPanel.VerticalScroll.Visible,
                scrollExercise.Elapsed.TotalMilliseconds,
                primaryAction.Focused,
                tabStops);
        }
        finally
        {
            monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void ValidateTabStops(
        IReadOnlyList<UiTabStop> tabStops,
        int providerTabIndex)
    {
        var names = tabStops
            .Select(item => item.AccessibleName)
            .ToHashSet(StringComparer.Ordinal);
        var expectedNames = providerTabIndex == 0
            ? new[]
        {
            "Refresh Codex usage now",
            "Start Monitoring for Codex",
            "Configure Codex integration and open its instructions",
            "Warning threshold percentage",
            "SafeWrap threshold percentage",
            "Critical SafeWrap threshold percentage",
            "Polling interval in seconds",
            "Validate and save these settings",
            "Restore default thresholds and preferences without changing the override"
        }
            : new[]
        {
            "Refresh Claude usage now",
            "Stop Monitoring for Claude",
            "Configure Claude integration and open its instructions",
            "5-hour usage limit warning threshold",
            "5-hour usage limit SafeWrap threshold",
            "5-hour usage limit Critical SafeWrap threshold",
            "Weekly usage limit warning threshold",
            "Weekly usage limit SafeWrap threshold",
            "Weekly usage limit Critical SafeWrap threshold",
            "Polling interval in seconds",
            "Save Claude settings"
        };
        foreach (var expected in expectedNames)
        {
            if (!names.Contains(expected))
            {
                throw new InvalidOperationException(
                    $"Required keyboard target is missing: {expected}");
            }
        }

        if (tabStops.Any(item => string.IsNullOrWhiteSpace(item.AccessibleName)))
        {
            throw new InvalidOperationException(
                "A keyboard target has no accessible name.");
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record UiScenario(
        string Name,
        Size Size,
        ThemePalette Palette,
        int ProviderTabIndex);

    private sealed record UiEvidenceReport(
        int SchemaVersion,
        DateTimeOffset RenderedAtUtc,
        int Dpi,
        bool ProductionSimulationUsed,
        bool AppServerInvoked,
        IReadOnlyList<UiRenderResult> Scenarios);

    private sealed record UiRenderResult(
        string Name,
        int Width,
        int Height,
        bool HorizontalScrollVisible,
        bool VerticalScrollVisible,
        double ScrollExerciseMilliseconds,
        bool PrimaryActionFocused,
        IReadOnlyList<UiTabStop> TabStops);

    private sealed record UiTabStop(
        string ControlType,
        string AccessibleName,
        int TabIndex,
        bool Enabled,
        bool Visible);

    private sealed class EvidenceClock : IObservationClock
    {
        public DateTimeOffset UtcNow => EvidenceTime;
    }

    private sealed class EvidenceProviderDiscovery : IAiProviderDiscovery
    {
        public IReadOnlyList<ProviderDetectionResult> Detect() =>
        [
            new ProviderDetectionResult(
                AiProviderId.Codex,
                "Codex",
                true,
                ProviderUsageCapability.LiveQuotaWindows,
                ApprovedCodexCli.Version,
                "official_cli_verified"),
            new ProviderDetectionResult(
                AiProviderId.ClaudeCode,
                "Claude Code",
                true,
                ProviderUsageCapability.DetectionOnly,
                "visual-fixture",
                "detected_usage_source_not_officially_machine_readable")
        ];
    }

    private sealed class NeverInvokedSource : IUsageObservationSource
    {
        public Task<AppServerUsageObservation> ObserveAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "UI evidence rendering must not invoke App Server.");
    }

    private sealed class EvidenceStorage : IGuardStorage
    {
        private GuardPersistentState _state = GuardPersistentState.Empty;

        public SettingsLoadResult LoadSettings() => new(
            GuardSettings.Default with
            {
                MonitoringEnabled = false,
                StartAtSignIn = false,
                UnrestrictedDevelopmentOverride = true
            },
            StorageLoadStatus.Loaded,
            SettingsValidationError.None);

        public StateLoadResult LoadState() => new(
            _state,
            StorageLoadStatus.Loaded);

        public void SaveSettings(GuardSettings settings)
        {
            _ = settings;
        }

        public void SaveState(GuardPersistentState state) => _state = state;
    }

    private sealed class EvidenceStartupStore : IStartupValueStore
    {
        public string? Read(string name)
        {
            _ = name;
            return null;
        }

        public void Write(string name, string value)
        {
            _ = name;
            _ = value;
            throw new InvalidOperationException(
                "UI evidence rendering must not write startup state.");
        }

        public void Delete(string name)
        {
            _ = name;
        }
    }
}
