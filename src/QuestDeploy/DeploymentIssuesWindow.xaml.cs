using System.Collections.ObjectModel;
using System.Windows;
using QuestDeploy.Models;

namespace QuestDeploy;

public partial class DeploymentIssuesWindow : Window
{
    public ObservableCollection<DeploymentIssue> Issues { get; }

    public DeploymentIssuesWindow(IEnumerable<DeploymentIssue> issues)
    {
        Issues = new ObservableCollection<DeploymentIssue>(issues);
        InitializeComponent();
        DataContext = this;
        SummaryText.Text = $"{Issues.Count} headset{(Issues.Count == 1 ? "" : "s")} reported an error. Follow the matching recovery steps, then retry only the affected devices.";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
