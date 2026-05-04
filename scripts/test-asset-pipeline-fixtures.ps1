param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$fixtureDir = Join-Path $repoRoot "tests\fixtures\asset-pipeline"
$outputDir = Join-Path $repoRoot "run\asset-pipeline-fixtures"
$pipelineProject = Join-Path $repoRoot "tools\Luma.AssetPipeline\Luma.AssetPipeline.csproj"
$pipelineExe = Join-Path $repoRoot "tools\Luma.AssetPipeline\bin\$Configuration\net10.0\Luma.AssetPipeline.exe"

$objPath = Join-Path $fixtureDir "tiny_rotor.obj"
$animPath = Join-Path $fixtureDir "tiny_rotor.anim.json"
$texturePath = Join-Path $fixtureDir "tiny_rotor.png"
$singleOutput = Join-Path $outputDir "tiny_rotor.bbmodel.json"
$chunkOutput = Join-Path $outputDir "tiny_rotor.chunks.json"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (-not (Test-Path -LiteralPath $texturePath)) {
    $pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="
    [System.IO.File]::WriteAllBytes($texturePath, [Convert]::FromBase64String($pngBase64))
}

dotnet build $pipelineProject -c $Configuration

& $pipelineExe model convert `
    $objPath `
    --target allumeria `
    --animation $animPath `
    --texture $texturePath `
    --output $singleOutput `
    --report
if ($LASTEXITCODE -ne 0) {
    throw "Single-model fixture export failed with exit code $LASTEXITCODE."
}

& $pipelineExe model convert `
    $objPath `
    --target allumeria `
    --animation $animPath `
    --texture $texturePath `
    --output $chunkOutput `
    --chunks `
    --light-chunks 2 `
    --report
if ($LASTEXITCODE -ne 0) {
    throw "Chunked fixture export failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath (Join-Path $GameDir "Allumeria.dll")) {
    & $pipelineExe validate-allumeria-bbmodel $GameDir $singleOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Single-model fixture validation failed with exit code $LASTEXITCODE."
    }

    & $pipelineExe validate-allumeria-bbmodel $GameDir $chunkOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Chunked fixture validation failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host "Skipping native Allumeria validation. Game directory not found: $GameDir"
}

Get-ChildItem -LiteralPath $outputDir | Select-Object Name, Length, LastWriteTime
