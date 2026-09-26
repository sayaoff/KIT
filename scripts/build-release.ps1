param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$installer = Join-Path $root "artifacts\installer"
$portableZip = Join-Path $root "artifacts\KIT-Alpha-0.1.0-RC1-win-x64.zip"

Push-Location $root
try {
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    if (Test-Path $installer) { Remove-Item -Recurse -Force $installer }
    if (Test-Path $portableZip) { Remove-Item -Force $portableZip }

    dotnet build KIT.sln --configuration $Configuration
    dotnet run --project tests\KIT.Core.Tests\KIT.Core.Tests.csproj --configuration $Configuration --no-build
    dotnet publish src\KIT.App\KIT.App.csproj --configuration $Configuration --runtime $Runtime --self-contained true --output $publish
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $portableZip
    Get-FileHash -Algorithm SHA256 $portableZip | Format-List

    $isccCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($isccCandidates.Count -eq 0) {
        Write-Warning "Portable build is ready at $publish. Install Inno Setup 6 to build the installer."
        exit 0
    }

    New-Item -ItemType Directory -Force -Path $installer | Out-Null
    & $isccCandidates[0] (Join-Path $root "installer\KIT.iss")
    Get-ChildItem $installer -Filter "*.exe" | Get-FileHash -Algorithm SHA256 | Format-List
    Write-Host "Release candidate is ready at $installer"
}
finally {
    Pop-Location
}
