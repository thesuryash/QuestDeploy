namespace QuestDeploy.Models;

public enum CastViewMode
{
    LeftEye,
    RightEye,
    BothEyes
}

public sealed record CastSettings(
    CastViewMode ViewMode,
    bool Fullscreen,
    bool AlwaysOnTop,
    bool CorrectLensTilt);

public readonly record struct DisplaySize(int Width, int Height);
