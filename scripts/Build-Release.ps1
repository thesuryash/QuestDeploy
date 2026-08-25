param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$appOutput = Join-Path $artifacts 'QuestDeploy-v6'
$appZip = Join-Path $artifacts 'QuestDeploy-v6-release.zip'
$installerPayload = Join-Path $root 'src\QuestDeploy.Installer\Payload\QuestDeploy-v6-release.zip'
$installerOutput = Join-Path $artifacts 'installer'
$setupExe = Join-Path $artifacts 'QuestDeploy-v6-Setup.exe'

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

Invoke-Checked { dotnet restore (Join-Path $root 'src\QuestDeploy\QuestDeploy.csproj') } 'App restore'
Invoke-Checked { dotnet publish (Join-Path $root 'src\QuestDeploy\QuestDeploy.csproj') -c $Configuration --no-restore -o $appOutput } 'App publish'

if (Test-Path $appZip) {
    Remove-Item -LiteralPath $appZip -Force
}
Compress-Archive -Path (Join-Path $appOutput '*') -DestinationPath $appZip -Force
Copy-Item -LiteralPath $appZip -Destination $installerPayload -Force

Invoke-Checked { dotnet restore (Join-Path $root 'src\QuestDeploy.Installer\QuestDeploy.Installer.csproj') -r win-x64 } 'Installer restore'
Invoke-Checked { dotnet publish (Join-Path $root 'src\QuestDeploy.Installer\QuestDeploy.Installer.csproj') -c $Configuration --no-restore -p:PublishSingleFile=true -o $installerOutput } 'Installer publish'
Copy-Item -LiteralPath (Join-Path $installerOutput 'QuestDeploy-v6-Setup.exe') -Destination $setupExe -Force

Write-Host "Release output:"
Write-Host "  $appZip"
Write-Host "  $setupExe"
