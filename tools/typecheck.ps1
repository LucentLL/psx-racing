# Compile the whole project with Unity's own Roslyn, without starting Unity.
#
# A compile error costs 15 seconds here and 20 minutes inside a sandbox scene
# build, so this is worth running before every Unity job.
#
# Two things it deliberately does NOT trust:
#   * the <Compile Include> lists in the csproj files, which go stale the moment
#     a file is added and then read as dozens of bogus "does not exist in the
#     current context" errors. The source list comes from disk.
#   * the <HintPath> values as written, which Unity emits RELATIVE to the
#     project and rewrites on every batchmode run. They are resolved against the
#     project directory here.

param(
    [string]$Project = (Split-Path -Parent $PSScriptRoot),
    [string]$Editor  = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor"
)

$ErrorActionPreference = "Stop"

$dotnet = Join-Path $Editor "Data\NetCoreRuntime\dotnet.exe"
$csc    = Get-ChildItem (Join-Path $Editor "Data\DotNetSdk\sdk") -Recurse -Filter csc.dll -ErrorAction SilentlyContinue |
          Where-Object { $_.FullName -like "*Roslyn\bincore*" } | Select-Object -First 1
if (-not (Test-Path $dotnet)) { Write-Host "no dotnet runtime at $dotnet"; exit 2 }
if (-not $csc) { Write-Host "no csc.dll under $Editor"; exit 2 }

function Get-Refs($csprojName) {
    $path = Join-Path $Project $csprojName
    if (-not (Test-Path $path)) { return @() }
    $xml = [xml](Get-Content $path)
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($n in $xml.SelectNodes("//*[local-name()='HintPath']")) {
        $p = $n.InnerText
        if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $Project $p }
        if (Test-Path $p) { $out.Add((Resolve-Path $p).Path) }
    }
    return $out
}

function Get-Defines($csprojName) {
    $path = Join-Path $Project $csprojName
    if (-not (Test-Path $path)) { return "" }
    $xml = [xml](Get-Content $path)
    $n = $xml.SelectNodes("//*[local-name()='DefineConstants']") | Select-Object -First 1
    if ($n) { return $n.InnerText } else { return "" }
}

# Source list from DISK, split on whether any path segment is named Editor.
$all = Get-ChildItem (Join-Path $Project "Assets") -Recurse -Filter *.cs -File
$runtimeSrc = @()
$editorSrc  = @()
foreach ($f in $all) {
    $rel = $f.FullName.Substring($Project.Length).TrimStart('\')
    if (($rel -split '\\') -contains 'Editor') { $editorSrc += $f.FullName }
    else { $runtimeSrc += $f.FullName }
}

$out = Join-Path $env:TEMP "psxtypecheck"
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Write-Rsp($file, $refs, $defines, $sources, $target) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("/nostdlib+")
    [void]$sb.AppendLine("/noconfig")
    [void]$sb.AppendLine("/langversion:9.0")
    [void]$sb.AppendLine("/nowarn:0169,0649,0414,CS8632")
    [void]$sb.AppendLine("/target:library")
    [void]$sb.AppendLine("/out:`"$target`"")
    if ($defines) { [void]$sb.AppendLine("/define:$defines") }
    foreach ($r in $refs)    { [void]$sb.AppendLine("/r:`"$r`"") }
    foreach ($s in $sources) { [void]$sb.AppendLine("`"$s`"") }
    Set-Content -Path $file -Value $sb.ToString() -Encoding UTF8
}

$runtimeDll = Join-Path $out "Assembly-CSharp.dll"
$rsp1 = Join-Path $out "runtime.rsp"
Write-Rsp $rsp1 (Get-Refs "Assembly-CSharp.csproj") (Get-Defines "Assembly-CSharp.csproj") $runtimeSrc $runtimeDll

Write-Host "== runtime assembly ($($runtimeSrc.Count) files) =="
& $dotnet $csc.FullName "@$rsp1"
$rc1 = $LASTEXITCODE

$editorRefs = @(Get-Refs "Assembly-CSharp-Editor.csproj")
if (Test-Path $runtimeDll) { $editorRefs += $runtimeDll }
$rsp2 = Join-Path $out "editor.rsp"
Write-Rsp $rsp2 $editorRefs (Get-Defines "Assembly-CSharp-Editor.csproj") $editorSrc (Join-Path $out "Assembly-CSharp-Editor.dll")

Write-Host "== editor assembly ($($editorSrc.Count) files) =="
& $dotnet $csc.FullName "@$rsp2"
$rc2 = $LASTEXITCODE

if ($rc1 -eq 0 -and $rc2 -eq 0) { Write-Host "TYPECHECK OK"; exit 0 }
Write-Host "TYPECHECK FAILED (runtime=$rc1 editor=$rc2)"
exit 1
