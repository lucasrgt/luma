param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo",
    [string]$Configuration = "Debug",
    [switch]$IncludeSampleMod
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resolvedGameDir = Resolve-Path $GameDir
$project = Join-Path $repoRoot "src\Luma.AllumeriaLoader\Luma.AllumeriaLoader.csproj"
$output = Join-Path $repoRoot "src\Luma.AllumeriaLoader\bin\$Configuration\net10.0"
$sampleProject = Join-Path $repoRoot "samples\Luma.SampleMod\Luma.SampleMod.csproj"
$sampleOutput = Join-Path $repoRoot "samples\Luma.SampleMod\bin\$Configuration\net10.0"
$mods = Join-Path $resolvedGameDir "mods"

dotnet build $project -c $Configuration -p:AllumeriaGameDir="$resolvedGameDir"
if ($LASTEXITCODE -ne 0) {
    throw "Loader build failed with exit code $LASTEXITCODE."
}

if ($IncludeSampleMod) {
    dotnet build $sampleProject -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Sample mod build failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Force -Path $mods | Out-Null

$filesToCopy = @(
    "Loader.dll",
    "Loader.deps.json",
    "Luma.Abstractions.dll",
    "Luma.Runtime.dll"
)

foreach ($fileName in $filesToCopy) {
    Copy-Item -LiteralPath (Join-Path $output $fileName) -Destination $mods -Force
}

if ($IncludeSampleMod) {
    Copy-Item -LiteralPath (Join-Path $sampleOutput "Luma.SampleMod.dll") -Destination $mods -Force

    $sampleAssets = Join-Path $mods "luma.sample\assets"
    New-Item -ItemType Directory -Force -Path $sampleAssets | Out-Null
    Copy-Item -Path (Join-Path $sampleOutput "assets\*") -Destination $sampleAssets -Recurse -Force
}

Get-ChildItem -Force $mods | Where-Object {
    $_.Name -in ($filesToCopy + @("Luma.SampleMod.dll", "luma.sample"))
} | Select-Object Name, Length, LastWriteTime
