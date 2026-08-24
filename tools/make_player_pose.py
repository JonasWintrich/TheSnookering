"""Pose the textured character into a cue-aiming stance and export it for the
first-person aim view. Renders preview PNGs for fast iteration.

  blender --background --python tools/make_player_pose.py
"""

import math
import os

import bpy
import mathutils

SRC = "tools/downloads/npc/suit2.glb"
OUT = "game/assets/models/npc/player_aim.glb"

bpy.ops.wm.read_homefile(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(SRC))

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")


def bone_dir(name):
    pb = arm.pose.bones[name]
    m = arm.matrix_world @ pb.matrix
    head = m.to_translation()
    tail = m @ mathutils.Vector((0, pb.length, 0))
    return (tail - head).normalized()


print("rest UpperArm.L dir:", tuple(round(v, 2) for v in bone_dir("UpperArm.L")))
print("rest Hips dir:", tuple(round(v, 2) for v in bone_dir("Hips")))

# Bend the body with eulers (these behaved as expected).
for name, x in [("Abdomen", 32), ("Torso", 18), ("Neck", -24), ("Head", -14)]:
    pb = arm.pose.bones[name]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(x), 0, 0)
    pb.keyframe_insert("rotation_euler", frame=1)
bpy.context.view_layer.update()

# Facing: perpendicular to the T-pose arm axis, horizontal.
arm_axis = bone_dir("UpperArm.L")
facing = arm_axis.cross(mathutils.Vector((0, 0, 1))).normalized()  # left-arm axis x up = forward
if facing.length < 0.5:
    facing = mathutils.Vector((0, -1, 0))
print("facing:", tuple(round(v, 2) for v in facing))
down = mathutils.Vector((0, 0, -1))


def aim_bone(name, direction):
    """Point the bone's +Y along `direction` (world), preserving position."""
    pb = arm.pose.bones[name]
    d = mathutils.Vector(direction).normalized()
    # world -> armature space (armature has identity transform for glTF imports,
    # but stay correct anyway)
    d_arm = (arm.matrix_world.inverted().to_3x3() @ d).normalized()
    loc = pb.matrix.to_translation()
    quat = d_arm.to_track_quat("Y", "Z")
    m = quat.to_matrix().to_4x4()
    m.translation = loc
    pb.matrix = m
    bpy.context.view_layer.update()
    pb.keyframe_insert("rotation_quaternion", frame=1)


# Bridge arm (left): forward and 30 deg down, forearm continuing straight.
bridge = (facing * math.cos(math.radians(30)) + down * math.sin(math.radians(30))).normalized()
aim_bone("UpperArm.L", bridge)
aim_bone("LowerArm.L", (facing * math.cos(math.radians(38)) + down * math.sin(math.radians(38))).normalized())
aim_bone("Hand.L", (facing * math.cos(math.radians(20)) + down * math.sin(math.radians(20))).normalized())

# Grip arm (right): upper arm back+down, forearm hanging almost straight down.
grip_up = (-facing * math.cos(math.radians(55)) + down * math.sin(math.radians(55))).normalized()
aim_bone("UpperArm.R", grip_up)
aim_bone("LowerArm.R", (down + -facing * 0.15).normalized())
aim_bone("Hand.R", (down + facing * 0.2).normalized())

bpy.ops.object.mode_set(mode="OBJECT")

# Preview renders.
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 640
scene.render.resolution_y = 640

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def render(name, loc, look_at):
    cam.location = loc
    direction = mathutils.Vector(look_at) - mathutils.Vector(loc)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.abspath(f"out/pose_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("rendered", scene.render.filepath)


render("side", (3.0, 0.0, 1.2), (0.0, 0.0, 0.9))
render("front34", (2.0, -2.2, 1.4), (0.0, 0.0, 0.9))
render("top", (0.0, 0.0, 4.0), (0.0, 0.0, 0.8))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.gltf(
    filepath=os.path.abspath(OUT),
    export_format="GLB",
    export_animations=True,
    export_current_frame=False,
)
print("exported", OUT)
