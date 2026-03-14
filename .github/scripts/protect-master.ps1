#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies branch protection rules to the master branch via the GitHub CLI.

.DESCRIPTION
    Run this once after cloning / forking the repo to lock down master:
      - No direct pushes — all changes must arrive via a Pull Request
      - Required status checks: Pre-build checks, Build, Security scan, Test, PR Title
      - Branches must be up-to-date with master before merging
      - 1 required review, but felisbinofarms (owner) can bypass and self-merge
      - Administrators are NOT exempt (rules apply to everyone)

.PREREQUISITES
    GitHub CLI installed: https://cli.github.com/
    Authenticated with repo admin rights: gh auth login

.USAGE
    .\.github\scripts\protect-master.ps1

    # Override repo if running from a fork:
    .\.github\scripts\protect-master.ps1 -Repo "your-org/your-fork"
#>

param(
    [string]$Repo = "",           # e.g. "myorg/log_analyser" — auto-detected if empty
    [string]$Branch = "master"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── 0. Verify gh CLI is available ────────────────────────────────────────────
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is not installed. Install from https://cli.github.com/"
    exit 1
}

# ── 1. Resolve the repo if not supplied ──────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $remoteUrl = git remote get-url origin 2>$null
    if ($remoteUrl -match "github\.com[:/](.+?)(?:\.git)?$") {
        $Repo = $Matches[1]
    } else {
        Write-Error "Could not auto-detect repo from git remote. Pass -Repo 'owner/repo' explicitly."
        exit 1
    }
}

Write-Host ('Applying branch protection to ' + $Branch + ' on ' + $Repo + '...') -ForegroundColor Cyan

# ── 2. Build the protection payload ──────────────────────────────────────────
# The GitHub REST API endpoint for branch protection:
# PUT /repos/{owner}/{repo}/branches/{branch}/protection
$payload = @{
    # ── Required status checks ─────────────────────────────────
    # These are the exact job names from .github/workflows/ci.yml and pr-title.yml
    required_status_checks = @{
        strict   = $true          # branch must be up-to-date with master before merge
        contexts = @(
            "Pre-build checks",   # restore + dotnet format
            "Build",              # Release compile, warnings-as-errors
            "Security scan",      # vulnerable NuGet package detection
            "Test",               # xUnit suite + 60% coverage floor
            "PR Title"            # conventional commit format check
        )
    }

    # ── Pull request reviews ───────────────────────────────────
    # No human approval required - CI pipeline is the gate.
    # Since this is a personal repo, the owner can merge their own PRs
    # directly once all required status checks are green.
    required_pull_request_reviews = $null

    # ── Commit signing (optional — uncomment if using GPG/SSH signing) ──
    # required_signatures = $true

    # ── Enforce for admins too ─────────────────────────────────
    enforce_admins = $true

    # ── No force-pushes / deletions ────────────────────────────
    allow_force_pushes = $false
    allow_deletions    = $false

    # ── Restrict who can push directly (empty = nobody) ────────
    restrictions = $null
} | ConvertTo-Json -Depth 10

# ── 3. Apply via gh api ───────────────────────────────────────────────────────
$encodedBranch = [Uri]::EscapeDataString($Branch)
$apiPath       = "repos/$Repo/branches/$encodedBranch/protection"

$tmpFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmpFile, $payload, [System.Text.UTF8Encoding]::new($false))

try {
    gh api `
        --method PUT `
        -H "Accept: application/vnd.github+json" `
        -H "X-GitHub-Api-Version: 2022-11-28" `
        "$apiPath" `
        --input $tmpFile
} finally {
    Remove-Item $tmpFile -ErrorAction SilentlyContinue
}

if ($LASTEXITCODE -eq 0) {
    Write-Host ''
    Write-Host 'Branch protection applied successfully!' -ForegroundColor Green
    Write-Host ''
    Write-Host ('Rules now active on ' + $Branch + ':') -ForegroundColor White
    Write-Host '  Direct pushes blocked - PRs required' -ForegroundColor Gray
    Write-Host '  Required checks: Pre-build checks, Build, Security scan, Test, PR Title' -ForegroundColor Gray
    Write-Host '  Branches must be up-to-date before merge' -ForegroundColor Gray
    Write-Host '  Reviews: none required - owner can merge own PRs once CI is green' -ForegroundColor Gray
    Write-Host '  Enforce for admins: yes' -ForegroundColor Gray
    Write-Host '  Force-push / deletion: blocked' -ForegroundColor Gray
} else {
    Write-Error ('gh api call failed (exit ' + $LASTEXITCODE + '). Check your token has repo scope and admin rights on ' + $Repo + '.')
}
