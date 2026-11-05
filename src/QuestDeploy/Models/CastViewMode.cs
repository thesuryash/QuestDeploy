namespace QuestDeploy.Models;

public enum CastViewMode
{
    LeftEye,
    RightEye,
    BothEyes
}

public readonly record struct DisplaySize(int Width, int Height);
