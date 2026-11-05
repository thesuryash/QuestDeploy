using System.Windows;
using QuestDeploy.Models;

namespace QuestDeploy;

public partial class CastOptionsWindow : Window
{
    public CastOptionsWindow(QuestDevice device)
    {
        InitializeComponent();
        DeviceText.Text = device.Name;
    }

    public CastViewMode SelectedMode => RightEyeOption.IsChecked == true
        ? CastViewMode.RightEye
        : BothEyesOption.IsChecked == true
            ? CastViewMode.BothEyes
            : CastViewMode.LeftEye;

    private void OnStartCasting(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
