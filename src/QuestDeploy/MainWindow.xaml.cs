using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuestDeploy.Models;
using QuestDeploy.Services;

namespace QuestDeploy;

public partial class MainWindow : Window
{
    private readonly AdbService _adb = new();
    private readonly DeviceStore _store = new();
    private readonly WindowsWifiProfileService _wifiProfiles = new();
    private readonly NetworkDiscoveryService _networkDiscovery = new();

    public ObservableCollection<QuestDevice> Devices { get; } = [];
    public ObservableCollection<WifiProfile> WifiProfiles { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        WifiProfilesBox.ItemsSource = WifiProfiles;
        Devices.CollectionChanged += OnDevicesCollectionChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _adb.TryLocateAdb();
        AdbStatusText.Text = _adb.AdbPath.Length == 0
            ? "ADB not found. Install Android SDK Platform Tools or set QUEST_DEPLOY_ADB."
            : $"ADB: {_adb.AdbPath}";

        foreach (var saved in await _store.LoadAsync())
        {
            Devices.Add(new QuestDevice
            {
                Name = saved.Name,
                Host = saved.Host,
                Port = saved.Port,
                Serial = $"{saved.Host}:{saved.Port}",
                Transport = "Wi-Fi",
                Status = "Known"
            });
        }

        UpdateSummary();
        await RefreshDevicesAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

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
        }
    }

    private async void OnConnectManual(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Connect headset", async token =>
        {
            var port = ReadPort();
            var endpoint = await _adb.ConnectAsync(HostBox.Text, port, token);
            UpsertDevice(new QuestDevice
            {
                Name = string.IsNullOrWhiteSpace(DeviceNameBox.Text) ? "Quest headset" : DeviceNameBox.Text.Trim(),
                Host = HostBox.Text.Trim(),
                Port = port,
                Serial = endpoint,
                Transport = "Wi-Fi",
                Status = "Connected",
                IsSelected = true
            });
            await SaveKnownAsync();
            await RefreshDevicesAsync();
        });
    }

    private async void OnRememberManual(object sender, RoutedEventArgs e)
    {
        var port = ReadPort();
        UpsertDevice(new QuestDevice
        {
            Name = string.IsNullOrWhiteSpace(DeviceNameBox.Text) ? "Quest headset" : DeviceNameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Port = port,
            Serial = $"{HostBox.Text.Trim()}:{port}",
            Transport = "Wi-Fi",
            Status = "Known"
        });
        await SaveKnownAsync();
    }

    private async void OnClearRemembered(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Clear remembered Wi-Fi endpoints and network candidates from this app?",
            "Clear remembered devices",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _store.ClearAsync();
        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            var device = Devices[index];
            if (device.Transport is "Wi-Fi" or "Candidate" &&
                device.Status is not "Connected")
            {
                Devices.RemoveAt(index);
            }
        }
        UpdateSummary();
        AppendLog("Cleared remembered endpoints and stale candidates.");
    }

    private async void OnFindHotspotClients(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Find hotspot clients", async token =>
        {
            var addresses = await _networkDiscovery.GetNeighborIpv4AddressesAsync(token);
            for (var index = Devices.Count - 1; index >= 0; index--)
            {
                if (Devices[index].Transport == "Candidate")
                {
                    Devices.RemoveAt(index);
                }
            }

            var added = 0;
            foreach (var address in addresses)
            {
                if (Devices.Any(device => string.Equals(device.Host, address, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var candidate = new QuestDevice
                {
                    Name = "Network client",
                    Host = address,
                    Port = 5555,
                    Serial = $"{address}:5555",
                    Transport = "Candidate",
                    Status = "Network only",
                    Detail = "ADB not connected"
                };
                Devices.Add(candidate);
                SubscribeDevice(candidate);
                added++;
            }
            UpdateSummary();
            AppendLog($"Found {addresses.Count} network clients; added {added} new candidate rows.");
        });
    }

    private async void OnConnectSelectedCandidate(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not QuestDevice { Host.Length: > 0 } device)
        {
            MessageBox.Show(this, "Select a network candidate or Wi-Fi row first.", "No IP selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync("Connect selected IP", async token =>
        {
            var endpoint = await _adb.ConnectAsync(device.Host, device.Port, token);
            device.Serial = endpoint;
            device.Transport = "Wi-Fi";
            device.Status = "Connected";
            device.Detail = "";
            device.IsSelected = true;
            device.NotifyEndpointChanged();
            UpdateSummary();
            await SaveKnownAsync();
            await RefreshDevicesAsync();
        });
    }

    private async void OnReconnectKnown(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Reconnect known devices", async token =>
        {
            foreach (var device in Devices.Where(device => device.Transport == "Wi-Fi").ToList())
            {
                try
                {
                    await _adb.ConnectAsync(device.Host, device.Port, token);
                    device.Status = "Connected";
                    device.Detail = "";
                }
                catch (Exception exception)
                {
                    device.Status = "Failed";
                    device.Detail = exception.Message;
                }
            }

            await RefreshDevicesAsync();
        });
    }

    private async void OnEnableWifiFromUsb(object sender, RoutedEventArgs e)
    {
        if (GetUsbBootstrapDevice() is not { } device)
        {
            MessageBox.Show(
                this,
                "No connected USB headset was found. Plug the headset in with USB, accept the debugging prompt inside the headset, then click Refresh devices.",
                "USB headset required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync("Enable Wi-Fi ADB", async token =>
        {
            var endpoint = await _adb.EnableWifiAdbFromUsbAsync(device.Serial, 5555, token);
            AppendLog($"Enabled Wi-Fi ADB for {device.Name}: {endpoint}");
            await RefreshDevicesAsync();
        });
    }

    private async void OnDetectSelected(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Select a connected headset first.", "No headset selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync("Detect headset", async token =>
        {
            device.Status = "Detecting";
            await _adb.PlayDetectSoundAsync(device.Serial, token);
            device.Status = "Connected";
            AppendLog($"Sent detect sound to {device.Name}.");
        });
    }

    private async void OnLoadWifiProfiles(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Load PC Wi-Fi networks", async token =>
        {
            WifiProfiles.Clear();
            foreach (var profile in await _wifiProfiles.GetProfilesAsync(token))
            {
                WifiProfiles.Add(profile);
            }
            if (WifiProfiles.Count > 0)
            {
                WifiProfilesBox.SelectedIndex = 0;
            }
        });
    }

    private async void OnProvisionWifi(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Select the headset to configure.", "No headset selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (WifiProfilesBox.SelectedItem is not WifiProfile profile)
        {
            MessageBox.Show(this, "Load and select a PC Wi-Fi profile first.", "No network selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var security = (WifiSecurityBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "wpa2";
        await RunUiActionAsync("Send Wi-Fi network", async token =>
        {
            device.Status = "Configuring Wi-Fi";
            device.Detail = profile.Name;
            await _adb.ProvisionWifiNetworkAsync(
                device.Serial,
                profile.Name,
                security,
                WifiPasswordBox.Password,
                HiddenNetworkBox.IsChecked == true,
                token);
            device.Status = "Connected";
            device.Detail = $"Sent network {profile.Name}";
        });
    }

    private void OnBrowseCertificate(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Certificates and configs (*.cer;*.crt;*.pem;*.p12;*.pfx;*.mobileconfig;*.json;*.xml)|*.cer;*.crt;*.pem;*.p12;*.pfx;*.mobileconfig;*.json;*.xml|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            CertificatePathBox.Text = dialog.FileName;
        }
    }

    private async void OnStageCertificate(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Select the headset to receive the file.", "No headset selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync("Stage certificate/config", async token =>
        {
            device.Status = "Staging file";
            await _adb.StageFileAsync(device.Serial, CertificatePathBox.Text, "/sdcard/Download/QuestDeploy", token);
            device.Status = "Connected";
            device.Detail = "Staged in Download/QuestDeploy";
        });
    }

    private async void OnOpenWifiSettings(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Select the headset first.", "No headset selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunUiActionAsync("Open Wi-Fi settings", async token =>
        {
            await _adb.OpenWifiSettingsAsync(device.Serial, token);
        });
    }

    private async void OnInstallSelected(object sender, RoutedEventArgs e) =>
        await InstallAsync(Devices.Where(device => device.IsSelected).ToList());

    private async void OnInstallAll(object sender, RoutedEventArgs e) =>
        await InstallAsync(Devices.Where(device => device.Transport == "Wi-Fi" && device.Status == "Connected").ToList());

    private async Task InstallAsync(IReadOnlyList<QuestDevice> targets)
    {
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select at least one connected Wi-Fi headset.", "No targets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parallelism = ReadParallelism();
        await RunUiActionAsync("Install APK", async token =>
        {
            using var gate = new SemaphoreSlim(Math.Min(parallelism, targets.Count));
            var tasks = targets.Select(async device =>
            {
                await gate.WaitAsync(token);
                try
                {
                    device.Status = "Installing";
                    device.Detail = "";
                    await _adb.InstallApkAsync(device.Serial, ApkPathBox.Text, ReplaceExistingBox.IsChecked == true, token);
                    if (LaunchAfterInstallBox.IsChecked == true && !string.IsNullOrWhiteSpace(PackageBox.Text))
                    {
                        device.Status = "Launching";
                        await _adb.LaunchPackageAsync(device.Serial, PackageBox.Text.Trim(), token);
                    }
                    device.Status = "Done";
                    AppendLog($"{device.Name} installed successfully.");
                }
                catch (Exception exception)
                {
                    device.Status = "Failed";
                    device.Detail = exception.Message;
                    AppendLog($"{device.Name} failed: {exception.Message}");
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
        });
    }

    private async Task RefreshDevicesAsync()
    {
        await RunUiActionAsync("Refresh devices", async token =>
        {
            var discovered = await _adb.GetDevicesAsync(token);
            var discoveredSerials = discovered
                .Select(device => device.Serial)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var index = Devices.Count - 1; index >= 0; index--)
            {
                var device = Devices[index];
                if (device.Transport == "USB" &&
                    !discoveredSerials.Contains(device.Serial))
                {
                    Devices.RemoveAt(index);
                }
            }

            foreach (var device in discovered)
            {
                UpsertDevice(device);
            }

            foreach (var known in Devices.Where(device => device.Transport == "Wi-Fi").ToList())
            {
                if (discovered.All(device => !string.Equals(device.Serial, known.Serial, StringComparison.OrdinalIgnoreCase)) &&
                    known.Status == "Connected")
                {
                    known.Status = "Known";
                }
            }
            UpdateSummary();
        }, showFailureDialog: false);
    }

    private QuestDevice? GetSelectedDevice() =>
        DevicesGrid.SelectedItem as QuestDevice ??
        Devices.FirstOrDefault(device => device.IsSelected && device.Status == "Connected") ??
        Devices.FirstOrDefault(device => device.Status == "Connected");

    private QuestDevice? GetUsbBootstrapDevice()
    {
        if (DevicesGrid.SelectedItem is QuestDevice { Transport: "USB", Status: "Connected" } selectedUsb)
        {
            return selectedUsb;
        }

        var checkedUsbDevices = Devices
            .Where(device => device is { Transport: "USB", Status: "Connected", IsSelected: true })
            .ToList();
        if (checkedUsbDevices.Count == 1)
        {
            return checkedUsbDevices[0];
        }

        var connectedUsbDevices = Devices
            .Where(device => device is { Transport: "USB", Status: "Connected" })
            .ToList();
        return connectedUsbDevices.Count == 1 ? connectedUsbDevices[0] : null;
    }

    private void UpsertDevice(QuestDevice incoming)
    {
        var existing = Devices.FirstOrDefault(device =>
            string.Equals(device.Serial, incoming.Serial, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(incoming.Host) && string.Equals(device.Host, incoming.Host, StringComparison.OrdinalIgnoreCase) && device.Port == incoming.Port));

        if (existing is null)
        {
            Devices.Add(incoming);
            SubscribeDevice(incoming);
            UpdateSummary();
            return;
        }

        existing.Name = string.IsNullOrWhiteSpace(existing.Name) ? incoming.Name : existing.Name;
        existing.Serial = incoming.Serial;
        existing.Host = incoming.Host;
        existing.Port = incoming.Port;
        existing.Transport = incoming.Transport;
        existing.Status = incoming.Status;
        existing.Detail = incoming.Detail;
        existing.IsSelected = existing.IsSelected || incoming.IsSelected;
        existing.NotifyEndpointChanged();
        UpdateSummary();
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

    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuestDevice.IsSelected) or nameof(QuestDevice.Transport) or nameof(QuestDevice.Status))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        UsbCountText.Text = Devices.Count(device => device is { Transport: "USB", Status: "Connected" }).ToString();
        WifiCountText.Text = Devices.Count(device => device is { Transport: "Wi-Fi", Status: "Connected" }).ToString();
        CandidateCountText.Text = Devices.Count(device => device.Transport == "Candidate").ToString();
        SelectedCountText.Text = Devices.Count(device => device.IsSelected).ToString();
    }

    private async Task SaveKnownAsync()
    {
        await _store.SaveAsync(Devices
            .Where(device => device.Transport == "Wi-Fi" && !string.IsNullOrWhiteSpace(device.Host))
            .Select(device => new SavedDevice(device.Name, device.Host, device.Port)));
    }

    private async Task RunUiActionAsync(string label, Func<CancellationToken, Task> action, bool showFailureDialog = true)
    {
        try
        {
            AppendLog($"{label}...");
            using var source = new CancellationTokenSource();
            await action(source.Token);
            AppendLog($"{label} complete.");
        }
        catch (Exception exception)
        {
            AppendLog($"{label} failed: {exception.Message}");
            if (showFailureDialog)
            {
                MessageBox.Show(this, exception.Message, label, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private int ReadPort()
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("Port must be a number from 1 to 65535.");
        }
        return port;
    }

    private int ReadParallelism()
    {
        if (!int.TryParse(ParallelismBox.Text, out var value) || value is < 1 or > 16)
        {
            throw new ArgumentException("Parallel limit must be a number from 1 to 16.");
        }
        return value;
    }

    private void AppendLog(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
