# Builds MDGlint's installer into Install\Output (docs/releasing.md). -Version builds it as another
# version than Directory.Build.props sets; the installer tests use that for an older setup to
# update from (.github/scripts/Test-Installer.ps1).
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$installDirectory = $PSScriptRoot
$desktopProject = Join-Path (Split-Path -Parent $installDirectory) 'MDGlint.Desktop.csproj'
$installerScript = Join-Path $installDirectory 'installer.iss'

function Find-InnoSetupCompiler {
    $fromPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    # Inno Setup 7 installs 64-bit to Program Files; Inno Setup 6 to Program Files (x86).
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $programFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
        (Join-Path $programFiles 'Inno Setup 6\ISCC.exe')
    )

    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$iscc = Find-InnoSetupCompiler
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw 'Inno Setup (6.3 or later) was not found. Install it or add ISCC.exe to PATH.'
}

# The version is set once, in Directory.Build.props at the repository root.
if ($Version) {
    $version = $Version
} else {
    $version = (& dotnet msbuild $desktopProject -nologo -getProperty:Version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Reading the version failed ($LASTEXITCODE)."
    }
}
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Could not read a valid version from the project (got '$version')."
}

# Start from an empty publish folder: setup packs everything in it, and a folder left over from
# an earlier publish can be missing files or hold stale ones.
$publishDirectory = Join-Path (Split-Path -Parent $installDirectory) 'bin\Release\net10.0-windows\win-x64\publish'
if (Test-Path $publishDirectory) {
    Remove-Item -Path $publishDirectory -Recurse -Force
}

Write-Host "Publishing MDGlint $version as self-contained win-x64..."
& dotnet publish $desktopProject -c Release -r win-x64 --self-contained true "-p:Version=$version"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# WPF's native libraries sit next to the single-file exe; without them the app can't start.
$requiredFiles = @('MDGlint.Desktop.exe', 'PresentationNative_cor3.dll', 'wpfgfx_cor3.dll', 'WebView2Loader.dll', 'Assets\index.html')
$missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $publishDirectory $_)) }
if ($missing) {
    throw "The publish folder is missing: $($missing -join ', ')."
}

# An update installs over the old version, and [InstallDelete] clears what the old one put there
# first. Anything setup installs that no entry covers would linger after an update once a later
# version stops shipping it, so every top-level item except the exe must be covered.
$installDelete = [regex]::Matches((Get-Content $installerScript -Raw), '(?m)^Type: (?<type>files|filesandordirs); Name: "\{app\}\\(?<name>[^"]+)"') |
    ForEach-Object { [pscustomobject]@{ Type = $_.Groups['type'].Value; Name = $_.Groups['name'].Value } }
$shipped = Get-ChildItem -Path $publishDirectory -Exclude '*.pdb', '*.xml', 'MDGlint.Desktop.exe'
$uncovered = $shipped | Where-Object {
    $item = $_
    -not ($installDelete | Where-Object {
        ($item.Name -like $_.Name) -and ($_.Type -eq 'filesandordirs' -or -not $item.PSIsContainer)
    })
}
if ($uncovered) {
    throw "Add these to [InstallDelete] in installer.iss: $(($uncovered | ForEach-Object Name) -join ', ')."
}

# The build folder's path often includes a user name, so it must not be in anything that ships
# (Directory.Build.props maps it away in Release builds; this catches a build that didn't).
$repositoryRoot = (Resolve-Path (Join-Path $installDirectory '..\..')).Path.TrimEnd('\')
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$leaks = Get-ChildItem -Path $publishDirectory -Recurse -File -Include '*.exe', '*.dll' | Where-Object {
    $latin1.GetString([System.IO.File]::ReadAllBytes($_.FullName)).IndexOf($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($leaks) {
    throw "These files contain the build folder's path: $(($leaks | ForEach-Object Name) -join ', ')."
}

Write-Host 'Compiling the installer...'
& $iscc "/DMyAppVersion=$version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created in $(Join-Path $installDirectory 'Output')."
