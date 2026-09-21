# Design Notes

Version 7 is intentionally organized around a professor's repeated classroom workflow:

1. Connect or refresh headsets.
2. Complete one-time Wi-Fi setup for a USB headset when needed.
3. Choose an APK, files, or a folder.
4. Deploy to selected headsets.

The primary window contains only two working surfaces:

- A headset list with editable names, connection state, current-network Wi-Fi MAC, model, battery, IP address, identify sound, and contextual Wi-Fi setup.
- A deployment panel for an optional APK, files/folders, destination directory, launch option, overall progress, and per-device results.

Technical controls such as manual endpoints, hotspot neighbor candidates, parallelism, certificate staging, and Wi-Fi command details are not exposed in Professor Mode.

## Device Identity

- A stable ADB serial property is the primary identity when available.
- USB and Wi-Fi transports reporting the same identity are merged into one headset row.
- A connected USB transport is preferred for deployment speed; Wi-Fi is used otherwise.
- Friendly classroom names are persisted with remembered Wi-Fi endpoints.
- The Wi-Fi MAC is displayed for network administration but is not treated as permanent identity because Android can randomize it per network.

## File Deployment

- The destination must be below `/sdcard/`.
- `mkdir -p` makes folder creation idempotent.
- Individual files preserve their names.
- Selected folders preserve their top-level folder name and relative contents.
- Each copied file is checked against the remote byte size when the headset supports `stat`.
- APK and file deployment can be used separately or together.
