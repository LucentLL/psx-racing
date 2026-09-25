PSX STYLE PADDED CAR SEAT

Made from scratch in Blender using the three supplied images. No geometry from previous vehicle projects was reused.

Files
psx_seat.blend: native editable Blender asset, no unapplied modifiers.
psx_seat.fbx: recommended Unity import; mesh objects and embedded texture.
psx_seat.glb: self-contained glTF with embedded texture.
psx_seat.obj / psx_seat.mtl: portable geometry and material.
seat_atlas.png: 128 x 128 charcoal upholstery atlas.
preview.png and view_*.png: renders of the actual exported-source mesh.
build_seat.py: reproducible Blender construction/export/render script.
model_stats.json: measured vertex/triangle counts, bounds and object names.

Scale and axes
Meters. Blender X = width, +Y = occupant-facing direction, Z = up.
Origin is centered laterally at the bottom of the cushion. Objects share this origin.
FBX and GLB export convert to Y-up. Estimated full-size proportions, since no dimensions were supplied. No seat rails or adjustment hardware were visible, so they are omitted. The unseen rear and underside are simplified closed surfaces. Headrest mounting is simplified as a concealed connection.

Style and topology
Flat-shaded low-poly upholstery, with deliberate contour rings for rounded cushion edges. Panel joins are geometric. No subdivision, armature, animation, or mirror dependency. Components are closed shells with intentional assembly intersections. Single material, nearest-neighbor atlas sampling in Blender and glTF. UV islands are packed. Fine stitching and glossy leather highlights from the reference are intentionally omitted for PSX readability.

Unity
Copy psx_seat.fbx and seat_atlas.png into your project's Assets folder. Use scale factor 1; the overall seat height should be approximately 0.94 meters. Drag the model into the scene and verify facing direction in your vehicle prefab. Import normals rather than recalculating smooth normals. Set the PNG's Filter Mode to Point, disable mipmaps for strict PSX appearance, and use no texture compression if it blurs pixel clusters. If Unity does not automatically resolve the embedded texture, create a material with seat_atlas.png as its base texture and assign it to all seat renderers. Use low metallic/specular and high roughness (low smoothness). Shader selection depends on your project's render pipeline. The FBX is the direct import option; GLB requires a glTF importer. No collider is included because this is an interior visual prop.

Blender
Open psx_seat.blend directly. GLB must instead use File > Import > glTF 2.0; do not use File > Open for a GLB. The texture is packed into the blend file.

Verification limits
Exports are inspected structurally and the actual Blender mesh is rendered. Unity itself is not available in this environment; runtime appearance depends on your chosen Unity shader and lighting.

Revision: Reduced cushion side rise from 33 mm to 7 mm above the center pad, and backrest side projection from 31 mm to 7 mm relative to the lower center pad. Broad, shallow edge contours replace the previous pronounced bolster ridges.

Measured asset statistics
{
  "vertices": 436,
  "triangles": 816,
  "dimensions_m": [
    0.5400000214576721,
    0.7082467973232269,
    0.9440000057220459
  ],
  "objects": [
    "Seat_Base",
    "Back_Shell",
    "Back_Lower_Pad",
    "Back_Upper_Pad",
    "Shoulder_Pad",
    "Back_Bolster_R",
    "Cushion_Bolster_R",
    "Back_Bolster_L",
    "Cushion_Bolster_L",
    "Cushion_Center",
    "Cushion_Front_Lip",
    "Headrest",
    "Headrest_Support_L",
    "Headrest_Support_R"
  ]
}
