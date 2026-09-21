# Security Notes

Quest Classroom Deploy keeps its local trust surface narrow:

- No bundled third-party APKs.
- No updater or background service.
- No local HTTP API.
- No Credential Manager or registry access.
- No administrator elevation requirement.
- ADB commands use `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`.
- Remote destinations are limited to absolute folders below `/sdcard/` and reject relative path segments.
- The installer rejects archive entries that resolve outside `%LOCALAPPDATA%\QuestDeploy`.
- The installer includes the official Android Platform Tools ADB executable, its required Windows libraries, and `NOTICE.txt`.
- Update checks use HTTPS and accept installer URLs only from this repository's GitHub Releases path.
- Downloaded update installers are checked against the release manifest's SHA-256 before execution.
- Updates require explicit user confirmation and the application does not run a background update service.

Known limitations:

- The setup EXE is not digitally signed, so Windows SmartScreen can warn on another computer.
- The update manifest and installer hash are delivered from the same GitHub Release. This detects corruption and mismatched assets, but code signing should still be added before broad external distribution.
- Wi-Fi ADB exposes the ADB daemon on the selected local network until the headset restarts or wireless ADB is disabled.
- The displayed Wi-Fi MAC can be randomized by Android and can change when the headset joins a different network.
- File copying relies on the permissions available to the ADB shell and does not bypass Android scoped-storage rules inside the installed APK.
