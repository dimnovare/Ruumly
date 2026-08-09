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

# Catch the placeholder before firing a doomed request - a 401 stack trace reads
# like a broken script rather than "you forgot to paste the token".
if ($Token -eq 'PASTE_TOKEN' -or $Token.Length -lt 40) {
    Write-Host "That token is the placeholder, not a real one." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Open https://ruumly.eu logged in as admin, press F12, and paste this into the Console:"
    Write-Host "  await (await fetch('https://api.ruumly.eu/api/auth/refresh',{method:'POST',credentials:'include'})).json()" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Copy the accessToken value (a long eyJ... string) and re-run with it in place of PASTE_TOKEN."
    Write-Host "Tokens are short-lived; if a later batch 401s, mint a fresh one and re-run - imported rows are skipped."
    exit 1
}

# Hand-copying a ~400-char JWT out of a console pane silently drops characters, and
# the API can only answer that with a 401 - which reads as "expired" and sends you
# to mint another one that breaks the same way. Decode it here so a mangled paste is
# named as a mangled paste.
$segments = $Token -split '\.'
if ($segments.Count -ne 3) {
    Write-Host "That is not a complete JWT - expected 3 dot-separated segments, got $($segments.Count)." -ForegroundColor Yellow
    Write-Host "You have probably copied only part of it. Use the clipboard method below." -ForegroundColor Yellow
    exit 1
}
$payload = $segments[1].Replace('-', '+').Replace('_', '/')
$payload = $payload.PadRight([int][Math]::Ceiling($payload.Length / 4) * 4, '=')
try {
    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
}
catch {
    Write-Host "The token is CORRUPT - its payload will not decode. Characters were lost copying it." -ForegroundColor Red
    Write-Host ""
    Write-Host "Copy it to the clipboard instead of selecting it by hand. In the F12 Console on ruumly.eu:"
    Write-Host "  copy((await (await fetch('https://api.ruumly.eu/api/auth/refresh',{method:'POST',credentials:'include'})).json()).accessToken)" -ForegroundColor Cyan
    Write-Host "then run:  ./scripts/import-directory.ps1 -Token (Get-Clipboard) -DataDir '$DataDir'" -ForegroundColor Cyan
    exit 1
}
$expiresAt = [DateTimeOffset]::FromUnixTimeSeconds([long]$claims.exp).ToLocalTime()
if ($expiresAt -lt [DateTimeOffset]::Now) {
    Write-Host "That token expired at $expiresAt. Mint a fresh one." -ForegroundColor Yellow
    exit 1
}
$role = $claims.'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'
if ($role -ne 'Admin') {
    Write-Host "That token's role is '$role', not Admin - the bulk endpoint will reject it." -ForegroundColor Yellow
    exit 1
}
Write-Host "Token OK - $($claims.email), role $role, valid until $($expiresAt.ToString('HH:mm:ss'))" -ForegroundColor Green

$headers = @{ Authorization = "Bearer $Token"; 'Content-Type' = 'application/json' }
$grand = [ordered]@{ created = 0; skipped = 0; errors = 0 }
$allErrors = @()

# Suppliers.RegistryCode has a UNIQUE index, but a chain legitimately shares one
# code across every branch: NOLIKTAVA1 has 13 Riga locations on 44103124767,
# Boxrent and Daiktams.lt the same. Sent as-is, the first branch imports and every
# sibling dies with "database error - supplier not saved" - which reads like a bad
# row rather than a chain. Keep the code on the first branch, null it on the rest;
# the branches are still distinct suppliers by slug, address and coordinates.
$seenRegistryCodes = [System.Collections.Generic.HashSet[string]]::new()

# Seed from production, not just from this run. Deduping only within the batch still
# collides with a code already stored - which is what produced 5 "database error -
# supplier not saved" rows on the first full run. A collision against an existing
# supplier usually means the row IS that supplier under a new slug, so nulling the
# code lets genuinely-new branches through while the duplicate stays visible as a row
# you can eyeball.
try {
    $existing = Invoke-RestMethod -Uri "$ApiBase/admin/suppliers?page=1&limit=1000" -Headers $headers
    $rows = if ($existing.items) { $existing.items } else { $existing }
    $n = 0
    foreach ($s in $rows) {
        if (-not [string]::IsNullOrWhiteSpace($s.registryCode)) { [void]$seenRegistryCodes.Add([string]$s.registryCode); $n++ }
    }
    Write-Host "Seeded $n existing registry code(s) from production." -ForegroundColor DarkGray
}
catch {
    Write-Warning "Could not read existing suppliers to seed registry codes: $($_.Exception.Message)"
    Write-Warning "Continuing - expect 'database error - supplier not saved' on any code that already exists."
}
$deduped = 0
function Resolve-RegistryCode($row) {
    $code = $row.registryCode
    if ([string]::IsNullOrWhiteSpace($code)) { return $null }
    if ($seenRegistryCodes.Add([string]$code)) { return $code }
    $script:deduped++
    return $null
}

foreach ($country in $Countries) {
    # Glob rather than exact-name: research arrives in waves (import-LV.json,
    # import-LV-wave2.json, ...). Matching one exact filename silently ignored a
    # whole wave once - 363 rows that were never even attempted, with no warning,
    # because "file not found" never fired.
    $files = @(Get-ChildItem -Path $DataDir -Filter "import-$country*.json" -ErrorAction SilentlyContinue | Sort-Object Name)
    if (-not $files) { Write-Warning "no import-$country*.json in $DataDir - skipping $country"; continue }

    $rows = @()
    foreach ($f in $files) { $rows += @(Get-Content $f.FullName -Raw -Encoding utf8 | ConvertFrom-Json) }
    Write-Host "`n=== $country : $($rows.Count) rows from $($files.Count) file(s): $(($files.Name) -join ', ') ===" -ForegroundColor Cyan

    for ($i = 0; $i -lt $rows.Count; $i += $BatchSize) {
        $end   = [Math]::Min($i + $BatchSize, $rows.Count) - 1
        $batch = @($rows[$i..$end])
        $label = "$country rows $($i + 1)-$($end + 1)"

        if (-not $PSCmdlet.ShouldProcess($label, 'POST /admin/suppliers/bulk')) { continue }

        foreach ($row in $batch) { $row.registryCode = Resolve-RegistryCode $row }

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
if ($deduped) {
    Write-Host "$deduped registry code(s) nulled as chain-branch duplicates (expected - see comment at top)" -ForegroundColor DarkGray
}

if ($allErrors) {
    Write-Host "`nErrors by reason:" -ForegroundColor Yellow
    $allErrors | Group-Object reason | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("  {0,3}x {1}" -f $_.Count, $_.Name)
        $_.Group | Select-Object -First 5 | ForEach-Object { Write-Host "        $($_.slug)" }
    }
}

Write-Host "`nVerify with:  Invoke-RestMethod '$ApiBase/locations?limit=2000' | Measure-Object | Select-Object Count"
