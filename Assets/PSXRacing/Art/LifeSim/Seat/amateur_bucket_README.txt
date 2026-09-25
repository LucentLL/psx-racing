AMATEUR BUCKET SEAT — PSX GAME ASSET

Original mesh created from the four attached bucket-seat images. Mid-range amateur aesthetic: plain charcoal cloth and shell, restrained bolsters, integrated head support, two actual harness openings. No logos.

Mesh vertices: 352
Rendered triangles: 676
GLB attribute vertices: 1212 (split for flat normals / UV seams).
Dimensions: 0.530 m wide x 0.768 m deep x 0.975 m high.
Blender axes: X width, +Y occupant forward, +Z up; meters.
Origin: centered laterally at the mounting plane; lowest shell sits 0.04 m above origin. Mounting rails omitted.
Texture: one 128 x 128 PNG atlas, embedded in GLB and packed in Blend. Closest filtering. Flat shading; no subdivision, mirror dependencies, armature, or animation.
Objects: Back_Shell, Back_Cushion, Head_Pad, Torso_Bolster_L, Torso_Bolster_R, Seat_Base, Seat_Cushion, Thigh_Bolster_L, Thigh_Bolster_R.

INFERENCES
No labeled dimensions supplied: all dimensions, back recline, rear shell thickness, cushion seams, material colors and unseen underside are estimated. The back is fixed; no recliner mechanism. Harness slots are simplified chamfered rectangles. Separate closed upholstery meshes overlap the supporting shell intentionally.

UNITY
Recommended: import amateur_bucket.fbx and bucket_atlas.png into Assets. Use scale 1, inspect roughly one-meter overall height. FBX uses Y-up conversion. Assign atlas to a URP Lit or Standard material if needed; set texture Filter Mode to Point, compression None, Max Size 128. Use a rough/low-specular material. No colliders supplied. GLB requires a compatible glTF importer. Unity itself was not available for an in-engine test.

BLENDER
Open amateur_bucket.blend normally. For GLB use File > Import > glTF 2.0, not File > Open. Preview camera/lights are only in Blend, not exports. OBJ is Z-up and references the adjacent MTL and PNG; keep these together.

FILES
Native Blend, FBX, embedded-texture GLB, OBJ/MTL, atlas PNG, five orthographic renders, combined preview, build script, statistics and validation report.
