using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using QuestDeploy.Models;
using QuestDeploy.Services;

namespace QuestDeploy;

public partial class MainWindow : Window
{
    private readonly AdbService _adb = new();
    private readonly DeviceStore _store = new();
    private readonly ScrcpyService _scrcpy = new();
    private readonly DispatcherTimer _deviceWatchTimer;
    private readonly CancellationTokenSource _windowLifetime = new();
    private bool _changingSelection;
    private bool _isBusy;
    private bool _deviceWatchRunning;
    private bool _deviceSnapshotInitialized;
    private bool _autoRefreshPending;
    private string _lastDeviceTransportSnapshot = "";
    private int _deviceFilterIndex;

    public ObservableCollection<QuestDevice> Devices { get; } = [];
    public ObservableCollection<string> DeploymentItems { get; } = [];
    public ICollectionView DevicesView { get; }

    public MainWindow()
    {
        InitializeComponent();
        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = DeviceMatchesActiveFilter;
        DataContext = this;
        Devices.CollectionChanged += OnDevicesCollectionChanged;
        DeploymentItems.CollectionChanged += (_, _) => UpdateDeploymentSummary();
        _deviceWatchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _deviceWatchTimer.Tick += OnDeviceWatchTick;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_adb.TryLocateAdb())
        {
            AdbStatusText.Text = "Android Platform Tools were not found. Reinstall Quest Classroom Deploy or set QUEST_DEPLOY_ADB.";
            ActivityStateText.Text = "Android tools missing";
            ActivityDetailText.Text = "ADB is required to communicate with Quest headsets.";
            DeployButton.IsEnabled = false;
            return;
        }

        AdbStatusText.Text = "Android tools ready";
        foreach (var saved in await _store.LoadAsync())
        {
            var device = new QuestDevice
            {
                Name = saved.Name,
                Identity = saved.Identity,
                Model = saved.Model,
                Host = saved.Host,
                Port = saved.Port,
                MacAddress = saved.MacAddress,
                Status = "Remembered"
            };
            Devices.Add(device);
        }

