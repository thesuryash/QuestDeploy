using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using QuestDeploy.Models;
using QuestDeploy.Services;

namespace QuestDeploy;

public partial class InstalledAppsWindow : Window
{
    private readonly AdbService _adb;
    private readonly QuestDevice _device;
    private readonly List<InstalledApp> _allApps = [];
    private bool _loading;

    public ObservableCollection<InstalledApp> Apps { get; } = [];

    public InstalledAppsWindow(AdbService adb, QuestDevice device)
    {
        _adb = adb;
        _device = device;
        InitializeComponent();
        DataContext = this;
        TitleText.Text = $"Apps on {device.Name}";
        DeviceText.Text = $"USB device {device.UsbSerial}";
        Loaded += async (_, _) => await LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Reading installed applications...";
        try
        {
            var apps = await _adb.GetInstalledAppsAsync(_device.UsbSerial, IncludeSystemBox.IsChecked == true, CancellationToken.None);
            _allApps.Clear();
            _allApps.AddRange(apps);
            ApplyFilter();
            StatusText.Text = $"{Apps.Count} app{(Apps.Count == 1 ? "" : "s")} shown";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not read applications.";
            MessageBox.Show(this, exception.Message, "Installed apps", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchBox?.Text?.Trim() ?? "";
        Apps.Clear();
        foreach (var app in _allApps.Where(app =>
                     filter.Length == 0 || app.PackageName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Apps.Add(app);
        }
        if (!_loading && StatusText is not null)
        {
            StatusText.Text = $"{Apps.Count} of {_allApps.Count} apps shown";
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnIncludeSystemChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            await LoadAppsAsync();
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAppsAsync();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
