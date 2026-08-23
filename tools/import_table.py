"""Convert the OpenGameArt 'Pool Table 3D Model' (BrightRetro, CC-BY 3.0) into the
game's hero table GLB, aligned to the physics TableSpec.

  blender --background --python tools/import_table.py

Alignment (measured from the OBJ):
  cushion nose lines at |x| = 1.424, |z| = 0.7037 (model units, Y-up OBJ)
  → scale so noses land exactly on the physics playfield (±1.27, ±0.635),
    cloth top stays at height 0.
Materials keep their OBJ names (Beize, BeizeCushions, TableWood, ...) — the game
remaps them by name at load.
"""

import bpy

SRC = "tools/downloads/pooltable/Objects/TournamentTable.obj"
OUT = "game/assets/models/table_pool.glb"

# Physics playfield half-extents over measured model nose lines.
SCALE_LENGTH = 1.27 / 1.424
SCALE_WIDTH = 0.635 / 0.7037
SCALE_HEIGHT = 0.90

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()

bpy.ops.wm.obj_import(filepath=SRC)

for obj in bpy.context.selected_objects:
    # OBJ (x=length, y=up, z=width) imports to Blender as (x, -z, y):
    # Blender X = length, Y = width, Z = up.
    obj.scale = (SCALE_LENGTH, SCALE_WIDTH, SCALE_HEIGHT)

bpy.ops.object.transform_apply(scale=True)

bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB")
print("exported", OUT)
