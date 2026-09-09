[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.3.0',
    [switch]$SkipPublish
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'publish\Tugle'
$compilerCandidates = @(
    @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
)

if ($compilerCandidates.Count -eq 0) {
    throw 'Inno Setup 6 is required. Install it with: winget install --id JRSoftware.InnoSetup --exact'
}

if (-not $SkipPublish) {
    dotnet publish (Join-Path $projectRoot 'Tugle.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tugle publish failed.' }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'Tugle.exe'))) {
    throw "Published Tugle.exe was not found at $publishDirectory."
}

& ($compilerCandidates[0]) "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'Tugle.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
