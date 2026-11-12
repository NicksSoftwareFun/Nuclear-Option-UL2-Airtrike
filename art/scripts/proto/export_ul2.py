"""Export the UL-2 parts for Unity. Usage: python3 -I export_ul2.py -- <ul2_proto.blend> <out_dir>
Writes UL2_parts.fbx (every part mesh and its convex hull collider at the origin, vertices in the part's pivot frame,
pre-rotated 180 deg about Z so the FBX import lands Blender (x, y, z) at Unity (x, z, y)) and UL2_layout.json (name,
kind, Blender world position of every part, collider and reference point)."""
import bpy, sys, os, json, math, bmesh
from mathutils import Matrix, Vector
a = sys.argv[sys.argv.index('--') + 1:]
src, outdir = a[0], a[1]; os.makedirs(outdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
parts = [o for o in bpy.data.collections['PARTS'].objects if o.type == 'MESH']

# reference points in final Blender space (nose +Y, up +Z, ground z = 0)
POINTS = {
    'pilot': (0, 1.38, 1.14), 'copilot': (0, 0.23, 1.34), 'gunner': (0, -0.55, 1.42),
    'wheel_F': (0, 1.98, 0.24), 'wheel_L': (-1.00, -0.67, 0.27), 'wheel_R': (1.00, -0.67, 0.27),
    'propCentre': (0, -2.008, 1.778), 'rotorCentre': (0, -0.97, 3.19),
    'gunPivot': (0.45, 1.28, 0.82), 'targetCam': (0, 1.33, 0.42), 'landingLight': (0, 2.87, 0.80),
    'navLight_L': (-0.99, -2.91, 1.18), 'navLight_R': (0.99, -2.91, 1.18), 'strobe': (0, -2.91, 2.106),
    'hardpoint_L': (-0.86, 0.53, 0.80), 'hardpoint_R': (0.86, 0.53, 0.80), 'exhaust': (0, -1.75, 1.41),
}
ORIGIN = Vector((0, 0, 1.20))    # Unity root origin (about the CG height); spawnOffset lifts it above the ground
root_part = [o for o in parts if o.parent is None][0]
d = root_part.matrix_world.translation - ORIGIN
root_part.data.transform(Matrix.Translation(d)); root_part.matrix_world = Matrix.Translation(ORIGIN)
for ch in root_part.children: ch.matrix_parent_inverse = root_part.matrix_world.inverted()
layout = []
for o in parts:
    mw = Matrix.Translation(-ORIGIN) @ o.matrix_world
    layout.append(dict(name=o.name, coll='MODEL', kind='MESH', pos=list(mw.translation), rot=[1, 0, 0, 0],
                       parent=o.parent.name if o.parent else '', mats=sorted([m.name for m in o.data.materials], key=lambda n: n != 'UL2_skin'),
                       bmin=[min(v.co[i] for v in o.data.vertices) for i in range(3)],
                       bmax=[max(v.co[i] for v in o.data.vertices) for i in range(3)]))
    layout.append(dict(name=o.name + '_colmesh', coll='COLLISION', kind='MESH', pos=list(mw.translation), rot=[1, 0, 0, 0],
                       parent='', mats=[]))
for n, p in POINTS.items():
    layout.append(dict(name=n, coll='POINTS', kind='EMPTY', pos=list(Vector(p) - ORIGIN), rot=[1, 0, 0, 0], parent='', mats=[]))
json.dump(layout, open(os.path.join(outdir, 'UL2_layout.json'), 'w'), indent=1)

Rz = Matrix.Rotation(math.pi, 4, 'Z')
exp = bpy.data.collections.new('EXPORT'); bpy.context.scene.collection.children.link(exp)
for c in list(bpy.context.scene.collection.children):
    if c.name != 'EXPORT': bpy.context.view_layer.layer_collection.children[c.name].exclude = True
for o in parts:
    # visible mesh
    me = o.data.copy(); me.name = o.name; me.transform(Rz)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    gl = [i for i, m in enumerate(me.materials) if m and m.name == 'UL2_glass']
    st = [i for i, m in enumerate(me.materials) if m and m.name in ('UL2_mech', 'UL2_frame')]
    sk = [i for i, m in enumerate(me.materials) if m and m.name == 'UL2_skin']
    for f in ([] if o.name.startswith('x_mfd') else bm.faces):
        if f.material_index in sk: continue                      # skin keeps its livery atlas UVs (from ul2_uv.blend)
        if f.material_index in st:
            nn = f.normal; ax = max(range(3), key=lambda i: abs(nn[i]))
            for lp in f.loops:
                c = lp.vert.co; u, v = [(c.y, c.z), (c.x, c.z), (c.x, c.y)][ax]; lp[uvl].uv = (u / 2.5, v / 2.5)
        else:
            for lp in f.loops: lp[uvl].uv = (0.25, 0.5) if f.material_index in gl else (0.0, 0.0)
    if sk and sk[0] != 0:
        order = [sk[0]] + [i for i in range(len(me.materials)) if i != sk[0]]
        remap = {old: new for new, old in enumerate(order)}
        for f in bm.faces: f.material_index = remap[f.material_index]
        mats = [me.materials[i] for i in order]; me.materials.clear()
        for mt in mats: me.materials.append(mt)
    bm.faces.sort(key=lambda f: f.material_index)
    bm.to_mesh(me); bm.free()
    n = bpy.data.objects.new(o.name + '__x', me); exp.objects.link(n)
    # convex hull collider, at most 240 triangles (Unity convex limit 255), stock LoD vertex colour
    hb = bmesh.new()
    for v in me.vertices: hb.verts.new(v.co)
    dist = 0.01
    while True:
        bmesh.ops.remove_doubles(hb, verts=hb.verts[:], dist=dist)
        bmesh.ops.convex_hull(hb, input=hb.verts[:])
        bmesh.ops.delete(hb, geom=[v for v in hb.verts if not v.link_faces], context='VERTS')
        bmesh.ops.triangulate(hb, faces=hb.faces[:])
        if len(hb.faces) <= 240: break
        dist *= 1.35
        pts = [v.co.copy() for v in hb.verts]; hb.free(); hb = bmesh.new()
        for c in pts: hb.verts.new(c)
    bmesh.ops.recalc_face_normals(hb, faces=hb.faces[:])
    cm = bpy.data.meshes.new(o.name + '_colmesh'); hb.to_mesh(cm); hb.free()
    ca = cm.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    for d in ca.data: d.color_srgb = (41 / 255, 41 / 255, 41 / 255, 1.0)
    cn = bpy.data.objects.new(o.name + '_colmesh__x', cm); exp.objects.link(cn)
for o in parts: o.name = o.name + '__src'
for o in list(exp.objects): o.name = o.name[:-3]
bpy.ops.object.select_all(action='DESELECT')
for o in exp.objects: o.select_set(True)
bpy.context.view_layer.objects.active = exp.objects[0]
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, 'UL2_parts.fbx'), use_selection=False, use_active_collection=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, use_tspace=True)
print('exported', len(exp.objects), 'meshes,', len(layout), 'layout entries')
