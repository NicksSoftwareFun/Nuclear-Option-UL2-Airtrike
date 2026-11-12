"""Report cockpit_F vertices (not skin/glass) that poke outside the pod skin. Usage: python3 -I clipcheck.py -- <blend>"""
import bpy, sys, math, re
src = open('/mnt/project-files/ul2/art/scripts/proto/ul2_proto.py').read()
seg = src[src.index('POD = ['):src.index('def build_pod')]
seg2 = src[src.index('def side_x'):src.index('edge = [')]
ns = {'math': math}; exec(seg, ns); exec(seg2, ns)
bpy.ops.wm.open_mainfile(filepath=sys.argv[sys.argv.index('--') + 1])
bad = {}
for o in bpy.data.collections['PARTS'].objects:
    if o.type != 'MESH' or o.name in ('UL2Airtrike',): continue
    me = o.data
    for poly in me.polygons:
        mn = me.materials[poly.material_index].name if me.materials else ''
        if mn in ('UL2_skin', 'UL2_glass'): continue
        for vi in poly.vertices:
            w = o.matrix_world @ me.vertices[vi].co
            y, z = w.y + 0.12, w.z - 0.12
            if not (0.0 < y < 2.9): continue
            hw, zb, zt = ns['pod_at'](y)
            if z > zt + 0.005 or z < zb - 0.005: continue
            if ns['OPEN_A'] < y < ns['OPEN_F'] and z > ns['sill'](y) - 0.01: continue
            if abs(w.x) > ns['pod_x'](y, z) + 0.004:
                key = (o.name, mn); d = abs(w.x) - ns['pod_x'](y, z)
                if d > bad.get(key, (0,))[0]: bad[key] = (round(d, 3), round(w.x, 3), round(y, 3), round(z, 3))
for k, v in sorted(bad.items(), key=lambda kv: -kv[1][0]): print('CLIP', k, 'out', v[0], 'at pre-shift x,y,z', v[1:])
print('done', len(bad))
