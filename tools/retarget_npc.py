"""Give the TEXTURED Quaternius modular characters the 2019 pack's animations.

  blender --background --python tools/retarget_npc.py

The modular GLBs (Suit/Casual/Formal — colored materials, faces) ship without
animations; the 2019 Animated Men/Women blends carry the clips. Both rigs share
the core bone names (Hips/Abdomen/Torso/Neck/Head/Shoulder/UpperArm/LowerArm/
UpperLeg/LowerLeg...), so assigning the old actions to the new armatures works —
fcurves for bones that don't exist (fingers, chest) silently drop out.
"""

import os

import bpy

JOBS = [
    ("tools/downloads/npc/suit2.glb", "tools/downloads/npc/Male_Suit.blend",
     ["Man_Idle", "Man_Sitting", "Man_Standing", "Man_Clapping"],
     "game/assets/models/npc/suit_man.glb"),
    ("tools/downloads/npc/casual_m.glb", "tools/downloads/npc/Male_Suit.blend",
     ["Man_Idle", "Man_Sitting", "Man_Standing", "Man_Clapping"],
     "game/assets/models/npc/casual_man.glb"),
    ("tools/downloads/npc/formal_w.glb", "tools/downloads/npc/Female_Casual.blend",
     ["Female_Idle", "Female_Sitting", "Female_Standing", "Female_Clapping"],
     "game/assets/models/npc/formal_woman.glb"),
]


def clear_scene():
    bpy.ops.wm.read_homefile(use_empty=True)


def source_hips_height(anim_blend):
    clear_scene()
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(anim_blend))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    return (arm.matrix_world @ arm.data.bones["Hips"].head_local.to_4d()).z


def run(model_glb, anim_blend, actions, out_path):
    src_hips = source_hips_height(anim_blend)

    clear_scene()
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(model_glb))

    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    dst_hips = (armature.matrix_world @ armature.data.bones["Hips"].head_local.to_4d()).z
    ratio = dst_hips / src_hips if src_hips > 1e-6 else 1.0
    print(f"hips: src={src_hips:.3f} dst={dst_hips:.3f} ratio={ratio:.3f}")

    # Append the wanted actions from the 2019 blend.
    blend = os.path.abspath(anim_blend)
    for name in actions:
        bpy.ops.wm.append(
            filepath=f"{blend}\\Action\\{name}",
            directory=f"{blend}\\Action\\",
            filename=name,
        )

    # Location curves are in the SOURCE rig's dimensions: keep only the Hips
    # channel (scaled into the target's proportions) and drop the rest, or the
    # target gets stretched apart.
    for name in actions:
        action = bpy.data.actions.get(name)
        if action is None:
            continue
        # Blender 5 layered actions: fcurves live in channelbags.
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in list(bag.fcurves):
                        if not fc.data_path.endswith(".location"):
                            continue
                        if '"Hips"' in fc.data_path:
                            for kp in fc.keyframe_points:
                                kp.co.y *= ratio
                                kp.handle_left.y *= ratio
                                kp.handle_right.y *= ratio
                        else:
                            bag.fcurves.remove(fc)

    # Stash each action on the armature's NLA so the glTF exporter emits them all.
    armature.animation_data_create()
    for name in actions:
        action = bpy.data.actions.get(name)
        if action is None:
            print("MISSING action:", name)
            continue
        track = armature.animation_data.nla_tracks.new()
        track.name = name
        track.strips.new(name, 1, action)

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=os.path.abspath(out_path),
        export_format="GLB",
        export_animations=True,
        export_animation_mode="NLA_TRACKS",
    )
    print("exported", out_path, "with", [a for a in actions if bpy.data.actions.get(a)])


for job in JOBS:
    run(*job)
