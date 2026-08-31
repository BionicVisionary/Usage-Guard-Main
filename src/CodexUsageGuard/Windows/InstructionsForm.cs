using System.Runtime.InteropServices;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.Windows;

public sealed class InstructionsForm : Form
{
    private readonly ThemePalette _palette;
    private readonly ProviderInstructionConfigurator _configurator;

    public InstructionsForm(
        ThemePalette palette,
        ProviderInstructionConfigurator? configurator = null,
        InstructionProvider? initialProvider = null)
    {
        _palette = palette;
        _configurator = configurator ?? new ProviderInstructionConfigurator();
        Text = "Configure AI & instructions";
        AccessibleName = "Configure AI and Usage Guard instructions";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 680);
        MinimumSize = new Size(620, 520);
        DoubleBuffered = true;
        Controls.Add(BuildContent(initialProvider));
        WindowsTheme.Apply(this, palette);
    }

    private Control BuildContent(InstructionProvider? initialProvider)
    {
        var layout = new SmoothTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "How Usage Guard works with each AI",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            AccessibleName = "Instructions heading"
        });

        var tabs = new SmoothTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "AI integration instructions"
        };
        tabs.TabPages.Add(BuildOverviewPage());
        tabs.TabPages.Add(BuildProviderPage(
            "Codex",
            UsageIntegrationInstructions.CodexSetup,
            "Copy Codex AGENTS.md agreement",
            UsageIntegrationInstructions.CodexAgreement,
            InstructionProvider.Codex,
            "Configure Codex",
            automaticAvailable: true));
        tabs.TabPages.Add(BuildProviderPage(
            "Claude",
            UsageIntegrationInstructions.ClaudeTerminalSetup + "\r\n\r\n" +
                UsageIntegrationInstructions.ClaudeSetup + "\r\n\r\n" +
                UsageIntegrationInstructions.ClaudeChatLimits,
            "Copy Claude CLAUDE.md agreement",
            UsageIntegrationInstructions.ClaudeAgreement,
            InstructionProvider.ClaudeCode,
            "Configure Claude",
            automaticAvailable: true));
        tabs.SelectedIndex = initialProvider switch
        {
            InstructionProvider.Codex => 1,
            InstructionProvider.ClaudeCode => 2,
            _ => 0
        };
        layout.Controls.Add(tabs);

        var close = new Button
        {
            Text = "Close",
            AccessibleName = "Close instructions",
            AutoSize = true,
            MinimumSize = new Size(92, 32),
            Anchor = AnchorStyles.Right
        };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close);
        return layout;
    }

    private TabPage BuildOverviewPage()
    {
        var page = new TabPage("Overview");
        var content = NewPageLayout();
        content.Controls.Add(NewExplanation(UsageIntegrationInstructions.Overview));
        content.Controls.Add(NewExplanation(
            "Standalone use: no provider instructions are required. Usage Guard can show status, monitor supported providers, notify, and remember sanitized settings by itself."));
        content.Controls.Add(NewExplanation(
            "Integrated use: press Configure on a supported provider. Usage Guard asks for confirmation, installs only its verified provider adapter, and appends only a delimited agreement while preserving existing instructions with a dated backup."));
        content.Controls.Add(NewExplanation(
            "Provider support is independent. Codex reads official 5-hour and weekly windows through App Server. The official Claude Code CLI can publish official 5-hour and weekly fields through its local status line after real responses; the tested Desktop Code tab does not invoke that bridge by itself. Each provider has separate settings, and its stricter configured window controls. Claude values represent the shared Claude plan pool, including Chat consumption, but ordinary Chat cannot invoke the local guard."));
        page.Controls.Add(content);
        return page;
    }

    private TabPage BuildProviderPage(
        string provider,
        string explanation,
        string copyButtonText,
        string agreement,
        InstructionProvider instructionProvider,
        string configureButtonText,
        bool automaticAvailable)
    {
        var page = new TabPage(provider)
        {
            AutoScroll = true,
            AccessibleName = $"{provider} integration page"
        };
        var content = NewPageLayout();
        content.Dock = DockStyle.Top;
        content.AutoSize = true;
        content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        content.Width = 680;
        content.RowCount = 5;
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(NewExplanation(explanation));
        var configureArea = new SmoothFlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            AccessibleName = $"{provider} automatic setup"
        };
        var configure = new Button
        {
            Text = configureButtonText,
            AccessibleName = configureButtonText,
            AutoSize = true,
            MinimumSize = new Size(190, 36),
            Enabled = automaticAvailable
        };
        var status = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            AccessibleName = $"{provider} configuration result",
            Text = automaticAvailable
                ? $"Not configured by this action yet. Press Configure to set up both Usage Guard and {provider} safely."
                : "Automatic protection is unavailable until a supported live Claude quota adapter exists."
        };
        if (automaticAvailable)
        {
            configure.Click += (_, _) => ConfigureProvider(
                instructionProvider,
                provider,
                status);
        }
        configureArea.Controls.Add(configure);
        configureArea.Controls.Add(status);
        content.Controls.Add(configureArea);
        content.Controls.Add(new Label
        {
            Text = "Troubleshooting and manual fallback",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 4),
            AccessibleName = $"{provider} troubleshooting heading"
        });
        var text = new TextBox
        {
            Text = agreement,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Top,
            Height = 180,
            AccessibleName = $"{provider} instruction template",
            WordWrap = true
        };
        content.Controls.Add(text);
        var copy = new Button
        {
            Text = copyButtonText,
            AccessibleName = copyButtonText,
            AutoSize = true,
            MinimumSize = new Size(180, 32)
        };
        copy.Click += (_, _) => CopyAgreement(agreement);
        content.Controls.Add(copy);
        page.Controls.Add(content);
        page.Resize += (_, _) => content.Width = Math.Max(
            420,
            page.ClientSize.Width - (page.VerticalScroll.Visible ? 36 : 22));
        return page;
    }

    private static SmoothTableLayoutPanel NewPageLayout() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        Padding = new Padding(14)
    };

    private static Label NewExplanation(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(520, 0),
        Margin = new Padding(0, 0, 0, 12),
        // These labels carry commands meant to be copied verbatim. Without this
        // a label eats "&" as an accelerator prefix, so a pasted command would
        // be silently wrong.
        UseMnemonic = false
    };

    private void CopyAgreement(string agreement)
    {
        try
        {
            Clipboard.SetText(agreement);
            MessageBox.Show(
                this,
                "The instruction template was copied. Review and merge it with existing instructions; do not overwrite unrelated rules.",
                "Instructions copied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                this,
                "The clipboard is currently unavailable. Select and copy the template manually.",
                "Clipboard unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ConfigureProvider(
        InstructionProvider provider,
        string providerName,
        Label statusLabel)
    {
        var confirmation = provider == InstructionProvider.ClaudeCode
            ? "Usage Guard will install or verify only its Claude-owned skill, bridge, isolated session settings, and delimited agreement. It will not read, copy, or edit Claude's user settings. You will then receive the official CLI launch command. Continue?"
            : $"Usage Guard will install or verify its {providerName} integration, append only its delimited section to the user-wide instructions, and create a dated sibling backup when a file already exists. Existing instructions will not be replaced. Continue?";
        var result = MessageBox.Show(
            this,
            confirmation,
            "Confirm AI configuration",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            statusLabel.Text = "No file was changed.";
            return;
        }

        var configured = _configurator.Configure(provider);
        statusLabel.Text = configured.BackupPath is null
            ? configured.Message
            : $"{configured.Message} A dated backup was created beside the original file.";
        MessageBox.Show(
            this,
            configured.Message,
            configured.Status is InstructionConfigurationStatus.Configured or
                InstructionConfigurationStatus.AlreadyConfigured
                ? "Usage Guard instructions"
                : "Usage Guard setup needs attention",
            MessageBoxButtons.OK,
            configured.Status is InstructionConfigurationStatus.Configured or
                InstructionConfigurationStatus.AlreadyConfigured
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);
    }
}
