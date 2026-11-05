using System.Windows;
using QuestDeploy.Models;

namespace QuestDeploy;

public partial class CastOptionsWindow : Window
{
    public CastOptionsWindow(QuestDevice device)
    {
        InitializeComponent();
        DeviceText.Text = $"{device.Name} · {device.Model}";
        if (device.HasUsb)
        {
            ConnectionBox.Items.Add(new CastConnectionChoice("USB cable (fastest)", device.UsbSerial, "USB"));
        }
        if (device.HasWifi)
        {
            ConnectionBox.Items.Add(new CastConnectionChoice(
                device.Host.Length > 0 ? $"Wi-Fi · {device.Host}:{device.Port}" : "Wi-Fi",
                device.WifiSerial,
                "Wi-Fi"));
        }
        ConnectionBox.SelectedIndex = 0;
        CorrectLensTiltBox.IsChecked = HasAngledEyePanels(device.Model);
    }

    public string SelectedSerial =>
        (ConnectionBox.SelectedItem as CastConnectionChoice)?.Serial ??
        throw new InvalidOperationException("Choose a connected casting transport.");

    public string SelectedTransportLabel =>
        (ConnectionBox.SelectedItem as CastConnectionChoice)?.TransportLabel ?? "connection";

    public CastSettings Settings => new(
        RightEyeOption.IsChecked == true
            ? CastViewMode.RightEye
            : BothEyesOption.IsChecked == true
                ? CastViewMode.BothEyes
                : CastViewMode.LeftEye,
        FullscreenOption.IsChecked == true,
        AlwaysOnTopBox.IsChecked == true,
        CorrectLensTiltBox.IsChecked == true);

    private void OnStartCasting(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static bool HasAngledEyePanels(string model) =>
        (model.Contains("Quest 3", StringComparison.OrdinalIgnoreCase) &&
         !model.Contains("Quest 3S", StringComparison.OrdinalIgnoreCase)) ||
        model.Contains("Quest Pro", StringComparison.OrdinalIgnoreCase);

    private sealed record CastConnectionChoice(string Label, string Serial, string TransportLabel);
}
