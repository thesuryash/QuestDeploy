# Quest Classroom Deploy

Quest Classroom Deploy is a simplified Windows utility for preparing and updating a classroom fleet of Meta Quest headsets. It installs APKs and copies videos, audio, folders, and other class files over USB or Wi-Fi ADB.

## Current Version

Version 0.10.2 includes the Phase 2 headset-management, support, and presentation tools, plus a proper per-user installation, reliable in-app updates, and automatic connection refresh:

- One-screen headset and deployment workflow.
- Automatic USB and remembered Wi-Fi headset discovery.
- Automatic headset-list refresh when USB or Wi-Fi ADB connections appear, disappear, or change state.
- One physical headset is combined into one row when both USB and Wi-Fi are connected.
- All, Wi-Fi, and Wired headset tabs backed by one shared selection state.
- Per-headset **Forget Wi-Fi** that disconnects and removes the remembered endpoint.
- Editable classroom names such as `Lab Headset 01`.
- Current Wi-Fi MAC address displayed prominently for network administration.
- Battery, model, IP address, connection state, and per-headset deployment status.
- One-click Wi-Fi ADB setup from a USB-authorized headset.
- Identify action that plays a short sound inside a selected headset.
- Optional APK installation or reinstallation.
- Multiple file and complete folder deployment.
- Automatic destination-folder creation and transferred-file size verification.
- Automatic Android media indexing for copied video, audio, and image files.
- Parallel deployment to as many as four selected headsets.
- Install-error diagnosis with error-specific causes, recovery steps, and raw ADB output.
- Installed third-party and system-app inventory for physically connected headsets.
- Branded read-only USB or Wi-Fi ADB screen casting through the bundled, verified scrcpy distribution, with Left eye, Right eye, and Both eyes views.
- Centered 16:9 one-eye presentation, 1280×720 windowed casting, fullscreen casting, and an optional always-on-top mode.
- Optional Quest 3 eye-panel leveling applies the matching ±22° correction and a safe centered crop; it is enabled by default when a Quest 3 is detected.
- Both-eyes mode intentionally shows the raw stereo framebuffer. A seamless wide panorama would require a dedicated real-time compositor to correct each eye independently and manage stereo parallax; scrcpy cannot perform that merge by itself.
- A shared QuestDeploy logo for the application, installer, taskbar, dialogs, and casting window.
- File menu actions for restarting or exiting the application.
- About dialog with authorship, operational notes, MIT licensing, Android Platform Tools attribution, and source links.
- Manual **Check for updates** action backed by GitHub Releases.
- Transactional installation to `%LOCALAPPDATA%\Programs\QuestDeploy`, with Start Menu and Desktop shortcuts.
- In-app updates wait for the running QuestDeploy process to exit before replacing installed files.
- SHA-256 verification before any downloaded installer is launched.

The default content destination is:

```text
/sdcard/Download/CourseMedia
```

It can be changed in the deployment panel. The exact folder used by a classroom app should be configured before distributing the installer.

## One-Time Headset Setup

1. Enable Developer Mode for the Quest headset.
2. Connect it to the Windows PC with a USB data cable.
3. Put on the headset and accept **Always allow USB debugging from this computer**.
4. Open Quest Classroom Deploy and connect the headset. It appears automatically; **Refresh headsets** remains available as a manual retry.
5. Edit the headset name if needed.
6. Press **Set up Wi-Fi** beside the USB headset.
7. After the row reports Wi-Fi ready, the USB cable can be removed.

The headset and computer must remain on the same network for Wi-Fi deployment. Android can use a different randomized Wi-Fi MAC for different networks, so the displayed MAC is the address for the current network and is not used as the application's permanent headset identity.

## Deploy

1. Select the connected headsets.
2. Choose an APK, class files, a folder, or any combination of them.
3. Confirm the destination folder.
4. Press **Deploy to selected headsets**.
5. Follow the status shown on each headset row.

The app automatically prefers a connected USB transport for speed and otherwise uses Wi-Fi.

## Installer

The Phase 2 release is self-contained for 64-bit Windows and includes the required ADB and scrcpy files. The target computer does not need a separate .NET runtime, Android SDK, or casting installation.

Build from PowerShell:

```powershell
.\scripts\Build-Release.ps1
```

Outputs:

```text
artifacts/QuestDeploy-v0.10.2/
artifacts/QuestDeploy-v0.10.2-release.zip
artifacts/QuestDeploy-v0.10.2-Setup.exe
```

The installer places the application under `%LOCALAPPDATA%\Programs\QuestDeploy` for the current Windows user and creates Start Menu and Desktop shortcuts. Application data such as remembered headset details remains under `%LOCALAPPDATA%\QuestDeploy`, separate from replaceable program files.

## Development Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Android SDK Platform Tools at `%LOCALAPPDATA%\Android\Sdk\platform-tools`, or pass `-AdbDirectory` to the release script
- Official scrcpy Windows directory passed with `-ScrcpyDirectory` to include casting in a local release
- Meta Quest Developer Mode

## ADB Discovery

At runtime, ADB is located in this order:

1. Bundled `platform-tools\adb.exe`
2. `adb.exe` beside the application
3. `QUEST_DEPLOY_ADB`
4. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`
5. `%ProgramFiles%\Android\platform-tools\adb.exe`
6. `PATH`

## Publishing Updates

Push a semantic version tag after the desired source is on `main`:

```powershell
git tag v0.10.2
git push origin v0.10.2
```

The GitHub Actions release workflow builds the Windows installer and publishes it with `QuestDeploy-update.json`. Installed copies check that latest-release manifest only when the user presses **About > Check for updates**. See `docs/update-workflow.md` for the complete flow and security checks.
