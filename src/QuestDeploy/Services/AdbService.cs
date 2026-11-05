using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed partial class AdbService
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FileTransferTimeout = TimeSpan.FromHours(1);

    public string AdbPath { get; private set; } = "";

    public bool TryLocateAdb(string? explicitPath = null)
    {
        AdbPath = LocateAdb(explicitPath) ?? "";
        return AdbPath.Length > 0;
    }

    public async Task<IReadOnlyList<QuestDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        EnsureAdb();
        var result = await RunAdbAsync(["devices", "-l"], ShortTimeout, cancellationToken);
        EnsureSuccess(result, "ADB device discovery");

        var devices = new List<QuestDevice>();
        foreach (var line in result.StandardOutput.ReplaceLineEndings("\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = parts[1];
            var isWifi = TrySplitEndpoint(serial, out var host, out var port);
            var model = ReadModelLabel(parts);
            devices.Add(new QuestDevice
            {
                Name = model,
                Model = model,
                Host = isWifi ? host : "",
                Port = port == 0 ? 5555 : port,
                UsbSerial = isWifi ? "" : serial,
                WifiSerial = isWifi ? serial : "",
                Status = state == "device" ? "Connected" : state,
                IsSelected = state == "device"
            });
        }

        await Task.WhenAll(devices
            .Where(device => device.Status == "Connected")
            .Select(device => PopulateDeviceFactsAsync(device, cancellationToken)));

        return devices;
    }

    public async Task<string> EnableWifiAdbFromUsbAsync(string usbSerial, int port, CancellationToken cancellationToken)
    {
        RequireSerial(usbSerial);
        if (usbSerial.Contains(':'))
        {
            throw new ArgumentException("Select a USB-connected headset to enable Wi-Fi ADB.");
        }

        var identity = await ReadDeviceIdentityAsync(usbSerial, cancellationToken);
        var route = await RunForDeviceAsync(usbSerial, ["shell", "ip route"], ShortTimeout, cancellationToken);
        EnsureSuccess(route, "Read headset Wi-Fi address");
        var host = ParseWifiAddress(route.StandardOutput);

        var tcpip = await RunForDeviceAsync(usbSerial, ["tcpip", port.ToString()], ShortTimeout, cancellationToken);
        EnsureSuccess(tcpip, "Enable Wi-Fi ADB");

        var endpoint = await ConnectAsync(host, port, cancellationToken);
        var networkIdentity = await ReadDeviceIdentityAsync(endpoint, cancellationToken);
        if (!string.Equals(identity, networkIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Wi-Fi endpoint did not match the USB headset identity.");
        }

        return endpoint;
    }

    public async Task<string> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        EnsureAdb();
        host = RequireHost(host);
        RequirePort(port);
        var endpoint = $"{host}:{port}";
        var result = await RunAdbAsync(["connect", endpoint], ShortTimeout, cancellationToken);
        EnsureSuccess(result, $"Connect {endpoint}");
        if (!result.StandardOutput.Contains(endpoint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"ADB did not report a connection to {endpoint}: {result.Combined}");
        }

        return endpoint;
    }

    public async Task DisconnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var endpoint = $"{RequireHost(host)}:{RequirePort(port)}";
        var result = await RunAdbAsync(["disconnect", endpoint], ShortTimeout, cancellationToken);
        EnsureSuccess(result, $"Disconnect {endpoint}");
    }

    public async Task InstallApkAsync(string serial, string apkPath, bool replace, CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        var fullPath = ValidateApkPath(apkPath);
        var args = new List<string> { "install" };
        if (replace)
        {
            args.Add("-r");
        }
        args.Add(fullPath);
        var result = await RunForDeviceAsync(serial, args, InstallTimeout, cancellationToken);
        if (result.ExitCode != 0 ||
            result.Combined.Contains("Failure [", StringComparison.OrdinalIgnoreCase) ||
            result.Combined.Contains("INSTALL_FAILED_", StringComparison.OrdinalIgnoreCase) ||
            result.Combined.Contains("INSTALL_PARSE_FAILED_", StringComparison.OrdinalIgnoreCase))
        {
            throw new AdbInstallException(Path.GetFileName(fullPath), result.Combined);
        }
    }

    public async Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(
        string usbSerial,
        bool includeSystemApps,
        CancellationToken cancellationToken)
    {
        RequireUsbSerial(usbSerial);
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        await AddPackagesAsync(usbSerial, ["shell", "pm", "list", "packages", "-3", "-f"], false, apps, cancellationToken);
        if (includeSystemApps)
        {
            await AddPackagesAsync(usbSerial, ["shell", "pm", "list", "packages", "-s", "-f"], true, apps, cancellationToken);
        }

        return apps.Values.OrderBy(app => app.PackageName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task ProvisionWifiNetworkAsync(
        string serial,
        string ssid,
        string security,
        string password,
        bool hidden,
        CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(ssid);
        security = security.Trim().ToLowerInvariant();
        if (security is not ("open" or "wpa2" or "wpa3"))
        {
            throw new ArgumentException("Security must be open, wpa2, or wpa3.");
        }
        if (security is not "open" && string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("A WPA2/WPA3 network needs a password.");
        }

        var args = new List<string> { "shell", "cmd", "-w", "wifi", "connect-network", ssid, security };
        if (security is not "open")
        {
            args.Add(password);
        }
        if (hidden)
        {
            args.Add("-h");
        }
        args.AddRange(["-r", "persistent"]);

        var result = await RunForDeviceAsync(serial, args, ShortTimeout, cancellationToken);
        EnsureSuccess(result, $"Send Wi-Fi profile for {ssid}");
    }

    public async Task StageFileAsync(
        string serial,
        string localPath,
        string remoteDirectory,
        CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var fullPath = Path.GetFullPath(localPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The file to stage was not found.", fullPath);
        }

        remoteDirectory = ValidateRemoteDirectory(remoteDirectory);
        await EnsureRemoteDirectoryAsync(serial, remoteDirectory, cancellationToken);
        var remotePath = CombineRemotePath(remoteDirectory, Path.GetFileName(fullPath));
        await PushAndVerifyFileAsync(serial, fullPath, remotePath, cancellationToken);
    }

    public async Task<int> StagePathAsync(
        string serial,
        string localPath,
        string remoteDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var fullPath = Path.GetFullPath(localPath);
        remoteDirectory = ValidateRemoteDirectory(remoteDirectory);
        await EnsureRemoteDirectoryAsync(serial, remoteDirectory, cancellationToken);

        if (File.Exists(fullPath))
        {
            progress?.Report(Path.GetFileName(fullPath));
            await PushAndVerifyFileAsync(
                serial,
                fullPath,
                CombineRemotePath(remoteDirectory, Path.GetFileName(fullPath)),
                cancellationToken);
            return 1;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected file or folder was not found.", fullPath);
        }

        var directoryName = new DirectoryInfo(fullPath).Name;
        var remoteRoot = CombineRemotePath(remoteDirectory, directoryName);
        await EnsureRemoteDirectoryAsync(serial, remoteRoot, cancellationToken);
        var files = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).ToList();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(fullPath, file).Replace('\\', '/');
            var remotePath = CombineRemotePath(remoteRoot, relativePath);
            var remoteParent = remotePath[..remotePath.LastIndexOf('/')];
            await EnsureRemoteDirectoryAsync(serial, remoteParent, cancellationToken);
            progress?.Report($"{directoryName}/{relativePath}");
            await PushAndVerifyFileAsync(serial, file, remotePath, cancellationToken);
        }

        return files.Count;
    }

    public async Task OpenWifiSettingsAsync(string serial, CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        var result = await RunForDeviceAsync(serial, ["shell", "am", "start", "-a", "android.settings.WIFI_SETTINGS"], ShortTimeout, cancellationToken);
        EnsureSuccess(result, "Open Wi-Fi settings");
    }

    public async Task PlayDetectSoundAsync(string serial, CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        var tempPath = Path.Combine(Path.GetTempPath(), "questdeploy-detect.wav");
        await File.WriteAllBytesAsync(tempPath, CreateBeepWav(), cancellationToken);
        var remotePath = "/sdcard/Download/questdeploy-detect.wav";
        var push = await RunForDeviceAsync(serial, ["push", tempPath, remotePath], ShortTimeout, cancellationToken);
        EnsureSuccess(push, "Stage detect sound");
        var play = await RunForDeviceAsync(
            serial,
            ["shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", $"file://{remotePath}", "-t", "audio/wav"],
            ShortTimeout,
            cancellationToken);
        EnsureSuccess(play, "Play detect sound");
    }

    public async Task LaunchPackageAsync(string serial, string packageName, CancellationToken cancellationToken)
    {
        RequireSerial(serial);
        if (!PackagePattern().IsMatch(packageName))
        {
            throw new ArgumentException("Enter a valid Android package name.");
        }

        var result = await RunForDeviceAsync(serial, ["shell", "monkey", "-p", packageName, "1"], ShortTimeout, cancellationToken);
        EnsureSuccess(result, $"Launch {packageName}");
    }

    private async Task<string> ReadDeviceIdentityAsync(string serial, CancellationToken cancellationToken)
    {
        foreach (var property in new[] { "ro.serialno", "ro.boot.serialno" })
        {
            var result = await RunForDeviceAsync(serial, ["shell", "getprop", property], ShortTimeout, cancellationToken);
            EnsureSuccess(result, "Read headset identity");
            var value = result.StandardOutput.Trim();
            if (value.Length > 0 && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new InvalidOperationException("The headset did not expose a stable serial identity.");
    }

    private async Task AddPackagesAsync(
        string serial,
        IReadOnlyList<string> arguments,
        bool isSystem,
        IDictionary<string, InstalledApp> destination,
        CancellationToken cancellationToken)
    {
        var result = await RunForDeviceAsync(serial, arguments, ShortTimeout, cancellationToken);
        EnsureSuccess(result, "Read installed applications");
        foreach (var line in result.StandardOutput.ReplaceLineEndings("\n").Split('\n'))
        {
            var value = line.Trim();
            if (!value.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = value["package:".Length..];
            var separator = value.LastIndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
            {
                continue;
            }

            var apkPath = value[..separator].Trim();
            var packageName = value[(separator + 1)..].Trim();
            if (PackagePattern().IsMatch(packageName))
            {
                destination[packageName] = new InstalledApp(packageName, apkPath, isSystem);
            }
        }
    }

    private async Task PopulateDeviceFactsAsync(QuestDevice device, CancellationToken cancellationToken)
    {
        var serial = device.PreferredSerial;
        try
        {
            device.Identity = await ReadDeviceIdentityAsync(serial, cancellationToken);
        }
        catch
        {
            device.Identity = serial;
        }

        try
        {
            var model = await RunForDeviceAsync(serial, ["shell", "getprop", "ro.product.model"], ShortTimeout, cancellationToken);
            if (model.ExitCode == 0 && !string.IsNullOrWhiteSpace(model.StandardOutput))
            {
                device.Model = model.StandardOutput.Trim().Replace('_', ' ');
                device.Name = device.Model;
            }
        }
        catch
        {
        }

        try
        {
            var link = await RunForDeviceAsync(serial, ["shell", "ip", "link", "show", "wlan0"], ShortTimeout, cancellationToken);
            var match = Regex.Match(link.StandardOutput, @"\blink/ether\s+(?<mac>[0-9a-f]{2}(?::[0-9a-f]{2}){5})\b", RegexOptions.IgnoreCase);
            if (match.Success && !match.Groups["mac"].Value.Equals("02:00:00:00:00:00", StringComparison.OrdinalIgnoreCase))
            {
                device.MacAddress = match.Groups["mac"].Value;
            }
        }
        catch
        {
        }

        try
        {
            var battery = await RunForDeviceAsync(serial, ["shell", "dumpsys", "battery"], ShortTimeout, cancellationToken);
            var match = Regex.Match(battery.StandardOutput, @"(?m)^\s*level:\s*(?<level>\d{1,3})\s*$");
            if (match.Success && int.TryParse(match.Groups["level"].Value, out var level))
            {
                device.BatteryPercent = Math.Clamp(level, 0, 100);
            }
        }
        catch
        {
        }
    }

    private async Task EnsureRemoteDirectoryAsync(string serial, string remoteDirectory, CancellationToken cancellationToken)
    {
        var mkdir = await RunForDeviceAsync(serial, ["shell", "mkdir", "-p", remoteDirectory], ShortTimeout, cancellationToken);
        EnsureSuccess(mkdir, $"Create {remoteDirectory}");
    }

    private async Task PushAndVerifyFileAsync(
        string serial,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var push = await RunForDeviceAsync(serial, ["push", localPath, remotePath], FileTransferTimeout, cancellationToken);
        EnsureSuccess(push, $"Copy {Path.GetFileName(localPath)}");

        var stat = await RunForDeviceAsync(serial, ["shell", "stat", "-c", "%s", remotePath], ShortTimeout, cancellationToken);
        if (stat.ExitCode == 0 && long.TryParse(stat.StandardOutput.Trim(), out var remoteSize))
        {
            var localSize = new FileInfo(localPath).Length;
            if (localSize != remoteSize)
            {
                throw new InvalidOperationException($"File verification failed for {Path.GetFileName(localPath)}. Expected {localSize} bytes, found {remoteSize} bytes.");
            }
        }

        await NotifyMediaScannerAsync(serial, remotePath, cancellationToken);
    }

    private async Task NotifyMediaScannerAsync(string serial, string remotePath, CancellationToken cancellationToken)
    {
        if (!IsMediaFile(remotePath))
        {
            return;
        }

        var mediaUri = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = "",
            Path = remotePath
        }.Uri.AbsoluteUri;
        var scan = await RunForDeviceAsync(
            serial,
            ["shell", "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", mediaUri],
            ShortTimeout,
            cancellationToken);

        // The transfer is still valid when a particular Quest OS build declines media indexing.
        if (scan.ExitCode != 0)
        {
            return;
        }
    }

    private static bool IsMediaFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".mp4" or ".m4v" or ".mkv" or ".mov" or ".webm" or ".avi" or
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" or
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";

    private Task<CommandResult> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var scoped = new List<string> { "-s", RequireSerial(serial) };
        scoped.AddRange(arguments);
        return RunAdbAsync(scoped, timeout, cancellationToken);
    }

    private async Task<CommandResult> RunAdbAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        EnsureAdb();
        var startInfo = new ProcessStartInfo
        {
            FileName = AdbPath,
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

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start adb.");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var outputTask = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"adb timed out after {timeout}.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        return new CommandResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string? LocateAdb(string? explicitPath)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb.exe"));
        candidates.Add(Environment.GetEnvironmentVariable("QUEST_DEPLOY_ADB"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "platform-tools", "adb.exe"));

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator).Select(entry => Path.Combine(entry.Trim(), "adb.exe")));
        }

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }

    private static string ValidateApkPath(string apkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var fullPath = Path.GetFullPath(apkPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The APK file was not found.", fullPath);
        }
        if (!Path.GetExtension(fullPath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Select an .apk file.");
        }
        return fullPath;
    }

    private static string ValidateRemoteDirectory(string remoteDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteDirectory);
        var normalized = remoteDirectory.Trim().Replace('\\', '/').TrimEnd('/');
        if (!normalized.StartsWith("/sdcard/", StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..") ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The headset destination must be a folder below /sdcard/ and cannot contain relative path segments.");
        }

        return normalized;
    }

    private static string CombineRemotePath(string directory, string name) =>
        $"{directory.TrimEnd('/')}/{name.Replace('\\', '/').TrimStart('/')}";

    private static string ParseWifiAddress(string routeOutput)
    {
        var match = Regex.Match(routeOutput, @"\bsrc\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b");
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not find the headset Wi-Fi IPv4 address from `adb shell ip route`.");
        }
        return RequireHost(match.Groups["ip"].Value);
    }

    private static string ReadModelLabel(string[] parts)
    {
        var modelPart = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
        return modelPart is null ? "Quest headset" : modelPart["model:".Length..].Replace('_', ' ');
    }

    private static bool TrySplitEndpoint(string serial, out string host, out int port)
    {
        host = "";
        port = 0;
        var separator = serial.LastIndexOf(':');
        if (separator <= 0 || separator == serial.Length - 1)
        {
            return false;
        }
        if (!int.TryParse(serial[(separator + 1)..], out port))
        {
            return false;
        }
        host = serial[..separator];
        return true;
    }

    private static string RequireHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        host = host.Trim();
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("Use the headset's non-loopback IPv4 address.");
        }
        return address.ToString();
    }

    private static int RequirePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The TCP port must be between 1 and 65535.");
        }
        return port;
    }

    private static string RequireSerial(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!SerialPattern().IsMatch(serial))
        {
            throw new ArgumentException("The ADB serial contains unsupported characters.");
        }
        return serial;
    }

    private static string RequireUsbSerial(string serial)
    {
        serial = RequireSerial(serial);
        if (serial.Contains(':'))
        {
            throw new ArgumentException("Connect this headset by USB to view its installed applications.");
        }
        return serial;
    }

    private void EnsureAdb()
    {
        if (AdbPath.Length == 0 && !TryLocateAdb())
        {
            throw new InvalidOperationException("ADB was not found. Install Android SDK Platform Tools or set QUEST_DEPLOY_ADB to adb.exe.");
        }
    }

    private static void EnsureSuccess(CommandResult result, string action)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{action} failed: {result.Combined}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static byte[] CreateBeepWav()
    {
        const int sampleRate = 44100;
        const double durationSeconds = 0.35;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataLength = sampleCount * channels * bitsPerSample / 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var envelope = Math.Min(1.0, i / 1200.0) * Math.Min(1.0, (sampleCount - i) / 1600.0);
            var value = (short)(Math.Sin(2 * Math.PI * 880 * t) * short.MaxValue * 0.38 * envelope);
            writer.Write(value);
        }
        writer.Flush();
        return stream.ToArray();
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    [GeneratedRegex("^[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Combined => string.Join(" ", new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }
}
