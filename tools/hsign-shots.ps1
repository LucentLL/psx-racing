# THE HIGHWAY SIGNS PASS (2026-10-29): Charlotte's freeway guide signs
# photographed where the owner photographed the real ones (I-77 exit 11B) and
# at three more exits - CityPreview's NamedViews group "hsigns", shot by
# CityPreview.RunEyePlay in the Charlotte scene (its sky, hour and tile ring,
# no HUD, no car), each view through its own lens. With -Check, first the
# guide-sign census (HighwaySignsCheck.Run: every exit's legend, structure and
# coverage, then the sign audit) in the same Unity run.
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\hsign-shots.ps1 -Tag before
#   powershell -ExecutionPolicy Bypass -File tools\hsign-shots.ps1 -Tag after -Data -Check
#
# Code only (Scripts, Editor, Shaders /MIR). -Data also puts this worktree's
# charlotte_signs.bytes (content only: the sandbox keeps its .meta) and the
# guide atlas Art/City/Signs/CityGuide.png (with its .meta when this worktree
# has one; else the sandbox mints it - copy that back and commit it) in the
# sandbox. Never Resources or Art wholesale (the GUID trap). Honours the shared
# sandbox's AGENT_LOCK.txt. PNGs: <sandbox>\Screenshots\hsigns_<Tag>\.
param([string]$Tag = "run", [switch]$Data, [switch]$Check, [switch]$NoSync, [string]$Hours = "noon",
      [string]$Views = "hs_11b_drive,hs_11b_read,hs_11b_gore,hs_i277_4_drive,hs_i277_4_read,hs_i277_2b_drive,hs_i277_2b_read,hs_i85_36_drive,hs_i85_36_read,hs_i85_36_adv,hs_11b_close,hs_i277_4_close,hs_i277_2b_close,hs_i85_36_close,hs_i85_36_advclose,hs_11b_goresign,hs_i85_36_goresign,hs_11b_ramp,hs_i85_36_gore2,hs_i85_36_mile",
      [string]$Owner = "city-highway-signs", [int]$MaxMinutes = 50, [switch]$NoWatch)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

$lock = Join-Path $proj "AGENT_LOCK.txt"
while ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 90) {
    "waiting: $(Get-Content $lock -Raw)"; Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($Owner + " " + (Get-Date).ToString("s"))
$log = "$proj\hsigns_$Tag.log"
try {
    if (-not $NoSync) {
        foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
            robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
        }
    }
    if ($Data) {
        Copy-Item "$src\Assets\PSXRacing\Resources\charlotte_signs.bytes" "$proj\Assets\PSXRacing\Resources\charlotte_signs.bytes" -Force
        foreach ($f in @("Art\City\Signs\CityGuide.png", "Art\City\Signs\CityGuide_night.png")) {
            $from = "$src\Assets\PSXRacing\$f"
            if (-not (Test-Path $from)) { continue }
            Copy-Item $from "$proj\Assets\PSXRacing\$f" -Force
            if (Test-Path "$from.meta") { Copy-Item "$from.meta" "$proj\Assets\PSXRacing\$f.meta" -Force }
        }
    }
    $env:PSX_EYE_VIEWS = $Views
    $env:PSX_EYE_HOURS = $Hours
    $env:PSX_HSIGN_SHOTS = "1"
    Remove-Item "$proj\Screenshots\eye_hs_*.png" -ErrorAction SilentlyContinue
    Remove-Item $log -ErrorAction SilentlyContinue
    $method = if ($Check) { "PSXRacing.EditorTools.HighwaySignsCheck.Run" } else { "PSXRacing.EditorTools.CityPreview.RunEyePlay" }
    $uargs = @("-batchmode", "-projectPath", $proj, "-executeMethod", $method,
               "-logFile", $log, "-accept-apiupdate", "-quit", "-force-glcore")
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log $log -MaxMinutes $MaxMinutes -UnityArgs $uargs | Out-Null
}
finally {
    Remove-Item $lock -ErrorAction SilentlyContinue
}

Select-String -Path $log -Pattern "error CS|\[EyePlay\]|\[HSigns\]|Exception" | Select-Object -First 40 | ForEach-Object { $_.Line }
$out = "$proj\Screenshots\hsigns_$Tag"
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem "$proj\Screenshots" -Filter "eye_hs_*.png" -ErrorAction SilentlyContinue | ForEach-Object { Move-Item $_.FullName (Join-Path $out $_.Name) -Force; $_.Name }
if (Test-Path "$proj\city_hsigns.txt") { Get-Content "$proj\city_hsigns.txt" -TotalCount 60 }
