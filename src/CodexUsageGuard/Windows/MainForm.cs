using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using CodexUsageGuard.Core;
using CodexUsageGuard.Monitoring;
using CodexUsageGuard.Providers;

namespace CodexUsageGuard.Windows;

public sealed class MainForm : Form
{
    private const int WmSandboxQaSelectCodex = 0x8501;
    private const int WmSandboxQaSelectClaude = 0x8502;
    private readonly UsageMonitor _monitor;
    private readonly StartupRegistration _startup;
    private readonly NotifyIcon _trayIcon;
    private readonly bool _startHidden;
    private readonly bool _showTrayIcon;
    private readonly string? _initialScreenDeviceName;
    private readonly bool _layoutQaMode;
    private readonly ThemePalette _palette;
    private readonly ProviderCatalogStorage _providerStorage;
    private readonly IAiProviderDiscovery _providerDiscovery;
    private readonly IUsageGuardUpdateService _updateService;
    private readonly IUsageGuardUpdateInstaller _updateInstaller;
    private readonly LaunchTogetherRegistration _launchTogether;
    private readonly ClaudeUsageStorage _claudeUsageStorage;
    private readonly ClaudeMonitorStateStorage _claudeMonitorStateStorage;
    private readonly Dictionary<AiProviderId, Action> _providerStatusRefreshers = [];
    private readonly Dictionary<AiProviderId, ProviderActionControls> _providerActionControls = [];
    private readonly Dictionary<AiProviderId, Label> _providerDetectionLabels = [];
    private readonly System.Windows.Forms.Timer _providerStateTimer = new()
    {
        Interval = 5_000
    };
    private readonly System.Windows.Forms.Timer _updateTimer = new()
    {
        Interval = 6 * 60 * 60 * 1_000
    };
    private ProviderCatalogSettings _providerCatalog;
    private IReadOnlyList<ProviderDetectionResult> _providerDetections;
    private readonly SmoothTabControl _providerTabs = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Configured AI providers",
        TabStop = true
    };
    private readonly Label _stateValue = NewValueLabel("Current guard state");
    private readonly Label _fiveHourRemainingValue = NewValueLabel("5-hour remaining percentage");
    private readonly Label _fiveHourResetValue = NewValueLabel("Next 5-hour reset");
    private readonly Label _remainingValue = NewValueLabel("Weekly remaining percentage");
    private readonly Label _explanationValue = NewWrapLabel("State explanation");
    private readonly Label _resetValue = NewValueLabel("Next weekly reset");
    private readonly Label _lastCheckValue = NewValueLabel("Last successful check");
    private readonly Label _freshnessValue = NewValueLabel("Observation freshness");
    private readonly Label _provenanceValue = NewWrapLabel("CLI provenance health");
    private readonly Label _monitoringValue = NewValueLabel("Monitoring status");
    private readonly Label _overrideBanner = NewWrapLabel("Unrestricted development override status");
    private readonly Button _detectProvidersButton = NewButton("Detect installed AIs", "Refresh safe installed AI provider detection");
    private readonly NumericUpDown _warningInput = NewPercentInput("Warning threshold percentage");
    private readonly NumericUpDown _safeWrapInput = NewPercentInput("SafeWrap threshold percentage");
    private readonly NumericUpDown _criticalInput = NewPercentInput("Critical SafeWrap threshold percentage");
    private readonly NumericUpDown _fiveHourWarningInput = NewPercentInput("5-hour Warning threshold percentage");
    private readonly NumericUpDown _fiveHourSafeWrapInput = NewPercentInput("5-hour SafeWrap threshold percentage");
    private readonly NumericUpDown _fiveHourCriticalInput = NewPercentInput("5-hour Critical SafeWrap threshold percentage");
    private readonly NumericUpDown _pollInput = NewPollingInput();
    private readonly CheckBox _notifyWarning = NewCheckBox("Notify on Warning");
    private readonly CheckBox _notifySafeWrap = NewCheckBox("Notify on SafeWrap");
    private readonly CheckBox _notifyUnknown = NewCheckBox("Notify on Unknown or provenance mismatch");
    private readonly CheckBox _notifyRecovery = NewCheckBox("Notify on recovery");
    private readonly CheckBox _notifyReset = NewCheckBox("Notify when a new quota-window reset is proven");
    private readonly CheckBox _minimizeToTray = NewCheckBox("Minimize to notification area");
    private readonly CheckBox _startAtSignIn = NewCheckBox("Start automatically at user sign-in");
    private readonly CheckBox _launchTogetherShortcuts = NewCheckBox("Create Launch Together shortcuts for detected AI desktop apps");
    private readonly CheckBox _resetWakeUp = NewCheckBox("Allow one-shot reset wake-up for Codex tasks");
    private readonly CheckBox _overrideCheck = NewCheckBox("Unrestricted development override");
    private readonly Button _applyButton = NewButton("Apply settings", "Validate and save these settings");
    private readonly Button _defaultsButton = NewButton("Restore defaults", "Restore default thresholds and preferences without changing the override");
    private readonly Button _updateButton = NewButton("Check for updates", "Check the configured verified Usage Guard release channel");
    private bool _loadingControls;
    private bool _exitRequested;
    private bool _trayDisposed;
    private bool _monitorTransitionInProgress;
    private bool _automaticUpdateCheckInProgress;
    private ToolStripMenuItem? _trayMonitoringItem;
    private Task<IReadOnlyList<ProviderDetectionResult>>? _providerDetectionTask;

    public MainForm(
        UsageMonitor monitor,
        StartupRegistration startup,
        bool startHidden,
        ThemePalette? palette = null,
        bool showTrayIcon = true,
        string? initialScreenDeviceName = null,
        bool layoutQaMode = false,
        ProviderCatalogStorage? providerStorage = null,
        IAiProviderDiscovery? providerDiscovery = null,
        IUsageGuardUpdateService? updateService = null,
        IUsageGuardUpdateInstaller? updateInstaller = null,
        LaunchTogetherRegistration? launchTogether = null,
        ClaudeUsageStorage? claudeUsageStorage = null,
        ClaudeMonitorStateStorage? claudeMonitorStateStorage = null)
    {
        _monitor = monitor;
        _startup = startup;
        _startHidden = startHidden;
        _showTrayIcon = showTrayIcon;
        _initialScreenDeviceName = initialScreenDeviceName;
        _layoutQaMode = layoutQaMode;
        _palette = palette ?? WindowsTheme.Current();
        _providerStorage = providerStorage ?? new ProviderCatalogStorage(
            GuardDataPaths.RootDirectory);
        _providerDiscovery = providerDiscovery ?? new WindowsAiProviderDiscovery();
        _updateService = updateService ?? new GitHubReleaseUpdateService();
        _updateInstaller = updateInstaller ?? new GitHubReleaseUpdateInstaller();
        _launchTogether = launchTogether ?? new LaunchTogetherRegistration(
            new WindowsLaunchTogetherPlatform(),
            startup.ExecutablePath);
        _claudeUsageStorage = claudeUsageStorage ?? new ClaudeUsageStorage(
            GuardDataPaths.RootDirectory);
        _claudeMonitorStateStorage = claudeMonitorStateStorage ??
            new ClaudeMonitorStateStorage(GuardDataPaths.RootDirectory);
        var loadedProviders = _providerStorage.Load();
        _providerCatalog = loadedProviders.Status is
            ProviderCatalogLoadStatus.Loaded or
            ProviderCatalogLoadStatus.MissingDefaults
            ? loadedProviders.Settings
            : ProviderCatalogSettings.Default;
        _providerDetections = [];
        Text = UsageGuardRelease.ProductNameWithVersion;
        AccessibleName = "Usage Guard settings and status";
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(660, 780);
        MinimumSize = new Size(560, 620);
        if (!string.IsNullOrWhiteSpace(initialScreenDeviceName))
        {
            var initialScreen = Screen.AllScreens.Single(screen =>
                screen.DeviceName.Equals(
                    initialScreenDeviceName,
                    StringComparison.Ordinal));
            var area = initialScreen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));
        }
        KeyPreview = true;
        if (_startHidden)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        SuspendLayout();
        _trayIcon = BuildTrayIcon(visible: false);
        try
        {
            Controls.Add(BuildContent());
            WireEvents();
            LoadSettingsIntoControls();
            ApplyTheme();
            Render(_monitor.Current);
            ResumeLayout(performLayout: false);
        }
        catch
        {
            ResumeLayout(performLayout: false);
            DisposeTrayIcon();
            throw;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (_layoutQaMode &&
            message.Msg is WmSandboxQaSelectCodex or WmSandboxQaSelectClaude)
        {
            var providerId = message.Msg == WmSandboxQaSelectCodex
                ? AiProviderId.Codex
                : AiProviderId.ClaudeCode;
            var page = _providerTabs.TabPages.Cast<TabPage>()
                .SingleOrDefault(item => item.Tag is AiProviderId id && id == providerId);
            if (page is not null)
            {
                _providerTabs.SelectedTab = page;
            }
            message.Result = new IntPtr(1);
            return;
        }

        base.WndProc(ref message);
    }

    public void ShowPopup()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowPopup);
            return;
        }

        ShowInTaskbar = true;
        Opacity = 1;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        if (_providerTabs.SelectedTab?.Tag is AiProviderId providerId &&
            _providerActionControls.TryGetValue(providerId, out var actions))
        {
            actions.CheckNow.Focus();
        }
    }

    public void RequestExit()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RequestExit);
            return;
        }

        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
        Application.ExitThread();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_layoutQaMode)
        {
            var providerIndex = LayoutQaShortcutPolicy.ProviderIndex(keyData);
            if (providerIndex is { } index && index < _providerTabs.TabPages.Count)
            {
                _providerTabs.SelectedIndex = index;
                UpdateLayoutQaTitle();
                return true;
            }
            if (LayoutQaShortcutPolicy.IsShutdown(keyData))
            {
                BeginInvoke(RequestExit);
                return true;
            }
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private Control BuildContent()
    {
        var outer = new SmoothTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            AccessibleName = "Usage Guard content"
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label
        {
            Text = UsageGuardRelease.ProductNameWithVersion,
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            AccessibleName = "Usage Guard heading and version"
        };
        var providerActions = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 8)
        };
        _detectProvidersButton.Click += async (_, _) =>
            await RefreshProviderTabsAsync();
        var isolationExplanation = new Label
        {
            Text = "Each configured AI has isolated limits, state and enforcement on this Windows user and machine only.",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 3),
            AccessibleName = "Provider isolation explanation"
        };
        providerActions.Controls.Add(_detectProvidersButton);
        providerActions.SetFlowBreak(_detectProvidersButton, true);
        providerActions.Controls.Add(isolationExplanation);
        void FitIsolationExplanation()
        {
            var width = Math.Max(
                1,
                providerActions.ClientSize.Width -
                isolationExplanation.Margin.Horizontal);
            if (isolationExplanation.MaximumSize.Width != width)
            {
                isolationExplanation.MaximumSize = new Size(width, 0);
            }
        }
        providerActions.Resize += (_, _) => FitIsolationExplanation();
        FitIsolationExplanation();
        BuildProviderTabs();
        outer.Controls.Add(heading, 0, 0);
        outer.Controls.Add(providerActions, 0, 1);
        outer.Controls.Add(_providerTabs, 0, 2);
        outer.Controls.Add(BuildApplicationActionRow(), 0, 3);
        return outer;
    }

    private Control BuildCodexPage()
    {
        var scroll = new SmoothScrollPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AccessibleName = "Codex provider content"
        };
        var root = new SmoothTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(18),
            Width = 610
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _overrideBanner.Margin = new Padding(0, 10, 0, 10);
        root.Controls.Add(_overrideBanner);
        root.Controls.Add(BuildStatusGroup());
        root.Controls.Add(BuildProviderActionRow(AiProviderId.Codex));
        root.Controls.Add(BuildSettingsGroup());
        root.Controls.Add(BuildExplanation());
        scroll.Controls.Add(root);
        scroll.Resize += (_, _) => root.Width = Math.Max(500, scroll.ClientSize.Width - 24);
        return scroll;
    }

    private void BuildProviderTabs()
    {
        _providerStatusRefreshers.Clear();
        _providerActionControls.Clear();
        _providerDetectionLabels.Clear();
        _providerTabs.TabPages.Clear();
        foreach (var provider in _providerCatalog.Providers.Where(item => item.Enabled))
        {
            var page = new TabPage(provider.DisplayName)
            {
                AccessibleName = $"{provider.DisplayName} usage settings",
                Tag = provider.ProviderId
            };
            page.Controls.Add(provider.ProviderId == AiProviderId.Codex
                ? BuildCodexPage()
                : BuildDetectionOnlyProviderPage(provider));
            _providerTabs.TabPages.Add(page);
        }
        if (_providerTabs.TabPages.Count > 0 && _providerTabs.SelectedIndex < 0)
        {
            _providerTabs.SelectedIndex = 0;
        }
        UpdateLayoutQaTitle();
    }

    private void UpdateLayoutQaTitle()
    {
        if (_layoutQaMode)
        {
            var selected = _providerTabs.SelectedTab ??
                (_providerTabs.TabPages.Count > 0 ? _providerTabs.TabPages[0] : null);
            Text = $"Usage Guard QA - {selected?.Text ?? "No provider"}";
        }
    }

    private Control BuildDetectionOnlyProviderPage(
        AiProviderConfiguration configuration)
    {
        var detection = _providerDetections.SingleOrDefault(item =>
            item.ProviderId == configuration.ProviderId);
        var scroll = new SmoothScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var root = new SmoothTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(18),
            Width = 610
        };
        var detectedText = detection is null
            ? "Checking safely…"
            : detection.Detected
                ? "Detected"
                : "Not detected";
        var installationStatus = new Label
        {
            Text = $"Installation: {detectedText}",
            AutoSize = true,
            AccessibleName = $"{configuration.DisplayName} installation status"
        };
        _providerDetectionLabels[configuration.ProviderId] = installationStatus;
        root.Controls.Add(installationStatus);
        root.Controls.Add(new Label
        {
            Text = configuration.ProviderId == AiProviderId.ClaudeCode
                ? "Shared Claude plan usage uses the official Claude Code CLI's local status-line fields after real responses. The tested Desktop Code tab does not invoke that bridge by itself. Both 5-hour and weekly windows are required; missing, stale, free Chat-only, or unsupported data is Unknown. Chat consumption affects the shared pool, but ordinary Chat cannot invoke the local guard. The bridge never starts a model session."
                : "Automatic usage is unavailable for this provider and remains Unknown.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = _palette.Error,
            Margin = new Padding(0, 8, 0, 12),
            AccessibleName = $"{configuration.DisplayName} usage capability"
        });

        if (configuration.ProviderId == AiProviderId.ClaudeCode)
        {
            root.Controls.Add(BuildClaudeStatus(configuration));
        }
        root.Controls.Add(BuildProviderActionRow(configuration.ProviderId));

        var windowControls = new Dictionary<QuotaWindowKind,
            (NumericUpDown Warning, NumericUpDown SafeWrap, NumericUpDown Critical)>();
        foreach (var window in configuration.QuotaWindows)
        {
            var group = NewGroup(window.DisplayName, $"{window.DisplayName} policy settings");
            var layout = new SmoothTableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1
            };
            var warning = NewPercentInput($"{window.DisplayName} warning threshold");
            var safeWrap = NewPercentInput($"{window.DisplayName} SafeWrap threshold");
            var critical = NewPercentInput($"{window.DisplayName} Critical SafeWrap threshold");
            warning.Value = window.WarningThresholdPercent;
            safeWrap.Value = window.SafeWrapThresholdPercent;
            critical.Value = window.CriticalBufferPercent;
            layout.Controls.Add(SettingRow(
                "Warning (% remaining)",
                "This quota window warns independently.",
                warning));
            layout.Controls.Add(SettingRow(
                "SafeWrap (% remaining)",
                "This quota window can independently require SafeWrap.",
                safeWrap));
            layout.Controls.Add(SettingRow(
                "Critical SafeWrap threshold (% remaining)",
                "Below SafeWrap, this marks greater urgency but uses the same safe checkpoint behavior. It never cancels or kills a task.",
                critical));
            group.Controls.Add(layout);
            root.Controls.Add(group);
            windowControls[window.Kind] = (warning, safeWrap, critical);
        }

        var notifyWarning = NewCheckBox("Notify on this provider's Warning");
        notifyWarning.Checked = configuration.NotifyWarning;
        var notifySafeWrap = NewCheckBox("Notify on this provider's SafeWrap");
        notifySafeWrap.Checked = configuration.NotifySafeWrap;
        var notifyUnknown = NewCheckBox("Notify when this provider becomes Unknown");
        notifyUnknown.Checked = configuration.NotifyUnknown;
        var notifyRecovery = NewCheckBox("Notify when this provider recovers");
        notifyRecovery.Checked = configuration.NotifyRecovery;
        var notifyReset = NewCheckBox("Notify when this provider proves a new quota window");
        notifyReset.Checked = configuration.NotifyReset;
        var unrestricted = NewCheckBox(
            $"Unrestricted development override for {configuration.DisplayName}");
        unrestricted.Checked = configuration.UnrestrictedDevelopmentOverride;
        var polling = NewPollingInput();
        polling.Value = configuration.PollingIntervalSeconds;
        root.Controls.Add(SettingRow(
            "Polling interval (seconds)",
            "Used only when this provider has a supported usage source.",
            polling));
        root.Controls.Add(notifyWarning);
        root.Controls.Add(notifySafeWrap);
        root.Controls.Add(notifyUnknown);
        root.Controls.Add(notifyRecovery);
        root.Controls.Add(notifyReset);
        root.Controls.Add(unrestricted);
        root.Controls.Add(new Label
        {
            Text = "This provider override disables only this AI's configured gating until you manually remove it. It does not change other AI tabs.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = $"{configuration.DisplayName} override explanation"
        });
        var save = NewButton("Apply provider settings", $"Save {configuration.DisplayName} settings");
        save.Click += (_, _) =>
        {
            var current = GetProvider(configuration.ProviderId) ?? configuration;
            SaveProviderSettings(
                current,
                current.MonitoringEnabled,
                decimal.ToInt32(polling.Value),
                notifyWarning.Checked,
                notifySafeWrap.Checked,
                notifyUnknown.Checked,
                notifyRecovery.Checked,
                notifyReset.Checked,
                unrestricted.Checked,
                windowControls);
        };
        root.Controls.Add(save);
        scroll.Controls.Add(root);
        scroll.Resize += (_, _) => root.Width = Math.Max(500, scroll.ClientSize.Width - 24);
        return scroll;
    }

    private void SaveProviderSettings(
        AiProviderConfiguration configuration,
        bool monitoringEnabled,
        int pollingInterval,
        bool notifyWarning,
        bool notifySafeWrap,
        bool notifyUnknown,
        bool notifyRecovery,
        bool notifyReset,
        bool unrestrictedDevelopmentOverride,
        IReadOnlyDictionary<QuotaWindowKind,
            (NumericUpDown Warning, NumericUpDown SafeWrap, NumericUpDown Critical)> controls)
    {
        var windows = configuration.QuotaWindows.Select(window =>
        {
            var values = controls[window.Kind];
            return window with
            {
                WarningThresholdPercent = values.Warning.Value,
                SafeWrapThresholdPercent = values.SafeWrap.Value,
                CriticalBufferPercent = values.Critical.Value
            };
        }).ToArray();
        var updated = configuration with
        {
            MonitoringEnabled = monitoringEnabled,
            PollingIntervalSeconds = pollingInterval,
            NotifyWarning = notifyWarning,
            NotifySafeWrap = notifySafeWrap,
            NotifyUnknown = notifyUnknown,
            NotifyRecovery = notifyRecovery,
            NotifyReset = notifyReset,
            UnrestrictedDevelopmentOverride = unrestrictedDevelopmentOverride,
            QuotaWindows = windows
        };
        var providers = _providerCatalog.Providers
            .Select(item => item.ProviderId == updated.ProviderId ? updated : item)
            .ToArray();
        var catalog = new ProviderCatalogSettings(
            ProviderCatalogSettings.CurrentSchemaVersion,
            providers);
        var validation = ProviderCatalogValidator.Validate(catalog);
        if (validation != ProviderCatalogValidationError.None)
        {
            MessageBox.Show(
                this,
                "Provider settings are invalid. Critical SafeWrap must be at or below SafeWrap, and SafeWrap at or below Warning for every quota window.",
                "Provider settings not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (updated.UnrestrictedDevelopmentOverride !=
            configuration.UnrestrictedDevelopmentOverride)
        {
            var enabling = updated.UnrestrictedDevelopmentOverride;
            var answer = MessageBox.Show(
                this,
                enabling
                    ? $"Enable the unrestricted development override for {configuration.DisplayName}? Only this AI's usage-based gating on this computer will be disabled until you manually turn it off."
                    : $"Disable the unrestricted development override for {configuration.DisplayName} and return this computer to its configured usage rules?",
                $"Confirm {configuration.DisplayName} override change",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        _providerStorage.Save(catalog);
        _providerCatalog = catalog;
        if (_providerStatusRefreshers.TryGetValue(
                updated.ProviderId, out var refresh))
        {
            refresh();
        }
    }

    private Control BuildClaudeStatus(AiProviderConfiguration configuration)
    {
        var group = NewGroup("Current Claude usage", "Sanitized current Claude usage status");
        var grid = NewTwoColumnGrid();
        var state = NewValueLabel("Claude configured decision");
        var fiveHour = NewValueLabel("Claude 5-hour remaining");
        var fiveReset = NewValueLabel("Claude 5-hour reset");
        var weekly = NewValueLabel("Claude weekly remaining");
        var weeklyReset = NewValueLabel("Claude weekly reset");
        var observed = NewValueLabel("Claude last observation");
        var freshness = NewValueLabel("Claude observation freshness");
        var monitoring = NewValueLabel("Claude monitoring status");
        AddStatusRow(grid, "State", state);
        AddStatusRow(grid, "5-hour remaining", fiveHour);
        AddStatusRow(grid, "5-hour reset", fiveReset);
        AddStatusRow(grid, "Weekly remaining", weekly);
        AddStatusRow(grid, "Weekly reset", weeklyReset);
        AddStatusRow(grid, "Last observation", observed);
        AddStatusRow(grid, "Freshness", freshness);
        AddStatusRow(grid, "Monitoring", monitoring);
        group.Controls.Add(grid);

        void Refresh(bool allowNotification)
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = _claudeUsageStorage.Load(now);
            var currentConfiguration = _providerCatalog.Providers
                .SingleOrDefault(item => item.ProviderId == AiProviderId.ClaudeCode) ??
                configuration;
            var decision = ClaudeGuardCheckOutput.Evaluate(
                currentConfiguration,
                snapshot,
                now);
            SetTextIfChanged(state, decision.Decision switch
            {
                "normal" => "Normal",
                "warning" => "Warning",
                "safe_wrap" when decision.CriticalBufferReached =>
                    "Critical SafeWrap",
                "safe_wrap" => "SafeWrap",
                "override_active" => "Override active",
                _ => "Unknown"
            });
            var trusted = decision.Decision is
                "normal" or "warning" or "safe_wrap";
            var five = snapshot.Windows.SingleOrDefault(item =>
                item.Kind == QuotaWindowKind.RollingFiveHour);
            var seven = snapshot.Windows.SingleOrDefault(item =>
                item.Kind == QuotaWindowKind.Weekly);
            SetTextIfChanged(fiveHour, trusted && five?.RemainingPercent is { } fiveRemaining
                ? $"{fiveRemaining:0.#}%"
                : "Unknown");
            SetTextIfChanged(fiveReset, trusted && five?.ResetsAtUtc is { } fiveResetAt
                ? fiveResetAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
                : "Unknown");
            SetTextIfChanged(weekly, trusted && seven?.RemainingPercent is { } weekRemaining
                ? $"{weekRemaining:0.#}%"
                : "Unknown");
            SetTextIfChanged(weeklyReset, trusted && seven?.ResetsAtUtc is { } weekResetAt
                ? weekResetAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
                : "Unknown");
            SetTextIfChanged(observed, snapshot.Available
                ? snapshot.ObservedAtUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)
                : snapshot.Error == "no_observation_yet"
                    ? "Unavailable from the Claude Desktop Code tab"
                    : snapshot.Error == "required_rate_limits_missing_or_invalid"
                        ? "Callback received; both quota windows unavailable"
                        : "Callback received; observation was invalid");
            SetTextIfChanged(freshness, trusted
                ? "Fresh, high-confidence Claude status-line data"
                : decision.Decision == "override_active"
                    ? "Configured override; live usage is not controlling"
                    : snapshot.Error == "no_observation_yet"
                        ? "Status lines run only in Claude Code CLI or IDE terminal sessions"
                        : snapshot.Error == "required_rate_limits_missing_or_invalid"
                            ? "Claude Code did not expose both required Pro/Max quota windows"
                            : "Unknown, missing, stale, or invalid");
            SetTextIfChanged(monitoring,
                IsProviderMonitoring(AiProviderId.ClaudeCode)
                    ? "On"
                    : _layoutQaMode
                        ? "Off (QA mode)"
                        : "Off");
            if (allowNotification && currentConfiguration.MonitoringEnabled)
            {
                try
                {
                    var persistent = _claudeMonitorStateStorage.Load();
                    var transition = ClaudeNotificationPolicy.Evaluate(
                        decision,
                        currentConfiguration,
                        persistent,
                        now);
                    _claudeMonitorStateStorage.Save(transition.State);
                    if (transition.Kind != GuardNotificationKind.None)
                    {
                        _trayIcon.BalloonTipTitle = "Usage Guard — Claude";
                        _trayIcon.BalloonTipText = ClaudeNotificationText(
                            transition.Kind,
                            decision.CriticalBufferReached);
                        _trayIcon.BalloonTipIcon = transition.Kind is
                            GuardNotificationKind.SafeWrap or
                            GuardNotificationKind.Unknown
                            ? ToolTipIcon.Warning
                            : ToolTipIcon.Info;
                        _trayIcon.ShowBalloonTip(5_000);
                    }
                }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // A provider-state persistence failure stays fail-closed in
                    // the displayed decision and must not crash the UI.
                }
            }
        }

        _providerStatusRefreshers[AiProviderId.ClaudeCode] = () => Refresh(true);
        Refresh(false);
        return group;
    }

    private async Task RefreshProviderTabsAsync()
    {
        if (_providerDetectionTask is { IsCompleted: false })
        {
            return;
        }

        _detectProvidersButton.Enabled = false;
        _detectProvidersButton.Text = "Detecting…";
        _providerDetectionTask = Task.Run(_providerDiscovery.Detect);
        try
        {
            var detections = await _providerDetectionTask;
            if (IsDisposed || Disposing)
            {
                return;
            }

            _providerDetections = detections;
            if (AddNewlyDetectedProviders())
            {
                SuspendLayout();
                try
                {
                    BuildProviderTabs();
                    ApplyTheme();
                }
                finally
                {
                    ResumeLayout(performLayout: true);
                }
            }
            else
            {
                foreach (var item in _providerDetectionLabels)
                {
                    var detected = _providerDetections.SingleOrDefault(result =>
                        result.ProviderId == item.Key)?.Detected;
                    SetTextIfChanged(item.Value, detected switch
                    {
                        true => "Installation: Detected",
                        false => "Installation: Not detected",
                        _ => "Installation: Checking safely…"
                    });
                }
            }
        }
        catch
        {
            // Discovery is optional status information. Provider usage remains
            // unavailable until a later successful detection and trusted read.
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _detectProvidersButton.Text = "Detect installed AIs";
                _detectProvidersButton.Enabled = true;
            }
        }
    }

    private bool AddNewlyDetectedProviders()
    {
        var providers = _providerCatalog.Providers.ToList();
        var changed = false;
        if (_providerDetections.Any(item =>
                item.ProviderId == AiProviderId.ClaudeCode && item.Detected) &&
            providers.All(item => item.ProviderId != AiProviderId.ClaudeCode))
        {
            providers.Add(ProviderCatalogSettings.DefaultClaudeCode);
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        var updated = new ProviderCatalogSettings(
            ProviderCatalogSettings.CurrentSchemaVersion,
            providers);
        _providerStorage.Save(updated);
        _providerCatalog = updated;
        return true;
    }

    private Control BuildStatusGroup()
    {
        var group = NewGroup("Current status", "Sanitized current usage status");
        var grid = NewTwoColumnGrid();
        AddStatusRow(grid, "State", _stateValue);
        AddStatusRow(grid, "5-hour remaining", _fiveHourRemainingValue);
        AddStatusRow(grid, "5-hour reset", _fiveHourResetValue);
        AddStatusRow(grid, "Weekly remaining", _remainingValue);
        AddStatusRow(grid, "Meaning", _explanationValue);
        AddStatusRow(grid, "Weekly reset", _resetValue);
        AddStatusRow(grid, "Last successful check", _lastCheckValue);
        AddStatusRow(grid, "Freshness", _freshnessValue);
        AddStatusRow(grid, "Source", _provenanceValue);
        AddStatusRow(grid, "Monitoring", _monitoringValue);
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildProviderActionRow(AiProviderId providerId)
    {
        var flow = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 10),
            AccessibleName = $"{ProviderDisplayName(providerId)} monitoring actions"
        };
        var checkNow = NewButton(
            "Check now",
            $"Refresh {ProviderDisplayName(providerId)} usage now");
        var monitorToggle = NewButton(
            "Start Monitoring",
            $"Start monitoring {ProviderDisplayName(providerId)}");
        var configure = NewButton(
            "Configure AI",
            $"Configure {ProviderDisplayName(providerId)} integration and open its instructions");
        var actions = new ProviderActionControls(checkNow, monitorToggle, configure);
        _providerActionControls[providerId] = actions;
        checkNow.Click += async (_, _) => await CheckProviderNowAsync(providerId, checkNow);
        monitorToggle.Click += async (_, _) => await ToggleProviderMonitoringAsync(providerId);
        configure.Click += (_, _) => ShowInstructions(providerId);
        if (_layoutQaMode)
        {
            monitorToggle.AccessibleDescription =
                "Monitoring is suppressed while Usage Guard is running in QA mode.";
        }
        flow.Controls.AddRange([checkNow, monitorToggle, configure]);
        ApplyProviderMonitoringTogglePresentation(providerId, IsProviderMonitoring(providerId));
        return flow;
    }

    private Control BuildApplicationActionRow()
    {
        var flow = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
            AccessibleName = "Application-wide actions"
        };
        var settings = NewButton(
            "Settings",
            "Open the application settings in the Codex tab");
        var minimize = NewButton(
            "Minimize to tray",
            "Hide this popup while leaving the monitor available");
        var exit = NewButton(
            "Exit",
            "Stop the helper and close it");
        flow.Controls.AddRange(new Control[]
        {
            settings,
            _updateButton,
            minimize,
            exit
        });
        settings.Click += (_, _) => FocusApplicationSettings();
        minimize.Click += (_, _) => HideToTray();
        exit.Click += (_, _) => RequestExit();
        return flow;
    }

    private void FocusApplicationSettings()
    {
        var page = _providerTabs.TabPages.Cast<TabPage>()
            .SingleOrDefault(item => item.Tag is AiProviderId.Codex);
        if (page is not null)
        {
            _providerTabs.SelectedTab = page;
        }
        _warningInput.Focus();
    }

    private Control BuildSettingsGroup()
    {
        var group = NewGroup("Settings", "Configurable Usage Guard settings");
        var layout = new SmoothTableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1
        };
        var fiveHourGroup = NewGroup(
            "5-hour usage limit",
            "Codex 5-hour usage policy settings");
        var fiveHourLayout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1
        };
        fiveHourLayout.Controls.Add(SettingRow(
            "Warning (% remaining)",
            "This quota window warns independently. Default: 30%.",
            _fiveHourWarningInput));
        fiveHourLayout.Controls.Add(SettingRow(
            "SafeWrap (% remaining)",
            "This quota window can independently require SafeWrap. Default: 25%.",
            _fiveHourSafeWrapInput));
        fiveHourLayout.Controls.Add(SettingRow(
            "Critical SafeWrap (% remaining)",
            "Below SafeWrap, this marks greater urgency but uses the same safe checkpoint behavior. It never cancels or kills a task. Default: 20%.",
            _fiveHourCriticalInput));
        fiveHourGroup.Controls.Add(fiveHourLayout);
        layout.Controls.Add(fiveHourGroup);

        var weeklyGroup = NewGroup(
            "Weekly usage limit",
            "Codex weekly usage policy settings");
        var weeklyLayout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1
        };
        weeklyLayout.Controls.Add(SettingRow(
            "Warning threshold (% remaining)",
            "Shows Warning at or below this value. Default: 30%.",
            _warningInput));
        weeklyLayout.Controls.Add(SettingRow(
            "SafeWrap threshold (% remaining)",
            "Completes the active checkpoint and starts no new phase. It does not kill tasks. Default: 25%.",
            _safeWrapInput));
        weeklyLayout.Controls.Add(SettingRow(
            "Critical SafeWrap threshold (% remaining)",
            "At or below this lower level, SafeWrap becomes urgent: finish the current coherent checkpoint as promptly as safely possible and start nothing new. It never instantly stops, cancels, or kills a task. Default: 20%.",
            _criticalInput));
        weeklyGroup.Controls.Add(weeklyLayout);
        layout.Controls.Add(weeklyGroup);
        layout.Controls.Add(SettingRow(
            "Polling interval (seconds)",
            "One bounded local check every 30–300 seconds. Default: 60.",
            _pollInput));
        layout.Controls.Add(_notifyWarning);
        layout.Controls.Add(_notifySafeWrap);
        layout.Controls.Add(_notifyUnknown);
        layout.Controls.Add(_notifyRecovery);
        layout.Controls.Add(_notifyReset);
        layout.Controls.Add(_minimizeToTray);
        layout.Controls.Add(_startAtSignIn);
        layout.Controls.Add(new Label
        {
            Text = "Start-at-sign-in is off by default, user-scoped, non-admin, and removable here.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = "Startup explanation"
        });
        layout.Controls.Add(_launchTogetherShortcuts);
        layout.Controls.Add(new Label
        {
            Text = "Creates separate Usage Guard + Codex and Usage Guard + Claude Start-menu shortcuts for detected desktop apps. Opening one starts Usage Guard and that AI together. Original AI icons are never changed and do not trigger this feature. Off by default and removable here.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = "Launch Together explanation"
        });
        layout.Controls.Add(_resetWakeUp);
        layout.Controls.Add(new Label
        {
            Text = "Off by default. When SafeWrap is genuine and reset data is fresh, an idle Codex task may schedule one deduplicated same-task check after every constraining quota window has reset plus a small provider-jitter margin. The wake-up only rechecks Usage Guard; it never resumes work automatically.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = "One-shot reset wake-up explanation"
        });
        layout.Controls.Add(_overrideCheck);
        layout.Controls.Add(new Label
        {
            Text = "While enabled, usage-based gating is disabled. The setting persists until you deliberately turn it off; usage, reset, time, and restart cannot turn it off.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = "Unrestricted development override explanation"
        });
        var buttons = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true
        };
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(_defaultsButton);
        layout.Controls.Add(buttons);
        group.Controls.Add(layout);
        return group;
    }

    private static Control BuildExplanation() => new Label
    {
        Text = "Codex evaluates its 5-hour and weekly limits independently; the stricter configured result controls. Normal means both are above Warning. Warning means plan carefully. SafeWrap is advisory: finish the current coherent checkpoint and start no new phase. Critical SafeWrap is the more urgent lower range inside SafeWrap; it uses the same safe checkpoint behavior and never cancels or kills a task. Unknown blocks a new phase under normal enforcement because either required window is unavailable, stale, ambiguous, or untrusted. A provenance mismatch means the pinned official CLI changed and must be separately verified.",
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 12, 0, 12),
        AccessibleName = "Guard state explanations"
    };

    private void WireEvents()
    {
        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_initialScreenDeviceName))
            {
                BeginInvoke(PositionOwnWindowOnInitialScreen);
            }
            _ = RefreshProviderTabsAsync();
            if (!_layoutQaMode)
            {
                _providerStateTimer.Start();
                _updateTimer.Start();
                _ = CheckForUpdatesSilentlyAsync();
            }
            if (_monitor.Settings.MonitoringEnabled && !_layoutQaMode)
            {
                _monitor.StartMonitoring();
            }

            if (_startHidden)
            {
                HideToTray();
            }
            else if (_showTrayIcon)
            {
                BeginInvoke(() => _trayIcon.Visible = true);
            }
        };
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized &&
                _monitor.Settings.MinimizeToTray)
            {
                HideToTray();
            }
        };
        _monitor.StateChanged += OnMonitorStateChanged;
        _applyButton.Click += (_, _) => ApplySettingsFromControls();
        _defaultsButton.Click += (_, _) => RestoreDefaults();
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _overrideCheck.CheckedChanged += OnOverrideCheckedChanged;
        _providerStateTimer.Tick += (_, _) =>
        {
            foreach (var refresh in _providerStatusRefreshers.Values.ToArray())
            {
                refresh();
            }
        };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesSilentlyAsync();
        _providerTabs.SelectedIndexChanged += (_, _) => UpdateLayoutQaTitle();
    }

    private void PositionOwnWindowOnInitialScreen()
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(_initialScreenDeviceName))
        {
            return;
        }
        var target = Screen.AllScreens.Single(screen => screen.DeviceName.Equals(
            _initialScreenDeviceName,
            StringComparison.Ordinal));
        if (!OwnWindowPlacement.TryCenter(Handle, target.WorkingArea))
        {
            Close();
        }
    }

    private void ShowInstructions(AiProviderId providerId)
    {
        using var instructions = new InstructionsForm(
            _palette,
            initialProvider: providerId == AiProviderId.Codex
                ? InstructionProvider.Codex
                : InstructionProvider.ClaudeCode);
        instructions.ShowDialog(this);
        var loaded = _providerStorage.Load();
        if (loaded.Status is ProviderCatalogLoadStatus.Loaded or
            ProviderCatalogLoadStatus.MissingDefaults)
        {
            var priorProviderIds = _providerCatalog.Providers
                .Select(item => item.ProviderId)
                .OrderBy(item => item)
                .ToArray();
            _providerCatalog = loaded.Settings;
            var currentProviderIds = _providerCatalog.Providers
                .Select(item => item.ProviderId)
                .OrderBy(item => item)
                .ToArray();
            if (!priorProviderIds.SequenceEqual(currentProviderIds))
            {
                BuildProviderTabs();
                ApplyTheme();
            }
            else if (_providerStatusRefreshers.TryGetValue(providerId, out var refresh))
            {
                refresh();
            }
        }
    }

    private async Task CheckProviderNowAsync(AiProviderId providerId, Button button)
    {
        button.Enabled = false;
        button.Text = "Checking…";
        try
        {
            if (providerId == AiProviderId.Codex)
            {
                await _monitor.CheckNowAsync();
            }
            else if (_providerStatusRefreshers.TryGetValue(providerId, out var refresh))
            {
                refresh();
                var snapshot = _claudeUsageStorage.Load(DateTimeOffset.UtcNow);
                if (!snapshot.Available)
                {
                    MessageBox.Show(
                        this,
                        snapshot.Error == "no_observation_yet"
                            ? "No Claude Code usage observation has arrived yet. Anthropic's status line is the only documented source of the 5-hour and weekly fields, and it runs only in Claude Code CLI or IDE terminal sessions. The Claude Desktop Code tab was measured not to run status-line commands at all, and hooks, which do run there, carry no rate-limit fields. Complete one ordinary response in a CLI or IDE terminal session in a trusted workspace. Ordinary Claude Chat cannot invoke this local bridge either."
                            : snapshot.Error == "required_rate_limits_missing_or_invalid"
                                ? "Claude Code invoked Usage Guard, but did not expose both official 5-hour and weekly rate-limit windows. These fields are documented for Claude.ai Pro/Max after an API response; unsupported plans and missing windows remain Unknown."
                            : "Claude's local usage observation is unavailable, stale, or invalid. Reopen Configure AI for the exact repair guidance.",
                        "Claude usage unavailable",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }
        finally
        {
            if (!IsDisposed && !button.IsDisposed)
            {
                button.Text = "Check now";
                button.Enabled = true;
            }
        }
    }

    private Task CheckNowAsync() => _monitor.CheckNowAsync();

    private async Task CheckForUpdatesAsync()
    {
        _updateButton.Enabled = false;
        _updateButton.Text = "Checking…";
        try
        {
            var result = await _updateService.CheckAsync();
            var answer = MessageBox.Show(
                this,
                result.Message,
                $"Usage Guard v.{result.CurrentVersion} updates",
                result.Status == UpdateCheckStatus.UpdateAvailable &&
                    result.InstallerAsset is not null && result.ChecksumAsset is not null
                    ? MessageBoxButtons.YesNo
                    : MessageBoxButtons.OK,
                result.Status == UpdateCheckStatus.UpdateAvailable
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes &&
                result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                _updateButton.Enabled = false;
                _updateButton.Text = "Downloading update…";
                var prepared = await _updateInstaller.DownloadAndVerifyAsync(result);
                if (prepared.Status != UpdatePreparationStatus.Ready ||
                    prepared.InstallerPath is null)
                {
                    MessageBox.Show(
                        this,
                        prepared.Message,
                        "Update not installed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var install = MessageBox.Show(
                    this,
                    "The installer SHA-256 matches the published release. Usage Guard will close and open the user-scoped installer for this installation folder. Continue?",
                    "Install verified update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2);
                if (install != DialogResult.Yes)
                {
                    return;
                }

                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = prepared.InstallerPath,
                    UseShellExecute = true
                };
                start.ArgumentList.Add("--install-directory");
                start.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                if (System.Diagnostics.Process.Start(start) is null)
                {
                    throw new InvalidOperationException("The verified update installer did not start.");
                }
                RequestExit();
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                "The verified update installer could not be started. No update was installed.",
                "Update not installed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _updateButton.Text = "Check for updates";
                _updateButton.Enabled = true;
            }
        }
    }

    private async Task CheckForUpdatesSilentlyAsync()
    {
        if (_automaticUpdateCheckInProgress || IsDisposed || Disposing)
        {
            return;
        }

        _automaticUpdateCheckInProgress = true;
        try
        {
            var result = await _updateService.CheckAsync();
            if (!UpdateNotificationPolicy.ShouldNotify(
                    result,
                    _monitor.PersistentState.NotificationLedger))
            {
                return;
            }

            var version = result.AvailableVersion!;
            _trayIcon.BalloonTipTitle = "Usage Guard update available";
            _trayIcon.BalloonTipText =
                $"Usage Guard v.{version} is available. Open Usage Guard and select Check for updates to review the release before installing.";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(8_000);
            _monitor.MarkNotification(
                UpdateNotificationPolicy.KeyFor(version),
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Automatic update notification is advisory. Storage or display
            // failures must not crash monitoring or weaken usage decisions.
        }
        finally
        {
            _automaticUpdateCheckInProgress = false;
        }
    }

    private void SetMonitoring(bool enabled)
    {
        var updated = _monitor.Settings with { MonitoringEnabled = enabled };
        _monitor.UpdateSettings(updated);
        UpdateProviderMonitoringSetting(AiProviderId.Codex, enabled);
        if (enabled)
        {
            _monitor.StartMonitoring();
        }

        Render(_monitor.Current);
    }

    private async Task SetMonitoringAsync(bool enabled)
    {
        var updated = _monitor.Settings with { MonitoringEnabled = enabled };
        _monitor.UpdateSettings(updated);
        UpdateProviderMonitoringSetting(AiProviderId.Codex, enabled);
        if (!enabled)
        {
            await _monitor.StopMonitoringAsync();
        }

        Render(_monitor.Current);
    }

    private async Task ToggleMonitoringAsync()
    {
        if (_monitorTransitionInProgress)
        {
            return;
        }

        _monitorTransitionInProgress = true;
        try
        {
            if (_monitor.IsMonitoring)
            {
                await SetMonitoringAsync(false);
            }
            else
            {
                SetMonitoring(true);
            }
        }
        finally
        {
            _monitorTransitionInProgress = false;
            if (!IsDisposed)
            {
                Render(_monitor.Current);
            }
        }
    }

    private async Task ToggleProviderMonitoringAsync(AiProviderId providerId)
    {
        if (_layoutQaMode)
        {
            ApplyProviderMonitoringTogglePresentation(providerId, false);
            return;
        }

        if (providerId == AiProviderId.Codex)
        {
            await ToggleMonitoringAsync();
            return;
        }

        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return;
        }

        UpdateProviderMonitoringSetting(providerId, !provider.MonitoringEnabled);
        ApplyProviderMonitoringTogglePresentation(
            providerId,
            !provider.MonitoringEnabled);
        if (_providerStatusRefreshers.TryGetValue(providerId, out var refresh))
        {
            refresh();
        }
    }

    private AiProviderConfiguration? GetProvider(AiProviderId providerId) =>
        _providerCatalog.Providers.SingleOrDefault(item => item.ProviderId == providerId);

    private bool IsProviderMonitoring(AiProviderId providerId) =>
        !_layoutQaMode && (providerId == AiProviderId.Codex
            ? _monitor.IsMonitoring
            : GetProvider(providerId)?.MonitoringEnabled == true);

    private void UpdateProviderMonitoringSetting(AiProviderId providerId, bool enabled)
    {
        var provider = GetProvider(providerId);
        if (provider is null || provider.MonitoringEnabled == enabled)
        {
            return;
        }

        var updated = new ProviderCatalogSettings(
            ProviderCatalogSettings.CurrentSchemaVersion,
            _providerCatalog.Providers.Select(item => item.ProviderId == providerId
                ? item with { MonitoringEnabled = enabled }
                : item).ToArray());
        _providerStorage.Save(updated);
        _providerCatalog = updated;
    }

    private void ApplySettingsFromControls()
    {
        var existing = _monitor.Settings;
        var updated = existing with
        {
            WarningThresholdPercent = _warningInput.Value,
            SafeWrapThresholdPercent = _safeWrapInput.Value,
            CriticalBufferPercent = _criticalInput.Value,
            FiveHourWarningThresholdPercent = _fiveHourWarningInput.Value,
            FiveHourSafeWrapThresholdPercent = _fiveHourSafeWrapInput.Value,
            FiveHourCriticalBufferPercent = _fiveHourCriticalInput.Value,
            PollingIntervalSeconds = decimal.ToInt32(_pollInput.Value),
            NotifyWarning = _notifyWarning.Checked,
            NotifySafeWrap = _notifySafeWrap.Checked,
            NotifyUnknown = _notifyUnknown.Checked,
            NotifyRecovery = _notifyRecovery.Checked,
            NotifyReset = _notifyReset.Checked,
            MinimizeToTray = _minimizeToTray.Checked,
            StartAtSignIn = _startAtSignIn.Checked,
            LaunchTogetherShortcutsEnabled = _launchTogetherShortcuts.Checked,
            UnrestrictedDevelopmentOverride = _overrideCheck.Checked,
            ResetWakeUpEnabled = _resetWakeUp.Checked
        };
        var validation = GuardSettingsValidator.Validate(updated);
        if (validation != SettingsValidationError.None)
        {
            MessageBox.Show(
                this,
                "Settings are invalid. Critical SafeWrap must be at or below SafeWrap, SafeWrap at or below Warning, and polling must be 30–300 seconds.",
                "Settings not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var priorStartup = _startup.IsEnabled();
            var priorLaunchTogether = _launchTogether.IsEnabled();
            if (priorLaunchTogether != updated.LaunchTogetherShortcutsEnabled)
            {
                _launchTogether.SetEnabled(updated.LaunchTogetherShortcutsEnabled);
            }
            try
            {
                if (priorStartup != updated.StartAtSignIn)
                {
                    _startup.SetEnabled(updated.StartAtSignIn);
                }
                _monitor.UpdateSettings(
                    updated,
                    SettingsUpdateAuthority.UserApply);
                Render(_monitor.Current);
                Invalidate(invalidateChildren: true);
            }
            catch
            {
                _startup.SetEnabled(priorStartup);
                _launchTogether.SetEnabled(priorLaunchTogether);
                throw;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or
            InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(
                this,
                "The user-scoped settings or startup entry could not be saved. No broader permissions were requested.",
                "Settings unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RestoreDefaults()
    {
        var restored = _monitor.Settings.RestoreDefaultsPreservingOverride();
        try
        {
            _startup.SetEnabled(false);
            _launchTogether.SetEnabled(false);
            _monitor.UpdateSettings(
                restored,
                SettingsUpdateAuthority.UserApply);
            LoadSettingsIntoControls();
            Render(_monitor.Current);
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or IOException or InvalidOperationException or
            System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(
                this,
                "Defaults could not be restored completely. Existing user-owned shortcuts were preserved.",
                "Restore defaults unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            LoadSettingsIntoControls();
        }
    }

    private void OnOverrideCheckedChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_loadingControls || _overrideCheck.Checked ==
            _monitor.Settings.UnrestrictedDevelopmentOverride)
        {
            return;
        }

        var enabling = _overrideCheck.Checked;
        var message = enabling
            ? "Enable Unrestricted development override? Usage-based gating will be disabled until you manually turn it off."
            : "Disable Unrestricted development override and return configured usage decisions to normal enforcement?";
        var answer = MessageBox.Show(
            this,
            message,
            "Confirm override change",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            _loadingControls = true;
            _overrideCheck.Checked = !enabling;
            _loadingControls = false;
        }
    }

    private void LoadSettingsIntoControls()
    {
        _loadingControls = true;
        var settings = _monitor.Settings;
        _warningInput.Value = settings.WarningThresholdPercent;
        _safeWrapInput.Value = settings.SafeWrapThresholdPercent;
        _criticalInput.Value = settings.CriticalBufferPercent;
        _fiveHourWarningInput.Value = settings.FiveHourWarningThresholdPercent;
        _fiveHourSafeWrapInput.Value = settings.FiveHourSafeWrapThresholdPercent;
        _fiveHourCriticalInput.Value = settings.FiveHourCriticalBufferPercent;
        _pollInput.Value = settings.PollingIntervalSeconds;
        _notifyWarning.Checked = settings.NotifyWarning;
        _notifySafeWrap.Checked = settings.NotifySafeWrap;
        _notifyUnknown.Checked = settings.NotifyUnknown;
        _notifyRecovery.Checked = settings.NotifyRecovery;
        _notifyReset.Checked = settings.NotifyReset;
        _minimizeToTray.Checked = settings.MinimizeToTray;
        _startAtSignIn.Checked = _startup.IsEnabled();
        _launchTogetherShortcuts.Checked = settings.LaunchTogetherShortcutsEnabled;
        _resetWakeUp.Checked = settings.ResetWakeUpEnabled;
        _overrideCheck.Checked = settings.UnrestrictedDevelopmentOverride;
        _loadingControls = false;
    }

    private void OnMonitorStateChanged(
        object? sender,
        MonitorStateChangedEventArgs eventArgs)
    {
        _ = sender;
        if (InvokeRequired)
        {
            BeginInvoke(() => OnMonitorStateChanged(sender, eventArgs));
            return;
        }

        Render(eventArgs.Current);
        var now = DateTimeOffset.UtcNow;
        var notification = NotificationTransitionPolicy.Evaluate(
            eventArgs.Previous,
            eventArgs.Current,
            _monitor.Settings,
            _monitor.PersistentState,
            now);
        if (notification.Kind != GuardNotificationKind.None)
        {
            _trayIcon.BalloonTipTitle = UsageGuardRelease.ProductName;
            _trayIcon.BalloonTipText = NotificationText(notification.Kind);
            _trayIcon.BalloonTipIcon = notification.Kind is
                GuardNotificationKind.SafeWrap or GuardNotificationKind.Unknown
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(5_000);
            _monitor.MarkNotification(notification.Key, now);
        }
    }

    private void Render(SanitizedUsageState state)
    {
        var palette = _palette;
        SetTextIfChanged(_stateValue, DisplayState(state.Decision, state.Reason));
        var stateColor = StateColor(state, palette);
        if (_stateValue.ForeColor != stateColor)
        {
            _stateValue.ForeColor = stateColor;
        }
        var fiveHour = state.Windows?.SingleOrDefault(item =>
            item.Kind == AppServerQuotaWindowKind.FiveHour);
        var weekly = state.Windows?.SingleOrDefault(item =>
            item.Kind == AppServerQuotaWindowKind.Weekly);
        SetTextIfChanged(_fiveHourRemainingValue, fiveHour is { } five
            ? $"{five.RemainingPercent.ToString("0.#", CultureInfo.CurrentCulture)}%"
            : "Unavailable");
        SetTextIfChanged(_fiveHourResetValue, fiveHour is { } fiveReset
            ? fiveReset.ResetsAtUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
            : "Unknown");
        SetTextIfChanged(_remainingValue, weekly is { } week
            ? $"{week.RemainingPercent.ToString("0.#", CultureInfo.CurrentCulture)}%"
            : "Unavailable");
        SetTextIfChanged(_explanationValue, Explain(state));
        SetTextIfChanged(_resetValue, weekly is { } weekReset
            ? weekReset.ResetsAtUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
            : "Unknown");
        SetTextIfChanged(_lastCheckValue,
            _monitor.PersistentState.LastSuccessfulObservationAtUtc is { } last
            ? last.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)
            : "No successful check yet");
        SetTextIfChanged(_freshnessValue,
            state.Freshness == ObservationFreshness.ObservedNow
            ? "Fresh — observed now"
            : "Unknown or stale");
        SetTextIfChanged(_provenanceValue,
            state.UnderlyingDecision == GuardRuntimeState.ProvenanceMismatch
            ? "Pinned official CLI path/version/SHA-256 mismatch. Verify an official update before trusting it."
            : $"Pinned official CLI {state.SourceProvenance.CodexCliVersion}; SHA-256 verified before each live launch.");
        SetTextIfChanged(_monitoringValue, _monitor.IsMonitoring ? "On" : "Off");
        SetTextIfChanged(_overrideBanner,
            _monitor.Settings.UnrestrictedDevelopmentOverride
            ? "UNRESTRICTED DEVELOPMENT OVERRIDE ACTIVE — usage-based gating is disabled until you manually turn it off."
            : "Configured usage gating is active.");
        var overrideColor = _monitor.Settings.UnrestrictedDevelopmentOverride
            ? palette.Warning
            : palette.Success;
        if (_overrideBanner.ForeColor != overrideColor)
        {
            _overrideBanner.ForeColor = overrideColor;
        }
        ApplyProviderMonitoringTogglePresentation(AiProviderId.Codex, _monitor.IsMonitoring);
        _trayIcon.Text = $"Usage Guard — {DisplayState(state.Decision, state.Reason)}";
    }

    private void ApplyProviderMonitoringTogglePresentation(
        AiProviderId providerId,
        bool isMonitoring)
    {
        if (!_providerActionControls.TryGetValue(providerId, out var actions))
        {
            return;
        }

        var presentation = MonitoringTogglePolicy.For(isMonitoring);
        var button = actions.MonitorToggle;
        SetTextIfChanged(button, presentation.Text);
        button.AccessibleName = $"{presentation.Text} for {ProviderDisplayName(providerId)}";
        if (isMonitoring)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = presentation.Background;
            button.ForeColor = presentation.Foreground;
            button.FlatAppearance.BorderColor = presentation.Border;
            button.FlatAppearance.BorderSize = 1;
        }
        else
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = _palette.Surface;
            button.ForeColor = _palette.Text;
        }
        if (providerId == AiProviderId.Codex && _trayMonitoringItem is not null)
        {
            _trayMonitoringItem.Text = $"Codex: {presentation.Text}";
        }
    }

    private static void SetTextIfChanged(Control control, string text)
    {
        if (!control.Text.Equals(text, StringComparison.Ordinal))
        {
            control.Text = text;
        }
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _ = sender;
        if (!_exitRequested && _monitor.Settings.MinimizeToTray &&
            _monitor.Settings.MonitoringEnabled &&
            eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            HideToTray();
            return;
        }

        _trayIcon.Visible = false;
        DisposeTrayIcon();
    }

    private NotifyIcon BuildTrayIcon(bool visible)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open status", null, (_, _) => ShowPopup());
        menu.Items.Add("Check now", null, async (_, _) => await CheckNowAsync());
        _trayMonitoringItem = new ToolStripMenuItem("Start Monitoring");
        _trayMonitoringItem.Click += async (_, _) => await ToggleMonitoringAsync();
        menu.Items.Add(_trayMonitoringItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => RequestExit());
        var icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = UsageGuardRelease.ProductName,
            Visible = visible,
            ContextMenuStrip = menu
        };
        icon.MouseClick += OnTrayIconMouseClick;
        icon.DoubleClick += (_, _) => ShowPopup();
        return icon;
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        _ = sender;
        if (TrayInteractionPolicy.OpensStatus(eventArgs.Button))
        {
            ShowPopup();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _providerStateTimer.Stop();
            _providerStateTimer.Dispose();
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _monitor.StateChanged -= OnMonitorStateChanged;
            DisposeTrayIcon();
        }

        base.Dispose(disposing);
    }

    private void DisposeTrayIcon()
    {
        if (_trayDisposed)
        {
            return;
        }

        _trayDisposed = true;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
    }

    private void ApplyTheme()
    {
        WindowsTheme.Apply(this, _palette);
        foreach (var providerId in _providerActionControls.Keys.ToArray())
        {
            ApplyProviderMonitoringTogglePresentation(
                providerId,
                IsProviderMonitoring(providerId));
        }
    }

    private static string NotificationText(GuardNotificationKind kind) => kind switch
    {
        GuardNotificationKind.Warning => "A Codex quota window entered Warning.",
        GuardNotificationKind.SafeWrap => "SafeWrap is active: finish the current checkpoint and start no new phase. Tasks are not killed.",
        GuardNotificationKind.Unknown => "Usage is Unknown or the approved CLI provenance changed. Normal enforcement starts no new phase.",
        GuardNotificationKind.Recovery => "A fresh trusted usage observation recovered.",
        GuardNotificationKind.Reset => "A fresh live observation proved a new Codex quota window.",
        _ => "Usage Guard state changed."
    };

    private static string ClaudeNotificationText(
        GuardNotificationKind kind,
        bool critical) => kind switch
        {
            GuardNotificationKind.Warning =>
                "A shared Claude quota window entered Warning.",
            GuardNotificationKind.SafeWrap =>
                critical
                    ? "Claude Critical SafeWrap is active: Claude Code should finish its current checkpoint as promptly as safely possible and start no new phase. Ordinary chats are not killed or controlled."
                    : "Claude SafeWrap is active: Claude Code should finish its current checkpoint and start no new phase. Ordinary chats are not killed or controlled.",
            GuardNotificationKind.Unknown =>
                "Shared Claude usage is Unknown. Claude Code starts no new phase under normal enforcement.",
            GuardNotificationKind.Recovery =>
                "A fresh trusted shared Claude usage observation recovered.",
            GuardNotificationKind.Reset =>
                "A fresh Claude observation proved a new 5-hour or weekly quota window.",
            _ => "Claude usage state changed."
        };

    private static string Explain(SanitizedUsageState state) => state.Decision switch
    {
        GuardRuntimeState.Normal => "Above the Warning threshold.",
        GuardRuntimeState.Warning => "At or below Warning, but above SafeWrap.",
        GuardRuntimeState.SafeWrap => state.Reason switch
        {
            GuardDecisionReason.GenuineLatchActive =>
                "A genuine SafeWrap latch remains active for this weekly window.",
            GuardDecisionReason.CriticalBufferReached =>
                "Critical SafeWrap: finish the current coherent checkpoint as promptly as safely possible and start no new phase. It never instantly stops, cancels, or kills tasks.",
            _ =>
                "Finish the current coherent checkpoint and start no new phase. This does not kill tasks."
        },
        GuardRuntimeState.OverrideActive => $"Usage gating is disabled by explicit user override. Underlying state: {DisplayState(state.UnderlyingDecision, state.Reason)}.",
        GuardRuntimeState.ProvenanceMismatch => "The pinned official CLI changed. The reading is Unknown until separately verified.",
        GuardRuntimeState.ResetDetected => "A fresh observation proved a new 5-hour or weekly window.",
        _ => "No fresh, unique, high-confidence 5-hour and weekly reading is available. Normal enforcement starts no new phase."
    };

    private static string DisplayState(
        GuardRuntimeState state,
        GuardDecisionReason? reason = null) => state switch
        {
            GuardRuntimeState.SafeWrap when reason == GuardDecisionReason.CriticalBufferReached =>
                "Critical SafeWrap",
            GuardRuntimeState.SafeWrap => "SafeWrap",
            GuardRuntimeState.OverrideActive => "Override active",
            GuardRuntimeState.ResetDetected => "Reset detected",
            GuardRuntimeState.ProvenanceMismatch => "Provenance mismatch",
            _ => state.ToString()
        };

    private static Color StateColor(SanitizedUsageState state, ThemePalette palette) =>
        state.Decision switch
        {
            GuardRuntimeState.Normal => palette.Success,
            GuardRuntimeState.Warning or GuardRuntimeState.OverrideActive => palette.Warning,
            GuardRuntimeState.SafeWrap or GuardRuntimeState.Unknown or
                GuardRuntimeState.ProvenanceMismatch => palette.Error,
            _ => palette.Accent
        };

    private static GroupBox NewGroup(string text, string accessibleName) => new()
    {
        Text = text,
        AccessibleName = accessibleName,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Padding = new Padding(10),
        Margin = new Padding(0, 6, 0, 6)
    };

    private static TableLayoutPanel NewTwoColumnGrid()
    {
        var grid = new SmoothTableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddStatusRow(
        TableLayoutPanel grid,
        string name,
        Control value)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = name,
            AutoSize = true,
            Margin = new Padding(3, 4, 8, 4)
        }, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static Control SettingRow(
        string title,
        string explanation,
        Control input)
    {
        var grid = NewTwoColumnGrid();
        grid.Margin = new Padding(0, 4, 0, 4);
        var labels = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        labels.Controls.Add(new Label { Text = title, AutoSize = true });
        labels.Controls.Add(new Label
        {
            Text = explanation,
            AutoSize = true,
            MaximumSize = new Size(360, 0)
        });
        grid.Controls.Add(labels, 0, 0);
        grid.Controls.Add(input, 1, 0);
        return grid;
    }

    private static Label NewValueLabel(string accessibleName) => new()
    {
        AutoSize = true,
        AccessibleName = accessibleName,
        Margin = new Padding(3, 4, 3, 4)
    };

    private static Label NewWrapLabel(string accessibleName) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(360, 0),
        AccessibleName = accessibleName,
        Margin = new Padding(3, 4, 3, 4)
    };

    private static Button NewButton(string text, string accessibleName) => new()
    {
        Text = text,
        AccessibleName = accessibleName,
        AutoSize = true,
        MinimumSize = new Size(92, 32),
        Margin = new Padding(3)
    };

    private static CheckBox NewCheckBox(string text) => new()
    {
        Text = text,
        AccessibleName = text,
        AutoSize = true,
        Margin = new Padding(3, 5, 3, 5)
    };

    private static NumericUpDown NewPercentInput(string accessibleName) => new()
    {
        Minimum = 0,
        Maximum = 100,
        DecimalPlaces = 1,
        Increment = 1,
        Width = 100,
        AccessibleName = accessibleName
    };

    private static NumericUpDown NewPollingInput() => new()
    {
        Minimum = GuardSettings.MinimumPollingIntervalSeconds,
        Maximum = GuardSettings.MaximumPollingIntervalSeconds,
        Increment = 10,
        Width = 100,
        AccessibleName = "Polling interval in seconds"
    };

    private static string ProviderDisplayName(AiProviderId providerId) => providerId switch
    {
        AiProviderId.Codex => "Codex",
        AiProviderId.ClaudeCode => "Claude",
        _ => providerId.ToString()
    };

    private sealed record ProviderActionControls(
        Button CheckNow,
        Button MonitorToggle,
        Button Configure);
}

internal static class OwnWindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static bool TryCenter(IntPtr window, Rectangle workingArea)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rectangle))
        {
            return false;
        }
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        var x = workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2);
        var y = workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2);
        const uint noSizeNoZOrderNoActivate = 0x0001 | 0x0004 | 0x0010;
        return SetWindowPos(
            window,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            noSizeNoZOrderNoActivate);
    }
}

