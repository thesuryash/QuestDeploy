using System.Diagnostics;
using System.IO;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed class ScrcpyService
{
    public void StartUsbCast(QuestDevice device)
    {
        if (!device.HasUsb)
        {
            throw new InvalidOperationException("Connect this headset by USB before starting screen casting.");
        }

        var scrcpyPath = LocateScrcpy() ?? throw new FileNotFoundException(
            "The screen-casting component was not found. Reinstall QuestDeploy, install scrcpy from its official GitHub release, or set QUEST_DEPLOY_SCRCPY to scrcpy.exe.");
        var startInfo = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            WorkingDirectory = Path.GetDirectoryName(scrcpyPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--serial");
        startInfo.ArgumentList.Add(device.UsbSerial);
        startInfo.ArgumentList.Add("--window-title");
        startInfo.ArgumentList.Add($"QuestDeploy - {device.Name}");
        startInfo.ArgumentList.Add("--no-control");
        startInfo.ArgumentList.Add("--max-size=1920");

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("The casting window could not be started.");
        }
    }

    private static string? LocateScrcpy()
    {
        var candidates = new List<string?>
        {
            Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe"),
            Path.Combine(AppContext.BaseDirectory, "scrcpy.exe"),
            Environment.GetEnvironmentVariable("QUEST_DEPLOY_SCRCPY")
        };
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator).Select(entry => Path.Combine(entry.Trim(), "scrcpy.exe")));
        }

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }
}
