param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        & dotnet build SMILE.sln -c Debug --no-restore -nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    & dotnet test tests/SMILE.Tests/SMILE.Tests.csproj `
        -c Debug `
        --no-build `
        --no-restore `
        --filter 'TestCategory=TextGameFoundation' `
        -nologo
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
