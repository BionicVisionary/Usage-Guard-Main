using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class UsageGuardBootstrapper
{
    private const string Version = "0.004";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm(InitialDestination(args)));
    }

    private static string InitialDestination(string[] args)
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Usage Guard");
        if (args == null || args.Length == 0)
        {
            return fallback;
        }
        if (args.Length != 2 ||
            !string.Equals(args[0], "--install-directory", StringComparison.Ordinal))
        {
            return fallback;
        }
        try
        {
            var candidate = Path.GetFullPath(args[1]);
            return candidate.Length >= 4 && candidate.IndexOfAny(
                new[] { '"', '\r', '\n' }) < 0
                ? candidate
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class InstallerForm : Form
    {
        private readonly TextBox _destination = new TextBox();
        private readonly Button _browse = new Button();
        private readonly Button _install = new Button();
        private readonly Button _cancel = new Button();
        private readonly Label _status = new Label();

        internal InstallerForm(string initialDestination)
        {
            Text = "Usage Guard v." + Version + " Setup";
            AccessibleName = "Usage Guard installer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(570, 245);
            Font = SystemFonts.MessageBoxFont;

            var heading = new Label
            {
                Text = "Install Usage Guard v." + Version,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(22, 20)
            };
            var explanation = new Label
            {
                Text = "Usage Guard installs only for the current Windows user. It does not require administrator rights and does not change AI account settings on other computers.",
                AutoSize = false,
                Location = new Point(22, 52),
                Size = new Size(520, 45)
            };
            var destinationLabel = new Label
            {
                Text = "Install location",
                AutoSize = true,
                Location = new Point(22, 106)
            };
            _destination.Location = new Point(22, 128);
            _destination.Size = new Size(430, 27);
            _destination.Text = initialDestination;
            _destination.AccessibleName = "Usage Guard install location";
            _browse.Text = "Browse…";
            _browse.AccessibleName = "Choose Usage Guard install location";
            _browse.Location = new Point(462, 126);
            _browse.Size = new Size(85, 30);
            _browse.Click += delegate { Browse(); };

            _status.AutoSize = false;
            _status.Location = new Point(22, 164);
            _status.Size = new Size(520, 25);
            _status.Text = "Ready to install.";
            _status.AccessibleName = "Installer status";

            _install.Text = "Install";
            _install.AccessibleName = "Install Usage Guard";
            _install.Location = new Point(352, 199);
            _install.Size = new Size(92, 32);
            _install.Click += async delegate { await InstallAsync(); };
            _cancel.Text = "Cancel";
            _cancel.AccessibleName = "Cancel installation";
            _cancel.DialogResult = DialogResult.Cancel;
            _cancel.Location = new Point(455, 199);
            _cancel.Size = new Size(92, 32);

            Controls.AddRange(new Control[]
            {
                heading, explanation, destinationLabel, _destination, _browse,
                _status, _install, _cancel
            });
            AcceptButton = _install;
            CancelButton = _cancel;
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a user-writable Usage Guard folder";
                dialog.SelectedPath = _destination.Text;
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _destination.Text = dialog.SelectedPath;
                }
            }
        }

        private async Task InstallAsync()
        {
            string destination;
            try
            {
                destination = Path.GetFullPath(_destination.Text.Trim());
                if (destination.Length < 4 || destination.IndexOfAny(
                    new[] { '\"', '\r', '\n' }) >= 0)
                {
                    throw new InvalidDataException();
                }
            }
            catch
            {
                MessageBox.Show(this, "Choose a valid user-writable folder.",
                    "Invalid install location", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Installing for the current user…");
            string work = Path.Combine(Path.GetTempPath(),
                "UsageGuard-Setup-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                string zip = Path.Combine(work, "payload.zip");
                using (Stream input = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("UsageGuard.Payload.zip"))
                {
                    if (input == null) { throw new InvalidDataException("Installer payload is missing."); }
                    using (var output = File.Create(zip)) { input.CopyTo(output); }
                }
                ZipFile.ExtractToDirectory(zip, work);
                string installer = Path.Combine(work, "Install-User.ps1");
                string app = Path.Combine(work, "app");
                if (!File.Exists(installer) || !Directory.Exists(app))
                {
                    throw new InvalidDataException("Installer payload is incomplete.");
                }

                var start = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory,
                        "WindowsPowerShell", "v1.0", "powershell.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " +
                        Quote(installer) + " -SourceDirectory " + Quote(app) +
                        " -InstallDirectory " + Quote(destination) + " -LaunchAfterInstall"
                };
                int exitCode = await Task.Run(delegate
                {
                    using (Process process = Process.Start(start))
                    {
                        if (process == null) { throw new InvalidOperationException("Installer process did not start."); }
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(180000))
                        {
                            process.Kill();
                            throw new TimeoutException("Installation timed out safely.");
                        }
                        stdout.GetAwaiter().GetResult();
                        stderr.GetAwaiter().GetResult();
                        return process.ExitCode;
                    }
                });
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        "The user-scoped installer exited with code " + exitCode + ".");
                }
                MessageBox.Show(this,
                    "Usage Guard was installed for this Windows user and launched.",
                    "Installation complete", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
            }
            catch (Exception exception)
            {
                _status.Text = "Installation stopped safely.";
                MessageBox.Show(this, exception.Message,
                    "Usage Guard was not installed", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, _status.Text);
                try
                {
                    if (Directory.Exists(work)) { Directory.Delete(work, true); }
                }
                catch
                {
                    // The random installer-owned temporary directory contains
                    // only the public package and can be removed by Windows later.
                }
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _destination.Enabled = !busy;
            _browse.Enabled = !busy;
            _install.Enabled = !busy;
            _cancel.Enabled = !busy;
            _status.Text = status;
            UseWaitCursor = busy;
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    slashCount++;
                    continue;
                }
                if (character == '\"')
                {
                    result.Append('\\', slashCount * 2 + 1);
                    result.Append('\"');
                    slashCount = 0;
                    continue;
                }
                result.Append('\\', slashCount);
                slashCount = 0;
                result.Append(character);
            }
            result.Append('\\', slashCount * 2);
            result.Append('\"');
            return result.ToString();
        }
    }
}
