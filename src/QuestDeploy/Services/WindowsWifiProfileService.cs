using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed partial class WindowsWifiProfileService
{
    public async Task<IReadOnlyList<WifiProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var result = await RunNetshAsync(["wlan", "show", "profiles"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not read Windows Wi-Fi profiles: {result.Combined}");
        }

        return result.StandardOutput
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ProfileLinePattern().Match(line))
            .Where(match => match.Success)
            .Select(match => new WifiProfile(match.Groups["name"].Value.Trim()))
            .DistinctBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<CommandResult> RunNetshAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start netsh.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, await outputTask, await errorTask);
    }

    [GeneratedRegex(@"^\s*(?:All User Profile|User Profile)\s*:\s*(?<name>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileLinePattern();

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Combined => string.Join(" ", new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }
}
