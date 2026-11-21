<#
.SYNOPSIS
    Prints one version's section of CHANGELOG.md, for the release notes.
.DESCRIPTION
    Returns the lines between "## [<Version>]" and the next "## [" heading (or the link list at
    the end), trimmed of blank lines at either end. Fails if the section is missing or empty, so
    a release can't go out without its changelog entry.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Changelog
)

$ErrorActionPreference = 'Stop'

$lines = Get-Content -Path $Changelog -Encoding utf8
$heading = "## [$Version]"
$start = [Array]::FindIndex([string[]]$lines, [Predicate[string]] { param($line) $line.StartsWith($heading) })
if ($start -lt 0) {
    throw "CHANGELOG.md has no '$heading' section."
}

$section = [System.Collections.Generic.List[string]]::new()
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    # The next version's heading, or the link definitions at the bottom ("[1.0.0]: https://...").
    if ($lines[$i].StartsWith('## [') -or $lines[$i] -match '^\[[^\]]+\]:\s') {
        break
    }
    $section.Add($lines[$i])
}

$text = ($section -join "`n").Trim()
if ($text.Length -eq 0) {
    throw "The '$heading' section of CHANGELOG.md is empty."
}

$text
