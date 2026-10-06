<#
.SYNOPSIS
  Creates .env from .env.example with freshly generated random secrets, for Docker Compose.
.DESCRIPTION
  Fills in POSTGRES_PASSWORD, JWT_SIGNING_KEY and, if ADMIN_EMAIL is blank, a development admin
  (admin@example.test with a random password). Values already present in an existing .env are never replaced
  unless -Force is given. The secrets are written only to .env (which Git ignores); they are not printed.
#>
param([switch]$Force)

$root = Split-Path $PSScriptRoot -Parent
$example = Join-Path $root '.env.example'
$target = Join-Path $root '.env'

if ((Test-Path $target) -and -not $Force) {
    Write-Host ".env already exists, so it was left unchanged. Run with -Force to replace it."
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


