# Design Notes

The UI follows the deployment pipeline:

1. Authorize USB
2. Enable ADB
3. Connect Wi-Fi
4. Deploy APK

Main surfaces:

- Top command bar for refresh, reconnect, clear remembered endpoints, and Wi-Fi ADB bootstrap.
- Summary tiles for USB-ready, Wi-Fi ADB, network candidates, and selected targets.
- Visual headset cards with transport badges and status badges.
- Deploy panel for APK selection, reinstall/launch options, and install actions.
- Connect panel for manual endpoints, Wi-Fi ADB bootstrap, and detect sound.
- Network panel for Windows Wi-Fi profiles, Wi-Fi provisioning attempts, and certificate/config staging.

Important product constraints:

- A hotspot client is only a network candidate until ADB is enabled and connected.
- Wi-Fi ADB normally needs USB authorization first.
- Windows can list Wi-Fi profile names without exposing the saved password in this UI.
- Silent certificate installation is not implemented because Android/Quest typically requires system UI approval.
