# Quest Deploy

Focused Windows WPF utility for installing one APK onto multiple Meta Quest headsets over Wi-Fi ADB.

## Workflow

1. Turn on the PC hotspot or join the same Wi-Fi network as the headsets.
2. Enable Developer Mode on each headset.
3. Connect each headset over USB once and accept the debugging prompt in-headset.
4. Select the USB headset row and click **Enable Wi-Fi ADB from selected USB**.
5. Repeat for other headsets, or add remembered `ip:5555` endpoints manually.
6. Choose an APK, select targets, then install selected or all connected Wi-Fi headsets.
7. Use the Network tab to list known PC Wi-Fi profiles, enter the network password, and try sending that network to the selected headset.
8. Use Detect selected headset to play a short sound on the selected headset.

ADB is located from `QUEST_DEPLOY_ADB`, common Android SDK locations, or `PATH`.

This first version does not manage Windows Mobile Hotspot directly. That avoids hidden network mutations; hotspot automation should be added as a separate explicit feature.

## Network provisioning limits

Windows profile names can be listed without reading the saved Wi-Fi password. Enter the password manually when sending a WPA2/WPA3 profile.

Quest firmware support for `adb shell cmd -w wifi connect-network` can vary. The app reports the ADB result so unsupported builds fail visibly.

Certificates and enterprise Wi-Fi files are staged to `/sdcard/Download/QuestDeploy`. Silent certificate installation is intentionally not implemented because non-root Android/Quest builds normally require user approval and system UI.
