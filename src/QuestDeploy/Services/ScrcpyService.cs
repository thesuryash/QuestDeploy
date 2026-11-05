using System.Diagnostics;
using System.IO;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed class ScrcpyService
{
    public void StartUsbCast(QuestDevice device, CastViewMode viewMode, DisplaySize? displaySize)
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
        startInfo.ArgumentList.Add($"QuestDeploy - {device.Name} - {ViewLabel(viewMode)}");
        startInfo.ArgumentList.Add("--no-control");
        startInfo.ArgumentList.Add("--max-size=1920");
        if (viewMode is not CastViewMode.BothEyes)
        {
            if (displaySize is not { } size)
            {
                throw new InvalidOperationException("A headset display size is required for one-eye casting.");
            }

            var cropWidth = MakeEven(size.Width / 2);
            var cropHeight = MakeEven(size.Height);
            var x = viewMode is CastViewMode.RightEye ? MakeEven(size.Width - cropWidth) : 0;
            startInfo.ArgumentList.Add($"--crop={cropWidth}:{cropHeight}:{x}:0");
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("The casting window could not be started.");
        }
    }

    private static int MakeEven(int value) => value - value % 2;

    private static string ViewLabel(CastViewMode mode) => mode switch
    {
        CastViewMode.RightEye => "Right eye",
        CastViewMode.BothEyes => "Both eyes",
        _ => "Left eye"
    };

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
