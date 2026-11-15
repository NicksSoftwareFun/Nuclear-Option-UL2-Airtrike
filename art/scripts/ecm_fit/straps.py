# ratchet straps: 6 in (0.152 m) flat webbing pulled taut around the pod front, hooks each end, ratchet block on one side
import bpy, bmesh, math
from mathutils import Vector, Matrix
def _mk(name, col, rough, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal; return m
WEB = _mk('webbing', (0.20, 0.21, 0.12), 0.9); STEEL = _mk('steel', (0.55, 0.56, 0.58), 0.35, 1.0); RED = _mk('ratchet', (0.12, 0.12, 0.13), 0.4, 0.6)
W, T = 0.152, 0.004
def hull2(P):
    P = sorted(set(P))
    def cr(o, a, b): return (a[0]-o[0])*(b[1]-o[1]) - (a[1]-o[1])*(b[0]-o[0])
    lo, up = [], []
    for p in P:
        while len(lo) >= 2 and cr(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(P):
        while len(up) >= 2 and cr(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]
def link(sc, name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(mat)
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); return o
def box(sc, name, M, size, mat):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1); bmesh.ops.transform(bm, matrix=M @ Matrix.Diagonal((*size, 1)), verts=bm.verts); return link(sc, name, bm, mat)
def tube(sc, name, pts, r, mat, seg=10):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = r; cu.bevel_resolution = 2
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts): sp.points[i].co = (*p, 1)
    cu.materials.append(mat); o = bpy.data.objects.new(name, cu); sc.collection.objects.link(o); return o
def frame_at(t, ax):
    t = t.normalized(); n = ax.cross(t).normalized(); return Matrix((t, ax, n)).transposed().to_4x4(), t, n
def strap(sc, tag, podverts, c, ax, f, anchors, ratchet_side=1, extra=()):
    """c: section centre, ax: pod long axis (= strap width dir), f: forward in the section plane, anchors: two 3D points (left, right)"""
    u = Vector((1, 0, 0)); f = (f - ax * f.dot(ax)).normalized(); u = (u - ax * u.dot(ax) - f * u.dot(f)).normalized()
    to2 = lambda p: (round((p - c).dot(u), 5), round((p - c).dot(f), 5))
    sec = []
    for p, q in podverts:
        for s in (-W / 2, W / 2):
            dp, dq = (p - c).dot(ax) - s, (q - c).dot(ax) - s
            if dp * dq < 0: sec.append(to2(p.lerp(q, dp / (dp - dq))))
    # offset section outward a touch so the web sits on the skin
    cx = sum(s[0] for s in sec) / len(sec); cy = sum(s[1] for s in sec) / len(sec)
    sec = [(cx + (x - cx) * 1.0 + math.copysign(0.004, x - cx), cy + (y - cy) + math.copysign(0.004, y - cy)) for x, y in sec]
    for p, q in extra:   # other things the strap wraps round (seat back)
        for s in (-W / 2, 0, W / 2):
            dp, dq = (p - c).dot(ax) - s, (q - c).dot(ax) - s
            if dp * dq < 0:
                x, y = to2(p.lerp(q, dp / (dp - dq))); sec.append((x + math.copysign(0.004, x), y))
    A, B = to2(anchors[0]), to2(anchors[1])
    H = hull2(sec + [A, B]); ia, ib = H.index(A), H.index(B)
    ch1 = [H[(ia + k) % len(H)] for k in range((ib - ia) % len(H) + 1)]
    ch2 = [H[(ia - k) % len(H)] for k in range((ia - ib) % len(H) + 1)]
    ch = max((ch1, ch2), key=lambda ch: max(p[1] for p in ch))   # the chain round the pod front
    P = [c + u * x + f * y for x, y in ch]
    # ribbon
    bm = bmesh.new(); prev = None; out = []
    for i, p in enumerate(P):
        t = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized(); n = ax.cross(t).normalized()
        if (p - c).dot(n) < 0 and 0 < i < len(P) - 1: n = -n
        vs = [bm.verts.new(p + ax * s * W / 2 + n * h) for s in (-1, 1) for h in (0, T)]
        if prev: 
            for a_, b_ in ((0, 1), (1, 3), (3, 2), (2, 0)): bm.faces.new((prev[a_], prev[b_], vs[b_], vs[a_]))
        prev = vs
    link(sc, f'web_{tag}', bm, WEB)
    # hooks at both ends: flat J hook bent round the anchor bar
    for k, (end, nxt) in enumerate(((P[0], P[1]), (P[-1], P[-2]))):
        t = (end - nxt).normalized(); n = ax.cross(t).normalized()
        M, t, n = frame_at(t, ax)
        box(sc, f'hookplate_{tag}{k}', Matrix.Translation(end - t * 0.03) @ M, (0.06, W * 0.75, 0.008), STEEL)
        R0 = 0.022; ctr = end + t * 0.01 + n * R0
        pts = [end - t * 0.01] + [ctr + t * (R0 * math.sin(math.radians(a))) - n * (R0 * math.cos(math.radians(a))) for a in range(0, 200, 20)] + [ctr - t * 0.02 + n * 0.02]
        for s in (-0.035, 0.035): tube(sc, f'hook_{tag}{k}{s}', [p + ax * s for p in pts], 0.007, STEEL)
    # ratchet block on one side, part way along the tail run
    i0 = 0 if ratchet_side < 0 else len(P) - 1; i1 = 1 if ratchet_side < 0 else len(P) - 2
    a_, b_ = P[i0], P[i1]; p = a_.lerp(b_, 0.45); t = (b_ - a_).normalized(); M, t, n = frame_at(t, ax)
    if (p - c).dot(n) < 0: n = -n; M = Matrix((t, ax, n)).transposed().to_4x4() if False else M
    box(sc, f'ratchet_body_{tag}', Matrix.Translation(p + n * 0.025) @ M, (0.17, W + 0.03, 0.035), RED)
    for s in (-1, 1): box(sc, f'ratchet_side_{tag}{s}', Matrix.Translation(p + n * 0.035 + ax * s * (W / 2 + 0.018)) @ M, (0.15, 0.008, 0.06), STEEL)
    tube(sc, f'ratchet_spool_{tag}', [p + n * 0.04 - ax * (W / 2 + 0.02), p + n * 0.04 + ax * (W / 2 + 0.02)], 0.02, STEEL)
    h0 = p + n * 0.05 - t * 0.02; tube(sc, f'ratchet_handle_{tag}', [h0 - ax * 0.07, h0 - ax * 0.07 + t * 0.13 + n * 0.04, h0 + ax * 0.07 + t * 0.13 + n * 0.04, h0 + ax * 0.07], 0.009, RED)
    return P
