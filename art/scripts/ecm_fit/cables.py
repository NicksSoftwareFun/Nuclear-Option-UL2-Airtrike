# ECM pod data/power cables: two MIL-style multi-pin connectors on the pod's pylon face, thick cables zip-tied along the
# left cockpit edge, plugging into the back of the gunner MFD
import bpy, bmesh, math
from mathutils import Vector, Matrix
def crom(P, n=8):
    P = [Vector(p) for p in P]; P = [P[0]] + P + [P[-1]]; out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            t = k / n; out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2]); return out
def run(sc, ST, base, ax, nz, hi, mk):
    BLK = mk('cable', (0.03, 0.03, 0.03), 0.6); CON = mk('connector', (0.30, 0.32, 0.28), 0.4, 0.8); TIE = mk('ziptie', (0.02, 0.02, 0.02), 0.5)
    def cyl(name, a, b, r, mat, seg=20):
        bm = bmesh.new(); d = b - a; q = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=Matrix.Translation((a + b) / 2) @ q)
        return ST.link(sc, name, bm, mat)
    # edge of the cockpit side wall, left side, sampled from the model
    src = open('/mnt/project-files/ul2/art/scripts/proto/ul2_proto.py').read()
    ns = {'math': math}; exec(src[src.index('POD = ['):src.index('OPEN_F, OPEN_A')], ns); exec(src[src.index('def sill(y)'):src.index('NS = 40')], ns)
    # final model: body ahead of seat 3 moved aft 0.12 (GAP23) and lifted 0.12 (LIFT)
    E = [Vector((-ns['pod_at'](y + 0.12)[0], y, ns['sill'](y + 0.12) + 0.12)) for y in [0.14 + 0.06 * k for k in range(11)]]
    print('edge', [(round(e.x, 2), round(e.y, 2), round(e.z, 2)) for e in E])
    zt = 0
    for k, sx in enumerate((-0.045, 0.045)):
        d = 1.10 + 0.0 * k
        p = base + ax * d + Vector((sx, 0, 0)); p = p + nz * (hi - p.dot(nz))
        # receptacle flange + shell on the pod, plug + coupling ring + 90 deg backshell
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(p + nz * 0.004) @ Matrix((Vector((1, 0, 0)), ax, nz)).transposed().to_4x4() @ Matrix.Diagonal((0.07, 0.07, 0.008, 1)))
        ST.link(sc, f'conflange{k}', bm, CON)
        cyl(f'conshell{k}', p, p + nz * 0.03, 0.026, CON)
        cyl(f'conring{k}', p + nz * 0.03, p + nz * 0.05, 0.031, CON, 24)
        cyl(f'conplug{k}', p + nz * 0.05, p + nz * 0.085, 0.026, CON)
        q = p + nz * 0.10
        ST.tube(sc, f'backshell{k}', [p + nz * 0.08, q, q + Vector((-0.04, 0, 0))], 0.02, CON)
        off = Vector((0, 0, -0.03 * k))
        pts = [q + Vector((-0.04, 0, 0)), q + Vector((-0.16, -0.04, -0.03)), Vector((-0.31, -0.86, 1.72)) + off, Vector((-0.34, -0.62, 1.30)) + off, Vector((-0.34, -0.30, 1.05)) + off]
        pts += [e + Vector((-0.018, 0, -0.03 - 0.026 * k)) for e in E]
        G0 = Vector((0, 0.87, 1.45)); gn = Vector((0, math.cos(math.radians(12)), -math.sin(math.radians(12)))); gu = Vector((0, math.sin(math.radians(12)), math.cos(math.radians(12))))
        face = G0 + gn * 0.020 + gu * 0.030 + Vector((-0.045 + 0.09 * k, 0, 0))     # receptacle on the MFD back
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(face + gn * 0.003) @ Matrix((Vector((1, 0, 0)), gn, gu)).transposed().to_4x4() @ Matrix.Diagonal((0.034, 0.006, 0.034, 1)))
        ST.link(sc, f'mfdflange{k}', bm, CON)
        cyl(f'mfdshell{k}', face, face + gn * 0.020, 0.0145, CON)
        cyl(f'mfdring{k}', face + gn * 0.020, face + gn * 0.034, 0.0175, CON, 24)
        cyl(f'mfdplug{k}', face + gn * 0.034, face + gn * 0.050, 0.0150, CON)
        q2 = face + gn * 0.060
        ST.tube(sc, f'mfdback{k}', [face + gn * 0.048, q2, q2 - gu * 0.035], 0.0125, CON)     # 90 deg backshell turning down
        e1 = E[-1]
        pts += [e1 + Vector((0.0, 0.07, -0.03 - 0.026 * k)), Vector((-0.24 + 0.02 * k, 0.94, 1.27)), q2 - gu * 0.09 + Vector((0, 0.01, 0)), q2 - gu * 0.035]
        path = crom(pts, 8)
        ST.tube(sc, f'cable{k}', path, 0.011, BLK)
    # zip ties (Nick 16:41): only the 1st and 4th counted from the MFD end, flat 4.5 mm bands pulled tight round both
    # cables and through a hole just under the wall lip, locking head on the outside with a short cut tail
    keep = [len(E) - 2, len(E) - 2 - 9]
    for i in keep:
        e = E[i]; X = Vector((1, 0, 0)); Z = Vector((0, 0, 1))
        # band round both cables and a stick-on tie mount bonded to the hull skin between the cables and the wall
        loop = [(0.0015, -0.029), (-0.008, -0.0185), (-0.017, -0.0178), (-0.0265, -0.024), (-0.0300, -0.032), (-0.0300, -0.054),
                (-0.0265, -0.062), (-0.017, -0.0682), (-0.008, -0.0675), (0.0015, -0.057)]
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(e + X * -0.0035 + Z * -0.043) @ Matrix.Diagonal((0.0075, 0.022, 0.030, 1)))
        ST.link(sc, f'zipmount{i}', bm, TIE)
        P = [e + X * x + Z * z for x, z in loop]
        bm = bmesh.new(); rings = []
        for j, p in enumerate(P):
            a, b = P[j - 1], P[(j + 1) % len(P)]; tdir = (b - a).normalized(); nrm = tdir.cross(Vector((0, 1, 0))).normalized()
            rings.append([bm.verts.new(p + Vector((0, sy * 0.00225, 0)) + nrm * h) for sy in (-1, 1) for h in (-0.0006, 0.0006)])
        for j in range(len(rings)):
            A, B = rings[j], rings[(j + 1) % len(rings)]
            for a_, b_ in ((0, 1), (1, 3), (3, 2), (2, 0)): bm.faces.new((A[a_], A[b_], B[b_], B[a_]))
        ST.link(sc, f'zip{i}', bm, TIE)
        hd = e + X * -0.0325 + Z * -0.040
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(hd) @ Matrix.Diagonal((0.006, 0.0075, 0.009, 1))); ST.link(sc, f'ziphead{i}', bm, TIE)
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(hd + X * -0.005 + Z * -0.003) @ Matrix.Diagonal((0.006, 0.0045, 0.0012, 1))); ST.link(sc, f'ziptail{i}', bm, TIE)
