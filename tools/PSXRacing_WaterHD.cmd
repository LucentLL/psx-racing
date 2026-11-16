@echo off
rem PSX Racing with the HD water ON (opt-in test of the water work).
rem Put this file in the build folder beside PSXRacing.exe and run it instead of the exe.
rem Lakes, rivers and Charlotte's water take the HD look; the Bogue Banks open sea
rem keeps the old water (remove "rem " from the second set line to try it there too).
set PSX_WATER_HD=1
rem set PSX_WATER_HD_SEA=1
start "" "%~dp0PSXRacing.exe" %*
