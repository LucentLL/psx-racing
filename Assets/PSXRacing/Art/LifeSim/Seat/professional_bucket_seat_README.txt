PROFESSIONAL BUCKET SEAT — PSX GAME ASSET

Native Blender model with FBX, self-contained GLB, OBJ/MTL, and atlas.
Geometry: 626 mesh vertices; 1212 rendered triangles.
Export vertex count may increase at UV seams and flat-shaded edges.
Dimensions: width 0.564 m, depth 0.772 m, height 0.995 m.
Units: meters. Blender/OBJ: X width, +Y toward knees, +Z up.
Origin: centered laterally at mounting datum, 20 mm below shell underside.
GLB uses standard glTF Y-up conversion. Flat shading; no subdivision.
One 128 x 128 RGBA atlas; closest/point sampling, opaque matte material.

DESIGN
Built from the four supplied seat screenshots, without prior seat geometry.
Integrated headrest, flared shoulder supports, tall hip/thigh side walls,
two actual shoulder-belt apertures and two actual lap-belt apertures.
Separate lumbar, middle-back, upper-back, pelvis and thigh cushions.
Conservative inferences: full-size dimensions, shell thickness, rear shell,
underside, upholstery colors, and cushion depth. No logos or seat rails.
This is a visual game asset, not an engineered or certified physical seat.

OBJECTS
Back_Shell
High_Support_L
High_Support_R
Seat_Pan
Thigh_Cushion
Pelvis_Cushion
Lumbar_Pad
Mid_Back_Pad
Upper_Back_Pad
Shoulder_Slot_Trim_L
Shoulder_Slot_Trim_R

UNITY
Recommended: drag professional_bucket_seat.fbx and seat_atlas.png into Assets.
FBX has embedded media; extract its material/texture if needed. Set texture
Filter Mode to Point, Compression to None, and disable mipmaps for strict PSX
styling. Use an opaque URP Lit or Unlit material and assign the atlas if your
pipeline does not translate the material automatically. Verify meter scale.
The seat faces Blender +Y; rotate its root to your vehicle's forward axis.
Do not add a MeshCollider unless gameplay needs one; a simple collider is cheaper.
Unity was not available for an in-engine test.

BLENDER
Open the .blend normally. Texture is packed. Studio camera/lights are only
in the native file, excluded from all engine exports. To use GLB instead:
File > Import > glTF 2.0, not File > Open.
OBJ requires its accompanying MTL and PNG in the same directory.

VALIDATION
Complete left/right geometry; both supports and four belt openings present.
No mirror/subdivision modifiers, rig, skins or animation. Embedded GLB texture
and OBJ material texture path checked. Five rendered views inspected.
Source build script included for reproducibility. ZIP CRC checked.
