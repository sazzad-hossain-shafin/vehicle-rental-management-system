<#
.SYNOPSIS
  Creates .env from .env.example with freshly generated random secrets, for Docker Compose.
.DESCRIPTION
  Fills in POSTGRES_PASSWORD, POSTGRES_APP_PASSWORD (the API's restricted database role), JWT_SIGNING_KEY and, if ADMIN_EMAIL is blank, a development admin
  (admin@example.test with a random password). Values already present in an existing .env are never replaced
  unless -Force is given. -Upgrade adds only the settings an older .env is missing. The secrets are written only to .env (which Git ignores); they are not printed.
#>
param([switch]$Force, [switch]$Upgrade)

$root = Split-Path $PSScriptRoot -Parent
$example = Join-Path $root '.env.example'
$target = Join-Path $root '.env'

if ((Test-Path $target) -and $Upgrade) {
    # Add only what an older .env is missing; every existing value, including secrets, stays as it is. Prints names only.
    $existing = Get-Content $target
    $added = @()
    if (-not ($existing -match '^POSTGRES_APP_USER=.+')) {
        $existing = @($existing | Where-Object { $_ -notmatch '^POSTGRES_APP_USER=\s*$' }) + 'POSTGRES_APP_USER=vehiclerental_app'
        $added += 'POSTGRES_APP_USER'
    }
    if (-not ($existing -match '^POSTGRES_APP_PASSWORD=.+')) {
        $bytes = New-Object byte[] 24
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $existing = @($existing | Where-Object { $_ -notmatch '^POSTGRES_APP_PASSWORD=\s*$' }) + ('POSTGRES_APP_PASSWORD=' + (($bytes | ForEach-Object { $_.ToString('x2') }) -join ''))
        $added += 'POSTGRES_APP_PASSWORD'
    }
    if ($added.Count -gt 0) {
        Set-Content -Path $target -Value $existing -Encoding ascii
        Write-Host "Added to .env: $($added -join ', ')"
    } else {
        Write-Host ".env already has everything; nothing changed."
    }
    exit 0
}

if ((Test-Path $target) -and -not $Force) {
    Write-Host ".env already exists, so it was left unchanged. Run with -Force to replace it, or -Upgrade to add only missing settings."
    exit 0
}

# Works on Windows PowerShell 5.1 and PowerShell 7.
function New-RandomBytes([int]$count) {
    $bytes = New-Object byte[] $count
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ,$bytes
}

function New-RandomHex([int]$count) {
    $bytes = New-RandomBytes $count
    ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
}

$values = @{
    POSTGRES_PASSWORD = New-RandomHex 24
    POSTGRES_APP_PASSWORD = New-RandomHex 24
    JWT_SIGNING_KEY   = [Convert]::ToBase64String((New-RandomBytes 48))
    ADMIN_EMAIL       = 'admin@example.test'
    ADMIN_PASSWORD    = 'Aa1-' + (New-RandomHex 10)
}

$lines = foreach ($line in Get-Content $example) {
    if ($line -match '^([A-Z_]+)=$' -and $values.ContainsKey($Matches[1])) {
        "$($Matches[1])=$($values[$Matches[1]])"
    } else {
        $line
    }
}

Set-Content -Path $target -Value $lines -Encoding ascii
Write-Host "Created .env with random secrets. The development admin is $($values.ADMIN_EMAIL); its password is in .env."


