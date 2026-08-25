# Quest Deploy

Quest Deploy is a Windows .NET WPF utility for deploying one APK to one or more Meta Quest headsets over USB or Wi-Fi ADB.

## Current Version

This repo contains the v6 source.

Key features:

- Visual headset dashboard with USB, Wi-Fi ADB, and hotspot candidate states.
- USB-connected Quest discovery through `adb devices -l`.
- Wi-Fi ADB bootstrap from a USB-authorized headset.
- Manual `ip:5555` connection and remembered headset endpoints.
- Hotspot candidate discovery from Windows ARP neighbors.
- Parallel APK install to selected or all connected Wi-Fi headsets.
- Optional post-install launch by Android package name.
- PC Wi-Fi profile listing through `netsh wlan show profiles`.
- Certificate/config file staging to `/sdcard/Download/QuestDeploy`.
- Detect action that plays a short sound on the selected headset.
- Clear remembered endpoints.

## Requirements

- Windows 10 or Windows 11.
- .NET 10 desktop runtime.
- Android SDK Platform Tools with `adb.exe`.
- Meta Quest Developer Mode enabled.
- USB debugging accepted in the headset at least once.

ADB is located from:

1. `QUEST_DEPLOY_ADB`
2. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`
3. `%ProgramFiles%\Android\platform-tools\adb.exe`
4. `PATH`

## Build

```powershell
.\scripts\Build-Release.ps1
```

Outputs are written to `artifacts/`:

- `QuestDeploy-v6/`
- `QuestDeploy-v6-release.zip`
- `QuestDeploy-v6-Setup.exe`

The setup EXE installs the app for the current Windows user under:

```text
%LOCALAPPDATA%\QuestDeploy
```

## Deploy Flow

1. Connect the Quest to the PC by USB.
2. Accept **Allow USB debugging** inside the headset.
3. Click **Refresh**.
4. Select the USB headset.
5. Click **Enable Wi-Fi ADB**.
6. Select/check the Wi-Fi headset card.
7. Pick an APK in the Deploy tab.
8. Click **Install selected** or **Install all Wi-Fi**.

## Create GitHub Repo

The local repo can be pushed with GitHub CLI after authentication:

```powershell
gh auth login
gh repo create quest-deploy --private --source . --remote origin --push
```

Use `--public` instead of `--private` only if you intend to publish the code publicly.
