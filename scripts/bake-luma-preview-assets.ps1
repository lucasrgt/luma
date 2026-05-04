param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo",
    [switch]$ChunkedMegaCrusher
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$modelDir = Join-Path $repoRoot "samples\Luma.MegaCrusherProbe\assets\models"
$pipelineProject = Join-Path $repoRoot "tools\Luma.AssetPipeline"
$gameAssetDir = Join-Path $GameDir "mods\luma\assets\models"
$texturePath = Join-Path $modelDir "retronism_megacrusher.png"

function Invoke-BbModelBake {
    param(
        [string]$ObjPath,
        [string]$AnimationPath,
        [string]$TexturePath,
        [string]$OutputPath,
        [string[]]$ExtraArgs = @()
    )

    $pipelineArgs = @(
        "bbmodel"
        $ObjPath
        $AnimationPath
        $TexturePath
        $OutputPath
    ) + $ExtraArgs

    dotnet run --project $pipelineProject -- @pipelineArgs
}

function Copy-JsonObject {
    param([object]$Value)

    return $Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json
}

function Convert-Vector {
    param(
        [object[]]$Value,
        [double[]]$SourcePivot,
        [double[]]$TargetPivot,
        [double]$Scale
    )

    return @(
        [Math]::Round($TargetPivot[0] + (([double]$Value[0] - $SourcePivot[0]) * $Scale), 6)
        [Math]::Round($TargetPivot[1] + (([double]$Value[1] - $SourcePivot[1]) * $Scale), 6)
        [Math]::Round($TargetPivot[2] + (([double]$Value[2] - $SourcePivot[2]) * $Scale), 6)
    )
}

function Convert-ElementCoordinates {
    param(
        [object]$Element,
        [double[]]$SourcePivot,
        [double[]]$TargetPivot,
        [double]$Scale
    )

    $clone = Copy-JsonObject $Element
    $properties = @($clone.PSObject.Properties.Name)

    if ($properties -contains "from") {
        $clone.from = [object[]](Convert-Vector $clone.from $SourcePivot $TargetPivot $Scale)
    }

    if ($properties -contains "to") {
        $clone.to = [object[]](Convert-Vector $clone.to $SourcePivot $TargetPivot $Scale)
    }

    if ($properties -contains "origin" -and $clone.type -ne "mesh") {
        $clone.origin = [object[]](Convert-Vector $clone.origin $SourcePivot $TargetPivot $Scale)
    }

    if ($properties -contains "vertices") {
        foreach ($vertex in $clone.vertices.PSObject.Properties) {
            $vertex.Value = [object[]](Convert-Vector $vertex.Value $SourcePivot $TargetPivot $Scale)
        }
    }

    return $clone
}

function Find-OutlinerNode {
    param(
        [object[]]$Nodes,
        [string]$Name
    )

    foreach ($node in $Nodes) {
        if ($node -is [string]) {
            continue
        }

        if ($node.name -eq $Name) {
            return $node
        }

        if ($null -ne $node.children) {
            $child = Find-OutlinerNode -Nodes @($node.children) -Name $Name
            if ($null -ne $child) {
                return $child
            }
        }
    }

    return $null
}

function Export-TestRotorFromMegaCrusher {
    param(
        [string]$MegaModelPath,
        [string]$OutputPath
    )

    $source = Get-Content -LiteralPath $MegaModelPath -Raw | ConvertFrom-Json
    $sourcePivot = @(2.5, 24.0, 24.0)
    $targetPivot = @(8.0, 8.0, 8.0)
    $scale = 0.75

    $elementNames = @("turbine_l_axle", "turbine_l_spinner")
    $elementNames += 0..11 | ForEach-Object { "turbine_l_blade_$_" }
    $elementNameSet = @{}
    foreach ($name in $elementNames) {
        $elementNameSet[$name] = $true
    }

    $elements = @(
        $source.elements |
            Where-Object { $elementNameSet.ContainsKey($_.name) } |
            ForEach-Object { Convert-ElementCoordinates $_ $sourcePivot $targetPivot $scale }
    )

    $turbineNode = Find-OutlinerNode -Nodes @($source.outliner) -Name "turbine_l"
    if ($null -eq $turbineNode) {
        throw "Could not find turbine_l outliner node in $MegaModelPath"
    }

    $outlinerNode = Copy-JsonObject $turbineNode
    $outlinerNode.origin = [object[]](Convert-Vector $outlinerNode.origin $sourcePivot $targetPivot $scale)

    $sourceAnimation = @($source.animations)[0]
    $sourceAnimator = $sourceAnimation.animators.PSObject.Properties[$turbineNode.uuid].Value
    if ($null -eq $sourceAnimator) {
        throw "Could not find turbine_l animator in $MegaModelPath"
    }

    $animators = [ordered]@{}
    $animators[$turbineNode.uuid] = Copy-JsonObject $sourceAnimator

    $animation = [ordered]@{
        uuid = "luma-test-rotor-spin"
        name = "spin"
        loop = "loop"
        override = $false
        length = $sourceAnimation.length
        snapping = $sourceAnimation.snapping
        anim_time_update = ""
        blend_weight = ""
        start_delay = ""
        loop_delay = ""
        animators = $animators
    }

    $model = [ordered]@{
        name = "test_rotor.bbmodel"
        resolution = $source.resolution
        elements = $elements
        outliner = @($outlinerNode)
        animations = @($animation)
    }

    $model | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Host "Wrote $OutputPath"
    Write-Host "  source: turbine_l from $(Split-Path -Leaf $MegaModelPath)"
    Write-Host "  elements: $($elements.Count)"
    Write-Host "  animations: 1"
}

New-Item -ItemType Directory -Force -Path $gameAssetDir | Out-Null

$megaOutput = if ($ChunkedMegaCrusher) {
    Join-Path $modelDir "mega_crusher.chunks.json"
} else {
    Join-Path $modelDir "mega_crusher.bbmodel.json"
}

$megaArgs = if ($ChunkedMegaCrusher) {
    @("--chunks", "--partial-rig", "--light-chunks", "9")
} else {
    @("--partial-rig")
}

Invoke-BbModelBake `
    -ObjPath (Join-Path $modelDir "MegaCrusher.obj") `
    -AnimationPath (Join-Path $modelDir "MegaCrusher.anim.json") `
    -TexturePath $texturePath `
    -OutputPath $megaOutput `
    -ExtraArgs $megaArgs

Copy-Item -LiteralPath $megaOutput -Destination $gameAssetDir -Force
Copy-Item -LiteralPath $texturePath -Destination $gameAssetDir -Force

if ($ChunkedMegaCrusher) {
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

$rotorSourceOutput = $megaOutput
if ($ChunkedMegaCrusher) {
    $rotorSourceOutput = Join-Path $modelDir "mega_crusher.bbmodel.json"
    Invoke-BbModelBake `
        -ObjPath (Join-Path $modelDir "MegaCrusher.obj") `
        -AnimationPath (Join-Path $modelDir "MegaCrusher.anim.json") `
        -TexturePath $texturePath `
        -OutputPath $rotorSourceOutput `
        -ExtraArgs @("--partial-rig")

    Copy-Item -LiteralPath $rotorSourceOutput -Destination $gameAssetDir -Force
}

$rotorOutput = Join-Path $modelDir "test_rotor.bbmodel.json"
Export-TestRotorFromMegaCrusher -MegaModelPath $rotorSourceOutput -OutputPath $rotorOutput
Copy-Item -LiteralPath $rotorOutput -Destination $gameAssetDir -Force

Get-ChildItem -Force $gameAssetDir | Select-Object Name, Length, LastWriteTime
