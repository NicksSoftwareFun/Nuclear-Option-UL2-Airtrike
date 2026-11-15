"""Seat 3 hands (Nick 2026-11-15: gunner hands get the same treatment as the pilot hands, soldier textures): the
stock soldier's own skinned hands are kept (his texture, his UVs) and closed into a grip in the REST mesh. Each finger
is bent round a circle under its knuckle (a beam bend: arc length along the finger becomes angle round the grip), so
the fingers wrap a grip of radius rg. The thumb is bent the same way toward the palm by a smaller angle.

    hole = curl(so, ao, 'R', rg)   -> dict(c, a, n) in hand-bone-local rest coordinates:
        c  centre of the grip hole, a  grip axis (unit, toward the thumb / index side), n  back-of-hand normal at the knuckles
"""
import bmesh, math
from mathutils import Vector

FING_R = 0.0085          # finger half thickness: grip surface to finger centreline


def _comps(bm, idx, P):
    S = set(idx); seen = set(); out = []
    for i in idx:
        if i in seen: continue
        st = [i]; seen.add(i); c = []
        while st:
            q = st.pop(); c.append(q)
            for e in bm.verts[q].link_edges:
                w = e.other_vert(bm.verts[q]).index
                if w in S and w not in seen: seen.add(w); st.append(w)
        out.append(c)
    return out


def _merge(comps, P, tol=0.013):
    """the soldier mesh carries two shells per finger; pair them up by centroid"""
    cen = [sum((P[i] for i in c), Vector()) / len(c) for c in comps]; grp = list(range(len(comps)))
    for i in range(len(comps)):
        for j in range(i + 1, len(comps)):
            if (cen[i] - cen[j]).length < tol:
                gi, gj = grp[i], grp[j]; grp = [gi if g == gj else g for g in grp]
    out = {}
    for k, g in enumerate(grp): out.setdefault(g, []).extend(comps[k])
    return list(out.values())


def _bend(P, ids, root, d, n, rho, tmax, yk):
    """beam bend: verts past the knuckle cut wrap round the circle centred rho below the root (toward -n). Arc length
    is counted from the cut plane (y = yk), so the verts on the cut stay put and the palm seam never tears"""
    k = d.cross(-n).normalized(); H = root - n * rho
    for i in ids:
        p = P[i]; t = (p.y - yk) / max(0.3, d.y)
        if t <= 0: continue
        u = (p - H).dot(n); w = (p - root).dot(k)
        th = min(t / rho, tmax); extra = max(0.0, t - tmax * rho)
        rad = n * math.cos(th) + d * math.sin(th); tan = -n * math.sin(th) + d * math.cos(th)
        P[i] = H + rad * u + tan * extra + k * w


