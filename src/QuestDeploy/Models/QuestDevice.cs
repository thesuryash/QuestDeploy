using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestDeploy.Models;

public sealed class QuestDevice : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _name = "Quest headset";
    private string _identity = "";
    private string _model = "Quest headset";
    private string _host = "";
    private int _port = 5555;
    private string _usbSerial = "";
    private string _wifiSerial = "";
    private string _macAddress = "";
    private int? _batteryPercent;
    private string _status = "Remembered";
    private string _detail = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Quest headset" : value.Trim());
    }

    public string Identity
    {
        get => _identity;
        set => SetField(ref _identity, value?.Trim() ?? "");
    }

    public string Model
    {
        get => _model;
        set
        {
            if (SetField(ref _model, string.IsNullOrWhiteSpace(value) ? "Quest headset" : value.Trim()))
            {
                OnPropertyChanged(nameof(DeviceFacts));
            }
        }
    }

    public string Host
    {
        get => _host;
        set
        {
            if (SetField(ref _host, value?.Trim() ?? ""))
            {
                NotifyConnectionChanged();
            }
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (SetField(ref _port, value))
            {
                NotifyConnectionChanged();
            }
        }
    }

    public string UsbSerial
    {
        get => _usbSerial;
        set
        {
            if (SetField(ref _usbSerial, value?.Trim() ?? ""))
            {
                NotifyConnectionChanged();
            }
        }
    }

    public string WifiSerial
    {
        get => _wifiSerial;
        set
        {
            if (SetField(ref _wifiSerial, value?.Trim() ?? ""))
            {
                NotifyConnectionChanged();
            }
        }
    }

    public string MacAddress
    {
        get => _macAddress;
        set
        {
            if (SetField(ref _macAddress, value?.Trim().ToUpperInvariant() ?? ""))
            {
                OnPropertyChanged(nameof(MacDisplay));
            }
        }
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (SetField(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(DeviceFacts));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value ?? ""))
            {
                NotifyConnectionChanged();
            }
        }
    }

    public string Detail
    {
        get => _detail;
        set => SetField(ref _detail, value ?? "");
    }

    public bool HasUsb => UsbSerial.Length > 0;
    public bool HasWifi => WifiSerial.Length > 0;
    public bool HasKnownWifi => Host.Length > 0;
    public bool IsConnected => (HasUsb || HasWifi) &&
        !Status.Equals("unauthorized", StringComparison.OrdinalIgnoreCase) &&
        !Status.Equals("offline", StringComparison.OrdinalIgnoreCase);
    public bool CanUseUsbTools => HasUsb && IsConnected;
    public bool CanEnableWifi => CanUseUsbTools && !HasWifi;
    public bool CanForgetWifi => HasKnownWifi;
    public string PreferredSerial => HasUsb ? UsbSerial : WifiSerial;
    public string Transport => HasUsb && HasWifi ? "USB + Wi-Fi" : HasUsb ? "USB" : HasWifi ? "Wi-Fi" : "Remembered";
    public string Endpoint => HasUsb && HasWifi ? $"USB + {Host}:{Port}" : HasUsb ? "USB cable" : Host.Length > 0 ? $"{Host}:{Port}" : "Not connected";
    public string MacDisplay => MacAddress.Length > 0 ? MacAddress : "Unavailable";
    public string DeviceFacts => string.Join("  |  ", new[]
    {
        Model,
        BatteryPercent is null ? "" : $"Battery {BatteryPercent}%",
        Host.Length == 0 ? "" : Host
    }.Where(value => value.Length > 0));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ResetLiveConnections()
    {
        UsbSerial = "";
        WifiSerial = "";
        Status = "Remembered";
        Detail = "";
    }

    public void NotifyEndpointChanged() => NotifyConnectionChanged();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void NotifyConnectionChanged()
    {
        OnPropertyChanged(nameof(HasUsb));
        OnPropertyChanged(nameof(HasWifi));
        OnPropertyChanged(nameof(HasKnownWifi));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanUseUsbTools));
        OnPropertyChanged(nameof(CanEnableWifi));
        OnPropertyChanged(nameof(CanForgetWifi));
        OnPropertyChanged(nameof(PreferredSerial));
        OnPropertyChanged(nameof(Transport));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(DeviceFacts));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
