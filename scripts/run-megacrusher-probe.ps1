$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$devOut = Join-Path $repoRoot "tools\Luma.DevHost\bin\Debug\net10.0"
$modsDir = Join-Path $devOut "mods"
$probeOut = Join-Path $repoRoot "samples\Luma.MegaCrusherProbe\bin\Debug\net10.0"
$logPath = Join-Path $devOut "luma.log"

dotnet build (Join-Path $repoRoot "Luma.slnx")

New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $modsDir "assets") | Out-Null

Copy-Item (Join-Path $probeOut "Luma.MegaCrusherProbe.dll") (Join-Path $modsDir "Luma.MegaCrusherProbe.dll") -Force
Copy-Item (Join-Path $probeOut "assets\*") (Join-Path $modsDir "assets") -Recurse -Force

if (Test-Path $logPath) {
    Remove-Item -LiteralPath $logPath -Force
}

dotnet (Join-Path $devOut "Luma.DevHost.dll")

Get-Content $logPath -Tail 40
