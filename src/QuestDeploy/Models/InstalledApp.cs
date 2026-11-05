namespace QuestDeploy.Models;

public sealed record InstalledApp(string PackageName, string ApkPath, bool IsSystem)
{
    public string AppType => IsSystem ? "System" : "Installed";
}
