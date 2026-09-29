# THE 16-BIT DECODE AUDIT (Editor\TexDecodeAudit.cs; the colour plan's C1b).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\colour\texdecode-audit.ps1 [-NoCopy] [-Target StandaloneWindows64]
#
# The 16-bit texture set is imported as linear data and decoded in the PSX
# shaders, switched per material at runtime by Scripts\PSXTexDecode.cs. A
# material the switch misses draws its texels raw (up to x9 too bright on
# the darkest). This proves none is missed: the imports and the materials on
# disk (edit mode), the grey card on the GPU, then EVERY scene of the build
# loaded in play mode as the player loads it, every renderer's materials
# against the rule, the whole car roster and every Resources prefab through
# the loader's own door, and no UI graphic on a texture of the set.
#
# An audit, not a play test: it runs hidden and batch (it needs a graphics
# device for the grey card, so no -nographics). Needs a sandbox with built
# scenes. Report: <sandbox>\PSXRacing_texdecode_audit.txt, ending
# "TEXDECODE AUDIT OK". Exit 0 = nothing missed.
param([switch]$NoCopy, [string]$Target = "", [int]$MaxMinutes = 40)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}
$report = "$proj\PSXRacing_texdecode_audit.txt"
# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item $report -ErrorAction SilentlyContinue
$log = "$proj\texdecode_audit.log"
$args2 = @("-batchmode","-projectPath",$proj,
           "-executeMethod","PSXRacing.EditorTools.TexDecodeAudit.Run",
           "-logFile",$log,"-accept-apiupdate")
if ($Target) { $args2 += @("-buildTarget", $Target) }
# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs $args2 | Out-Null
Select-String -Path $log -Pattern "error CS|Shader error in 'PSX/" -ErrorAction SilentlyContinue | Select-Object -First 12 | ForEach-Object { $_.Line }
if (-not (Test-Path $report)) {
    Write-Host "NO REPORT - the audit threw or never finished. Tail of ${log}:" -ForegroundColor Red
    Get-Content $log -Tail 40
    exit 1
}
Get-Content $report
if (Select-String -Path $report -Pattern '^TEXDECODE AUDIT OK$' -CaseSensitive -Quiet) { exit 0 }
exit 1
