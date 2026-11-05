using System.Diagnostics;
using System.IO;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed class ScrcpyService
{
    private const double CorrectedEyeWidthRatio = 1792d / 2064d;
    private const int QuestPanelAngle = 22;

    public void StartCast(QuestDevice device, string serial, CastSettings settings, DisplaySize? displaySize)
    {
        if (string.IsNullOrWhiteSpace(serial) || !device.IsConnected)
        {
            throw new InvalidOperationException("Connect this headset by USB or remembered Wi-Fi before starting screen casting.");
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
        startInfo.ArgumentList.Add(serial);
        startInfo.ArgumentList.Add("--window-title");
        startInfo.ArgumentList.Add($"QuestDeploy - {device.Name} - {ViewLabel(settings.ViewMode)}");
        startInfo.ArgumentList.Add("--no-control");
        startInfo.ArgumentList.Add("--max-size=1920");
        startInfo.ArgumentList.Add("--max-fps=60");
        startInfo.ArgumentList.Add("--video-bit-rate=16M");
        startInfo.ArgumentList.Add("--capture-orientation=@");
        if (settings.ViewMode is not CastViewMode.BothEyes)
        {
            if (displaySize is not { } size)
            {
                throw new InvalidOperationException("A headset display size is required for one-eye casting.");
            }

            var eyeWidth = MakeEven(size.Width / 2);
            var cropWidth = settings.CorrectLensTilt
                ? MakeEven((int)Math.Floor(eyeWidth * CorrectedEyeWidthRatio))
                : eyeWidth;
            var cropHeight = MakeEven(Math.Min(size.Height, cropWidth * 9 / 16));
            var eyeStart = settings.ViewMode is CastViewMode.RightEye ? eyeWidth : 0;
            var x = MakeEven(eyeStart + (eyeWidth - cropWidth) / 2);
            var y = MakeEven((size.Height - cropHeight) / 2);
            startInfo.ArgumentList.Add($"--crop={cropWidth}:{cropHeight}:{x}:{y}");
            if (settings.CorrectLensTilt)
            {
                var angle = settings.ViewMode is CastViewMode.RightEye ? -QuestPanelAngle : QuestPanelAngle;
                startInfo.ArgumentList.Add($"--angle={angle}");
            }
        }
        if (settings.Fullscreen)
        {
            startInfo.ArgumentList.Add("--fullscreen");
        }
        else
        {
            startInfo.ArgumentList.Add("--window-width=1280");
            startInfo.ArgumentList.Add("--window-height=720");
        }
        if (settings.AlwaysOnTop)
        {
            startInfo.ArgumentList.Add("--always-on-top");
        }

        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "branding");
        if (File.Exists(Path.Combine(iconDirectory, "scrcpy.png")))
        {
            startInfo.Environment["SCRCPY_ICON_DIR"] = iconDirectory;
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