        UpdateSummary();
        await ReconnectKnownAsync(showDialog: false);
        await InitializeDeviceWatcherAsync();
    }

    private async Task InitializeDeviceWatcherAsync()
    {
        try
        {
            _lastDeviceTransportSnapshot = await _adb.GetDeviceTransportSnapshotAsync(_windowLifetime.Token);
            _deviceSnapshotInitialized = true;
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            _lastDeviceTransportSnapshot = "";
        }

        _deviceWatchTimer.Start();
    }

    private async void OnDeviceWatchTick(object? sender, EventArgs e)
    {
        if (_deviceWatchRunning || _windowLifetime.IsCancellationRequested)
        {
            return;
        }

        _deviceWatchRunning = true;
        try
        {
            var snapshot = await _adb.GetDeviceTransportSnapshotAsync(_windowLifetime.Token);
            if (!_deviceSnapshotInitialized)
            {
                _lastDeviceTransportSnapshot = snapshot;
                _deviceSnapshotInitialized = true;
                return;
            }
            if (snapshot.Equals(_lastDeviceTransportSnapshot, StringComparison.Ordinal))
            {
                return;
            }

            _lastDeviceTransportSnapshot = snapshot;
            _autoRefreshPending = true;
            await RunPendingAutoRefreshAsync();
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // ADB may be restarting while a cable changes. The next tick retries quietly.
        }
        finally
        {
            _deviceWatchRunning = false;
        }
    }

    private async Task RunPendingAutoRefreshAsync()
    {
        if (!_autoRefreshPending || _isBusy || _windowLifetime.IsCancellationRequested)
        {
            return;
        }

        _autoRefreshPending = false;
        await RunBusyActionAsync("Headset connection changed", RefreshDevicesAsync, showFailureDialog: false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _deviceWatchTimer.Stop();
        _windowLifetime.Cancel();
    }

    private void OnRestartApplication(object sender, RoutedEventArgs e)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            MessageBox.Show(this, "The application executable could not be located.", "Restart unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        });
        Application.Current.Shutdown();
    }

    private void OnExitApplication(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    private void OnShowAbout(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow
        {
            Owner = this
        };
        about.ShowDialog();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) =>
        await RunBusyActionAsync("Checking headsets", RefreshDevicesAsync);

    private void OnBrowseApk(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            ApkPathBox.Text = dialog.FileName;
            UpdateDeploymentSummary();
        }
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose videos, audio, or class files",
            Filter = "All supported files|*.mp4;*.mkv;*.mov;*.webm;*.mp3;*.wav;*.m4a;*.pdf;*.json;*.xml;*.txt;*.csv;*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddDeploymentItems(dialog.FileNames);
        }
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to copy to every headset",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddDeploymentItems([dialog.FolderName]);
        }
    }

    private void OnClearContent(object sender, RoutedEventArgs e)
    {
        DeploymentItems.Clear();
        ApkPathBox.Clear();
        UpdateDeploymentSummary();
    }

    private void AddDeploymentItems(IEnumerable<string> paths)
    {
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (!DeploymentItems.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                DeploymentItems.Add(path);
            }
        }
        UpdateDeploymentSummary();
    }

    private void OnLaunchOptionChanged(object sender, RoutedEventArgs e)
    {
        if (PackageBox is not null && LaunchAfterInstallBox is not null)
        {
            PackageBox.IsEnabled = LaunchAfterInstallBox.IsChecked == true;
        }
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (_changingSelection)
        {
            return;
        }

        var selected = SelectAllBox.IsChecked == true;
        foreach (var device in VisibleDevices().Where(device => device.IsConnected))
        {
            device.IsSelected = selected;
        }
        UpdateSummary();
    }

    private void OnDeviceFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || DeviceFilterTabs is null || DevicesView is null)
        {
            return;
        }

        _deviceFilterIndex = Math.Max(0, DeviceFilterTabs.SelectedIndex);
        DevicesView.Refresh();
        UpdateSummary();
    }

    private async void OnDeviceNameChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        await SaveKnownAsync();
        UpdateSummary();
    }

    private async void OnEnableWifiForDevice(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not QuestDevice { HasUsb: true } device)
        {
            MessageBox.Show(this, "Connect the headset by USB and press Refresh headsets first.", "USB headset required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunBusyActionAsync("Setting up Wi-Fi", async token =>
        {
            device.Status = "Setting up Wi-Fi";
            device.Detail = "Enabling wireless ADB and verifying this headset";
            var endpoint = await _adb.EnableWifiAdbFromUsbAsync(device.UsbSerial, 5555, token);
            if (TrySplitEndpoint(endpoint, out var host, out var port))
            {
                device.Host = host;
                device.Port = port;
                device.WifiSerial = endpoint;
            }
            device.Status = "Connected";
            device.Detail = "Wi-Fi is ready; the USB cable can be removed";
            await RefreshDevicesAsync(token);
            await SaveKnownAsync();
            SetFooter($"Wi-Fi configured for {device.Name} at {endpoint}.");
        });
    }

    private async void OnForgetWifiDevice(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not QuestDevice { HasKnownWifi: true } device)
        {
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Forget the remembered Wi-Fi connection for {device.Name}?{Environment.NewLine}{Environment.NewLine}This removes {device.Host}:{device.Port} from QuestDeploy and disconnects Wi-Fi ADB. It does not revoke the headset's USB-debugging authorization.",
            "Forget Wi-Fi headset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyActionAsync("Forgetting Wi-Fi headset", async token =>
        {
            var endpoint = $"{device.Host}:{device.Port}";
            try
            {
                await _adb.DisconnectAsync(device.Host, device.Port, token);
            }
            catch
            {
                // The saved endpoint must still be removable when it is already offline.
            }

            device.WifiSerial = "";
            device.Host = "";
            device.Port = 5555;
            device.Detail = "Remembered Wi-Fi connection removed";
            if (device.HasUsb)
            {
                device.Status = "Connected";
            }
            else
            {
                Devices.Remove(device);
            }

            await SaveKnownAsync();
            DevicesView.Refresh();
            SetFooter($"Forgot Wi-Fi endpoint {endpoint} for {device.Name}.");
        });
    }

    private void OnShowInstalledApps(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not QuestDevice { HasUsb: true } device)
        {
            return;
        }

        new InstalledAppsWindow(_adb, device) { Owner = this }.ShowDialog();
    }

    private async void OnCastDevice(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not QuestDevice { CanCast: true } device)
        {
            return;
        }

        var options = new CastOptionsWindow(device) { Owner = this };
        if (options.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var settings = options.Settings;
            var serial = options.SelectedSerial;
            DisplaySize? displaySize = settings.ViewMode is CastViewMode.BothEyes
                ? null
                : await _adb.GetDisplaySizeAsync(serial, CancellationToken.None);
            _scrcpy.StartCast(device, serial, settings, displaySize);
            var view = settings.ViewMode switch
            {
                CastViewMode.RightEye => "right eye",
                CastViewMode.BothEyes => "both eyes",
                _ => "left eye"
            };
            SetFooter($"Opened a read-only {view} cast for {device.Name} via {options.SelectedTransportLabel}.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not cast headset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnReconnectKnown(object sender, RoutedEventArgs e) =>
        await ReconnectKnownAsync(showDialog: true);

    private async Task ReconnectKnownAsync(bool showDialog)
    {
        await RunBusyActionAsync("Reconnecting remembered headsets", async token =>
        {
            foreach (var device in Devices.Where(device => device.Host.Length > 0).ToList())
            {
                try
                {
                    device.Status = "Connecting";
                    var endpoint = await _adb.ConnectAsync(device.Host, device.Port, token);
                    device.WifiSerial = endpoint;
                    device.Status = "Connected";
                    device.Detail = "";
                }
                catch (Exception exception)
                {
                    device.Status = "Remembered";
                    device.Detail = "Not currently reachable";
                    SetFooter($"Could not reconnect {device.Name}: {exception.Message}");
                }
            }

            await RefreshDevicesAsync(token);
        }, showDialog);
    }

    private async void OnDeploy(object sender, RoutedEventArgs e)
    {
        var targets = Devices.Where(device => device is { IsSelected: true, IsConnected: true }).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select at least one connected headset.", "No headsets selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var hasApk = !string.IsNullOrWhiteSpace(ApkPathBox.Text);
        if (!hasApk && DeploymentItems.Count == 0)
        {
            MessageBox.Show(this, "Choose an APK, class files, or a folder to deploy.", "Nothing to deploy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (LaunchAfterInstallBox.IsChecked == true && string.IsNullOrWhiteSpace(PackageBox.Text))
        {
            MessageBox.Show(this, "Enter the Android package name before enabling Open the app when finished.", "Package name required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var apkPath = ApkPathBox.Text;
        var remoteDirectory = TargetFolderBox.Text;
        var items = DeploymentItems.ToList();
        var replace = ReplaceExistingBox.IsChecked == true;
        var launch = LaunchAfterInstallBox.IsChecked == true;
        var packageName = PackageBox.Text.Trim();

        await RunBusyActionAsync("Deploying class content", async token =>
        {
            var completed = 0;
            var failed = 0;
            var issues = new ConcurrentBag<DeploymentIssue>();
            DeploymentProgress.IsIndeterminate = false;
            DeploymentProgress.Value = 0;
            ActivityCountText.Text = $"0 of {targets.Count}";
            using var gate = new SemaphoreSlim(Math.Min(4, targets.Count));
            var tasks = targets.Select(async device =>
            {
                await gate.WaitAsync(token);
                try
                {
                    var serial = device.PreferredSerial;
                    if (hasApk)
                    {
                        device.Status = "Installing app";
                        device.Detail = Path.GetFileName(apkPath);
                        await _adb.InstallApkAsync(serial, apkPath, replace, token);
                    }

                    foreach (var item in items)
                    {
                        device.Status = "Copying files";
                        var progress = new Progress<string>(name => device.Detail = name);
                        await _adb.StagePathAsync(serial, item, remoteDirectory, progress, token);
                    }

                    if (launch)
                    {
                        device.Status = "Opening app";
                        device.Detail = packageName;
                        await _adb.LaunchPackageAsync(serial, packageName, token);
                    }

                    device.Status = "Complete";
                    device.Detail = "App and files verified";
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref failed);
                    var issue = InstallFailureAdvisor.Create(device.Name, exception, replace);
                    issues.Add(issue);
                    device.Status = "Failed";
                    device.Detail = issue.Summary;
                    SetFooter($"{device.Name} failed: {issue.ErrorCode}");
                }
                finally
                {
                    var finished = Interlocked.Increment(ref completed);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        DeploymentProgress.Value = finished * 100d / targets.Count;
                        ActivityCountText.Text = $"{finished} of {targets.Count}";
                    });
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            ActivityStateText.Text = failed == 0 ? "Deployment complete" : "Deployment finished with errors";
            ActivityDetailText.Text = failed == 0
                ? $"App and files were deployed to {targets.Count} headset(s)."
                : $"{targets.Count - failed} succeeded and {failed} failed. Review the recovery steps.";
            SetFooter(ActivityDetailText.Text);
            if (!issues.IsEmpty)
            {
                new DeploymentIssuesWindow(issues.OrderBy(issue => issue.HeadsetName, StringComparer.OrdinalIgnoreCase))
                {
                    Owner = this
                }.ShowDialog();
            }
        });
    }

    private async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await _adb.GetDevicesAsync(cancellationToken);
        foreach (var existing in Devices)
        {
            existing.ResetLiveConnections();
        }

        foreach (var incoming in discovered)
        {
            UpsertDevice(incoming);
        }

        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            var device = Devices[index];
            if (!device.IsConnected && device.Host.Length == 0)
            {
                Devices.RemoveAt(index);
            }
        }

        await SaveKnownAsync();
        UpdateSummary();
        SetFooter($"Headset check complete: {Devices.Count(device => device.IsConnected)} connected.");
    }

    private void UpsertDevice(QuestDevice incoming)
    {
        var endpointMatch = incoming.Host.Length == 0
            ? null
            : Devices.FirstOrDefault(device => device.Host.Equals(incoming.Host, StringComparison.OrdinalIgnoreCase) && device.Port == incoming.Port);
        var identityMatch = incoming.Identity.Length == 0
            ? null
            : Devices.FirstOrDefault(device => device.Identity.Equals(incoming.Identity, StringComparison.OrdinalIgnoreCase));
        var existing = endpointMatch ?? identityMatch;

        if (existing is null)
        {
            Devices.Add(incoming);
            SubscribeDevice(incoming);
            return;
        }

        if (endpointMatch is not null && identityMatch is not null && !ReferenceEquals(endpointMatch, identityMatch))
        {
            MergeDevice(endpointMatch, identityMatch);
            Devices.Remove(identityMatch);
            existing = endpointMatch;
        }

        MergeDevice(existing, incoming);
    }

    private static void MergeDevice(QuestDevice target, QuestDevice source)
    {
        if (target.Name is "" or "Quest headset" || target.Name.Equals(target.Model, StringComparison.OrdinalIgnoreCase))
        {
            target.Name = source.Name;
        }
        if (source.Identity.Length > 0)
        {
            target.Identity = source.Identity;
        }
        if (source.Model.Length > 0)
        {
            target.Model = source.Model;
        }
        if (source.UsbSerial.Length > 0)
        {
            target.UsbSerial = source.UsbSerial;
        }
        if (source.WifiSerial.Length > 0)
        {
            target.WifiSerial = source.WifiSerial;
            target.Host = source.Host;
            target.Port = source.Port;
        }
        if (source.MacAddress.Length > 0)
        {
            target.MacAddress = source.MacAddress;
        }
        if (source.BatteryPercent is not null)
        {
            target.BatteryPercent = source.BatteryPercent;
        }
        target.Status = source.Status;
        target.Detail = source.Detail;
        target.IsSelected = target.IsSelected || source.IsSelected;
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (QuestDevice device in e.NewItems)
            {
                SubscribeDevice(device);
            }
        }
        UpdateSummary();
    }

    private void SubscribeDevice(QuestDevice device)
    {
        device.PropertyChanged -= OnDevicePropertyChanged;
        device.PropertyChanged += OnDevicePropertyChanged;
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuestDevice.IsSelected) or nameof(QuestDevice.Status) or nameof(QuestDevice.IsConnected) or nameof(QuestDevice.HasUsb) or nameof(QuestDevice.HasWifi))
        {
            DevicesView.Refresh();
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        if (ReadyCountText is null || DeviceSummaryText is null || EmptyState is null || SelectAllBox is null)
        {
            return;
        }

        var visible = VisibleDevices().ToList();
        var connected = visible.Where(device => device.IsConnected).ToList();
        var selected = Devices.Count(device => device is { IsConnected: true, IsSelected: true });
        ReadyCountText.Text = $"{connected.Count} ready";
        DeviceSummaryText.Text = $"{visible.Count} in view | {selected} selected overall";
        EmptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _changingSelection = true;
        SelectAllBox.IsChecked = connected.Count > 0 && connected.All(device => device.IsSelected);
        _changingSelection = false;
        UpdateDeploymentSummary();
    }

    private IEnumerable<QuestDevice> VisibleDevices() => Devices.Where(DeviceMatchesActiveFilter);

    private bool DeviceMatchesActiveFilter(object value)
    {
        if (value is not QuestDevice device)
        {
            return false;
        }

        return _deviceFilterIndex switch
        {
            1 => device.HasWifi || device.HasKnownWifi,
            2 => device.HasUsb,
            _ => true
        };
    }

    private void UpdateDeploymentSummary()
    {
        if (DeployButton is null)
        {
            return;
        }

        var targets = Devices.Count(device => device is { IsConnected: true, IsSelected: true });
        var contentCount = DeploymentItems.Count + (string.IsNullOrWhiteSpace(ApkPathBox?.Text) ? 0 : 1);
        DeployButton.Content = targets == 0 ? "Deploy to selected headsets" : $"Deploy to {targets} headset{(targets == 1 ? "" : "s")}";
        if (!_isBusy)
        {
            DeployButton.IsEnabled = targets > 0 && contentCount > 0;
        }
        ActivityCountText.Text = contentCount == 0 ? "" : $"{contentCount} item{(contentCount == 1 ? "" : "s")}";
    }

    private async Task SaveKnownAsync()
    {
        await _store.SaveAsync(Devices
            .Where(device => device.Host.Length > 0)
            .Select(device => new SavedDevice(device.Name, device.Host, device.Port, device.Identity, device.MacAddress, device.Model)));
    }

    private async Task RunBusyActionAsync(
        string label,
        Func<CancellationToken, Task> action,
        bool showFailureDialog = true)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        DeployButton.IsEnabled = false;
        ActivityStateText.Text = label;
        ActivityDetailText.Text = "Please keep the computer and headsets connected.";
        DeploymentProgress.IsIndeterminate = true;
        SetFooter($"{label}...");
        try
        {
            using var source = new CancellationTokenSource();
            await action(source.Token);
        }
        catch (Exception exception)
        {
            ActivityStateText.Text = $"{label} failed";
            ActivityDetailText.Text = exception.Message;
            SetFooter($"{label} failed: {exception.Message}");
            if (showFailureDialog)
            {
                MessageBox.Show(this, exception.Message, label, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            DeploymentProgress.IsIndeterminate = false;
            _isBusy = false;
            UpdateSummary();
            if (_autoRefreshPending && !_windowLifetime.IsCancellationRequested)
            {
                _ = Dispatcher.InvokeAsync(RunPendingAutoRefreshAsync, DispatcherPriority.Background);
            }
        }
    }

    private void SetFooter(string message) =>
        FooterStatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";

    private static bool TrySplitEndpoint(string endpoint, out string host, out int port)
    {
        host = "";
        port = 0;
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || separator == endpoint.Length - 1 || !int.TryParse(endpoint[(separator + 1)..], out port))
        {
            return false;
        }
        host = endpoint[..separator];
        return true;
    }
}
