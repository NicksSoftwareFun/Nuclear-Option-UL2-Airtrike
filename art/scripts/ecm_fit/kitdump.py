# run ecmfit mode C without rendering, then dump every ECM kit object (not the stock pod, not the aircraft) as one
# static mesh in seat-3-hardpoint-local Unity coords: binary for the builder
import bpy, sys, os, struct
KOUT = os.environ['KITOUT']
bpy.ops.wm.open_mainfile(filepath=sys.argv[sys.argv.index('--') + 1])
pre = set(o.name for o in bpy.data.objects)
sys.argv = [sys.argv[0], '--'] + sys.argv[sys.argv.index('--') + 1:]
os.environ['NR'] = '1'
try:
    exec(compile(open('<out> 'ecmfit.py', 'exec'))
except SystemExit: pass
from mathutils import Vector
G = Vector((0, -0.55, 1.42))   # seat3_hp = layout 'gunner' (Blender world)
dg = bpy.context.evaluated_depsgraph_get()
V, N, subs, mats = [], [], {}, {}
for o in bpy.data.objects:
    if o.type not in ('MESH', 'CURVE') or o.name in pre or o.name == 'ECMPod1' or o.name.startswith('x_propblade') or o.name in ('ground',) or o.hide_render: continue
    me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles() if hasattr(me, 'calc_loop_triangles') else None
    mw = o.matrix_world; nm = mw.to_3x3().inverted().transposed()
    for tri in me.loop_triangles:
        mat = me.materials[tri.material_index] if me.materials else None
        key = mat.name if mat else 'default'
        if key not in mats and mat and mat.use_nodes:
            b = mat.node_tree.nodes.get('Principled BSDF')
            if b: c = b.inputs['Base Color'].default_value; mats[key] = (c[0], c[1], c[2], 1 - b.inputs['Roughness'].default_value, b.inputs['Metallic'].default_value)
        idx = []
        for li, vi in zip(tri.loops, tri.vertices):
            p = mw @ me.vertices[vi].co - G
            n = (nm @ (me.loops[li].normal if not tri.use_smooth else me.vertices[vi].normal)).normalized() if True else None
            if not tri.use_smooth: n = (nm @ tri.normal).normalized()
            V.append((p.x, p.z, p.y)); N.append((n.x, n.z, n.y)); idx.append(len(V) - 1)
        # Blender->Unity swaps y/z (mirror), so flip winding
        subs.setdefault(key, []).extend([idx[0], idx[2], idx[1]])
    print('kit', o.name, len(me.loop_triangles))
print('SKIPPED', [o.name for o in bpy.data.objects if o.name not in pre and o.type=='MESH' and o.hide_render])
with open(KOUT, 'wb') as f:
    f.write(struct.pack('<i', len(V)))
    for v in V: f.write(struct.pack('<3f', *v))
    for n in N: f.write(struct.pack('<3f', *n))
    f.write(struct.pack('<i', len(subs)))
    for k, t in subs.items():
        kb = k.encode(); f.write(struct.pack('<i', len(kb))); f.write(kb); f.write(struct.pack('<i', len(t))); f.write(struct.pack('<%di' % len(t), *t))
    f.write(struct.pack('<i', len(mats)))
    for k, m in mats.items():
        kb = k.encode(); f.write(struct.pack('<i', len(kb))); f.write(kb); f.write(struct.pack('<5f', *m))
print('KIT verts', len(V), 'tris', sum(len(t) for t in subs.values()) // 3, 'subs', list(subs), 'mats', mats)
