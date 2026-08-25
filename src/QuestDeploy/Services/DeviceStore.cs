using System.IO;
using System.Text.Json;
using QuestDeploy.Models;

namespace QuestDeploy.Services;

public sealed class DeviceStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuestDeploy",
        "devices.json");

    public async Task<IReadOnlyList<SavedDevice>> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<SavedDevice>>(stream) ?? [];
    }

    public async Task SaveAsync(IEnumerable<SavedDevice> devices)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, devices.DistinctBy(device => $"{device.Host}:{device.Port}").ToList(), new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public Task ClearAsync()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }
}
