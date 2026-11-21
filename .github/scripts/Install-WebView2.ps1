# Makes sure the CI runner has the Microsoft Edge WebView2 Runtime (docs/testing.md).
#
# The end-to-end tests and the installer tests start the real app, which needs the runtime, and
# GitHub's Windows Server images don't always include it. Without it the preview never loads, and
# setup stops at its "WebView2 is missing" message, which nobody is there to close.
#
# Installs the Evergreen runtime with Microsoft's bootstrapper, only on CI. This is for the runner
# only: MDGlint itself never ships or downloads the bootstrapper (docs/decisions.md).
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'This installs software machine-wide and runs on CI only.'
}

# The same check setup makes (installer.iss): the runtime's EdgeUpdate client key.
$clientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
function Get-WebView2Version {
    foreach ($key in "HKLM:\SOFTWARE\WOW6432Node\$clientKey", "HKCU:\Software\$clientKey") {
        $version = (Get-ItemProperty -Path $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($version -and $version -ne '0.0.0.0') { return $version }
    }
    return $null
}

# Policies can change what WebView2 allows (for example remote debugging, which the end-to-end
# tests use), so the log lists any the runner has.
foreach ($policies in 'HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\WebView2',
                      'HKCU:\Software\Policies\Microsoft\Edge', 'HKCU:\Software\Policies\Microsoft\Edge\WebView2') {
    if (Test-Path $policies) {
        Write-Host "Policies in ${policies}:"
        (Get-ItemProperty $policies).PSObject.Properties |
            Where-Object { $_.Name -notlike 'PS*' } |
            ForEach-Object { Write-Host "  $($_.Name) = $($_.Value)" }
    }
}
Get-ChildItem env: | Where-Object { $_.Name -like 'WEBVIEW2*' } | ForEach-Object { Write-Host "Environment: $($_.Name) = $($_.Value)" }

$version = Get-WebView2Version
if ($version) {
    Write-Host "WebView2 Runtime $version is already installed."
    return
}

Write-Host 'WebView2 Runtime not found; installing the Evergreen runtime.'
$bootstrapper = Join-Path ([IO.Path]::GetTempPath()) "MicrosoftEdgeWebview2Setup-$([guid]::NewGuid().ToString('N')).exe"
# Microsoft's documented link to the Evergreen bootstrapper.
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -UseBasicParsing

$signature = Get-AuthenticodeSignature -FilePath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw "The bootstrapper isn't validly signed by Microsoft ($($signature.Status), $($signature.SignerCertificate.Subject))."
}

$process = Start-Process -FilePath $bootstrapper -ArgumentList '/silent', '/install' -Wait -PassThru
Remove-Item $bootstrapper -Force
if ($process.ExitCode -ne 0) {
    throw "The WebView2 bootstrapper failed ($($process.ExitCode))."
}

$version = Get-WebView2Version
if (-not $version) {
    # Show where the bootstrapper did put things, so the log says what to change.
    Write-Host "Runner: $((Get-CimInstance Win32_OperatingSystem).Caption) $([Environment]::OSVersion.Version)"
    foreach ($root in 'HKLM:\SOFTWARE\WOW6432Node', 'HKLM:\SOFTWARE', 'HKCU:\Software') {
        $clients = "$root\Microsoft\EdgeUpdate\Clients"
        Write-Host "--- $clients"
        Get-ChildItem $clients -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty $_.PSPath
            Write-Host "  $($_.PSChildName)  name=$($p.name)  pv=$($p.pv)"
        }
    }
    foreach ($folder in "${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application",
                        "$env:ProgramFiles\Microsoft\EdgeWebView\Application",
                        "$env:LOCALAPPDATA\Microsoft\EdgeWebView\Application") {
        Write-Host "--- $folder exists: $(Test-Path $folder)"
        Get-ChildItem $folder -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.Name)" }
    }
    $log = "$env:ProgramData\Microsoft\EdgeUpdate\Log\MicrosoftEdgeUpdate.log"
    if (Test-Path $log) {
        Write-Host "--- last lines of $log"
        Get-Content $log -Tail 60 | ForEach-Object { Write-Host "  $_" }
    }
    throw 'The WebView2 bootstrapper finished, but the runtime still isn''t registered.'
}
Write-Host "Installed WebView2 Runtime $version."
