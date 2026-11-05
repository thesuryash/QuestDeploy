param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.10.4',
    [string]$AdbDirectory = (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools'),
    [string]$ScrcpyDirectory = $env:QUEST_DEPLOY_SCRCPY_DIRECTORY
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use MAJOR.MINOR.PATCH format, for example 0.10.5.'
}

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$appOutput = Join-Path $artifacts "QuestDeploy-v$Version"
$appZip = Join-Path $artifacts "QuestDeploy-v$Version-release.zip"
$installerPayload = Join-Path $root 'src\QuestDeploy.Installer\Payload\QuestDeploy-release.zip'
$installerOutput = Join-Path $artifacts 'installer'
$setupExe = Join-Path $artifacts "QuestDeploy-v$Version-Setup.exe"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Force $artifacts | Out-Null
New-Item -ItemType Directory -Force (Split-Path -Parent $installerPayload) | Out-Null

$artifactsRoot = [IO.Path]::GetFullPath($artifacts).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($directory in @($appOutput, $installerOutput)) {
    $resolvedDirectory = [IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean release directory outside artifacts: $resolvedDirectory"
    }
    if (Test-Path -LiteralPath $resolvedDirectory) {
        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
}

$appProject = Join-Path $root 'src\QuestDeploy\QuestDeploy.csproj'
Invoke-Checked { dotnet restore $appProject -r win-x64 } 'App restore'
Invoke-Checked { dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true --no-restore -p:Version=$Version -o $appOutput } 'App publish'

$platformToolsOutput = Join-Path $appOutput 'platform-tools'
New-Item -ItemType Directory -Force $platformToolsOutput | Out-Null
foreach ($fileName in @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt')) {
    $source = Join-Path $AdbDirectory $fileName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required Android Platform Tools file was not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $platformToolsOutput $fileName) -Force
}

if (-not [string]::IsNullOrWhiteSpace($ScrcpyDirectory)) {
    $scrcpySource = [IO.Path]::GetFullPath($ScrcpyDirectory)
    $scrcpyExe = Join-Path $scrcpySource 'scrcpy.exe'
    if (-not (Test-Path -LiteralPath $scrcpyExe)) {
        throw "scrcpy.exe was not found in: $scrcpySource"
    }

    $scrcpyOutput = Join-Path $appOutput 'scrcpy'
    New-Item -ItemType Directory -Force $scrcpyOutput | Out-Null
    Copy-Item -Path (Join-Path $scrcpySource '*') -Destination $scrcpyOutput -Recurse -Force
}
else {
    Write-Warning 'scrcpy was not supplied. The app will build, but Cast screen will require QUEST_DEPLOY_SCRCPY or a separate scrcpy installation.'
}

$brandingOutput = Join-Path $appOutput 'branding'
$castIcon = Join-Path $brandingOutput 'scrcpy.png'
if (Test-Path -LiteralPath $castIcon) {
    Copy-Item -LiteralPath $castIcon -Destination (Join-Path $brandingOutput 'disconnected.png') -Force
}

if (Test-Path $appZip) {
    Remove-Item -LiteralPath $appZip -Force
}
Compress-Archive -Path (Join-Path $appOutput '*') -DestinationPath $appZip -Force
Copy-Item -LiteralPath $appZip -Destination $installerPayload -Force

$installerProject = Join-Path $root 'src\QuestDeploy.Installer\QuestDeploy.Installer.csproj'
Invoke-Checked { dotnet restore $installerProject -r win-x64 } 'Installer restore'
Invoke-Checked { dotnet publish $installerProject -c $Configuration -r win-x64 --self-contained true --no-restore -p:Version=$Version -p:PublishSingleFile=true -o $installerOutput } 'Installer publish'
Copy-Item -LiteralPath (Join-Path $installerOutput 'QuestDeploy-Setup.exe') -Destination $setupExe -Force

Write-Host "Release output:"
Write-Host "  $appZip"
Write-Host "  $setupExe"
