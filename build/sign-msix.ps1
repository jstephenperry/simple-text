#requires -Version 5.1
<#
.SYNOPSIS
  Builds and self-signs the SimpleText WinUI MSIX with a persistent, reusable
  certificate so the package installs on your own PC without the "this app may
  potentially harm your computer" warning.

.DESCRIPTION
  A self-signed package still trips Windows' untrusted-publisher warning until the
  signing certificate is trusted on the machine. This script closes that loop:

    1. Reuses (or creates once) a self-signed code-signing certificate in your
       per-user store whose subject exactly matches the package manifest's
       Publisher (Windows rejects the package otherwise).
    2. Builds the MSIX for the chosen platform and signs it with that cert.
    3. Exports the public certificate (SimpleText.cer) next to the package so it
       can be trusted on this or any other machine.
    4. With -Trust, installs the certificate into LocalMachine\TrustedPeople
       (elevating just for that step) so App Installer treats the package as
       trusted and installs it with no warning.

  Because the certificate persists in your store, every build is signed by the
  same publisher: trust it once and future versions install cleanly too.

  Run from a "Developer PowerShell for VS" (or any shell where msbuild is on
  PATH). msbuild — not `dotnet` — is required for MSIX packaging.

.PARAMETER Platform
  Target architecture: x64 (default) or ARM64.

.PARAMETER Configuration
  Build configuration: Release (default) or Debug.

.PARAMETER Trust
  Also install the certificate into LocalMachine\TrustedPeople. Needs admin; the
  trust step self-elevates (a UAC prompt) without re-running the build.

.PARAMETER ExportPfx
  Path to also export the cert + private key as a .pfx, then print its base64 and
  password. Use this to seed the SIGNING_CERTIFICATE_BASE64 /
  SIGNING_CERTIFICATE_PASSWORD GitHub secrets so signed releases reuse one stable
  publisher (no re-trusting on every version).

.EXAMPLE
  pwsh build/sign-msix.ps1 -Trust
  Build, sign, and trust an x64 package, then install AppPackages\...\*.msix.

.EXAMPLE
  pwsh build/sign-msix.ps1 -Platform ARM64
  Build + sign an ARM64 package; trust it later with build/trust-cert.ps1.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$Trust,

    [string]$ExportPfx
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = Join-Path $RepoRoot 'SimpleText.WinUI\SimpleText.WinUI.csproj'
$Manifest   = Join-Path $RepoRoot 'SimpleText.WinUI\Package.appxmanifest'
$PackageDir = Join-Path $RepoRoot 'AppPackages'

if (-not (Get-Command msbuild -ErrorAction SilentlyContinue)) {
    throw "msbuild was not found on PATH. Open a 'Developer PowerShell for VS' (or the VS Developer Command Prompt) and try again."
}

# The signing certificate's subject MUST exactly equal the manifest's Publisher,
# or Windows refuses to install the package. Read it from the manifest so the two
# can never drift apart.
[xml]$xml = Get-Content -LiteralPath $Manifest
$publisher = $xml.Package.Identity.Publisher
if (-not $publisher) { throw "Could not read Identity/@Publisher from $Manifest." }
Write-Host "Manifest Publisher: $publisher"

# 1. Reuse a usable cert (right subject, code-signing EKU, has key, not expired),
#    otherwise create one that persists in the store for next time.
$now = Get-Date
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt $now -and
        ($_.EnhancedKeyUsageList.ObjectId -contains $codeSigningOid)
    } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if ($cert) {
    Write-Host "Reusing certificate $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))."
} else {
    Write-Host "Creating a persistent self-signed code-signing certificate for $publisher ..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $publisher `
        -FriendlyName 'SimpleText self-signed (sideload)' `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable `
        -NotAfter $now.AddYears(5)
    Write-Host "Created certificate $($cert.Thumbprint)."
}

# 2. Build + sign. PackageCertificateThumbprint signs straight from the store, so
#    there's no .pfx or password to juggle locally.
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
& msbuild $Project `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:GenerateAppxPackageOnBuild=true `
    /p:AppxPackageDir="$PackageDir\" `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxBundle=Never `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateThumbprint=$($cert.Thumbprint)
if ($LASTEXITCODE -ne 0) { throw "msbuild failed with exit code $LASTEXITCODE." }

# 3. Export the public certificate for trusting on this (or any) machine.
$cerPath = Join-Path $PackageDir 'SimpleText.cer'
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "Exported public certificate -> $cerPath"

$msix = Get-ChildItem -Recurse $PackageDir -Filter *.msix |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($msix) { Write-Host "Built package          -> $($msix.FullName)" }

# Optional: export a PFX to reuse as a CI secret so releases share one publisher.
if ($ExportPfx) {
    $pfxPwd = [guid]::NewGuid().ToString('N')
    $sec = ConvertTo-SecureString -String $pfxPwd -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $ExportPfx -Password $sec | Out-Null
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($ExportPfx))
    Write-Host ""
    Write-Host "Exported PFX -> $ExportPfx"
    Write-Host "For repeatable signed releases, set these GitHub Actions secrets:"
    Write-Host "  SIGNING_CERTIFICATE_PASSWORD = $pfxPwd"
    Write-Host "  SIGNING_CERTIFICATE_BASE64   = (base64 below)"
    Write-Host $b64
}

# 4. Optionally trust the cert now so the package installs without warnings.
if ($Trust) {
    & (Join-Path $PSScriptRoot 'trust-cert.ps1') -Path $cerPath
} else {
    Write-Host ""
    Write-Host "To install without the untrusted-publisher warning, trust the cert once:"
    Write-Host "  pwsh build/trust-cert.ps1 -Path `"$cerPath`""
    if ($msix) {
        Write-Host "then double-click the .msix, or:  Add-AppxPackage `"$($msix.FullName)`""
    }
}
