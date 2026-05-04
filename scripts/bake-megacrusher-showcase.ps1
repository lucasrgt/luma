param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo",
    [switch]$Chunked
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$modelDir = Join-Path $repoRoot "showcase\Luma.MegaCrusherShowcase\assets\models"
$outputModel = if ($Chunked) {
    Join-Path $modelDir "mega_crusher.chunks.json"
} else {
    Join-Path $modelDir "mega_crusher.bbmodel.json"
}
$gameAssetDir = Join-Path $GameDir "mods\luma.showcase\assets\models"

$pipelineArgs = @(
    "bbmodel"
    (Join-Path $modelDir "MegaCrusher.obj")
    (Join-Path $modelDir "MegaCrusher.anim.json")
    (Join-Path $modelDir "retronism_megacrusher.png")
    $outputModel
)

if ($Chunked) {
    $pipelineArgs += "--chunks"
    $pipelineArgs += "--partial-rig"
    $pipelineArgs += "--light-chunks"
    $pipelineArgs += "9"
} else {
    $pipelineArgs += "--partial-rig"
}

dotnet run --project (Join-Path $repoRoot "tools\Luma.AssetPipeline") -- @pipelineArgs

New-Item -ItemType Directory -Force -Path $gameAssetDir | Out-Null
Copy-Item -LiteralPath $outputModel -Destination $gameAssetDir -Force
Copy-Item -LiteralPath (Join-Path $modelDir "retronism_megacrusher.png") -Destination $gameAssetDir -Force

if ($Chunked) {
    Get-ChildItem -LiteralPath $gameAssetDir -Filter "mega_crusher.chunk_*.bbmodel.json" -File |
        Remove-Item -Force
    Copy-Item -Path (Join-Path $modelDir "mega_crusher.chunk_*.bbmodel.json") -Destination $gameAssetDir -Force
} else {
    $staleManifest = Join-Path $gameAssetDir "mega_crusher.chunks.json"
    if (Test-Path -LiteralPath $staleManifest) {
        Remove-Item -LiteralPath $staleManifest -Force
    }

    Get-ChildItem -LiteralPath $gameAssetDir -Filter "mega_crusher.chunk_*.bbmodel.json" -File |
        Remove-Item -Force
}

Get-ChildItem -Force $gameAssetDir | Select-Object Name, Length, LastWriteTime
