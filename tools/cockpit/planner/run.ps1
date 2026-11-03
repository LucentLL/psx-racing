param([string]$Verbose = "")
# The cockpit arms' PLANNER (CockpitArms.Planner: which hand holds the rim,
# where, when one lets go and comes back) tested in about a second, no Unity:
# the Planner and Hand are copied out of Assets\PSXRacing\Scripts\CockpitArms.cs
# beside stub Mathf/Vector (stubs.cs), compiled by the Unity install's bundled
# csc and run on its .NET 8 runtime. harness.cs drives the self-test's
# sequences plus fast flicks and a random drive, and fails on: both hands off
# the rim, the forearms crossed (Planner.Crossed), hands under 35 deg apart on
# the rim, or not back at rest after an unwind. The full arm solve (fingers,
# cabin, elbows) still needs tools\cockpit\cockpit-arms.ps1.
#
#   powershell -File tools\cockpit\planner\run.ps1 [-Verbose "big right turn"]
#
# -Verbose prints every frame of the named sequence.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $here))
$out = Join-Path $env:TEMP "psx_planner"
New-Item -ItemType Directory -Force $out | Out-Null
$ed = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Data"
$dotnet = "$ed\NetCoreRuntime\dotnet.exe"
$csc = (Get-ChildItem "$ed\DotNetSdk\sdk" -Recurse -Filter csc.dll | Where-Object { $_.FullName -like "*Roslyn\bincore*" } | Select-Object -First 1).FullName
$fx = (Get-ChildItem "$ed\NetCoreRuntime\shared\Microsoft.NETCore.App" | Select-Object -First 1).FullName
$src = Get-Content (Join-Path $repo "Assets\PSXRacing\Scripts\CockpitArms.cs") -Raw
$a = $src.IndexOf("        public enum Mode {")
$b = $src.IndexOf("        // ------------------------------------------------------------------`r`n        //  The instance")
if ($b -lt 0) { $b = $src.IndexOf("        // ------------------------------------------------------------------`n        //  The instance") }
$s1 = $src.IndexOf("        static float SegDist(Vector2")
$s2 = $src.IndexOf("        /// <summary>For the shot tool: the arms' chains now")
if ($a -lt 0 -or $b -lt 0 -or $s1 -lt 0 -or $s2 -lt 0) { throw "CockpitArms.cs markers not found ($a $b $s1 $s2)" }
$code = "using UnityEngine;`npublic sealed partial class CockpitArms`n{`n" + $src.Substring($a, $b - $a) + "`n" + $src.Substring($s1, $s2 - $s1) + "`n}`n"
Set-Content "$out\planner.cs" $code -Encoding utf8
$refs = @("System.Runtime.dll", "System.Private.CoreLib.dll", "System.Console.dll", "System.Collections.dll", "System.Linq.dll") | ForEach-Object { "/r:`"$fx\$_`"" }
& $dotnet $csc /nologo /nostdlib+ /noconfig /langversion:9.0 /nowarn:1701,1702 /t:exe "/out:$out\ph.dll" @refs "$here\stubs.cs" "$out\planner.cs" "$here\harness.cs"
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
$ver = Split-Path -Leaf $fx
Set-Content "$out\ph.runtimeconfig.json" "{ `"runtimeOptions`": { `"tfm`": `"net8.0`", `"framework`": { `"name`": `"Microsoft.NETCore.App`", `"version`": `"$ver`" } } }"
$env:PH_VERBOSE = $Verbose
& $dotnet "$out\ph.dll"
exit $LASTEXITCODE
