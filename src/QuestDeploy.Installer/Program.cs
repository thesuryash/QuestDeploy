using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuestDeploy.Installer;

internal static class Program
{
    private const string ProductName = "QuestDeploy";
    private const string PayloadName = "QuestDeploy-release.zip";
    private const string WaitForPidPrefix = "--wait-for-pid=";
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromMinutes(2);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            WaitForRequestedProcess(args);

            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                ProductName);
            WaitForInstalledInstances(installDirectory);

            var executablePath = InstallPayloadTransactionally(installDirectory);
            CreateApplicationShortcuts(executablePath, installDirectory);
            WriteLegacyLauncherRedirect(executablePath);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var displayVersion = version is null ? "" : $" {version.Major}.{version.Minor}.{version.Build}";
            var launch = MessageBox.Show(
                $"{ProductName}{displayVersion} was installed for this Windows account.{Environment.NewLine}{Environment.NewLine}" +
                $"Installed application:{Environment.NewLine}{executablePath}{Environment.NewLine}{Environment.NewLine}" +
                "Shortcuts were added to the Start Menu and Desktop. Launch it now?",
                $"{ProductName} Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (launch == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = installDirectory,
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

    private static void WaitForRequestedProcess(IEnumerable<string> args)
    {
        var argument = args.FirstOrDefault(value =>
            value.StartsWith(WaitForPidPrefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return;
        }

        if (!int.TryParse(argument[WaitForPidPrefix.Length..], out var processId) || processId <= 0)
        {
            throw new InvalidOperationException("The updater supplied an invalid process identifier.");
        }
        if (processId == Environment.ProcessId)
        {
            throw new InvalidOperationException("Setup cannot wait for its own process.");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit((int)ProcessExitTimeout.TotalMilliseconds))
            {
                throw new InvalidOperationException(
                    "QuestDeploy did not close in time. Close every QuestDeploy window, then run the downloaded installer again.");
            }
        }
        catch (ArgumentException)
        {
            // The application exited before setup reached the wait step.
        }
    }

    private static void WaitForInstalledInstances(string installDirectory)
    {
        while (FindInstalledProcesses(installDirectory).Count > 0)
        {
            var result = MessageBox.Show(
                "QuestDeploy is still running from the installation folder. Close every QuestDeploy window, then choose Retry.",
                $"{ProductName} is running",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Retry)
            {
                throw new OperationCanceledException("Installation was canceled because QuestDeploy is still running.");
            }
        }
    }

    private static List<int> FindInstalledProcesses(string installDirectory)
    {
        var normalizedDirectory = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var processIds = new List<int>();
        foreach (var process in Process.GetProcessesByName("QuestDeploy"))
        {
            using (process)
            {
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executablePath) &&
                        Path.GetFullPath(executablePath).StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        processIds.Add(process.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A process owned by another account cannot lock this per-user installation.
                }
            }
        }

        return processIds;
    }

    private static string InstallPayloadTransactionally(string installDirectory)
    {
        var fullInstallDirectory = Path.GetFullPath(installDirectory);
        var expectedRoot = Path.GetFullPath(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullInstallDirectory.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Install directory is outside the current user's local Programs folder.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullInstallDirectory)!);
        var stagingDirectory = fullInstallDirectory + ".installing-" + Guid.NewGuid().ToString("N");
        var backupDirectory = fullInstallDirectory + ".previous-" + Guid.NewGuid().ToString("N");
        ExtractPayload(stagingDirectory);

        var stagedExecutable = Path.Combine(stagingDirectory, "QuestDeploy.exe");
        if (!File.Exists(stagedExecutable))
        {
            TryDeleteDirectory(stagingDirectory);
            throw new FileNotFoundException("QuestDeploy.exe was not found in the installer payload.", stagedExecutable);
        }

        var previousInstallMoved = false;
        try
        {
            if (Directory.Exists(fullInstallDirectory))
            {
                Directory.Move(fullInstallDirectory, backupDirectory);
                previousInstallMoved = true;
            }

            Directory.Move(stagingDirectory, fullInstallDirectory);
        }
        catch
        {
            if (!Directory.Exists(fullInstallDirectory) && previousInstallMoved && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, fullInstallDirectory);
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }

        TryDeleteDirectory(backupDirectory);
        return Path.Combine(fullInstallDirectory, "QuestDeploy.exe");
    }

    private static void ExtractPayload(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
            ?? throw new InvalidOperationException("The installer payload is missing.");
        using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            var safePrefix = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
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

    private static void CreateApplicationShortcuts(string executablePath, string installDirectory)
    {
        var startMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ProductName);
        Directory.CreateDirectory(startMenuDirectory);
        CreateShortcut(
            Path.Combine(startMenuDirectory, $"{ProductName}.lnk"),
            executablePath,
            installDirectory);

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CreateShortcut(
            Path.Combine(desktopDirectory, $"{ProductName}.lnk"),
            executablePath,
            installDirectory);
    }

    private static void CreateShortcut(string shortcutPath, string executablePath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows shortcut support could not be started.");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                throw new InvalidOperationException("Windows could not create the application shortcut.");
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [executablePath]);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory]);
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [$"{executablePath},0"]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["Deploy apps and classroom files to Meta Quest headsets"]);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void WriteLegacyLauncherRedirect(string executablePath)
    {
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        var launcherPath = Path.Combine(legacyDirectory, "Launch Quest Deploy.cmd");
        File.WriteAllText(
            launcherPath,
            $"@echo off{Environment.NewLine}start \"QuestDeploy\" \"{executablePath}\"{Environment.NewLine}");
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A prior version backup is harmless and can be removed on a later setup run.
        }
        catch (UnauthorizedAccessException)
        {
            // A prior version backup is harmless and can be removed on a later setup run.
        }
    }
}
