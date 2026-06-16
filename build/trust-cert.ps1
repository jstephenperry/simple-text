#requires -Version 5.1
<#
.SYNOPSIS
  Trusts the SimpleText signing certificate so its self-signed MSIX installs
  without the untrusted-publisher / "this app may potentially harm your computer"
  warning.

.DESCRIPTION
  Imports a code-signing certificate into LocalMachine\TrustedPeople — the store
  Windows App Installer checks when sideloading MSIX packages. Once the publisher
  is trusted there, the package installs cleanly.

  Accepts either the exported public certificate (SimpleText.cer) or a signed
  .msix/.msixbundle, in which case the embedded signer certificate is extracted
  automatically. Only the public certificate is trusted — no private key is ever
  involved.

  Writing to LocalMachine requires administrator rights; the script re-launches
  itself elevated (a UAC prompt) if needed.

.PARAMETER Path
  Path to a .cer/.crt, or to a signed .msix/.msixbundle/.appx/.appxbundle.

.EXAMPLE
  pwsh build/trust-cert.ps1 -Path .\SimpleText.cer

.EXAMPLE
  pwsh build/trust-cert.ps1 -Path .\SimpleText.Editor_1.0.0.0_x64.msix
  Trust the publisher straight from a downloaded release package.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$resolved = (Resolve-Path -LiteralPath $Path).Path

# Importing into LocalMachine needs elevation; relaunch the same host as admin.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Elevation required to write LocalMachine\TrustedPeople; relaunching as admin..."
    $exe = (Get-Process -Id $PID).Path   # pwsh.exe or powershell.exe
    Start-Process -FilePath $exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"", '-Path', "`"$resolved`""
    )
    return
}

# Resolve the certificate: read a .cer directly, or pull the signer from a package.
$ext = [IO.Path]::GetExtension($resolved).ToLowerInvariant()
if ($ext -in '.cer', '.crt') {
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($resolved)
} elseif ($ext -in '.msix', '.msixbundle', '.appx', '.appxbundle') {
    $sig = Get-AuthenticodeSignature -LiteralPath $resolved
    if (-not $sig.SignerCertificate) {
        throw "No signature found on $resolved — build a signed package first (build/sign-msix.ps1)."
    }
    $cert = $sig.SignerCertificate
} else {
    throw "Unsupported file type '$ext'. Pass a .cer or a signed .msix."
}

Write-Host "Trusting publisher: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Expires:    $($cert.NotAfter.ToString('yyyy-MM-dd'))"

$store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople', 'LocalMachine')
$store.Open('ReadWrite')
try {
    $store.Add($cert)
} finally {
    $store.Close()
}

Write-Host ""
Write-Host "Done. '$($cert.Subject)' is trusted on this machine."
Write-Host "Install by double-clicking the .msix, or:  Add-AppxPackage <path>.msix"
Write-Host ""
Write-Host "To revoke trust later (as admin):"
Write-Host "  Remove-Item Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
