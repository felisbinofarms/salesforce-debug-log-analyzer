<#
.SYNOPSIS
    Black Widow — Loves-Prod Bulk Log Downloader
.DESCRIPTION
    1. Renews all existing TraceFlags in Loves-Prod (expires in 2 hours)
    2. Polls for new ApexLog records every 20 seconds
    3. Downloads each log body and saves to ./loves-prod-logs/
    4. Stops automatically after TARGET_LOGS logs are collected
.USAGE
    .\download-loves-prod-logs.ps1
#>

Set-StrictMode -Off
$ErrorActionPreference = "Continue"

# ── Config ────────────────────────────────────────────────────────────────────
$TARGET_LOGS   = 200
$POLL_INTERVAL = 20   # seconds between polls
$ORG_ALIAS     = "loves-prod"
$OUT_DIR       = Join-Path $PSScriptRoot "loves-prod-logs"
# ──────────────────────────────────────────────────────────────────────────────

function Write-Step { param($msg) Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err  { param($msg) Write-Host "  ✗ $msg" -ForegroundColor Red }

# ── Header ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "🕷️  Black Widow — Loves-Prod Log Downloader" -ForegroundColor Magenta
Write-Host "   Target: $TARGET_LOGS logs → $OUT_DIR" -ForegroundColor DarkGray
Write-Host ""

# ── Step 1: Get access token from sfdx ────────────────────────────────────────
Write-Step "Fetching Loves-Prod credentials from sfdx..."
$userJson = sfdx force:user:display --targetusername $ORG_ALIAS --json 2>$null | ConvertFrom-Json
if (-not $userJson -or -not $userJson.result) {
    Write-Err "Could not get credentials for '$ORG_ALIAS'. Run: sfdx force:auth:web:login -a loves-prod"
    exit 1
}
$ACCESS_TOKEN  = $userJson.result.accessToken
$INSTANCE_URL  = $userJson.result.instanceUrl.TrimEnd('/')
$API_VERSION   = "v60.0"
$TOOLING_BASE  = "$INSTANCE_URL/services/data/$API_VERSION/tooling"

Write-Ok "Connected as $($userJson.result.username)"
Write-Ok "Instance: $INSTANCE_URL"

# ── Step 2: Renew all existing TraceFlags ─────────────────────────────────────
Write-Step "Querying existing TraceFlags..."

$headers = @{
    "Authorization" = "Bearer $ACCESS_TOKEN"
    "Content-Type"  = "application/json"
}

$tfQuery   = [Uri]::EscapeDataString("SELECT Id, TracedEntityId, LogType, DebugLevelId FROM TraceFlag")
$tfUrl     = "$TOOLING_BASE/query/?q=$tfQuery"
$tfResult  = Invoke-RestMethod -Uri $tfUrl -Headers $headers -Method GET
$traceFlags = $tfResult.records
Write-Ok "Found $($traceFlags.Count) existing TraceFlags"

# Set window: start now, expire 2 hours from now (ISO 8601 UTC)
$startsAt  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000+0000")
$expiresAt = (Get-Date).ToUniversalTime().AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss.000+0000")
Write-Step "Renewing all TraceFlags  $startsAt → $expiresAt UTC..."

$renewed = 0
$failed  = 0
foreach ($tf in $traceFlags) {
    $patchUrl  = "$TOOLING_BASE/sobjects/TraceFlag/$($tf.Id)"
    $patchBody = @{
        StartDate      = $startsAt
        ExpirationDate = $expiresAt
    } | ConvertTo-Json -Compress
    try {
        Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method PATCH `
            -Body $patchBody -ContentType 'application/json' | Out-Null
        $renewed++
    } catch {
        $failed++
        # Surface the actual Salesforce error body
        $errBody = ""
        try { $errBody = $_.ErrorDetails.Message } catch {}
        Write-Warn "Could not renew $($tf.Id): $errBody"
    }
}
Write-Ok "Renewed $renewed TraceFlags ($failed failed)"

# ── Step 3: Create output directory ───────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $OUT_DIR | Out-Null
Write-Ok "Output directory ready: $OUT_DIR"

# ── Step 4: Poll and download logs ────────────────────────────────────────────
Write-Host ""
Write-Host "  📡 Polling for logs every ${POLL_INTERVAL}s  (Ctrl+C to stop early)" -ForegroundColor White
Write-Host ""

$downloadedIds  = @{}    # track IDs we've already saved
$downloadedCount = 0
$pollCount       = 0
$startTime       = Get-Date

while ($downloadedCount -lt $TARGET_LOGS) {
    $pollCount++
    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds

    # Query newest logs — fetch up to 500 at a time, most recent first
    $logQuery = [Uri]::EscapeDataString(
        "SELECT Id,Application,DurationMilliseconds,LogLength,LogUserId,Operation,Request,StartTime,Status " +
        "FROM ApexLog ORDER BY StartTime DESC LIMIT 500"
    )
    $logUrl = "$TOOLING_BASE/query/?q=$logQuery"

    try {
        $logResult = Invoke-RestMethod -Uri $logUrl -Headers $headers -Method GET
    } catch {
        Write-Warn "Poll #$pollCount failed: $_  — retrying in ${POLL_INTERVAL}s"
        Start-Sleep -Seconds $POLL_INTERVAL
        continue
    }

    $newLogs = $logResult.records | Where-Object { -not $downloadedIds.ContainsKey($_.Id) }

    if ($newLogs.Count -gt 0) {
        Write-Host "  Poll #$pollCount (+${elapsed}s): $($logResult.totalSize) logs in org, $($newLogs.Count) new" -ForegroundColor White

        foreach ($log in $newLogs) {
            if ($downloadedCount -ge $TARGET_LOGS) { break }

            # ── Download body ───────────────────────────────────────────────
            $bodyUrl = "$TOOLING_BASE/sobjects/ApexLog/$($log.Id)/Body"
            try {
                $body = Invoke-RestMethod -Uri $bodyUrl -Headers $headers -Method GET
            } catch {
                Write-Warn "  Could not download body for $($log.Id): $_"
                $downloadedIds[$log.Id] = $true
                continue
            }

            # ── Build safe filename ─────────────────────────────────────────
            $ts         = ($log.StartTime -replace '[:\./]', '-') -replace 'T', '_'
            $op         = ($log.Operation -replace '[\\/:*?"<>|]', '_') -replace '\s+', '_'
            $userId     = $log.LogUserId
            $fileName   = "${ts}_${userId}_${op}_$($log.Id).log"
            $filePath   = Join-Path $OUT_DIR $fileName

            # ── Save ────────────────────────────────────────────────────────
            if ($body -is [string]) {
                [System.IO.File]::WriteAllText($filePath, $body, [System.Text.Encoding]::UTF8)
            } else {
                $body | ConvertTo-Json -Depth 10 | Set-Content -Path $filePath -Encoding UTF8
            }

            $downloadedIds[$log.Id] = $true
            $downloadedCount++
            $kb = [math]::Round($log.LogLength / 1024, 1)
            Write-Host "    [$downloadedCount/$TARGET_LOGS] $($log.StartTime)  ${kb}KB  $($log.Operation)" -ForegroundColor Green
        }
    } else {
        Write-Host "  Poll #$pollCount (+${elapsed}s): $($logResult.totalSize) logs in org, 0 new — waiting..." -ForegroundColor DarkGray
    }

    if ($downloadedCount -ge $TARGET_LOGS) { break }
    Start-Sleep -Seconds $POLL_INTERVAL
}

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ✅ Done! $downloadedCount log(s) saved to: $OUT_DIR" -ForegroundColor Green
$totalMB = [math]::Round((Get-ChildItem $OUT_DIR | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
Write-Host "  📦 Total size: ${totalMB} MB" -ForegroundColor Cyan
Write-Host ""
