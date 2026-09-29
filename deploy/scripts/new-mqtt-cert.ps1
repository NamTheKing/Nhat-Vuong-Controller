<#
.SYNOPSIS
  Creates a self-signed PKCS#12 certificate for the embedded broker's MQTT-over-TLS endpoint (NFR-04).
  Campus hosts without public DNS use this (or a certificate from the university CA) and install the CA on modules.

.EXAMPLE
  ./deploy/scripts/new-mqtt-cert.ps1 -HostName nvc.campus.local -Password (Read-Host -AsSecureString)
#>
param(
    [string] $HostName = "localhost",
    [Parameter(Mandatory)] [securestring] $Password,
    [string] $OutFile = "$PSScriptRoot/../certs/mqtt.pfx"
)

$cert = New-SelfSignedCertificate -DnsName $HostName, "localhost" -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm ECDSA_nistP256 -NotAfter (Get-Date).AddYears(2) -FriendlyName "Nhat Vuong MQTT"
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $OutFile -Password $Password | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
Write-Host "Wrote $OutFile (SHA-1 thumbprint $($cert.Thumbprint)). Keep it out of source control."
