using System.Text.RegularExpressions;

namespace QuestDeploy.Services;

public sealed partial class AdbInstallException : InvalidOperationException
{
    public AdbInstallException(string apkName, string diagnosticOutput)
        : base($"Could not install {apkName}.")
    {
        DiagnosticOutput = string.IsNullOrWhiteSpace(diagnosticOutput)
            ? "ADB did not return additional details."
            : diagnosticOutput.Trim();
        FailureCode = FailureCodePattern().Match(DiagnosticOutput) is { Success: true } match
            ? match.Value
            : "ADB_INSTALL_FAILED";
    }

    public string FailureCode { get; }

    public string DiagnosticOutput { get; }

    [GeneratedRegex(@"INSTALL_(?:FAILED|PARSE_FAILED)_[A-Z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex FailureCodePattern();
}
