param([ValidateSet('win-x64', 'linux-x64', 'linux-arm64')][string[]]$Runtime = @('win-x64', 'linux-x64'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if (Test-Path (Join-Path $projectRoot '.tools/dotnet/dotnet.exe')) { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
foreach ($rid in $Runtime) {
    & $dotnetCommand publish (Join-Path $projectRoot 'src/GAIP.Desktop/GAIP.Desktop.csproj') -c Release -r $rid --self-contained true -p:PublishSingleFile=false -o (Join-Path $projectRoot "artifacts/$rid")
    if ($LASTEXITCODE -ne 0) { throw "Échec de publication pour $rid" }
}
