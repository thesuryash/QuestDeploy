using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestDeploy.Models;

public sealed class QuestDevice : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status = "Known";
    private string _detail = "";

    public string Name { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5555;
    public string Transport { get; set; } = "Wi-Fi";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }

    public string Endpoint => string.IsNullOrWhiteSpace(Host) ? Serial : $"{Host}:{Port}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyEndpointChanged()
    {
        OnPropertyChanged(nameof(Endpoint));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
