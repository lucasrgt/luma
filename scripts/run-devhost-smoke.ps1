$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$devOut = Join-Path $repoRoot "tools\Luma.DevHost\bin\Debug\net10.0"
$modsDir = Join-Path $devOut "mods"
$sampleOut = Join-Path $repoRoot "samples\Luma.SampleMod\bin\Debug\net10.0"
$showcaseOut = Join-Path $repoRoot "showcase\Luma.MegaCrusherShowcase\bin\Debug\net10.0"
$logPath = Join-Path $devOut "luma.log"

dotnet build (Join-Path $repoRoot "Luma.slnx")
if ($LASTEXITCODE -ne 0) {
    throw "Solution build failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $modsDir) {
    Remove-Item -LiteralPath $modsDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $modsDir | Out-Null

Copy-Item -LiteralPath (Join-Path $sampleOut "Luma.SampleMod.dll") -Destination $modsDir -Force
Copy-Item -LiteralPath (Join-Path $showcaseOut "Luma.MegaCrusherShowcase.dll") -Destination $modsDir -Force

$sampleAssets = Join-Path $modsDir "luma.sample\assets"
$showcaseAssets = Join-Path $modsDir "luma.showcase\assets"
New-Item -ItemType Directory -Force -Path $sampleAssets | Out-Null
New-Item -ItemType Directory -Force -Path $showcaseAssets | Out-Null
Copy-Item -Path (Join-Path $sampleOut "assets\*") -Destination $sampleAssets -Recurse -Force
Copy-Item -Path (Join-Path $showcaseOut "assets\*") -Destination $showcaseAssets -Recurse -Force

if (Test-Path $logPath) {
    Remove-Item -LiteralPath $logPath -Force
}

dotnet (Join-Path $devOut "Luma.DevHost.dll")

Get-Content $logPath -Tail 40
