# Design Notes

Phase 2 remains organized around a professor's repeated classroom workflow:

1. Connect or refresh headsets.
2. Complete one-time Wi-Fi setup for a USB headset when needed.
3. Choose an APK, files, or a folder.
4. Deploy to selected headsets.

The primary window contains two working surfaces:

- A headset list with All/Wi-Fi/Wired filters, editable names, connection state, current-network Wi-Fi MAC, model, battery, IP address, identify sound, Wi-Fi setup/removal, USB app inventory, and USB casting.
- A deployment panel for an optional APK, files/folders, destination directory, launch option, overall progress, and per-device results.

Technical controls such as manual endpoints, hotspot neighbor candidates, parallelism, certificate staging, and Wi-Fi command details are not exposed in Professor Mode.

## Device Identity

- A stable ADB serial property is the primary identity when available.
- USB and Wi-Fi transports reporting the same identity are merged into one headset row.
- A connected USB transport is preferred for deployment speed; Wi-Fi is used otherwise.
- Friendly classroom names are persisted with remembered Wi-Fi endpoints.
- Forget Wi-Fi disconnects the endpoint and removes only QuestDeploy's saved endpoint; it does not revoke the computer's USB-debugging key on the headset.
- The Wi-Fi MAC is displayed for network administration but is not treated as permanent identity because Android can randomize it per network.

## File Deployment

- The destination must be below `/sdcard/`.
- `mkdir -p` makes folder creation idempotent.
- Individual files preserve their names.
- Selected folders preserve their top-level folder name and relative contents.
- Each copied file is checked against the remote byte size when the headset supports `stat`.
- APK and file deployment can be used separately or together.

## Support Tools

- Installed-app inventory uses Android's package manager and is limited to a physically connected headset.
- Third-party apps are the default view; system packages can be included explicitly.
- Casting invokes the official scrcpy executable with the selected USB serial and read-only control disabled. Left eye is the default; left/right modes crop the live headset width in half, while Both eyes streams the complete frame.
- Deployment failures are classified from Android package-manager and ADB diagnostics, while the original output remains visible for support.
