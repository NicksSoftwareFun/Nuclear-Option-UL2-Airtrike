"""Offline collider check: convex hulls exactly as export_ul2 builds them (thin hardware left out), then
(1) prop blade lines vs every hull, (2) overlap depth of every hull pair that is not parent/child.
Usage: python3 -I hullcheck.py -- <ul2_proto.blend>"""
import bpy, sys, math, itertools
import numpy as np
from scipy.spatial import ConvexHull
from scipy.optimize import linprog
from mathutils import Vector
src = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.open_mainfile(filepath=src)
parts = [o for o in bpy.data.collections['PARTS'].objects if o.type == 'MESH']
H = {}
for o in parts:
    nca = o.data.attributes.get('nocol')
    pts = [o.matrix_world @ v.co for v in o.data.vertices if nca is None or nca.data[v.index].value == 0]
    if len(pts) < 4: pts = [o.matrix_world @ v.co for v in o.data.vertices]
    P = np.array([tuple(p) for p in pts])
    try: h = ConvexHull(P)
    except Exception: continue
    H[o.name] = (h.equations, P.min(0), P.max(0), o.parent.name if o.parent else '')
hub = np.array((0, -2.008, 1.778)); bad = []
for name, (eq, lo, hi, _) in H.items():
    for k in range(36):
        a = math.radians(k * 10)
        for r in np.linspace(0.12, 0.86, 30):
            p = hub + r * np.array((math.sin(a), 0, math.cos(a)))
            if np.all(eq[:, :3] @ p + eq[:, 3] <= -0.002): bad.append((name, k * 10, round(r, 2))); break
print('PROP HITS', sorted(set((b[0], b[1]) for b in bad))[:40] if bad else 'none')
def depth(e1, e2):
    A = np.vstack([e1[:, :3], e2[:, :3]]); b = -np.concatenate([e1[:, 3], e2[:, 3]])
    A = np.hstack([A, np.ones((len(A), 1))])
    r = linprog([0, 0, 0, -1], A_ub=A, b_ub=b, bounds=[(None, None)] * 3 + [(None, 1)])
    return r.x[3] if r.success else -1
for (n1, (e1, l1, h1, p1)), (n2, (e2, l2, h2, p2)) in itertools.combinations(H.items(), 2):
    if p1 == n2 or p2 == n1: continue
    if np.any(l1 > h2) or np.any(l2 > h1): continue
    d = depth(e1, e2)
    if d > 0.003: print(f'OVERLAP {n1} x {n2} depth {d:.3f}')
