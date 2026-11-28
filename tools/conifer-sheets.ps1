# The conifer round's sheets from ConiferShots' frames (labelled, EDITOR): per species, beside the NM beech
# (rows = form x SUN AND SHADE off/on, columns = 3 / 8 / 30 / 80 m, one sheet per hour); the road poses
# (models | cards); the 50 m outlines; the approach frames at the swaps.
#   powershell -ExecutionPolicy Bypass -File tools\conifer-sheets.ps1 -Dir <proof folder>
param([Parameter(Mandatory = $true)][string]$Dir)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$font = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Bold)
$small = New-Object System.Drawing.Font("Segoe UI", 12)
$ink = [System.Drawing.Brushes]::Black
$cw = 480; $ch = 270; $lab = 26

function New-Sheet([int]$cols, [int]$rows, [string]$title, [int]$labelW = 0) {
    $w = $labelW + $cols * ($cw + 4) + 4; $h = 44 + $rows * ($ch + $lab + 4)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(244, 242, 236))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawString($title, $font, $ink, 8, 8)
    return @($bmp, $g)
}
function Put($g, [string]$file, [int]$col, [int]$row, [string]$label, [int]$labelW = 0) {
    $x = $labelW + 4 + $col * ($cw + 4); $y = 44 + $row * ($ch + $lab + 4)
    $g.DrawString($label, $small, $ink, $x, $y + 2)
    if (Test-Path $file) {
        $img = [System.Drawing.Image]::FromFile($file)
        $g.DrawImage($img, $x, $y + $lab, $cw, $ch); $img.Dispose()
    } else { $g.DrawString("(no frame)", $small, $ink, $x + 10, $y + $lab + 10) }
}
function Save($pair, [string]$name) {
    $pair[1].Dispose()
    $enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $ep = New-Object System.Drawing.Imaging.EncoderParameters 1
    $ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 88L
    $pair[0].Save((Join-Path $Dir $name), $enc, $ep); $pair[0].Dispose()
    "sheet: $name"
}

$species = "EasternHemlock", "EasternWhitePine", "RedSpruce", "FraserFir"
foreach ($sp in $species) {
    $forms = @(Get-ChildItem $Dir -Filter "EDITOR_BESIDE_${sp}_*_Noon_LLOFF_03m.jpg" | ForEach-Object { ($_.Name -replace "^EDITOR_BESIDE_", "") -replace "_Noon_LLOFF_03m.jpg$", "" } | Sort-Object)
    if ($forms.Count -eq 0) { continue }
    foreach ($hour in "Noon", "Dusk", "Night") {
        $s = New-Sheet 4 ($forms.Count * 2) "EDITOR  $sp beside NM beech_tree_00_A (right of the conifer) and beech_tree_02 (left), $hour, stage slope; rows: form x TREES: SUN AND SHADE off / on"
        $r = 0
        foreach ($f in $forms) {
            foreach ($ll in "OFF", "ON") {
                $c = 0
                foreach ($m in "03", "08", "30", "80") {
                    Put $s[1] (Join-Path $Dir "EDITOR_BESIDE_${f}_${hour}_LL${ll}_${m}m.jpg") $c $r "$f  $hour  sun+shade $ll  $([int]$m) m"
                    $c++
                }
                $r++
            }
        }
        Save $s "SHEET_${sp}_${hour}.jpg"
    }
}
# road poses: models | cards
$poses = @(Get-ChildItem $Dir -Filter "EDITOR_ROAD_*_MODELS.jpg" | ForEach-Object { $_.Name -replace "^EDITOR_ROAD_", "" -replace "_MODELS.jpg$", "" } | Sort-Object)
if ($poses.Count) {
    $s = New-Sheet 2 $poses.Count "EDITOR  From the road, BeechGap: the conifer models (left) | the crossed cards they replace (right), same pose"
    $r = 0
    foreach ($p in $poses) { Put $s[1] (Join-Path $Dir "EDITOR_ROAD_${p}_MODELS.jpg") 0 $r "$p  MODELS"; Put $s[1] (Join-Path $Dir "EDITOR_ROAD_${p}_CARDS.jpg") 1 $r "$p  CARDS"; $r++ }
    Save $s "SHEET_road_models_vs_cards.jpg"
}
$outl = @(Get-ChildItem $Dir -Filter "EDITOR_OUTLINE50_*_MODELS.jpg" | ForEach-Object { $_.Name -replace "^EDITOR_OUTLINE50_", "" -replace "_MODELS.jpg$", "" } | Sort-Object)
if ($outl.Count) {
    $s = New-Sheet 2 $outl.Count "EDITOR  The outline at 40-60 m from the road, driving height (1.2 m), 55 deg lens: models | cards"
    $r = 0
    foreach ($p in $outl) { Put $s[1] (Join-Path $Dir "EDITOR_OUTLINE50_${p}_MODELS.jpg") 0 $r "$p  MODELS"; Put $s[1] (Join-Path $Dir "EDITOR_OUTLINE50_${p}_CARDS.jpg") 1 $r "$p  CARDS"; $r++ }
    Save $s "SHEET_outline50.jpg"
}
foreach ($sp in $species) {
    $ap = @(Get-ChildItem $Dir -Filter "EDITOR_AP_${sp}_*.jpg" | Sort-Object Name -Descending)
    if ($ap.Count -eq 0) { continue }
    $cols = 4; $rows = [Math]::Ceiling($ap.Count / $cols)
    $s = New-Sheet $cols $rows "EDITOR  $sp mature, the approach: frames either side of each level swap and the hand-over to the far card (noon)"
    for ($i = 0; $i -lt $ap.Count; $i++) {
        $d = [int](($ap[$i].BaseName -split "_")[-1])
        Put $s[1] $ap[$i].FullName ($i % $cols) ([Math]::Floor($i / $cols)) "$sp  $d m"
    }
    Save $s "SHEET_approach_${sp}.jpg"
}
