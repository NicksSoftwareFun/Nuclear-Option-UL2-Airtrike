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
        e1 = E[-1]
        pts += [e1 + Vector((-0.01, 0.10, -0.02 - 0.026 * k)), Vector((-0.20, 0.97, 1.33 - 0.03 * k)), Vector((-0.06 + 0.05 * k, 0.94, 1.44))]
        path = crom(pts, 8)
        ST.tube(sc, f'cable{k}', path, 0.011, BLK)
        end = Vector((-0.06 + 0.05 * k, 0.94, 1.44))
        cyl(f'mfdplug{k}', end, end - Vector((0, 0.04, 0)), 0.02, CON); cyl(f'mfdring{k}', end - Vector((0, 0.01, 0)), end - Vector((0, 0.025, 0)), 0.024, CON)
    # zip ties round both cables and the edge lip every ~0.18 m
    for i, e in enumerate(E):
        if i % 3: continue
        c0 = e + Vector((-0.018, 0, -0.043)); lip = e + Vector((0.01, 0, -0.005))
        loop = [lip + Vector((0.006, 0, 0.006)), c0 + Vector((-0.016, 0, 0.03)), c0 + Vector((-0.016, 0, -0.045)), c0 + Vector((0.012, 0, -0.045)), lip + Vector((0.006, 0, -0.03)), lip + Vector((0.006, 0, 0.006))]
        ST.tube(sc, f'zip{i}', loop, 0.0035, TIE)
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(c0 + Vector((-0.02, 0, 0.0))) @ Matrix.Diagonal((0.01, 0.012, 0.014, 1))); ST.link(sc, f'ziphead{i}', bm, TIE)
    # a couple of ties to the seat 3 frame on the way down
    for i, pp in enumerate((Vector((-0.33, -0.70, 1.48)), Vector((-0.34, -0.42, 1.14)))):
        ST.tube(sc, f'zipS{i}', [pp + Vector((0.02 * math.cos(a), 0, 0.03 * math.sin(a))) for a in [j * math.pi / 5 for j in range(11)]], 0.0035, TIE)
