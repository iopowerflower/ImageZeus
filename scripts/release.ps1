param(
    [Parameter(Position = 0)]
    [string]$Bump = "patch",

    [switch]$NoPush,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

function Run-Git {
    param([string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
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

Push-Location $Root
try {
    Run-Git -GitArgs @("rev-parse", "--is-inside-work-tree") | Out-Null

    $dirty = git status --porcelain
    if ($dirty) {
        throw @"
Working tree has uncommitted changes.

Commit or stash them first, then run the release script again.
This script only wants to commit the version bump, so it will not mix a release tag with unrelated local work.
"@
    }

    Run-Git -GitArgs @("fetch", "--tags", "--quiet")

    $latestTag = git tag --list "v[0-9]*" --sort=-v:refname | Select-Object -First 1
    if (-not $latestTag) {
        throw "Could not find any existing v* tags"
    }

    $currentVersion = $latestTag -replace '^v', ''
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

    Write-Host "Latest tag:      $latestTag"
    Write-Host "Next version:    $nextVersion"
    Write-Host "Tag:             $tag"
    Write-Host ""

    if ($DryRun) {
        Write-Host "Dry run only. Would tag the current commit and push branch/tag."
        return
    }

    Run-Git -GitArgs @("tag", "-a", $tag, "-m", "ImageZeus $tag")

    if ($NoPush) {
        Write-Host ""
        Write-Host "Created tag locally. Push later with:"
        Write-Host "  git push origin HEAD"
        Write-Host "  git push origin $tag"
        return
    }

    Run-Git -GitArgs @("push", "origin", "HEAD")
    Run-Git -GitArgs @("push", "origin", $tag)

    Write-Host ""
    Write-Host "Release pushed. GitHub Actions should build and publish $tag."
}
finally {
    Pop-Location
}
