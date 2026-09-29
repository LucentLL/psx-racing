# The colour protocol's frames (Editor\ColourShots.cs), for one build target.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\colour\colour-shots.ps1                       # WebGL (what ships)
#   powershell -ExecutionPolicy Bypass -File tools\colour\colour-shots.ps1 -Target StandaloneWindows64   # the decoded BASELINE
#   ... -Sets pred,protocol,ab,sweep   (default: all)
#   ... -NoCopy                        (the sandbox's code as it stands)
#   ... -Out <folder name>             frames into <sandbox>\Screenshots\<name> instead of colour_<target>
#
# THE COLOUR PASS'S A/B SWITCHES (C2-C4) ride in the environment, so a
# before and an after of the same code come from the same sandbox session
# with identically named frames (compare them with colour_stats.py compare
# / match / abpair):
#   $env:PSX_TONE='0'       the exposure and the one tone curve off (the old per-surface roll-offs)
#   $env:PSX_EMITKEY='0'    the halation and lens dirt keyed on brightness again
#   $env:PSX_HALATION='0'   the grade without its halation (the bloom on/off frames)
#   $env:PSX_SHOT_ALPHA='1' also write each frame's emitter mask as <frame>_alpha.png
# and, for step 4 of the pass (C5-C12):
#   $env:PSX_BEAM_SWEEP='0.12,0.22,0.35'   -Sets beam: the night spots lit at each low-beam
#                           intensity (CarLights.BeamIntensity) - colour_stats.py beam
#   $env:PSX_CONE='0'       the beam in the air (PSX/Beam) at this strength for the whole run
#   $env:PSX_LITFX='0'      the smoke and the snow unlit again (the C7 before-picture) - colour_stats.py fx
#   $env:PSX_ADAPT='0'      the eye held at 1 (no C10 adaptation)
#   $env:PSX_G1='1' / $env:PSX_COOLDARKS='1'   the owner's two open choices on for every frame
#                           (-Sets look renders both ways by itself) - see Scripts\LookChoices.cs
# Sets beyond the default: beam (the C5 sweep), fx (lit particles, each with
# its twin without), look (C11/C12 A/B, with the HUD).
#
# WHY THE TARGET: a texture is imported for the editor's active build target,
# and the release budget's 16-bit override exists only on WebGL, where WebGL2
# has no sRGB 565 and the texels are read as linear. The same editor frame is
# therefore two different pictures on the two targets - the ONLY difference
# being that import - which is exactly the Step-0 A/B the colour plan asks
# for. Switching a sandbox's target reimports its textures once.
#
# Needs a sandbox with built scenes (a scene build; nothing here bakes).
# Frames: <sandbox>\Screenshots\colour_<target>\cs_*.png, each with a .json
# sidecar; log cs_log.txt ending "COLOUR SHOTS OK". Then measure:
#   py tools\colour\colour_stats.py stats <sandbox>\Screenshots\colour_WebGL
#   py tools\colour\colour_stats.py gate  <...>\colour_WebGL <...>\colour_StandaloneWindows64
param([string]$Target = "WebGL", [string]$Sets = "", [switch]$NoCopy, [int]$MaxMinutes = 90, [string]$Out = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    robocopy "$src\Assets\Plugins" "$proj\Assets\Plugins" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
}
$env:PSX_COLOUR_SETS = $Sets
$out = if ($Out) { Join-Path $proj "Screenshots\$Out" } else { Join-Path $proj "Screenshots\colour_$Target" }
$env:PSX_COLOUR_OUT = if ($Out) { $out } else { "" }
foreach ($k in @("PSX_TONE", "PSX_EMITKEY", "PSX_HALATION", "PSX_SHOT_ALPHA", "PSX_BEAM_SWEEP", "PSX_CONE", "PSX_LITFX", "PSX_ADAPT", "PSX_G1", "PSX_COOLDARKS")) {
    $v = [Environment]::GetEnvironmentVariable($k)
    if ($v) { Write-Host "  $k=$v" }
}
Remove-Item "$out\cs_log.txt" -ErrorAction SilentlyContinue
$log = "$proj\colourshots_$Target.log"
# NO -nographics: every frame is a render read back.
$ok = Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,"-buildTarget",$Target,
    "-executeMethod","PSXRacing.EditorTools.ColourShots.Capture",
    "-logFile",$log,"-quit","-accept-apiupdate")
Select-String -Path $log -Pattern "error CS|Shader error in 'PSX/|NullReferenceException" -ErrorAction SilentlyContinue |
    Select-Object -First 12 | ForEach-Object { $_.Line }
if (Test-Path "$out\cs_log.txt") { Get-Content "$out\cs_log.txt" } else { Write-Host "no cs_log.txt - the capture never finished" -ForegroundColor Red; exit 1 }
if (-not (Select-String -Path "$out\cs_log.txt" -Pattern '^COLOUR SHOTS OK$' -CaseSensitive -Quiet)) { Write-Host "COLOUR SHOTS did not say OK" -ForegroundColor Red; exit 1 }
$n = @(Get-ChildItem "$out\cs_*.png").Count
Write-Host "$n colour frames in $out (target $Target)"
