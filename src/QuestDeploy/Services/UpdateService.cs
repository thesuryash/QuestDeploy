using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuestDeploy.Services;

public sealed partial class UpdateService
{
    private const string ManifestUrl = "https://github.com/thesuryash/QuestDeploy/releases/latest/download/QuestDeploy-update.json";
    private const string AllowedDownloadPrefix = "/thesuryash/QuestDeploy/releases/download/";
    private static readonly HttpClient Client = CreateClient();

    public async Task<AvailableUpdate?> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("The update manifest was empty.");

        if (!Version.TryParse(manifest.Version, out var availableVersion) || availableVersion.Build < 0)
        {
            throw new InvalidOperationException("The update manifest contains an invalid version.");
        }
        if (availableVersion <= currentVersion)
        {
            return null;
        }

        var installerUri = ValidateInstallerUri(manifest.InstallerUrl);
        var sha256 = manifest.Sha256?.Trim().ToUpperInvariant() ?? "";
        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new InvalidOperationException("The update manifest contains an invalid SHA-256 value.");
        }

        return new AvailableUpdate(availableVersion, installerUri, sha256, manifest.ReleaseNotesUrl ?? "");
    }

    public async Task<string> DownloadInstallerAsync(
        AvailableUpdate update,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading the installer...");
        var versionDirectory = Path.Combine(
            Path.GetTempPath(),
            "QuestDeploy",
            "Updates",
            update.Version.ToString(3));
        var updateDirectory = Path.Combine(versionDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, $"QuestDeploy-v{update.Version.ToString(3)}-Setup.exe");
        var partialPath = installerPath + ".download";

        using var response = await Client.GetAsync(update.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_000_000_000)
        {
            throw new InvalidOperationException("The update installer is unexpectedly large.");
        }

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            partialPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        progress?.Report("Verifying the installer...");
        string actualHash;
        await using (var installerStream = new FileStream(
            partialPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken));
        }

        if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidOperationException("The downloaded installer did not match the published release hash.");
        }

        // Close the verification stream before the atomic rename. Windows will not
        // rename an open file that was not shared for deletion.
        // A unique attempt directory also prevents a still-running prior installer
        // from locking the destination used by a new update attempt.
        File.Move(partialPath, installerPath);
        return installerPath;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuestDeploy-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, application/octet-stream");
        return client;
    }

    private static Uri ValidateInstallerUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(AllowedDownloadPrefix, StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The update manifest points to an untrusted installer location.");
        }

        return uri;
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed record UpdateManifest(
        string Version,
        string InstallerUrl,
        string Sha256,
        string? ReleaseNotesUrl);
}

public sealed record AvailableUpdate(
    Version Version,
    Uri InstallerUri,
    string Sha256,
    string ReleaseNotesUrl);
