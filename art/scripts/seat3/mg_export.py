"""Export the seat 3 LMG and its harness arm pieces as JSON meshes for the Unity builder (Unity coords).
Pieces: gun (gun frame: origin under the receiver, z = muzzle), arm_hip (soldier rig space), arm_link1 / arm_link2
(each along +z from 0 to its length; the plugin aims them hip -> elbow -> post).
Usage: python3 -I mg_export.py -- <out.json>"""
import bpy, bmesh, sys, json
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
import ul2lib; from ul2lib import part, box, tube
import mg_model as mg
L1 = L2 = 0.26
pieces = {}
def take(name, objs):
    pieces[name] = objs
part('gun'); mg.build_gun(Matrix.Identity(4)); take('gun', list(ul2lib.PARTS['gun']['objs']))
part('arm_hip'); hip = Vector((0.22, -0.04, 0.14))
box('arm_hip_plate', hip + Vector((0.0, 0, 0.02)), (0.030, 0.16, 0.20), ul2lib.DARK, bevel=0.006)
box('arm_hip_socket', hip + Vector((0.022, 0, 0.0)), (0.03, 0.06, 0.09), ul2lib.MECH, bevel=0.006)
take('arm_hip', list(ul2lib.PARTS['arm_hip']['objs']))
for nm, L in (('arm_link1', L1), ('arm_link2', L2)):
    part(nm); a, b = Vector((0, 0, 0)), Vector((0, L, 0))
    mg.hinge(nm + '_hinge', a, 0.07, 0.021)
    mg.link(nm, a, b)
    take(nm, list(ul2lib.PARTS[nm]['objs']))
part('gun_post'); post_top = mg.MOUNT + Vector((0, 0, -0.028))
mg.hinge('arm_wrist', post_top - Vector((0, 0, 0.10)), 0.05, 0.018); tube('arm_post', post_top - Vector((0, 0, 0.10)), post_top, 0.010, ul2lib.MECH, segs=10)
pieces['gun'] += ul2lib.PARTS['gun_post']['objs']
dg = bpy.context.evaluated_depsgraph_get()
U = lambda v: [round(v[0], 5), round(v[2], 5), round(v[1], 5)]
res = {'L1': L1, 'L2': L2}
for name, objs in pieces.items():
    verts, nrm, subs = [], [], {}
    for o in objs:
        me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles() if hasattr(me, 'calc_loop_triangles') else None
        mname = o.data.materials[0].name if o.data.materials else 'UL2_mech'
        sub = subs.setdefault(mname, [])
        for lt in me.loop_triangles:
            idx = []
            for li in lt.loops:
                v = me.vertices[me.loops[li].vertex_index]
                n = lt.normal if not lt.use_smooth else me.loops[li].normal if hasattr(me.loops[li], 'normal') else v.normal
                verts.append(U(o.matrix_world @ v.co)); nrm.append(U(n)); idx.append(len(verts) - 1)
            sub += [idx[0], idx[2], idx[1]]
        o.evaluated_get(dg).to_mesh_clear()
    res[name] = {'verts': verts, 'normals': nrm, 'subs': subs}
    print(name, len(verts) // 3, 'tris', {k: len(v) // 3 for k, v in subs.items()})
json.dump(res, open(out, 'w'))
