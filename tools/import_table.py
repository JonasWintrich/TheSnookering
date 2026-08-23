"""Convert the OpenGameArt 'Pool Table 3D Model' (BrightRetro, CC-BY 3.0) into the
game's hero table GLBs — one for pool, one rescaled for snooker — aligned to the
physics TableSpec.

  blender --background --python tools/import_table.py

Alignment (measured from the OBJ):
  cushion nose lines at |x| = 1.424, |z| = 0.7037 (model units, Y-up OBJ)
  → scale so the noses land exactly on each game's physics playfield;
    cloth top stays at height 0.
Materials keep their OBJ names (Beize, BeizeCushions, TableWood, ...) — the game
remaps them by name at load (snooker gets its own cloth colors).
"""

import bpy

SRC = "tools/downloads/pooltable/Objects/TournamentTable.obj"

NOSE_X = 1.424
NOSE_Z = 0.7037

TABLES = {
    "game/assets/models/table_pool.glb": (1.27, 0.635, 0.90),
    "game/assets/models/table_snooker.glb": (1.7845, 0.889, 0.95),
}


def build(out_path, half_length, half_width, height_scale):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    # Purge orphaned data blocks so the next import's materials keep their
    # original names (otherwise Blender appends .001 and the game's name-based
    # material remap misses them).
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)

    bpy.ops.wm.obj_import(filepath=SRC)

    for obj in bpy.context.selected_objects:
        # OBJ (x=length, y=up, z=width) imports to Blender as X=length, Y=width, Z=up.
        obj.scale = (half_length / NOSE_X, half_width / NOSE_Z, height_scale)

    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.export_scene.gltf(filepath=out_path, export_format="GLB")
    print("exported", out_path)


for path, (hl, hw, hs) in TABLES.items():
    build(path, hl, hw, hs)
