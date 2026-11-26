# Builds the app and installs it into a folder only your own account can write to:
#   %LOCALAPPDATA%\Programs\StayFocusNotes
# Run again after changing the source to update the installed copy.
#   powershell -ExecutionPolicy Bypass -File install.ps1
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Sign       (also sign the exe, see below)
#   powershell -ExecutionPolicy Bypass -File install.ps1 -NoSign     (never sign)
#
# Signing is optional and off until you ask for it with -Sign. It creates a self-signed certificate in
# your personal certificate store, trusted only on this PC for your account, and asks Windows to trust
# it: Windows shows a "Security Warning" prompt - choose Yes. (Choosing No still signs the exe; the
# signature then shows as untrusted, but a modified file is still detected.)
# Once that certificate exists, later runs sign automatically; use -NoSign to skip.
param([switch]$Sign, [switch]$NoSign)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$certSubject = 'CN=StayFocus Notes (self-signed)'

function Find-SigningCertificate {
    Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $certSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
}

function New-SigningCertificate {
    # The private key cannot be exported, so it cannot simply be copied off this PC.
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable `
        -KeyUsage DigitalSignature -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
    # Write-Host, not plain output: plain output would become part of this function's return value.
    Write-Host "Created signing certificate $($cert.Thumbprint) (valid until $($cert.NotAfter.ToString('yyyy-MM-dd')))"
    return $cert
}

# Adds the public half of the certificate to the stores that make its signatures count as trusted.
function Add-Trust($cert) {
    $public = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (, $cert.RawData)
    foreach ($storeName in 'Root', 'TrustedPublisher') {
        if (Test-Path "Cert:\CurrentUser\$storeName\$($cert.Thumbprint)") { continue }
        if ($storeName -eq 'Root') { Write-Host 'Windows will now ask whether to trust the certificate - choose Yes.' }
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store ($storeName, 'CurrentUser')
        try {
            $store.Open('ReadWrite')
            $store.Add($public)
        }
        catch {
            Write-Warning "The certificate was not added to '$storeName' ($($_.Exception.Message)). The signature will show as untrusted."
        }
        finally { $store.Close() }
    }
}

function Add-Signature([string]$file, $cert) {
    try {
        # The timestamp keeps the signature valid after the certificate expires. It needs internet access.
        $null = Set-AuthenticodeSignature $file -Certificate $cert -HashAlgorithm SHA256 `
            -TimestampServer 'http://timestamp.digicert.com' -ErrorAction Stop
    }
    catch {
        Write-Warning "Timestamp server not reachable, signing without a timestamp: $($_.Exception.Message)"
        $null = Set-AuthenticodeSignature $file -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop
    }
    $signature = Get-AuthenticodeSignature $file
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or
        $signature.Status -in 'NotSigned', 'HashMismatch') {
        throw "Signing failed: $($signature.Status) $($signature.StatusMessage)"
    }
}

& (Join-Path $root 'build.ps1')
$built = Join-Path $root 'bin\StayFocusNotes.exe'

# Sign before touching the installed copy: if anything here fails, the running app is left as it is.
$cert = $null
if (-not $NoSign) {
    $cert = Find-SigningCertificate
    if (-not $cert -and $Sign) { $cert = New-SigningCertificate }
}
if ($cert) {
    Add-Trust $cert
    Add-Signature $built $cert
}

$dest = Join-Path $env:LOCALAPPDATA 'Programs\StayFocusNotes'
$target = Join-Path $dest 'StayFocusNotes.exe'
New-Item -ItemType Directory -Force $dest | Out-Null

# Ask a running copy to save and exit, then wait for it to be gone before replacing the file.
if (Get-Process StayFocusNotes -ErrorAction SilentlyContinue) {
    Start-Process $built -ArgumentList '--quit' -Wait
    Get-Process StayFocusNotes -ErrorAction SilentlyContinue | Wait-Process -Timeout 15
}
# Windows can keep the old file locked for a moment after the process ends, so retry briefly.
for ($attempt = 1; ; $attempt++) {
    try {
        Copy-Item $built $target -Force
        break
    }
    catch [System.IO.IOException] {
        if ($attempt -ge 20) { throw }
        Start-Sleep -Milliseconds 500
    }
}

# If "Start with Windows" is on, point it at the installed copy.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).StayFocusNotes) {
    Set-ItemProperty -Path $runKey -Name 'StayFocusNotes' -Value "`"$target`""
}

Start-Process $target
"Installed and started: $target"
if ($cert) { "Signature: $((Get-AuthenticodeSignature $target).Status)" }
else { 'Not signed (optional: run again with -Sign).' }
