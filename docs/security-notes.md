# Security Notes

Quest Classroom Deploy keeps its local trust surface narrow:

- No bundled third-party APKs.
- No background service or silent updater.
- No local HTTP API.
- No Credential Manager or registry access.
- No administrator elevation requirement.
- ADB commands use `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`.
- Remote destinations are limited to absolute folders below `/sdcard/` and reject relative path segments.
- The installer stages program files under `%LOCALAPPDATA%\Programs`, rejects archive entries that escape the staging directory, then swaps the completed payload into `%LOCALAPPDATA%\Programs\QuestDeploy`.
- Remembered-device data remains under `%LOCALAPPDATA%\QuestDeploy` and is not replaced during application updates.
- The installer includes the official Android Platform Tools ADB executable, its required Windows libraries, and `NOTICE.txt`.
- Release builds download scrcpy only from `Genymobile/scrcpy`, verify the pinned SHA-256, and retain its Apache-2.0 license.
- Casting is started with an explicit USB serial and `--no-control`, so QuestDeploy does not forward keyboard or mouse input to the headset.
- Update checks use HTTPS and accept installer URLs only from this repository's GitHub Releases path.
- Downloaded update installers are checked against the release manifest's SHA-256 before execution.
- Updates require explicit user confirmation and the application does not run a background update service.

Known limitations:

- The setup EXE is not digitally signed, so Windows SmartScreen can warn on another computer.
- The update manifest and installer hash are delivered from the same GitHub Release. This detects corruption and mismatched assets, but code signing should still be added before broad external distribution.
- Wi-Fi ADB exposes the ADB daemon on the selected local network until the headset restarts or wireless ADB is disabled.
- The displayed Wi-Fi MAC can be randomized by Android and can change when the headset joins a different network.
- File copying relies on the permissions available to the ADB shell and does not bypass Android scoped-storage rules inside the installed APK.
- Protected or DRM-controlled headset surfaces can appear black in a cast because Android may prohibit screen capture.
