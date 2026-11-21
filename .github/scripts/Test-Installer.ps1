# End-to-end tests of setup (docs/testing.md): updating from older versions, and uninstalling.
#
# They install, update and uninstall MDGlint for the current user with the real AppId, so they
# replace any MDGlint installed on the machine and its settings. They run on CI runners only,
# unless -OnThisMachine is given.
#
# The "older versions" are today's code built with a lower version number: what is tested is that
# setup updates in place, not how a given old release behaved.
[CmdletBinding()]
param(
    [switch]$OnThisMachine
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -and -not $OnThisMachine) {
    throw 'These tests replace any installed MDGlint and its settings. Run them on CI, or pass -OnThisMachine.'
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildInstaller = Join-Path $root 'MDGlint.Desktop\Install\build-installer.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) "MDGlintInstallerTests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

$appId = '{8650A31D-C8E8-48EC-9A9E-F38B9825ED9E}'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\MDGlint'
$exe = Join-Path $installFolder 'MDGlint.Desktop.exe'
$settingsFolder = Join-Path $env:APPDATA 'MDGlint'
$localFolder = Join-Path $env:LOCALAPPDATA 'MDGlint'
$settings = Join-Path $settingsFolder 'settings.json'
# What MDGlint passes when the user installs an update (UpdateLauncher.InstallerArguments).
$updateArguments = '/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /UPDATE=1'
$quiet = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
$failures = 0

function Check([bool]$condition, [string]$what) {
    if ($condition) {
        Write-Host "  ok    $what"
    } else {
        Write-Host "  FAIL  $what" -ForegroundColor Red
        $script:failures++
    }
}

function Build-Setup([string]$version) {
    & $buildInstaller -Version $version | Out-Host
    $setup = Join-Path $work "MDGlintSetup-$version.exe"
    Copy-Item (Join-Path $root 'MDGlint.Desktop\Install\Output\MDGlintSetup.exe') $setup
    return $setup
}

function Wait-Until([scriptblock]$condition, [string]$what, [int]$seconds = 60) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while (-not (& $condition)) {
        if ((Get-Date) -gt $deadline) {
            throw "Timed out: $what"
        }
        Start-Sleep -Milliseconds 300
    }
}

function Stop-MDGlint {
    Get-Process -Name 'MDGlint.Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Install([string]$setup, [string]$arguments) {
    # Wait for setup only. In PowerShell 7, Start-Process -Wait also waits for every process setup
    # starts, and after an update setup reopens MDGlint, which keeps running.
    $process = Start-Process $setup -ArgumentList $arguments -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "$setup $arguments failed with exit code $($process.ExitCode)."
    }
}

function Get-Installed {
    Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
}

# The uninstaller copies itself to a temporary file and runs that, so waiting on the process it
# was started as isn't enough.
function Wait-Uninstalled {
    Wait-Until { -not (Get-Installed) -and -not (Get-Process | Where-Object { $_.ProcessName -like '_unins*' -or $_.ProcessName -like 'unins0*' }) } 'the uninstaller to finish'
}

function Uninstall-Quietly {
    Stop-MDGlint
    $installed = Get-Installed
    if ($installed) {
        Start-Process $installed.UninstallString.Trim('"') -ArgumentList $quiet | Out-Null
        Wait-Uninstalled
    }
}