public static class TrayInteractionPolicy
{
    public static bool OpensStatus(MouseButtons button) =>
        button == MouseButtons.Left;
}

public static class LayoutQaShortcutPolicy
{
    public static int? ProviderIndex(Keys keys) => keys switch
    {
        Keys.Control | Keys.D1 => 0,
        Keys.Control | Keys.D2 => 1,
        _ => null
    };

    public static bool IsShutdown(Keys keys) =>
        keys == (Keys.Control | Keys.Shift | Keys.F12);
}

public readonly record struct MonitoringTogglePresentation(
    string Text,
    Color Background,
    Color Foreground,
    Color Border);

public static class MonitoringTogglePolicy
{
    private static readonly Color StopRed = Color.FromArgb(185, 28, 28);

    public static MonitoringTogglePresentation For(bool isMonitoring) =>
        isMonitoring
            ? new MonitoringTogglePresentation(
                "Stop Monitoring",
                StopRed,
                Color.White,
                Color.FromArgb(125, 15, 15))
            : new MonitoringTogglePresentation(
                "Start Monitoring",
                Color.White,
                Color.Black,
                Color.FromArgb(110, 110, 110));
}

public sealed class SmoothScrollPanel : Panel
{
    public SmoothScrollPanel()
    {
        ResizeRedraw = true;
    }

    public bool UsesNativePainting => !DoubleBuffered;
}

public sealed class SmoothTableLayoutPanel : TableLayoutPanel
{
    public SmoothTableLayoutPanel()
    {
        ResizeRedraw = true;
    }

    public bool UsesNativePainting => !DoubleBuffered;
}

public sealed class SmoothFlowLayoutPanel : FlowLayoutPanel
{
    public SmoothFlowLayoutPanel()
    {
        ResizeRedraw = true;
    }

    public bool UsesNativePainting => !DoubleBuffered;
}

public sealed class SmoothTabControl : TabControl
{
    public SmoothTabControl()
    {
        ResizeRedraw = true;
    }

    public bool UsesNativePainting => !DoubleBuffered;
}
