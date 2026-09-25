#!/bin/sh
# Convert every ripped car in this folder (one <key>.json each) and render the
# check shots. Then `node ../export_models.mjs <key>` brings one into the game.
#   sh tools/carmodels/rips/convert_all.sh [key ...]
cd "$(dirname "$0")"
B="/c/Program Files/Blender Foundation/Blender 4.1/blender.exe"
keys="$*"
[ -z "$keys" ] && keys=$(ls *.json | sed 's/\.json$//')
for k in $keys; do
  echo "=== $k"
  "$B" -b --factory-startup -P ../convert_rip.py -- "$k.json" 2>&1 | grep -E "ALIGNED|WELD|FLIPPED|MATTE|OVERLAY|PARTSTAT|WROTE|Error|rror|Traceback"
  "$B" -b --factory-startup -P ../render_rip.py -- "$(cd ../converted/$k && pwd -W)/$k.obj" "$(cd ../converted/$k && pwd -W)/check" 2>&1 | grep -E "Error|rror"
done