function Reset-Machine {
    Uninstall-Quietly
    Remove-Item $settingsFolder, $localFolder -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-Update([string]$from, [string]$tasks) {
    Write-Host "Updating from $from to $current (tasks: '$tasks')"
    Reset-Machine
    Install $setups[$from] "$quiet /TASKS=`"$tasks`""
    Check ((Get-Installed).DisplayVersion -eq $from) "$from is installed"

    # Files an older version had that this one doesn't, which the update must remove.
    Set-Content (Join-Path $installFolder 'Assets\old-script.js') 'old'
    Set-Content (Join-Path $installFolder 'OldLibrary.dll') 'old'
    New-Item -ItemType Directory -Path $settingsFolder -Force | Out-Null
    $saved = '{"Theme":"dark","AllowRemoteContent":true,"CheckForUpdates":false}'
    Set-Content $settings $saved -NoNewline
    $associated = (Get-ItemProperty 'HKCU:\Software\Classes\.md' -ErrorAction SilentlyContinue).'(default)'

    # As MDGlint runs setup when the user installs an update.
    Install $setups[$current] $updateArguments

    $entries = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall' |
        Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -like 'MDGlint*' })
    Check ($entries.Count -eq 1) 'one MDGlint is installed'
    Check ((Get-Installed).DisplayVersion -eq $current) "the installed version is $current"
    Check ((Get-Item $exe).VersionInfo.ProductVersion -like "$current*") "the exe is $current"
    Check (-not (Test-Path (Join-Path $installFolder 'Assets\old-script.js'))) 'an old file in Assets is gone'
    Check (-not (Test-Path (Join-Path $installFolder 'OldLibrary.dll'))) 'an old library is gone'
    Check (Test-Path (Join-Path $installFolder 'Assets\preview.js')) "the new version's files are there"
    Check ((Get-Content $settings -Raw) -eq $saved) 'settings are kept'
    Check ((Get-ItemProperty 'HKCU:\Software\Classes\.md' -ErrorAction SilentlyContinue).'(default)' -eq $associated) 'the .md association is kept'
    $choice = if ($tasks -match 'check_updates') { 1 } else { 0 }
    Check ((Get-ItemProperty 'HKCU:\Software\MDGlint').CheckForUpdates -eq $choice) "setup's update choice ($choice) is kept"

    # Setup opens MDGlint again once it's done.
    $reopened = $false
    try {
        Wait-Until { Get-Process -Name 'MDGlint.Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } } 'MDGlint to reopen' 30
        $reopened = $true
    } catch {
    }
    Check $reopened 'MDGlint opens again after the update'
    Stop-MDGlint
}

# Answers the uninstaller's message boxes by clicking their buttons.
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class Dialogs {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  static string Text(IntPtr h) { var s = new StringBuilder(4096); GetWindowText(h, s, 4096); return s.ToString(); }
  static List<IntPtr> Children(IntPtr parent) { var r = new List<IntPtr>(); EnumChildWindows(parent, (h, l) => { r.Add(h); return true; }, IntPtr.Zero); return r; }

  // Clicks `button` in a visible message box of one of these processes whose text matches; returns its text.
  public static string Answer(HashSet<uint> processes, string match, string button) {
    string answered = null;
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (answered != null || !processes.Contains(pid) || !IsWindowVisible(h) || Class(h) != "#32770") return true;
      var children = Children(h);
      var text = string.Join(" ", children.FindAll(c => Class(c) == "Static").ConvertAll(Text));
      if (!text.Contains(match)) return true;
      var target = children.Find(c => Class(c) == "Button" && Text(c).Replace("&", "") == button);
      if (target == IntPtr.Zero) return true;
      SendMessage(target, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
      answered = text;
      return false;
    }, IntPtr.Zero);
    return answered;
  }
}
'@

function Uninstall-Answering([string]$keepSettings) {
    Stop-MDGlint
    Start-Process (Get-Installed).UninstallString.Trim('"') | Out-Null
    $answers = [ordered]@{ 'Are you sure' = 'Yes'; 'keep your MDGlint settings' = $keepSettings; 'successfully removed' = 'OK' }
    foreach ($question in $answers.Keys) {
        Wait-Until {
            $processes = New-Object 'System.Collections.Generic.HashSet[uint32]'
            Get-Process | Where-Object { $_.ProcessName -like '*unins*' } | ForEach-Object { [void]$processes.Add([uint32]$_.Id) }
            [Dialogs]::Answer($processes, $question, $answers[$question])
        } "the uninstaller to ask '$question'"
    }
    Wait-Uninstalled
}

function Test-Uninstall([string]$how) {
    Write-Host "Uninstalling: $how"
    Reset-Machine
    Install $setups[$current] "$quiet /TASKS=`"associate_md,check_updates`""
    New-Item -ItemType Directory -Path $settingsFolder, (Join-Path $localFolder 'WebView2') -Force | Out-Null
    Set-Content $settings '{"Theme":"dark"}'

    switch ($how) {
        'silent' { Uninstall-Quietly }
        'keep' { Uninstall-Answering 'Yes' }
        'remove' { Uninstall-Answering 'No' }
    }

    Check (-not (Get-Installed)) 'MDGlint is no longer installed'
    Check (-not (Test-Path $exe)) 'its files are gone'
    Check (-not (Test-Path 'HKCU:\Software\MDGlint')) "setup's registry key is gone"
    Check ((Get-ItemProperty 'HKCU:\Software\Classes\.md' -ErrorAction SilentlyContinue).'(default)' -ne 'MDGlint.md') 'the .md association is gone'
    if ($how -eq 'remove') {
        Check (-not (Test-Path $settingsFolder)) 'settings are removed'
        Check (-not (Test-Path $localFolder)) 'cached data is removed'
    } else {
        Check (Test-Path $settings) 'settings are kept'
    }
}

try {
    $current = (& dotnet msbuild (Join-Path $root 'MDGlint.Desktop\MDGlint.Desktop.csproj') -nologo -getProperty:Version | Out-String).Trim()
    $setups = @{}
    foreach ($version in '0.8.0', '0.9.0', $current) {
        $setups[$version] = Build-Setup $version
    }

    Test-Update '0.9.0' 'associate_md,check_updates'
    # Skipping a version, with nothing ticked in setup.
    Test-Update '0.8.0' ''

    Test-Uninstall 'silent'
    Test-Uninstall 'keep'
    Test-Uninstall 'remove'
} finally {
    Reset-Machine
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    throw "$failures installer check(s) failed."
}
Write-Host 'All installer checks passed.'
