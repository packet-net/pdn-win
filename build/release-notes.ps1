<#
  Writes the release notes for a version to -OutFile.

  The body is the "## <version>" section of CHANGELOG.md. If there is none, it falls back to the
  commit subjects since the previous version tag, with a warning, so a release is never published
  with empty notes; but the CHANGELOG section is the procedure (docs/releasing.md).
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Ref,
    [Parameter(Mandatory)] [string] $OutFile
)
$ErrorActionPreference = 'Stop'
$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'packet-net/pdn-win' }

# The nearest earlier version tag, if any.
$previous = git describe --tags --abbrev=0 --match '[0-9]*.[0-9]*.[0-9]*' "$Ref^" 2>$null
if ($LASTEXITCODE -ne 0) { $previous = $null }
$global:LASTEXITCODE = 0

$notes = New-Object System.Collections.Generic.List[string]
$changelog = if (Test-Path CHANGELOG.md) { [string[]](Get-Content CHANGELOG.md) } else { [string[]]@() }
$start = [Array]::FindIndex($changelog, [Predicate[string]] { param($l) $l -match "^##\s+$([regex]::Escape($Version))(\s|$)" })
if ($start -ge 0) {
    $section = if ($start + 1 -lt $changelog.Count) { $changelog[($start + 1)..($changelog.Count - 1)] } else { @() }
    $end = [Array]::FindIndex([string[]]$section, [Predicate[string]] { param($l) $l -match '^##\s' })
    if ($end -ge 0) { $section = if ($end -gt 0) { $section[0..($end - 1)] } else { @() } }
    foreach ($line in $section) { $notes.Add($line) }
} else {
    Write-Warning "No '## $Version' section in CHANGELOG.md; listing commits instead."
    $range = if ($previous) { "$previous..$Ref" } else { $Ref }
    $notes.Add('## Changes')
    $notes.Add('')
    git log --no-merges --pretty=format:'- %s' $range | ForEach-Object { $notes.Add($_) }
}

while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[0])) { $notes.RemoveAt(0) }
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[$notes.Count - 1])) { $notes.RemoveAt($notes.Count - 1) }
$notes.Add('')
$notes.Add('## Downloads')
$notes.Add('')
$notes.Add("- **pdn-win-$Version-win-x64.msi**: installs to Program Files with a Start menu shortcut; later versions upgrade in place. Needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64); Windows offers it on first start if it is missing.")
$notes.Add("- **pdn-win-$Version-win-x64-portable.exe**: the same app as one self-contained file. Nothing to install and no runtime needed; run it from anywhere.")
$notes.Add('')
$notes.Add('Windows 10 or 11, x64. Settings are kept in `%APPDATA%\pdn-win`, shared by both.')
$notes.Add('')
$notes.Add($(if ($previous) { "**Full changelog**: https://github.com/$repo/compare/$previous...$Version" } else { "**Commits**: https://github.com/$repo/commits/$Version" }))

$text = ($notes -join "`n").Trim() + "`n"
Set-Content -Path $OutFile -Value $text -NoNewline -Encoding utf8
Write-Host $text
