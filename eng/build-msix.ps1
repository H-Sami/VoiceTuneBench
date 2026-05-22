param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$CertificateThumbprint = "",
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\VoiceTuneBench.WinUI\VoiceTuneBench.WinUI.csproj"
$profile = "Properties\PublishProfiles\win10-$Platform.pubxml"

$buildArgs = @(
    "build",
    $project,
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-p:WindowsPackageType=MSIX",
    "-p:GenerateAppxPackageOnBuild=true",
    "-p:PublishProfile=$profile",
    "-p:AppxPackageSigningEnabled=false",
    "-p:UseSharedCompilation=false"
)

if (-not $Restore) {
    $buildArgs += "--no-restore"
}

dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$package = Get-ChildItem (Join-Path $root "artifacts\msix") -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $package) {
    throw "MSIX package was not created."
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
    Set-AuthenticodeSignature -FilePath $package.FullName -Certificate $certificate -HashAlgorithm SHA256 | Out-Null
}

Write-Host "MSIX package:"
Write-Host $package.FullName

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Host "Package is unsigned. Sign it with a trusted code-signing certificate before public release."
}
