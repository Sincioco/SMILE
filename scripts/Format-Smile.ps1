[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]] $Path,

    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $repositoryRoot 'src\SMILE.Cli\SMILE.Cli.csproj'

if (-not $Path) {
    $Path = @(
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'examples') -Filter '*.smile' -File -Recurse |
            Sort-Object FullName |
            Select-Object -ExpandProperty FullName
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests\CoreBasicParity') -Filter '*.smile' -File |
            Sort-Object FullName |
            Select-Object -ExpandProperty FullName
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests\CoreBasic2Parity') -Filter '*.smile' -File |
            Sort-Object FullName |
            Select-Object -ExpandProperty FullName
    )
}

if (-not $Path) {
    Write-Error 'No SMILE source files were selected.'
    exit 2
}

$mode = if ($Check) { '--check' } else { '--format' }
$exitCode = 0

foreach ($sourcePath in $Path) {
    $resolvedPath = (Resolve-Path -LiteralPath $sourcePath).Path
    & dotnet run --project $cliProject --configuration Debug --no-launch-profile -- $resolvedPath $mode
    if ($LASTEXITCODE -ne 0) {
        $exitCode = $LASTEXITCODE
    }
}

exit $exitCode
