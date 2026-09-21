param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64')][string[]]$Runtime = @('win-x64', 'linux-x64'),
    [ValidateSet('Development', 'Release')][string]$Profile = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if (Test-Path (Join-Path $projectRoot '.tools/dotnet/dotnet.exe')) { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$project = Join-Path $projectRoot 'src/GAIP.Desktop/GAIP.Desktop.csproj'

function Assert-SingleFilePublish([string]$outputPath, [string]$label) {
    if (Get-ChildItem -LiteralPath $outputPath -Recurse -Filter '*.pdb') {
        throw "Symboles PDB inattendus dans $label : $outputPath"
    }
    if (Get-ChildItem -LiteralPath $outputPath -File | Where-Object {
        $_.Extension -in '.dll', '.so', '.pdb' -or
        $_.Name -like '*.deps.json' -or
        $_.Name -like '*.runtimeconfig.json'
    }) {
        throw "Dépendances séparées inattendues dans la publication single-file $label : $outputPath"
    }
}

foreach ($rid in $Runtime) {
    $configuration = if ($Profile -eq 'Development') { 'Debug' } else { 'Release' }

    & $dotnetCommand publish $project -c $configuration -r $rid "-p:PublishProfile=$Profile"
    if ($LASTEXITCODE -ne 0) { throw "Échec de publication $Profile pour $rid" }

    if ($Profile -eq 'Release') {
        Assert-SingleFilePublish (Join-Path $projectRoot "artifacts/Release/$rid") "Release $rid"
    }
}
