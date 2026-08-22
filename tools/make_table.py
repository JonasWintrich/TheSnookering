"""Blender headless hero-table builder.

  blender --background --python tools/make_table.py -- tools/tables.json game/assets/models

Reads the table specs dumped from Snookering.Core (single source of truth) and
builds one GLB per table with real cushion profiles, a boolean-cut wood rail
ring, leather pocket castings, skirt and legs. Coordinates match the sim plane
(x long axis, y across, z up) — the glTF exporter's Y-up conversion then lands
exactly on the game's SimWorld mapping.

Material names are the contract with the game: Cloth, CushionCloth, Wood,
DarkWood, Leather, Hole. Godot replaces them by name with its richer runtime
materials.
"""

import json
import math
import sys

import bpy

# ----------------------------------------------------------------- constants
NOSE_H = 0.036          # cushion nose height (~63.5% ball diameter, pool scale)
CUSH_TOP = 0.048        # cushion top surface height
RAIL_TOP = 0.052
RAIL_BOTTOM = -0.025
RAIL_W = 0.15
BED_T = 0.045
SKIRT_H = 0.17
LEG_TOP = RAIL_BOTTOM
FLOOR_Y = -0.82

# Cushion cross-section (s = offset along the inward normal, z = height).
# s=0 is the physics contact line; the body extends behind it.
PROFILE = [
    (-0.050, 0.000),
    (-0.050, CUSH_TOP),
    (-0.022, CUSH_TOP),
    (0.000, NOSE_H),
    (-0.014, 0.000),
]


# ----------------------------------------------------------------- helpers
def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def material(name, color, rough, metallic=0.0):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metallic
    return mat


MATS = {}


def init_materials():
    MATS["Cloth"] = material("Cloth", (0.045, 0.36, 0.15), 0.95)
    MATS["CushionCloth"] = material("CushionCloth", (0.04, 0.33, 0.135), 0.95)
    MATS["Wood"] = material("Wood", (0.16, 0.08, 0.04), 0.28)
    MATS["DarkWood"] = material("DarkWood", (0.10, 0.05, 0.028), 0.45)
    MATS["Leather"] = material("Leather", (0.045, 0.035, 0.03), 0.6)
    MATS["Hole"] = material("Hole", (0.004, 0.004, 0.005), 1.0)


def add_mesh(name, verts, faces, mat):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(MATS[mat])
    bpy.context.collection.objects.link(obj)
    return obj


def add_box(name, cx, cy, cz, sx, sy, sz, mat):
    bpy.ops.mesh.primitive_cube_add(location=(cx, cy, cz))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (sx / 2, sy / 2, sz / 2)
    bpy.ops.object.transform_apply(scale=True)
    obj.data.materials.append(MATS[mat])
    return obj


def add_cylinder(name, cx, cy, cz, radius, depth, mat, verts=48):
    bpy.ops.mesh.primitive_cylinder_add(location=(cx, cy, cz), radius=radius, depth=depth, vertices=verts)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(MATS[mat])
    return obj


def boolean(obj, cutter, op="DIFFERENCE"):
    mod = obj.modifiers.new("bool", "BOOLEAN")
    mod.operation = op
    mod.object = cutter
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def bevel(obj, width=0.006, segments=2):
    mod = obj.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(50)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def sweep_profile(name, rings, mat):
    """rings: list of vert-rings (each = PROFILE placed in world). Connect into a prism."""
    verts = []
    faces = []
    n = len(PROFILE)
    for ring in rings:
        verts.extend(ring)
    for r in range(len(rings) - 1):
        base0 = r * n
        base1 = (r + 1) * n
        for k in range(n):
            k2 = (k + 1) % n
            faces.append([base0 + k, base0 + k2, base1 + k2, base1 + k])
    # End caps.
    faces.append(list(range(n - 1, -1, -1)))
    last = (len(rings) - 1) * n
    faces.append(list(range(last, last + n)))
    return add_mesh(name, verts, faces, mat)


def profile_ring(px, py, nx, ny):
    """Place PROFILE at 2D point (px,py) with inward normal (nx,ny)."""
    return [(px + nx * s, py + ny * s, z) for (s, z) in PROFILE]


