using QuestDeploy.Models;

namespace QuestDeploy.Services;

public static class InstallFailureAdvisor
{
    public static DeploymentIssue Create(string headsetName, Exception exception, bool replaceExisting)
    {
        var raw = exception is AdbInstallException installException
            ? installException.DiagnosticOutput
            : exception.Message;
        var code = exception is AdbInstallException adbException
            ? adbException.FailureCode
            : ClassifyConnectionFailure(raw);

        var (summary, steps) = AdviceFor(code, replaceExisting);
        return new DeploymentIssue(
            headsetName,
            code,
            summary,
            string.Join(Environment.NewLine, steps.Select((step, index) => $"{index + 1}. {step}")),
            raw);
    }

    private static (string Summary, string[] Steps) AdviceFor(string code, bool replaceExisting) => code switch
    {
        "INSTALL_FAILED_UPDATE_INCOMPATIBLE" or "INSTALL_FAILED_SHARED_USER_INCOMPATIBLE" =>
            ("The installed app and this APK were signed with different certificates.",
            [
                "Confirm that this APK came from the same developer and signing key as the installed version.",
                "If preserving app data matters, obtain an APK signed with the original key.",
                "Otherwise uninstall the old app from the headset, then deploy again. Uninstalling also removes that app's local data."
            ]),
        "INSTALL_FAILED_VERSION_DOWNGRADE" or "INSTALL_FAILED_PERMISSION_MODEL_DOWNGRADE" or "INSTALL_FAILED_SANDBOX_VERSION_DOWNGRADE" =>
            ("The APK is older or uses a lower security model than the installed version.",
            [
                "Use a build with a higher versionCode than the installed app.",
                "If the older build is intentional, uninstall the current app first, understanding that its local data will be removed.",
                "Deploy the APK again after the installed version is removed or replaced with a newer build."
            ]),
        "INSTALL_FAILED_INSUFFICIENT_STORAGE" =>
            ("The headset does not have enough free internal storage for installation and optimization.",
            [
                "Remove unused recordings, media, or apps from the headset.",
                "Restart the headset so Android can finish pending storage cleanup.",
                "Leave more free space than the APK size because Android needs temporary installation and optimization space, then retry."
            ]),
        "INSTALL_FAILED_NO_MATCHING_ABIS" or "INSTALL_FAILED_CPU_ABI_INCOMPATIBLE" or "INSTALL_FAILED_MULTI_ARCH_NOT_MATCH_ALL_NATIVE_ABIS" =>
            ("The APK does not contain native libraries compatible with the headset processor.",
            [
                "Request or build an APK that includes arm64-v8a native libraries.",
                "If this came from an Android App Bundle, export a universal APK or the complete split set for this device.",
                "Deploy the compatible APK instead of retrying the same file."
            ]),
        "INSTALL_FAILED_OLDER_SDK" =>
            ("The APK requires a newer Android API level than this headset currently provides.",
            [
                "Update the headset operating system if an update is available.",
                "Otherwise obtain a build whose minSdk supports the headset's Android version.",
                "Retry with the compatible build."
            ]),
        "INSTALL_FAILED_DEPRECATED_SDK_VERSION" =>
            ("The APK targets an Android version that the headset no longer permits.",
            [
                "Rebuild the application with a supported targetSdkVersion.",
                "Use a current production build rather than an old development APK.",
                "Deploy the rebuilt APK."
            ]),
        "INSTALL_FAILED_MISSING_SPLIT" =>
            ("This is only part of a split application package.",
            [
                "Obtain a universal APK, or export the complete device-specific split set from the app bundle.",
                "Do not install only base.apk from an .apks archive.",
                "Select the complete universal APK in QuestDeploy and retry."
            ]),
        "INSTALL_FAILED_CONFLICTING_PROVIDER" or "INSTALL_FAILED_DUPLICATE_PERMISSION" or "INSTALL_FAILED_DUPLICATE_PACKAGE" =>
            ("The APK conflicts with a package, content provider, or permission already installed on the headset.",
            [
                "Confirm that the APK uses the intended package name and provider authorities.",
                "Identify the conflicting app from the raw diagnostic and remove it only if its data is no longer needed.",
                "Otherwise ask the app developer for a build with unique package, provider, and permission names."
            ]),
        "INSTALL_FAILED_MISSING_SHARED_LIBRARY" =>
            ("The APK requires an Android shared library that is not available on this headset.",
            [
                "Use a Quest-compatible build that does not require the missing device library.",
                "Ask the developer to make that library optional or package the dependency with the app where supported.",
                "Do not repeatedly retry the same APK; it cannot install until the dependency requirement changes."
            ]),
        "INSTALL_FAILED_REPLACE_COULDNT_DELETE" or "INSTALL_FAILED_DEXOPT" or "INSTALL_FAILED_INTERNAL_ERROR" =>
            ("Android could not replace or optimize the application.",
            [
                "Restart the headset and confirm it has ample free storage.",
                "Retry once after the restart.",
                "If it repeats, uninstall the existing app only if losing its local data is acceptable, then deploy again."
            ]),
        "INSTALL_FAILED_TEST_ONLY" =>
            ("Android rejected a test-only APK.",
            [
                "Use a signed release or development build that is not marked testOnly.",
                "If this flag was added by the build system, remove it and rebuild the APK.",
                "Deploy the rebuilt APK."
            ]),
        "INSTALL_FAILED_INVALID_APK" or "INSTALL_FAILED_BAD_SIGNATURE" or "INSTALL_PARSE_FAILED_NOT_APK" or
        "INSTALL_PARSE_FAILED_BAD_MANIFEST" or "INSTALL_PARSE_FAILED_NO_CERTIFICATES" or "INSTALL_PARSE_FAILED_INCONSISTENT_CERTIFICATES" =>
            ("Android could not validate or parse the APK.",
            [
                "Download or export the APK again; partial and renamed bundle files are not valid APKs.",
                "Verify that the APK is signed and contains AndroidManifest.xml.",
                "If the app uses split APKs, create a universal APK or use the complete split set, then retry."
            ]),
        "INSTALL_FAILED_ALREADY_EXISTS" =>
            ("The package is already installed and replacement was not allowed.",
            [
                replaceExisting
                    ? "The replacement option was enabled, so confirm the APK package name and review the raw ADB detail below."
                    : "Enable 'Reinstall over the existing app' and deploy again.",
                "If replacement still fails, compare the signing certificate with the installed app.",
                "Uninstall the existing package only if losing its local data is acceptable."
            ]),
        "INSTALL_FAILED_USER_RESTRICTED" or "INSTALL_FAILED_ABORTED" =>
            ("The headset or its current user policy blocked the installation.",
            [
                "Put on the headset and accept any pending USB debugging or installation prompt.",
                "Confirm Developer Mode remains enabled for this headset.",
                "Reconnect the USB cable, press Refresh headsets, and retry."
            ]),
        "ADB_UNAUTHORIZED" =>
            ("The computer has not been authorized for USB debugging on this headset.",
            [
                "Put on the headset and accept the USB debugging prompt.",
                "Select 'Always allow from this computer' when appropriate for this classroom PC.",
                "Reconnect the cable and press Refresh headsets before retrying."
            ]),
        "ADB_OFFLINE" =>
            ("ADB can see the headset, but the connection is offline.",
            [
                "Disconnect and reconnect the USB cable or Wi-Fi ADB connection.",
                "Wake and unlock the headset, then press Refresh headsets.",
                "If it remains offline, restart the headset and retry."
            ]),
        "ADB_CONNECTION_LOST" =>
            ("The headset connection was lost during deployment.",
            [
                "Keep the headset awake and reconnect its USB cable or Wi-Fi network.",
                "For large APKs, prefer USB or move the PC and headset closer to the access point.",
                "Press Refresh headsets, select the device again, and retry."
            ]),
        "ADB_TIMEOUT" =>
            ("ADB did not finish the operation before the safety timeout.",
            [
                "Confirm the headset is awake and still connected.",
                "Use USB for large APKs or unstable Wi-Fi links.",
                "Restart ADB by reconnecting the headset, press Refresh headsets, and retry."
            ]),
        "ADB_DEVICE_STORAGE_FULL" =>
            ("The headset ran out of storage while receiving deployment content.",
            [
                "Remove unused recordings, media, or apps from the headset.",
                "Restart the headset so Android can reclaim pending storage.",
                "Press Refresh headsets and retry after confirming enough free space for both the file and temporary installation data."
            ]),
        _ =>
            ("Android or ADB returned a deployment error that needs review.",
            [
                "Read the raw diagnostic below and confirm the headset is connected and authorized.",
                "Verify that the file is a complete, signed APK intended for Meta Quest hardware.",
                "Press Refresh headsets and retry once. If it repeats, provide the error code and raw diagnostic to the app developer."
            ])
    };

    private static string ClassifyConnectionFailure(string detail)
    {
        if (detail.Contains("no space left", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("not enough space", StringComparison.OrdinalIgnoreCase))
        {
            return "ADB_DEVICE_STORAGE_FULL";
        }
        if (detail.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "ADB_UNAUTHORIZED";
        }
        if (detail.Contains("offline", StringComparison.OrdinalIgnoreCase))
        {
            return "ADB_OFFLINE";
        }
        if (detail.Contains("timed out", StringComparison.OrdinalIgnoreCase) || detail.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "ADB_TIMEOUT";
        }
        if (detail.Contains("device", StringComparison.OrdinalIgnoreCase) &&
            (detail.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("disconnected", StringComparison.OrdinalIgnoreCase)))
        {
            return "ADB_CONNECTION_LOST";
        }
        return "ADB_INSTALL_FAILED";
    }
}
