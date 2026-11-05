# Update Workflow

QuestDeploy uses GitHub Releases as its stable update channel. Installers are not committed to a deployment branch.

## Application Flow

The **About > Check for updates** button requests:

```text
https://github.com/thesuryash/QuestDeploy/releases/latest/download/QuestDeploy-update.json
```

When the manifest reports a newer semantic version, the application:

1. Confirms the installer URL uses HTTPS and belongs to this repository's GitHub Releases path.
2. Asks the user before downloading anything.
3. Downloads the installer to the current user's temporary directory.
4. Calculates SHA-256 and compares it with the manifest.
5. Starts the installer visibly and exits the current application.

The updater never silently installs software, never runs an installer with a hash mismatch, and does not require an update service running in the background.

## Publishing A Release

1. Update and merge the source on `main`.
2. Choose a new `MAJOR.MINOR.PATCH` version.
3. Create and push a matching tag:

```powershell
git tag v0.10.0
git push origin v0.10.0
```

The `Build and publish Windows release` GitHub Actions workflow then:

- Verifies the tag format.
- Installs .NET 10 on a Windows runner.
- Builds the self-contained app and installer.
- Bundles Android Platform Tools from the runner's Android SDK.
- Downloads the pinned official scrcpy Windows archive, verifies its published SHA-256, and bundles it with its license.
- Generates `QuestDeploy-update.json` with the installer URL and SHA-256.
- Creates the GitHub Release and marks it as latest.
- Uploads the installer, portable ZIP, and update manifest.

Use a higher version for every release. Do not replace an existing release asset in place because already-published manifests and hashes should remain immutable.
