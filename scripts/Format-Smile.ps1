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
    $projectFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'examples') -File -Recurse |
        Where-Object { $_.Extension -in '.smileproj', '.smilelibproj' } | Sort-Object FullName)
    $projectSources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($projectFile in $projectFiles) {
        [xml] $projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
        foreach ($sourceItem in $projectXml.SelectNodes('/*[local-name()="SmileProject"]/*[local-name()="ItemGroup"]/*[local-name()="SmileSource"]')) {
            $includedPath = [System.IO.Path]::GetFullPath((Join-Path $projectFile.DirectoryName $sourceItem.GetAttribute('Include')))
            [void] $projectSources.Add($includedPath)
        }
    }
    $Path = @(
        $projectFiles | Select-Object -ExpandProperty FullName
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'examples') -Filter '*.smile' -File -Recurse |
            Where-Object { -not $projectSources.Contains($_.FullName) } |
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

& dotnet build $cliProject --configuration Debug -nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($sourcePath in $Path) {
    $resolvedPath = (Resolve-Path -LiteralPath $sourcePath).Path
    & dotnet run --project $cliProject --configuration Debug --no-build --no-launch-profile -- $resolvedPath $mode
    if ($LASTEXITCODE -ne 0) {
        $exitCode = $LASTEXITCODE
    }
}

exit $exitCode
