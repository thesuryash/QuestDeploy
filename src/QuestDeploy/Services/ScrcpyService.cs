using System.Diagnostics;
using System.IO;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed class ScrcpyService
{
    private const double CorrectedEyeWidthRatio = 1792d / 2064d;
    private const int QuestPanelAngle = 22;

    public async Task StartCastAsync(
        QuestDevice device,
        string serial,
        CastSettings settings,
        DisplaySize? displaySize,
        string adbPath,
        Action<string> reportFailure)
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
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            startInfo.Environment["ADB"] = adbPath;
        }
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

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("The casting window could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(2))) == exitTask)
        {
            await exitTask;
            var failure = CastFailure(process.ExitCode, await outputTask, await errorTask);
            process.Dispose();
            throw new InvalidOperationException(failure);
        }

        _ = ReportLaterFailureAsync(process, exitTask, outputTask, errorTask, reportFailure);
    }

    private static async Task ReportLaterFailureAsync(
        Process process,
        Task exitTask,
        Task<string> outputTask,
        Task<string> errorTask,
        Action<string> reportFailure)
    {
        try
        {
            await exitTask;
            if (process.ExitCode != 0)
            {
                reportFailure(CastFailure(process.ExitCode, await outputTask, await errorTask));
            }
        }
        catch (Exception exception)
        {
            reportFailure($"Could not monitor the casting window: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string CastFailure(int exitCode, string output, string error)
    {
        var detail = string.Join(Environment.NewLine,
            new[] { error.Trim(), output.Trim() }.Where(value => value.Length > 0));
        if (detail.Length > 1800)
        {
            detail = detail[^1800..];
        }
        return detail.Length == 0
            ? $"The casting component exited with code {exitCode} before showing a video window."
            : $"The casting component exited with code {exitCode}:{Environment.NewLine}{detail}";
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
