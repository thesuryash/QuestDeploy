using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QuestDeploy;

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        var foreground = string.Equals(parameter?.ToString(), "Foreground", StringComparison.OrdinalIgnoreCase);
        return new SolidColorBrush(ColorFromHex(foreground ? Foreground(status) : Background(status)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Background(string status) =>
        status.ToLowerInvariant() switch
        {
            "connected" or "done" or "complete" => "#E4F6E9",
            "failed" => "#FDE7E9",
            "installing" or "installing app" or "launching" or "opening app" or "configuring wi-fi" or "setting up wi-fi" or "staging file" or "copying files" or "detecting" or "identifying" or "connecting" => "#E8F1FF",
            "network only" => "#FFF4CE",
            _ => "#EEF1F5"
        };

    private static string Foreground(string status) =>
        status.ToLowerInvariant() switch
        {
            "connected" or "done" or "complete" => "#107C41",
            "failed" => "#A4262C",
            "installing" or "installing app" or "launching" or "opening app" or "configuring wi-fi" or "setting up wi-fi" or "staging file" or "copying files" or "detecting" or "identifying" or "connecting" => "#115EA3",
            "network only" => "#8A6100",
            _ => "#4F5B67"
        };

    private static Color ColorFromHex(string value) =>
        (Color)ColorConverter.ConvertFromString(value);
}

public sealed class TransportBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var transport = value?.ToString() ?? "";
        var foreground = string.Equals(parameter?.ToString(), "Foreground", StringComparison.OrdinalIgnoreCase);
        return new SolidColorBrush(ColorFromHex(foreground ? Foreground(transport) : Background(transport)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Background(string transport) =>
        transport.ToLowerInvariant() switch
        {
            "usb" => "#EDEBFF",
            "wi-fi" => "#E2F2FF",
            "candidate" => "#FFF4CE",
            _ => "#EEF1F5"
        };

    private static string Foreground(string transport) =>
        transport.ToLowerInvariant() switch
        {
            "usb" => "#5B3DB2",
            "wi-fi" => "#0067B8",
            "candidate" => "#8A6100",
            _ => "#4F5B67"
        };

    private static Color ColorFromHex(string value) =>
        (Color)ColorConverter.ConvertFromString(value);
}
