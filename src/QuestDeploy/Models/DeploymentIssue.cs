namespace QuestDeploy.Models;

public sealed record DeploymentIssue(
    string HeadsetName,
    string ErrorCode,
    string Summary,
    string RecoverySteps,
    string RawDetails);
