# Security Notes

Quest Deploy intentionally keeps the local trust surface narrow:

- No bundled third-party APKs.
- No updater.
- No local HTTP API.
- No Credential Manager access.
- No registry writes.
- No services.
- No elevation requirement.
- ADB commands are launched with `ProcessStartInfo.ArgumentList` and `UseShellExecute=false`.
- The setup EXE installs under the current user's `%LOCALAPPDATA%\QuestDeploy`.

Known limitations:

- The setup EXE is unsigned.
- The setup EXE is framework-dependent and expects the .NET 10 desktop runtime.
- Network provisioning uses Android's `cmd -w wifi connect-network`, which can vary by device firmware.
