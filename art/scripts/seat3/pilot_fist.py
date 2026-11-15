"""Seat 3 hands (Nick 2026-11-15 19:36: the pilot hands look great, the gunner hands should be the same quality): the
soldier's own open hands are replaced by the stock pilot's closed fist (the same hand mesh the UL-2 pilots show in game,
pilot_hands.json, read from the stock pilot in Unity in hand-bone space, metres), grafted onto the soldier mesh and
skinned to his hand bones. The fist takes the soldier's glove UVs, so it wears the soldier texture.

    hole = graft(so, ao, 'R')  -> dict(c, a, w) in soldier hand-bone rest coordinates:
        c  centre of the fist's grip hole, a  hole axis (toward the index / thumb end), w  hole -> wrist direction
"""
import bmesh, json, os
from mathutils import Vector
from mathutils.kdtree import KDTree

SRC = os.path.join(os.path.dirname(__file__), 'pilot_hands.json')
# grip hole of the stock fist (Unity hand-bone frame, measured for the pilot hands in 0.15.72), axis = bone z
HOLE_U = {'R': Vector((-0.008, 0.14, -0.012)), 'L': Vector((0.002, 0.124, -0.012))}


# the soldier's glove island (back of the hand with the knuckle pads) in his texture; the fist is projected flat onto
# its middle (across the hand -> u, along the hand -> v), so the whole fist wears the soldier glove, no stray islands
GLOVE_U, GLOVE_V = (0.310, 0.390), (0.440, 0.560)


def _glove_uv(q):
    fx = min(1.0, max(0.0, (q.x + 0.055) / 0.11)); fy = min(1.0, max(0.0, (q.y + 0.055) / 0.25))
    return Vector((GLOVE_U[0] + fx * (GLOVE_U[1] - GLOVE_U[0]), GLOVE_V[0] + fy * (GLOVE_V[1] - GLOVE_V[0])))


def _u2b(p): return Vector((-p[0], p[1], p[2]))      # Unity (left-handed) -> Blender: mirror x, the fist stays a right fist


def graft(so, ao, side, strip=0.5, log=None):
    D = json.load(open(SRC))[side]
    gi = so.vertex_groups[f'hand_{side}'].index; fi = so.vertex_groups[f'forearm_{side}'].index
    b = ao.data.bones[f'hand_{side}']
    M = (ao.matrix_world @ b.matrix_local).inverted() @ so.matrix_world; Mi = M.inverted()
    bm = bmesh.new(); bm.from_mesh(so.data); bm.verts.ensure_lookup_table(); dl = bm.verts.layers.deform.active
    uvl = bm.loops.layers.uv.active
    # soldier glove UVs (his own hand, hand-bone frame) for the nearest-point lookup, and his material slot
    hand = [v for v in bm.verts if v[dl].get(gi, 0) > strip]
    kd = KDTree(len(hand)); uvs = []; mats = {}
    for k, v in enumerate(hand):
        kd.insert(M @ v.co, k)
        lu = [l[uvl].uv.copy() for l in v.link_loops]; uvs.append(sum(lu, Vector((0, 0))) / max(1, len(lu)))
        for f in v.link_faces: mats[f.material_index] = mats.get(f.material_index, 0) + 1
    kd.balance(); mi = max(mats, key=mats.get)
    bmesh.ops.delete(bm, geom=hand, context='VERTS')
    # the fist
    nv = []
    for p in D['v']:
        q = _u2b(p); v = bm.verts.new(Mi @ q); h = p[5]
        v[dl][gi] = 1.0                    # rigid on the hand bone: blending into the soldier forearm tore the cuff
        nv.append((v, q))
    bm.verts.index_update()
    for f in D['f']:
        try:
            face = bm.faces.new([nv[f[0]][0], nv[f[2]][0], nv[f[1]][0]])   # mirrored, so flip the winding
        except ValueError:
            continue
        face.material_index = mi; face.smooth = True
        for l in face.loops:
            q = next(qq for vv, qq in nv if vv is l.vert)
            l[uvl].uv = _glove_uv(q)
    bm.to_mesh(so.data); bm.free(); so.data.update()
    c = _u2b(HOLE_U[side]); a = Vector((0, 0, 1))
    w = -(c - a * c.dot(a)); w.normalize()
    if log is not None: log.append(f'hand_{side}: pilot fist {len(nv)} verts {len(D["f"])} tris, soldier hand {len(hand)} verts removed')
    return dict(c=c, a=a, w=w)
