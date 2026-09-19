param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64')][string[]]$Runtime = @('win-x64', 'linux-x64'),
    [ValidateSet('Development', 'Release')][string]$Profile = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if (Test-Path (Join-Path $projectRoot '.tools/dotnet/dotnet.exe')) { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
foreach ($rid in $Runtime) {
    $configuration = if ($Profile -eq 'Development') { 'Debug' } else { 'Release' }
    & $dotnetCommand publish (Join-Path $projectRoot 'src/GAIP.Desktop/GAIP.Desktop.csproj') -c $configuration -r $rid "-p:PublishProfile=$Profile"
    if ($LASTEXITCODE -ne 0) { throw "Échec de publication pour $rid" }
    if ($Profile -eq 'Release') {
        $outputPath = Join-Path $projectRoot "artifacts/Release/$rid"
        if (Get-ChildItem -LiteralPath $outputPath -Recurse -Filter '*.pdb') { throw "Symboles PDB inattendus dans $outputPath" }
        if (Get-ChildItem -LiteralPath $outputPath -File | Where-Object { $_.Extension -in '.dll', '.so', '.pdb' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' }) {
            throw "Dépendances séparées inattendues dans la publication single-file $outputPath"
        }
    }
}
