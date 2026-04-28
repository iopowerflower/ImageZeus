param(
    [Parameter(Position = 0)]
    [string]$Bump = "patch",

    [switch]$SkipBuild,
    [switch]$NoPush,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$InstallerScript = Join-Path $Root "installer\ImageZeus.iss"

function Run-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    & git @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Args -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Run-Command {
    param(
        [string]$FileName,
        [string[]]$Arguments
    )

    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-CurrentVersion {
    $text = [IO.File]::ReadAllText($InstallerScript)
    $match = [regex]::Match($text, '#define\s+MyAppVersion\s+"(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)"')
    if (-not $match.Success) {
        throw "Could not find MyAppVersion in $InstallerScript"
    }

    return $match.Groups["version"].Value
}

function Resolve-NextVersion {
    param(
        [string]$CurrentVersion,
        [string]$RequestedBump
    )

    if ($RequestedBump -match '^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>-[0-9A-Za-z.-]+)?$') {
        return "$($Matches.major).$($Matches.minor).$($Matches.patch)$($Matches.suffix)"
    }

    if ($CurrentVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[0-9A-Za-z.-]+)?$') {
        throw "Current version '$CurrentVersion' is not a simple semver version"
    }

    $major = [int]$Matches.major
    $minor = [int]$Matches.minor
    $patch = [int]$Matches.patch

    switch ($RequestedBump.ToLowerInvariant()) {
        "major" { return "$($major + 1).0.0" }
        "minor" { return "$major.$($minor + 1).0" }
        "patch" { return "$major.$minor.$($patch + 1)" }
        default {
            throw "Use one of: patch, minor, major, or an explicit version like 1.2.3"
        }
    }
}

function Set-Version {
    param([string]$Version)

    $text = [IO.File]::ReadAllText($InstallerScript)
    $updated = [regex]::Replace(
        $text,
        '#define\s+MyAppVersion\s+"[^"]+"',
        "#define MyAppVersion `"$Version`"",
        1)

    if ($updated -eq $text) {
        throw "Failed to update MyAppVersion in $InstallerScript"
    }

    [IO.File]::WriteAllText($InstallerScript, $updated)
}

Push-Location $Root
try {
    Run-Git rev-parse --is-inside-work-tree | Out-Null

    $dirty = git status --porcelain
    if ($dirty) {
        throw @"
Working tree has uncommitted changes.

Commit or stash them first, then run the release script again.
This script only wants to commit the version bump, so it will not mix a release tag with unrelated local work.
"@
    }

    Run-Git fetch --tags --quiet

    $currentVersion = Get-CurrentVersion
    $nextVersion = Resolve-NextVersion -CurrentVersion $currentVersion -RequestedBump $Bump
    $tag = "v$nextVersion"

    $existingLocalTag = git tag --list $tag
    if ($existingLocalTag) {
        throw "Tag '$tag' already exists locally"
    }

    $existingRemoteTag = git ls-remote --tags origin "refs/tags/$tag"
    if ($existingRemoteTag) {
        throw "Tag '$tag' already exists on origin"
    }

    Write-Host "Current version: $currentVersion"
    Write-Host "Next version:    $nextVersion"
    Write-Host "Tag:             $tag"
    Write-Host ""

    if ($DryRun) {
        Write-Host "Dry run only. Would update installer version, commit, tag, and push."
        return
    }

    Set-Version $nextVersion

    if (-not $SkipBuild) {
        Run-Command dotnet @("build", "ImageZeus.sln", "-c", "Release")
    }

    Run-Git add "installer/ImageZeus.iss"
    Run-Git commit -m "Release $tag"
    Run-Git tag -a $tag -m "ImageZeus $tag"

    if ($NoPush) {
        Write-Host ""
        Write-Host "Created commit and tag locally. Push later with:"
        Write-Host "  git push origin HEAD"
        Write-Host "  git push origin $tag"
        return
    }

    Run-Git push origin HEAD
    Run-Git push origin $tag

    Write-Host ""
    Write-Host "Release pushed. GitHub Actions should build and publish $tag."
}
finally {
    Pop-Location
}
