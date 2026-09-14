"""SIGHTLINE — the 3D prop kit generator (PROGRAM PARALLAX, wave P28).

THE ASSET IS THIS SCRIPT. The .glb files under assets/props/ are its build output.
Change a number here, re-run, and every instance of that prop in the game changes -- which is
the whole point: nothing about the current shape vocabulary is enshrined in a binary nobody
can edit. Same contract as scripts/changelog.sh: derived, not written.

    pip install bpy        # needs Python 3.11 exactly (bpy ships cp311 wheels)
    python3 tools/props/props.py

Runs fully headless -- no display, no GUI, no Blender install. See assets/props/CREDITS.txt for
the licence position and the two export gotchas this script already handles.
"""
import bpy, math, os
from mathutils import Vector

# Output lands in the repo's asset folder, resolved from THIS FILE, so the script works from
# any working directory. The .glb files are build OUTPUT -- committed only so a machine without
# Blender can still build. Regenerate, never hand-edit.
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "props")
OUT = os.path.normpath(OUT)
KEY_G = Vector((-0.40, 0.86, 0.32)); KEY_G.normalize()
KEY = Vector((KEY_G.x, -KEY_G.z, KEY_G.y))     # glTF(Y-up) -> Blender(Z-up)
AMB = 0.42

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device='CPU'; sc.cycles.samples = 48
    sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False
    sc.render.bake.target = 'VERTEX_COLORS'
    return sc

def newmat():
    m = bpy.data.materials.new("m"); m.use_nodes = True; return m

def box(loc, sx, sy, sz, bev=0.03, segs=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    ob = bpy.context.object
    ob.scale = (sx, sy, sz); bpy.ops.object.transform_apply(scale=True)
    if bev > 0:
        m = ob.modifiers.new("b",'BEVEL'); m.width=bev; m.segments=segs; m.limit_method='ANGLE'
        bpy.ops.object.modifier_apply(modifier="b")
    return ob

def cyl(loc, r, h, verts=8):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc)
    return bpy.context.object

def ico(loc, r, sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=r, location=loc)
    return bpy.context.object

def finish(objs, name, ground=True):
    """join -> bake AO -> combine with key light -> export glb"""
    for ob in objs:
        if not ob.data.materials: ob.data.materials.append(newmat())
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs: ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    ob = bpy.context.object; ob.name = name

    # a floor so AO produces real contact shadows, baked but not exported
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0,0,0))
    fl = bpy.context.object; fl.data.materials.append(newmat())

    for o in (ob, fl):
        o.data.color_attributes.new(name="AO", type='BYTE_COLOR', domain='CORNER')
        o.data.color_attributes.active_color = o.data.color_attributes["AO"]
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); fl.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.bake(type='AO')

    me = ob.data
    ao = me.color_attributes["AO"]
    lit = me.color_attributes.new(name="Lit", type='BYTE_COLOR', domain='CORNER')
    for poly in me.polygons:
        lam = max(0.0, poly.normal.normalized().dot(KEY))
        base = AMB + (1.0-AMB)*lam
        for li in poly.loop_indices:
            a = ao.data[li].color[0]
            v = min(1.0, base * (0.58 + 0.42*a))
            lit.data[li].color = (v,v,v,1.0)
    me.color_attributes.remove(ao)
    me.color_attributes.active_color = me.color_attributes["Lit"]

    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                              export_normals=True, export_vertex_color='ACTIVE',
                              export_materials='NONE', export_yup=True)
    tris = sum(len(p.vertices)-2 for p in me.polygons)
    print(f"PROP {name}: tris={tris}")

# ── 1. EDGE WALL, high. Sits ON the edge line: 1 tile long (Y), thin (X), tall (Z).
reset(); finish([box((0,0,0.58), 0.13, 1.0, 1.15, bev=0.02)], "wall_high")

# ── 2. EDGE WALL, low — cover but no sight block.
reset(); finish([box((0,0,0.26), 0.15, 1.0, 0.52, bev=0.02)], "wall_low")

# ── 3. EDGE WALL with a DOORWAY: two jambs + a lintel. Passable, sight-permeable.
reset()
finish([box((0,-0.36,0.58), 0.13, 0.28, 1.15, bev=0.02),
        box((0, 0.36,0.58), 0.13, 0.28, 1.15, bev=0.02),
        box((0, 0.00,1.05), 0.13, 0.46, 0.22, bev=0.02)], "wall_door")

# ── 4. TREE — tile obstacle. Trunk + two faceted canopy masses.
reset()
t = cyl((0,0,0.34), 0.09, 0.68, verts=6)
c1 = ico((0,0,0.86), 0.40, sub=1); c2 = ico((0.10,0.06,1.10), 0.26, sub=1)
finish([t,c1,c2], "tree")

# ── 5. CAR — spans TWO tiles along Y.
reset()
body = box((0,0,0.30), 0.78, 1.85, 0.42, bev=0.06, segs=2)
cabin= box((0,-0.12,0.62), 0.66, 0.85, 0.34, bev=0.06, segs=2)
finish([body,cabin], "car")

# ── 6. CRATE — small tile obstacle, stacked pair.
reset()
finish([box((-0.08,-0.06,0.24), 0.46, 0.46, 0.48, bev=0.03),
        box(( 0.16, 0.14,0.62), 0.34, 0.34, 0.36, bev=0.03)], "crate")
print("KIT DONE")
