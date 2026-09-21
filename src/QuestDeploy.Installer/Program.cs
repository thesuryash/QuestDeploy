using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace QuestDeploy.Installer;

internal static class Program
{
    private const string ProductName = "Quest Deploy";
    private const string PayloadName = "QuestDeploy-release.zip";

    [STAThread]
    private static int Main()
    {
        try
        {
            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuestDeploy");
            InstallPayload(installDirectory);
            var launcherPath = WriteLauncher(installDirectory);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var displayVersion = version is null ? "" : $" {version.Major}.{version.Minor}.{version.Build}";
            var launch = MessageBox.Show(
                $"{ProductName}{displayVersion} Professor Mode was installed to:{Environment.NewLine}{installDirectory}{Environment.NewLine}{Environment.NewLine}Launch it now?",
                $"{ProductName} Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (launch == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    UseShellExecute = true
                });
            }

            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                $"{ProductName} Setup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void InstallPayload(string installDirectory)
    {
        var fullInstallDirectory = Path.GetFullPath(installDirectory);
        var expectedRoot = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullInstallDirectory.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Install directory is outside the current user's local app data folder.");
        }

        Directory.CreateDirectory(fullInstallDirectory);

        using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
            ?? throw new InvalidOperationException("The installer payload is missing.");
        using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(fullInstallDirectory, entry.FullName));
            var safeInstallPrefix = fullInstallDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(safeInstallPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Installer payload contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static string WriteLauncher(string installDirectory)
    {
        var exePath = Path.Combine(installDirectory, "QuestDeploy.exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("QuestDeploy.exe was not found after extraction.", exePath);
        }

        var launcherPath = Path.Combine(installDirectory, "Launch Quest Deploy.cmd");
        File.WriteAllText(
            launcherPath,
            $"@echo off{Environment.NewLine}start \"Quest Deploy\" \"{exePath}\"{Environment.NewLine}");
        return launcherPath;
    }
}
