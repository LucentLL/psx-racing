@echo off
rem PSX Racing with RAIN 2 ON (opt-in test: puddles, splashes, wipers, beads on the glass, spray, rain sound).
rem Put this file in the build folder beside PSXRacing.exe and run it instead of the exe. Pick rain in the weather.
rem Remove "rem " from the second set line to have the HD water on too.
set PSX_RAIN2=1
rem set PSX_WATER_HD=1
start "" "%~dp0PSXRacing.exe" %*
