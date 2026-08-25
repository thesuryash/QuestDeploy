using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuestDeploy.Services;

public sealed partial class NetworkDiscoveryService
{
    public async Task<IReadOnlyList<string>> GetNeighborIpv4AddressesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("arp.exe", ["-a"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not read network neighbors: {result.Combined}");
        }

        return result.StandardOutput
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Ipv4Pattern().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["ip"].Value)
            .Where(IsUsableClientAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static ip => IPAddress.Parse(ip).GetAddressBytes(), ByteArrayComparer.Instance)
            .ToList();
    }

    private static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static bool IsUsableClientAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes is [not 0, _, _, not 0 and not 255] &&
            bytes[0] is not 127 and not 169 and not >= 224;
    }

    [GeneratedRegex(@"\b(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Pattern();

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Combined => string.Join(" ", new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }

            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                var comparison = x[i].CompareTo(y[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