# ----------------------------------------------------------------- build one table
def build_table(spec, out_path):
    clear_scene()
    init_materials()

    hl, hw = spec["halfLength"], spec["halfWidth"]
    pockets = list(spec["pockets"])

    # Bed with pocket holes.
    bed = add_box("Bed", 0, 0, -BED_T / 2, 2 * hl + 0.14, 2 * hw + 0.14, BED_T, "Cloth")
    for p in pockets:
        cutter = add_cylinder("cut", p["x"], p["y"], 0, p["r"] + 0.006, 0.3, "Hole")
        boolean(bed, cutter)

    # Cushions: straight segments swept with the real profile.
    for idx, c in enumerate(spec["cushions"]):
        rings = [
            profile_ring(c["ax"], c["ay"], c["nx"], c["ny"]),
            profile_ring(c["bx"], c["by"], c["nx"], c["ny"]),
        ]
        sweep_profile(f"Cushion{idx}", rings, "CushionCloth")

    # Jaw arcs (snooker): profile swept along the arc, normal = radial outward.
    for idx, j in enumerate(spec["jaws"]):
        a0 = math.atan2(j["sy"], j["sx"])
        a1 = math.atan2(j["ey"], j["ex"])
        sweep = (a1 - a0) % (2 * math.pi)
        samples = 7
        rings = []
        for s in range(samples):
            a = a0 + sweep * s / (samples - 1)
            dx, dy = math.cos(a), math.sin(a)
            px, py = j["cx"] + j["r"] * dx, j["cy"] + j["r"] * dy
            rings.append(profile_ring(px, py, dx, dy))
        sweep_profile(f"Jaw{idx}", rings, "CushionCloth")

    # Wood rail ring: outer box minus inner box minus pocket cylinders, beveled.
    inner_l = hl + 0.05
    inner_w = hw + 0.05
    rail = add_box("Rails", 0, 0, (RAIL_TOP + RAIL_BOTTOM) / 2,
                   2 * (inner_l + RAIL_W), 2 * (inner_w + RAIL_W), RAIL_TOP - RAIL_BOTTOM, "Wood")
    inner_cut = add_box("cut", 0, 0, 0, 2 * inner_l, 2 * inner_w, 1.0, "Hole")
    boolean(rail, inner_cut)
    for p in pockets:
        cutter = add_cylinder("cut", p["x"], p["y"], 0, p["r"] + 0.034, 1.0, "Hole")
        boolean(rail, cutter)
    bevel(rail, width=0.008, segments=2)

    # Leather pocket castings: cylinder shell arc (mouth side cut away by a box
    # aimed at the playfield) + a black hole disc at cloth level.
    for p in pockets:
        is_side = abs(p["x"]) < 0.2
        if is_side:
            ox, oy = 0.0, math.copysign(1.0, p["y"])
        else:
            ox = math.copysign(0.70710678, p["x"])
            oy = math.copysign(0.70710678, p["y"])

        shell_r = p["r"] + 0.030
        shell = add_cylinder(f"Casting{p['id']}", p["x"], p["y"], (RAIL_TOP + RAIL_BOTTOM) / 2,
                             shell_r, RAIL_TOP - RAIL_BOTTOM, "Leather", verts=36)
        hole = add_cylinder("cut", p["x"], p["y"], 0, shell_r - 0.012, 1.0, "Hole")
        boolean(shell, hole)
        # Cut away the playfield-facing side of the shell.
        cut = add_box("cut", p["x"] - ox * (shell_r + 0.06), p["y"] - oy * (shell_r + 0.06), 0,
                      2 * shell_r + 0.1, 2 * shell_r + 0.1, 1.2, "Hole")
        cut.rotation_euler = (0, 0, math.atan2(oy, ox))
        bpy.ops.object.transform_apply(rotation=True)
        boolean(shell, cut)

        add_cylinder(f"Hole{p['id']}", p["x"], p["y"], -0.004, p["r"] + 0.004, 0.006, "Hole", verts=36)

    # Skirt + legs.
    add_box("SkirtN", 0, -(inner_w + RAIL_W - 0.02), RAIL_BOTTOM - SKIRT_H / 2, 2 * inner_l, 0.05, SKIRT_H, "DarkWood")
    add_box("SkirtS", 0, inner_w + RAIL_W - 0.02, RAIL_BOTTOM - SKIRT_H / 2, 2 * inner_l, 0.05, SKIRT_H, "DarkWood")
    add_box("SkirtE", inner_l + RAIL_W - 0.02, 0, RAIL_BOTTOM - SKIRT_H / 2, 0.05, 2 * inner_w, SKIRT_H, "DarkWood")
    add_box("SkirtW", -(inner_l + RAIL_W - 0.02), 0, RAIL_BOTTOM - SKIRT_H / 2, 0.05, 2 * inner_w, SKIRT_H, "DarkWood")

    leg_h = LEG_TOP - FLOOR_Y
    for sx in (1, -1):
        for sy in (1, -1):
            leg = add_box(f"Leg{sx}{sy}", sx * (inner_l - 0.05), sy * (inner_w - 0.05),
                          LEG_TOP - leg_h / 2, 0.14, 0.14, leg_h, "DarkWood")
            bevel(leg, width=0.01, segments=2)

    bpy.ops.export_scene.gltf(filepath=out_path, export_format="GLB")
    print("exported", out_path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    spec_path, out_dir = argv[0], argv[1]
    with open(spec_path) as f:
        data = json.load(f)
    build_table(data["pool"], f"{out_dir}/table_pool.glb")
    build_table(data["snooker"], f"{out_dir}/table_snooker.glb")


main()
