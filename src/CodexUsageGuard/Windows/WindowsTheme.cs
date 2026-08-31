using Microsoft.Win32;

namespace CodexUsageGuard.Windows;

public sealed record ThemePalette(
    Color Window,
    Color Surface,
    Color Text,
    Color MutedText,
    Color Accent,
    Color Error,
    Color Warning,
    Color Success);

public static class WindowsTheme
{
    public static ThemePalette Light { get; } = new(
        SystemColors.Window,
        Color.FromArgb(247, 247, 249),
        SystemColors.WindowText,
        SystemColors.GrayText,
        Color.FromArgb(0, 95, 184),
        Color.FromArgb(180, 30, 30),
        Color.FromArgb(145, 90, 0),
        Color.FromArgb(20, 120, 65));

    public static ThemePalette Dark { get; } = new(
        Color.FromArgb(32, 32, 32),
        Color.FromArgb(45, 45, 48),
        Color.FromArgb(245, 245, 245),
        Color.FromArgb(185, 185, 185),
        Color.FromArgb(83, 155, 245),
        Color.FromArgb(255, 120, 120),
        Color.FromArgb(255, 196, 92),
        Color.FromArgb(105, 210, 145));

    public static ThemePalette Current()
    {
        if (SystemInformation.HighContrast)
        {
            return new ThemePalette(
                SystemColors.Window,
                SystemColors.Control,
                SystemColors.WindowText,
                SystemColors.GrayText,
                SystemColors.Highlight,
                SystemColors.HotTrack,
                SystemColors.Highlight,
                SystemColors.Highlight);
        }

        return IsDarkMode() ? Dark : Light;
    }

    public static void Apply(Control root, ThemePalette palette)
    {
        root.BackColor = palette.Window;
        root.ForeColor = palette.Text;
        ApplyChildren(root, palette);
    }

    private static void ApplyChildren(Control parent, ThemePalette palette)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = palette.Text;
            control.BackColor = control is Button or NumericUpDown or TextBox
                ? palette.Surface
                : palette.Window;
            ApplyChildren(control, palette);
        }
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
