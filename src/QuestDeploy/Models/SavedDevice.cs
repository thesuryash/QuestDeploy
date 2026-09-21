namespace QuestDeploy.Models;

public sealed record SavedDevice(
    string Name,
    string Host,
    int Port,
    string Identity = "",
    string MacAddress = "",
    string Model = "Quest headset");
