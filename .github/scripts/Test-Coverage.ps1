# Fails unless every line and branch of the shipped code is covered (docs/testing.md):
# MDGlint.Core by MDGlint.Core.Tests, MDGlint.Desktop by MDGlint.Desktop.Tests, and
# Assets/preview.js by the Node tests. Each assembly is judged by its own test project's report,
# so one project's tests can't make up for another's. Code excluded with
# [ExcludeFromCodeCoverage] (each with its reason) is covered by the end-to-end tests instead.
[CmdletBinding()]
param(
    # Holds Core\ and Desktop\ (cobertura reports from coverlet) and preview.lcov (from Node).
    [string]$Results = 'TestResults'
)

$ErrorActionPreference = 'Stop'
$failed = $false

function Test-Assembly([string]$folder, [string]$package) {
    $report = Get-ChildItem (Join-Path $Results $folder) -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $report) {
        Write-Host "No coverage report for $package in $Results\$folder." -ForegroundColor Red
        return $false
    }

    [xml]$xml = Get-Content $report.FullName
    $coverage = $xml.coverage.packages.package | Where-Object name -eq $package
    if (-not $coverage) {
        Write-Host "$package isn't in $($report.FullName)." -ForegroundColor Red
        return $false
    }

    $uncovered = foreach ($class in $coverage.classes.class) {
        foreach ($line in $class.lines.line) {
            if ($line.hits -eq '0' -or ($line.'condition-coverage' -and $line.'condition-coverage' -notlike '100%*')) {
                "  $($class.filename):$($line.number) $($line.'condition-coverage')"
            }
        }
    }

    $lines = [double]$coverage.'line-rate' * 100
    $branches = [double]$coverage.'branch-rate' * 100
    Write-Host ("{0}: {1:0.##}% of lines, {2:0.##}% of branches" -f $package, $lines, $branches)
    if ($uncovered -or $lines -lt 100 -or $branches -lt 100) {
        Write-Host 'Not covered:' -ForegroundColor Red
        $uncovered | Sort-Object -Unique | Write-Host
        return $false
    }

    return $true
}

function Test-Script([string]$file, [string]$name) {
    if (-not (Test-Path $file)) {
        Write-Host "No coverage report for $name ($file)." -ForegroundColor Red
        return $false
    }

    # lcov: one record per file, from SF: to end_of_record.
    $record = (Get-Content $file -Raw) -split 'end_of_record' | Where-Object { $_ -match "SF:.*[\\/]$([regex]::Escape($name))\r?\n" }
    if (-not $record) {
        Write-Host "$name isn't in ${file}: the tests didn't load it." -ForegroundColor Red
        return $false
    }

    $count = @{}
    foreach ($key in 'LF', 'LH', 'BRF', 'BRH', 'FNF', 'FNH') {
        $count[$key] = if ($record -match "(?m)^${key}:(\d+)") { [int]$Matches[1] } else { 0 }
    }

    Write-Host ("{0}: {1}/{2} lines, {3}/{4} branches, {5}/{6} functions" -f $name,
        $count.LH, $count.LF, $count.BRH, $count.BRF, $count.FNH, $count.FNF)
    if ($count.LF -eq 0 -or $count.LH -lt $count.LF -or $count.BRH -lt $count.BRF -or $count.FNH -lt $count.FNF) {
        $missed = [regex]::Matches($record, '(?m)^DA:(\d+),0') | ForEach-Object { $_.Groups[1].Value }
        Write-Host "Not covered: lines $($missed -join ', ') (branch details: node --test with --experimental-test-coverage)" -ForegroundColor Red
        return $false
    }

    return $true
}

if (-not (Test-Assembly 'Core' 'MDGlint.Core')) { $failed = $true }
if (-not (Test-Assembly 'Desktop' 'MDGlint.Desktop')) { $failed = $true }
if (-not (Test-Script (Join-Path $Results 'preview.lcov') 'preview.js')) { $failed = $true }

if ($failed) {
    throw 'Coverage dropped below 100%. Add tests for the lines above (docs/testing.md).'
}

Write-Host 'Coverage is complete.'
