<#
.SYNOPSIS
  Bulk-imports the researched provider rows into the Ruumly directory.

.DESCRIPTION
  Reads the import payloads produced from docs/research/partners-2026-08-08 and POSTs
  them to /api/admin/suppliers/bulk in batches. The endpoint is idempotent by slug, so
  re-running is safe: rows that already exist come back as "skipped", never duplicated
  or modified.

  You supply the admin token yourself so no credential is ever stored or shared. Get one
  by opening https://ruumly.eu while logged in as admin, pressing F12, and pasting this
  into the Console:

      await (await fetch('https://api.ruumly.eu/api/auth/refresh',
        {method:'POST',credentials:'include'})).json()

  Copy the accessToken value out of the result.

.EXAMPLE
  .\import-directory.ps1 -Token "eyJhbGciOi..." -DataDir "C:\path\to\payloads"

.EXAMPLE
  # See what would be sent without touching production:
  .\import-directory.ps1 -Token "x" -DataDir "C:\path" -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string] $Token,
    [Parameter(Mandatory = $true)][string] $DataDir,
    [string]   $ApiBase   = 'https://api.ruumly.eu/api',
    [string[]] $Countries = @('EE', 'LV', 'LT'),
    [int]      $BatchSize = 25
)

$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $Token"; 'Content-Type' = 'application/json' }
$grand = [ordered]@{ created = 0; skipped = 0; errors = 0 }
$allErrors = @()

foreach ($country in $Countries) {
    $file = Join-Path $DataDir "import-$country.json"
    if (-not (Test-Path $file)) { Write-Warning "missing $file - skipping $country"; continue }

    $rows = Get-Content $file -Raw -Encoding utf8 | ConvertFrom-Json
    Write-Host "`n=== $country : $($rows.Count) rows ===" -ForegroundColor Cyan

    for ($i = 0; $i -lt $rows.Count; $i += $BatchSize) {
        $end   = [Math]::Min($i + $BatchSize, $rows.Count) - 1
        $batch = @($rows[$i..$end])
        $label = "$country rows $($i + 1)-$($end + 1)"

        if (-not $PSCmdlet.ShouldProcess($label, 'POST /admin/suppliers/bulk')) { continue }

        # -Depth 5 matters: the default of 2 silently flattens serviceTypes into
        # type names instead of an array, and the API then rejects every row.
        $body = $batch | ConvertTo-Json -Depth 5 -Compress
        try {
            $resp = Invoke-RestMethod -Uri "$ApiBase/admin/suppliers/bulk" -Method Post `
                                      -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($body))
        }
        catch {
            Write-Host "  $label -> REQUEST FAILED: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.Exception.Response.StatusCode.value__ -eq 401) {
                throw 'Token expired or invalid. Mint a fresh one and re-run - already-imported rows will simply be skipped.'
            }
            continue
        }

        $c = @($resp.created).Count; $s = @($resp.skipped).Count; $e = @($resp.errors).Count
        $grand.created += $c; $grand.skipped += $s; $grand.errors += $e
        if ($e) { $allErrors += $resp.errors }

        $colour = if ($e) { 'Yellow' } else { 'Green' }
        Write-Host "  $label -> created $c, skipped $s, errors $e" -ForegroundColor $colour
    }
}

Write-Host "`n=== TOTAL ===" -ForegroundColor Cyan
Write-Host "created $($grand.created), skipped $($grand.skipped), errors $($grand.errors)"

if ($allErrors) {
    Write-Host "`nErrors by reason:" -ForegroundColor Yellow
    $allErrors | Group-Object reason | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("  {0,3}x {1}" -f $_.Count, $_.Name)
        $_.Group | Select-Object -First 5 | ForEach-Object { Write-Host "        $($_.slug)" }
    }
}

Write-Host "`nVerify with:  Invoke-RestMethod '$ApiBase/locations?limit=2000' | Measure-Object | Select-Object Count"
