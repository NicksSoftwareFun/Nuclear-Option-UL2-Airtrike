"""Copy the UL2_skin UVs from an older blend onto the same skin faces in a newer one (faces matched by their vertex
positions), so a model change elsewhere does not repack the livery atlas. Usage: keep_skin_uv.py -- old.blend new.blend"""
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]
def grab(path):
    bpy.ops.wm.open_mainfile(filepath=path); d = {}
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.data.uv_layers: continue
        me = o.data; si = {i for i, m in enumerate(me.materials) if m and m.name == 'UL2_skin'}
        if not si: continue
        uv = me.uv_layers.active.data; per = {}
        for f in me.polygons:
            if f.material_index not in si: continue
            k = (tuple(round(c, 2) for c in f.normal),) + tuple(sorted(tuple(round(c, 4) for c in me.vertices[me.loops[li].vertex_index].co) for li in f.loop_indices))
            per[k] = {tuple(round(c, 4) for c in me.vertices[me.loops[li].vertex_index].co): tuple(uv[li].uv) for li in f.loop_indices}
        d[o.name] = per
    return d
old = grab(a[0])
bpy.ops.wm.open_mainfile(filepath=a[1]); hit = miss = 0
for o in bpy.data.objects:
    if o.name not in old: continue
    me = o.data; si = {i for i, m in enumerate(me.materials) if m and m.name == 'UL2_skin'}; uv = me.uv_layers.active.data; per = old[o.name]
    for f in me.polygons:
        if f.material_index not in si: continue
        k = (tuple(round(c, 2) for c in f.normal),) + tuple(sorted(tuple(round(c, 4) for c in me.vertices[me.loops[li].vertex_index].co) for li in f.loop_indices))
        m = per.get(k)
        if m is None: miss += 1; continue
        for li in f.loop_indices: uv[li].uv = m[tuple(round(c, 4) for c in me.vertices[me.loops[li].vertex_index].co)]
        hit += 1
print('skin faces kept', hit, 'not found', miss)
bpy.ops.wm.save_as_mainfile(filepath=a[1])
