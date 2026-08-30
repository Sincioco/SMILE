[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $Smile2Root = ''
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$Smile2Root = if ([string]::IsNullOrWhiteSpace($Smile2Root)) {
    Join-Path $repository '..\SMILE 2.0'
} else {
    $Smile2Root
}
$smile2 = [IO.Path]::GetFullPath($Smile2Root)
$profile1 = Get-Content -Raw (Join-Path $repository 'tests\CoreBasicParity\profile.json') | ConvertFrom-Json
$profile2 = Get-Content -Raw (Join-Path $repository 'tests\CoreBasic2Parity\profile.json') | ConvertFrom-Json
if ($profile1.authority.commit -ne $profile2.authority.commit) {
    throw 'Core BASIC parity manifests must pin the same SMILE 2.0 authority commit.'
}
$authorityCommit = $profile2.authority.commit

$beforeCommit = (& git -C $smile2 rev-parse HEAD).Trim()
$beforeStatus = (& git -C $smile2 status --porcelain) -join "`n"
if ($beforeCommit -ne $authorityCommit) {
    throw "SMILE 2.0 is at $beforeCommit; parity is frozen to $authorityCommit."
}
if ($beforeStatus) {
    throw 'SMILE 2.0 must be clean before the read-only parity check.'
}

$previousSmile2Root = $env:SMILE2_ROOT
try {
    $env:SMILE2_ROOT = $smile2
    & dotnet test (Join-Path $repository 'tests\SMILE.Tests\SMILE.Tests.csproj') `
        -c $Configuration `
        --filter 'TestCategory=CoreBasicParity|TestCategory=CoreBasic2Parity' `
        -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Core BASIC parity tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:SMILE2_ROOT = $previousSmile2Root
}

$afterCommit = (& git -C $smile2 rev-parse HEAD).Trim()
$afterStatus = (& git -C $smile2 status --porcelain) -join "`n"
if ($afterCommit -ne $beforeCommit -or $afterStatus) {
    throw 'The authoritative SMILE 2.0 repository changed during parity verification.'
}

Write-Host "Core BASIC Profiles 1 and 2 parity passed against SMILE 2.0 $afterCommit."