def curl(so, ao, side, rg, thumb_ang=55.0, wrap=math.radians(250), log=None):
    gi = so.vertex_groups[f'hand_{side}'].index; b = ao.data.bones[f'hand_{side}']
    M = (ao.matrix_world @ b.matrix_local).inverted() @ so.matrix_world; Mi = M.inverted()
    bm = bmesh.new(); bm.from_mesh(so.data); bm.verts.ensure_lookup_table(); dl = bm.verts.layers.deform.active
    hv = [v.index for v in bm.verts if v[dl].get(gi, 0) > 0.3]
    P = {i: M @ bm.verts[i].co for i in hv}
    ymax = max(P[i].y for i in hv)
    # knuckle line: lowest cut where exactly four fingers (pairs of shells) come out above it
    yk = None
    for k in range(60):
        y = 0.08 + 0.0025 * k
        cs = _merge(_comps(bm, [i for i in hv if P[i].y > y], P), P)
        fing = [c for c in cs if max(P[i].y for i in c) > ymax - 0.035 and len(c) > 8]
        if len(fing) == 4 and all(max(P[i].x for i in c) - min(P[i].x for i in c) < 0.045 for c in fing): yk = y; break
    if yk is None: raise RuntimeError(f'hand_{side}: fingers not found')
    cs = _merge(_comps(bm, [i for i in hv if P[i].y > yk], P), P)
    fing = [c for c in cs if max(P[i].y for i in c) > ymax - 0.035 and len(c) > 8]
    rest = [c for c in cs if c not in fing and len(c) > 8]
    if log is not None:
        for c in cs: log.append(f'  piece n{len(c)} x{sum(P[i].x for i in c)/len(c):.3f} ymax{max(P[i].y for i in c):.3f} z{sum(P[i].z for i in c)/len(c):.3f} {"F" if c in fing else "-"}')
    # knuckle frame: a across the knuckles (toward the thumb side), d mean finger direction, n back of the hand
    roots = []; tips = []
    for c in fing:
        lo = [P[i] for i in c if P[i].y < yk + 0.008] or [min((P[i] for i in c), key=lambda p: p.y)]
        r_ = sum(lo, Vector()) / len(lo); r_.y = yk; roots.append(r_)
        far = sorted(c, key=lambda i: -(P[i] - roots[-1]).length)[:6]; tips.append(sum((P[i] for i in far), Vector()) / len(far))
    tsx = -1 if side == 'R' else 1                                   # thumb side in the bone frame (measured, 2026-11-15)
    order = sorted(range(4), key=lambda j: roots[j].x * tsx)        # little ... index
    a = (roots[order[-1]] - roots[order[0]]).normalized()
    # the thumb side is where the hand sticks out furthest past the end knuckle (the rest poses are rotated in the bone
    # frame, so x alone is not reliable)
    band = [P[i] for i in hv if yk - 0.07 < P[i].y < yk + 0.03]
    pr = [(p - roots[order[-1]]).dot(a) for p in band]; pl = [(p - roots[order[0]]).dot(a) for p in band]
    if -min(pl) > max(pr): order = order[::-1]; a = -a
    dm = sum(((tips[j] - roots[j]).normalized() for j in range(4)), Vector()).normalized()
    dm = (dm - a * dm.dot(a)).normalized()
    n = dm.cross(a).normalized()
    if n.z < 0: n = -n                                               # back of the hand = bone +z side
    if log is not None:
        bow = 0.0
        for j, c in enumerate(fing):
            ch = tips[j] - roots[j]; L = ch.length; u = ch / L
            for i in c:
                t = (P[i] - roots[j]).dot(u)
                if 0.25 * L < t < 0.75 * L: bow += (P[i] - roots[j] - u * t).dot(n)
        log.append(f'hand_{side}: relaxed-finger bow along n {bow:+.3f} (dorsal bulge > 0)')
    # the thumb: lower the cut from the knuckle line until the thumb would join the palm; the largest piece above the cut
    # that holds no finger verts is the thumb (its part below the cut stays put, the soft weights keep the seam closed)
    from mathutils import Quaternion
    infing = set(i for c in fing for i in c); tv = []
    for k in range(40):
        y = yk + 0.02 - 0.0025 * k
        cand = [c for c in _merge(_comps(bm, [i for i in hv if P[i].y > y], P), P) if len(c) > 8 and not (set(c) & infing)]
        if not cand:
            if tv: break
            continue
        c = max(cand, key=len)
        if tv and len(c) > 3 * len(tv) + 40: break            # grew into the palm: keep the previous cut
        tv = c
    # palm side: the thumb sits on the palm side of the knuckle plane (the bone axes do not say it, the right hand's rest
    # pose is rolled 45 deg in its bone frame)
    kc = sum(roots, Vector()) / 4
    if len(tv) > 10 and sum((P[i] - kc).dot(n) for i in tv) > 0: n = -n
    rho = rg + FING_R
    Hs = []
    for j, c in enumerate(fing):
        d = (tips[j] - roots[j]); d = (d - a * d.dot(a)).normalized()
        nf = (n - d * n.dot(d)).normalized()
        _bend(P, c, roots[j], d, nf, rho, wrap, yk); Hs.append(roots[j] - nf * rho)
    H = sum(Hs, Vector()) / 4
    # thumb swung toward the palm and across the grip front (soft weights from its root)
    if len(tv) > 10 and thumb_ang > 0:
        lo = sorted(tv, key=lambda i: P[i].y)[:max(3, len(tv) // 8)]
        root = sum((P[i] for i in lo), Vector()) / len(lo)
        tip = sum((P[i] for i in sorted(tv, key=lambda i: -(P[i] - root).length)[:6]), Vector()) / 6
        dt = (tip - root).normalized(); tdir = (-n * 0.7 - a * 0.7).normalized()
        ax = dt.cross(tdir); ax = ax.normalized() if ax.length > 1e-4 else a
        Lt = max(1e-3, (tip - root).length)
        for i in tv:
            f = max(0.0, min(1.0, (P[i] - root).dot(dt) / Lt)); f = f * f * (3 - 2 * f)
            P[i] = root + Quaternion(ax, math.radians(thumb_ang) * f) @ (P[i] - root)
        if log is not None: log.append(f'hand_{side}: thumb {len(tv)} verts, length {Lt:.3f}')
    for i in hv: bm.verts[i].co = Mi @ P[i]
    bm.to_mesh(so.data); bm.free(); so.data.update()
    if log is not None: log.append(f'hand_{side}: knuckles y {yk:.3f}, hole {tuple(round(x, 3) for x in H)}, axis {tuple(round(x, 2) for x in a)}')
    return dict(c=H, a=a, n=n, d=dm)
