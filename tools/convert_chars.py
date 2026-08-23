"""Convert Quaternius animated character .blend files (CC0) to GLB with animations.

  blender --background --python tools/convert_chars.py

Prints each file's animation clip names — the game's NPC controller picks
idle clips and occasional one-shot actions from these by name.
"""

import os

import bpy

SRC = {
    "tools/downloads/npc/Male_Suit.blend": "game/assets/models/npc/male_suit.glb",
    "tools/downloads/npc/Female_Casual.blend": "game/assets/models/npc/female_casual.glb",
}

for blend, out in SRC.items():
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(blend))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    print(f"--- {blend}")
    print("actions:", sorted(a.name for a in bpy.data.actions))
    bpy.ops.export_scene.gltf(
        filepath=os.path.abspath(out),
        export_format="GLB",
        export_animations=True,
    )
    print("exported", out)
