# Installation Failure Guidance

QuestDeploy keeps Android's original diagnostic and maps common package-manager and ADB failures to professor-facing recovery steps. The guidance is based on Android's package manager return-code definitions and ADB package-manager documentation.

Primary references:

- Android ADB and package manager: <https://developer.android.com/tools/adb#pm>
- AOSP `PackageManager` install result definitions: <https://android.googlesource.com/platform/frameworks/base/+/main/core/java/android/content/pm/PackageManager.java>

## Mapped Failures

| Diagnostic | Meaning | Primary recovery |
| --- | --- | --- |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | Existing and new APK signatures differ | Use the original signing key or uninstall the old app after accounting for data loss |
| `INSTALL_FAILED_VERSION_DOWNGRADE` | APK version is older than installed version | Build with a higher `versionCode` or uninstall the newer app |
| `INSTALL_FAILED_INSUFFICIENT_STORAGE` | Android lacks install and optimization space | Remove content/apps, restart, and retry with sufficient headroom |
| `INSTALL_FAILED_NO_MATCHING_ABIS` | APK native libraries do not support the headset CPU | Use an APK containing `arm64-v8a` |
| `INSTALL_FAILED_OLDER_SDK` | APK requires a newer Android API level | Update the headset or use a build with a compatible `minSdk` |
| `INSTALL_FAILED_MISSING_SPLIT` | A base APK was supplied without required split APKs | Produce a universal APK or the complete device-specific split set |
| `INSTALL_FAILED_TEST_ONLY` | APK is marked `testOnly` | Use a normal signed development or release build |
| `INSTALL_FAILED_INVALID_APK` and parse/signature failures | APK is malformed, incomplete, or improperly signed | Re-export, verify signing, and use a complete APK |
| `INSTALL_FAILED_USER_RESTRICTED` | Current user/device policy blocked install | Check Developer Mode and prompts inside the headset |
| ADB unauthorized/offline/disconnected/timeout | Transport or authorization failed | Accept USB authorization, reconnect, refresh, and prefer USB for large files |

The displayed steps intentionally avoid automatically uninstalling an app because uninstalling deletes that application's local data. Raw ADB output remains available under each failure for cases not covered by a known code.
