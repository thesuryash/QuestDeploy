using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using QuestDeploy.Services;

namespace QuestDeploy;

public partial class AboutWindow : Window
{
    private readonly UpdateService _updateService = new();

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Version 0.10.0" : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        UpdateStatusText.Text = "Contacting GitHub Releases...";
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 10, 0);
            var update = await _updateService.CheckForUpdateAsync(currentVersion, CancellationToken.None);
            if (update is null)
            {
                UpdateStatusText.Text = "You already have the latest published version.";
                MessageBox.Show(this, "No newer published version is available.", "QuestDeploy is up to date", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateStatusText.Text = $"Version {update.Version.ToString(3)} is available.";
            var decision = MessageBox.Show(
                this,
                $"QuestDeploy {update.Version.ToString(3)} is available. Download and run the installer now?{Environment.NewLine}{Environment.NewLine}The application will close after the installer starts.",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }

            CheckUpdatesButton.Content = "Downloading...";
            var progress = new Progress<string>(message => UpdateStatusText.Text = message);
            var installerPath = await _updateService.DownloadInstallerAsync(update, progress, CancellationToken.None);
            UpdateStatusText.Text = "Starting the verified installer...";
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = "The update check failed.";
            MessageBox.Show(this, exception.Message, "Could not check for updates", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check for updates";
        }
    }

    private void OnOpenProjectSource(object sender, RoutedEventArgs e) =>
        OpenTarget("https://github.com/thesuryash/QuestDeploy", "project source");

    private void OnOpenMitLicense(object sender, RoutedEventArgs e) =>
        OpenTarget(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"), "MIT license");

    private void OnOpenAndroidNotices(object sender, RoutedEventArgs e) =>
        OpenTarget(Path.Combine(AppContext.BaseDirectory, "platform-tools", "NOTICE.txt"), "Android notices");

    private void OnOpenScrcpyLicense(object sender, RoutedEventArgs e) =>
        OpenTarget(Path.Combine(AppContext.BaseDirectory, "scrcpy", "LICENSE.txt"), "scrcpy license");

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OpenTarget(string target, string label)
    {
        try
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.IsFile)
            {
                var fullPath = Path.GetFullPath(target);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"The {label} file was not found.", fullPath);
                }
                target = fullPath;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, $"Could not open {label}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
