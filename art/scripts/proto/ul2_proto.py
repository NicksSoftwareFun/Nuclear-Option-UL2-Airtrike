"""UL-2 Airtrike prototype airframe, built in game parts. Usage: python3 -I ul2_proto.py -- out.blend
Nose +Y, up +Z, right +X, metres, ground z=0. Untextured grey."""
import sys, math, bpy, bmesh
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from ul2lib import *
from mathutils import Vector, Matrix
OUT = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

# ================================================================ layout (crew pelvis points; stock pilot pose)
P1 = Vector((0, 1.50, 1.02))     # pilot
P2 = Vector((0, 0.35, 1.22))     # gunner
P3 = Vector((0, -0.55, 1.30))    # passenger (seat 3 mount); lowered 0.12 (Nick 2026-11-12)
# seat shells fitted to the figures (Nick 2026-11-12 11:01): stock pilot reclines ~16 deg, Ibis soldier sits upright
S_PIL, LEAN_PIL = Vector((0, -0.08, -0.07)), 16
S_SOL, LEAN_SOL = Vector((0, -0.25, -0.075)), 0
Q1, Q2, Q3 = P1 + S_PIL, P2 + S_PIL, P3 + S_SOL
LIFT = 0.12                      # ride height added at the end to everything but the gear
DY_AFT, DY_GEAR = 0.30, 0.43     # aft group (mast, engine, tail) and main gear shifted forward at the end
KZ = 0.60                        # keel top-ish
MAST_B = Vector((0, -1.38, KZ)); HEAD = Vector((0, -1.27, 3.02))
PROP = Vector((0, -2.22, 1.74)); PROP_R = 0.86
ROTOR_R = 4.6
WR, TRACK, GEAR_Y = 0.27, 1.00, -1.08

def bucket(tag, P, width=0.46, back=0.66, pan=0.40, lean=14):
    a = R(lean); up = Vector((0, -math.sin(a), math.cos(a))); nrm = Vector((0, math.cos(a), math.sin(a)))
    c0 = P + Vector((0, 0.30, -0.13)); c1 = P + Vector((0, -0.12, -0.15)); c2 = c1 + up * back
    path = crom([c0, P + Vector((0, 0.08, -0.15)), c1 + Vector((0, 0.02, 0.02)), c1 + up * 0.25, c2], 4)
    w = width / 2; t = 0.03
    sec = [(-w, 0.10), (-w, -0.0), (w, -0.0), (w, 0.10), (w - t, 0.10), (w - t, t), (-w + t, t), (-w + t, 0.10)]
    sweep(tag + '_shell', path, sec, SEAT, side=(1, 0, 0), smooth=False)
    box(tag + '_cushion', P + Vector((0, 0.08, -0.115)), (width - 0.08, 0.36, 0.05), SEAT, bevel=0.02)
    # back pad, headrest and harness follow the shell's lean (they used to tip forward into the figure)
    box(tag + '_backpad', c1 + up * 0.36 + nrm * 0.055, (width - 0.09, 0.05, 0.42), SEAT, rot=(a, 0, 0), bevel=0.02)
    hr = c2 - up * 0.04
    box(tag + '_headrest', hr + nrm * 0.065, (0.24, 0.07, 0.16), SEAT, rot=(a, 0, 0), bevel=0.02)
    for sx in (-1, 1):   # harness
        box(f'{tag}_strap{sx}', c1 + up * 0.52 + nrm * 0.086 + Vector((sx * 0.09, 0, 0)), (0.045, 0.012, 0.42), DARK, rot=(a, 0, 0))
    return c0, c1, c2

# Ibis soldier rifle (from the stock 8-man troop mesh, shipping approved) for the seat-side rifles
D = '/mnt/user-data/uploads/Nuclear Option Mods/UL2Art/ingame_ref/'
def extract_rifle():
    tr = load_obj(D + 'ibis_troops_F.obj', 'x', 'TMP')[0]
    bm = bmesh.new(); bm.from_mesh(tr.data); bm.verts.ensure_lookup_table(); seen = set(); keep = set()
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = []; seen.add(v.index)
        while st:
            a = st.pop(); comp.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen: seen.add(b.index); st.append(b)
        lo = Vector([min(q.co[i] for q in comp) for i in range(3)]); hi = Vector([max(q.co[i] for q in comp) for i in range(3)])
        # rifle of the soldier at x~0.27 (troop mesh space), held vertically in front of him
        if lo.x >= 0.255 and hi.x <= 0.36 and lo.y >= 0.49 and hi.y <= 0.87 and hi.z <= 0.96 and len(comp) < 40: keep.update(q.index for q in comp)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    print('rifle bounds', lo, hi, len(bm.verts))
    bmesh.ops.translate(bm, verts=bm.verts, vec=-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)))
    me = bpy.data.meshes.new('ibis_rifle'); bm.to_mesh(me); bm.free(); me.materials.append(mat(DARK))
    o = bpy.data.objects.new('ibis_rifle', me); bpy.data.objects.remove(tr); return [o]
RIFLE = extract_rifle()

# screen quad with 0..1 UVs (the game's camera feed is drawn on it), facing -Y in frame M
def screen(name, w, h, M, y=0.0, rot=False):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    vs = [bm.verts.new(M @ Vector(p)) for p in ((-w / 2, y, -h / 2), (w / 2, y, -h / 2), (w / 2, y, h / 2), (-w / 2, y, h / 2))]
    f = bm.faces.new(vs)
    # rot: the stock tac screen canvas is landscape with its side panels turned 90 deg for a portrait display:
    # canvas left goes to the screen top, canvas top to the screen right
    for lp, t in zip(f.loops, ((1, 0), (1, 1), (0, 1), (0, 0)) if rot else ((1, 0), (0, 0), (0, 1), (1, 1))): lp[uv].uv = t
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    if me.polygons[0].normal.dot(M.to_3x3() @ Vector((0, -1, 0))) < 0: me.flip_normals()
    o = bpy.data.objects.new(name, me); me.materials.append(mat(SCREEN)); coll('WORK').objects.link(o)
    PARTS[CUR[0]]['objs'].append(o); return o

# ================================================================ nose pod (cockpit_F + nose)
POD = [  # y, half width, bottom z, top z
    # nose v3 (Nick 2026-11-12 11:19): longer and aerodynamic, top sloping down to a low tip, clear tip fairing
    (3.05, 0.012, 0.688, 0.712), (3.043, 0.039, 0.671, 0.718), (3.03, 0.071, 0.645, 0.741), (3.004, 0.112, 0.609, 0.777), (2.965, 0.156, 0.571, 0.821), (2.92, 0.195, 0.536, 0.866), (2.868, 0.231, 0.504, 0.912), (2.803, 0.268, 0.473, 0.964), (2.725, 0.303, 0.443, 1.060), (2.647, 0.331, 0.421, 1.150), (2.562, 0.355, 0.404, 1.240), (2.478, 0.371, 0.393, 1.320), (2.4, 0.378, 0.39, 1.385), (2.20, 0.392, 0.375, 1.460),
    # cowl raised 2026-11-12 20:53 (Nick: the slope up to the dashboard higher, MFD and gauges in the first person view)
    (2.00, 0.398, 0.372, 1.410), (1.80, 0.394, 0.375, 1.280), (1.60, 0.380, 0.385, 1.150), (1.40, 0.370, 0.395, 1.180),
    # side walls carried aft past seat 2 as an open fairing round the copilot's legs, closing in a boat tail (Nick 2026-11-12 11:28)
    (1.00, 0.366, 0.405, 1.250), (0.60, 0.362, 0.415, 1.250), (0.35, 0.350, 0.430, 1.220), (0.20, 0.322, 0.452, 1.150),
    (0.08, 0.266, 0.492, 0.990), (-0.02, 0.180, 0.548, 0.925), (-0.08, 0.090, 0.610, 0.860), (-0.10, 0.025, 0.660, 0.800)]
def pod_at(y):
    for a, b in zip(POD, POD[1:]):
        if a[0] >= y >= b[0]:
            t = (a[0] - y) / (a[0] - b[0]); return [a[k] + (b[k] - a[k]) * t for k in (1, 2, 3)]
    return list((POD[0] if y > POD[0][0] else POD[-1])[1:])
OPEN_F, OPEN_A = 2.22, 0.10      # one open cockpit for pilot and copilot; the side walls run past seat 2
def sill(y):                     # opening edge height (0.15.5: the coaming runs straight into the windscreen base)
    keys = [(2.22, 1.455), (2.18, 1.400), (2.12, 1.260), (2.05, 1.120), (1.95, 1.060), (1.65, 1.030), (1.38, 1.000), (1.10, 0.970), (0.80, 0.980), (0.60, 1.020), (0.35, 1.030), (0.20, 1.000), (0.10, 0.960)]
    for a, b in zip(keys, keys[1:]):
        if a[0] >= y >= b[0]: t = (a[0] - y) / (a[0] - b[0]); return a[1] + (b[1] - a[1]) * t
    return 1.460 if y > 2.22 else 0.800
NS = 40
def e_top(y):                    # upper superellipse exponent: square shoulders round the instrument panel
    if 1.70 <= y <= 2.45: return 4.0
    if 2.45 < y < 2.70: return 4.0 + (2.1 - 4.0) * (y - 2.45) / 0.25
    if 1.40 < y < 1.70: return 2.1 + (4.0 - 2.1) * (y - 1.40) / 0.30
    return 2.1
def pod_ring(y):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); pts = []
    for i in range(NS):
        a = 2 * math.pi * i / NS; c, s = math.cos(a), math.sin(a)
        e = 2.6 if s < 0 else e_top(y)
        x = hw * math.copysign(abs(c) ** (2 / e), c)
        z = zm + (zt - zm if s > 0 else zm - zb) * math.copysign(abs(s) ** (2 / e), s)
        pts.append(Vector((x, y, z)))
    return pts
def build_pod(ys, name, m=SKIN, cut=True, cap_front=False, cap_back=False):
    bm = bmesh.new(); rings = [[bm.verts.new(p) for p in pod_ring(y)] for y in ys]
    for i in range(len(rings) - 1):
        ym = (ys[i] + ys[i + 1]) / 2
        for k in range(NS):
            q = (rings[i][k], rings[i][(k + 1) % NS], rings[i + 1][(k + 1) % NS], rings[i + 1][k])
            zc = sum(v.co.z for v in q) / 4
            if cut and OPEN_A < ym < OPEN_F and zc > sill(ym) - 0.004: continue
            bm.faces.new(q)
    if cap_front: bm.faces.new(rings[0])
    if cap_back: bm.faces.new(rings[-1][::-1])
    o = finish(name, bm, m); s = o.modifiers.new('thick', 'SOLIDIFY'); s.thickness = 0.018; s.offset = -1
    return o
def frange(a, b, n): return [a + (b - a) * i / n for i in range(n + 1)]

part('cockpit_F', 'UL2Airtrike', (0, 1.20, 0.62))
ys = sorted(set([round(y, 4) for y in frange(2.58, -0.10, 64)] + [OPEN_F, OPEN_A]), reverse=True)
build_pod(ys, 'pod_shell')
# rim along the cut edge
rim = [Vector((-pod_ring_x, y, sill(y))) for y, pod_ring_x in []]
def side_x(y, z):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); s = min(1, (z - zm) / (zt - zm)); e = e_top(y); return hw * (1 - abs(s) ** e) ** (1 / e)
def pod_x(y, z):                 # skin half width at any height
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb)
    if z >= zm: return side_x(y, z)
    s = min(1, (zm - z) / (zm - zb)); return hw * (1 - s ** 2.6) ** (1 / 2.6)
edge = [Vector((side_x(y, sill(y)) - 0.004, y, sill(y))) for y in frange(OPEN_F - 0.01, OPEN_A + 0.01, 24)]
sweep('rim_R', edge, circ(0.011, 10), DARK, side=(0, 0, 1))
sweep('rim_L', [Vector((-p.x, p.y, p.z)) for p in edge], circ(0.011, 10), DARK, side=(0, 0, 1))
# pod floor, ring frames (internal structure), keel shoe
box('pod_floor', (0, 1.93, 0.43), (0.50, 0.90, 0.025), CPIT)
for k, y in enumerate((2.55, 1.95, 1.45)):
    r = pod_ring(y); sec = [Vector((p.x * 0.94, y, 0.5 * (pod_at(y)[1] + pod_at(y)[2]) + (p.z - 0.5 * (pod_at(y)[1] + pod_at(y)[2])) * 0.94)) for p in r]
    sec = [p for p in sec if not (OPEN_A < y < OPEN_F and p.z > min(sill(y) - 0.03, 0.84 if y < 1.6 else 0.66))]
    sweep(f'pod_frame{k}', sec, rect(0.025, 0.03), MECH, side=(0, 1, 0), smooth=False)
# windscreen: low, HUD height only
def windscreen(y0, top_z, rake, name):
    bm = bmesh.new(); N = 18; rows = []
    for j in range(5):
        f = j / 4; row = []
        for i in range(N + 1):
            a = math.pi * i / N; base_z = sill(y0) + (pod_at(y0)[2] - sill(y0)) * math.sin(a)
            x0 = math.cos(a) * side_x(y0, sill(y0)) * 0.99
            z = base_z + (top_z - base_z) * f * (0.55 + 0.45 * math.sin(a)) if True else 0
            row.append(bm.verts.new((x0 * (1 - 0.18 * f), y0 - rake * f * (0.4 + 0.6 * math.sin(a)), z)))
        rows.append(row)
    for j in range(4):
        for i in range(N): bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    o = finish(name, bm, GLASS); s = o.modifiers.new('thick', 'SOLIDIFY'); s.thickness = 0.008
    sweep(name + '_frame', [v.co.copy() for v in []] or [Vector(r) for r in [(rows_xyz) for rows_xyz in []]] or
          [Vector((math.cos(math.pi * i / 18) * side_x(y0, sill(y0)) * 0.99, y0, sill(y0) + (pod_at(y0)[2] - sill(y0)) * math.sin(math.pi * i / 18))) for i in range(19)],
          rect(0.03, 0.022), DARK, side=(0, 1, 0), smooth=False)
def windscreen2(name, y0=2.315, z0=1.392, z1=1.68, hw=0.31, bow=0.07, rake=0.16):
    bm = bmesh.new(); N = 16; rows = []
    for j in range(5):
        f = j / 4; row = []
        for k in range(N + 1):
            a = math.pi * k / N
            row.append(bm.verts.new((math.cos(a) * hw * (1 - 0.12 * f), y0 + bow * math.sin(a) - rake * f, z0 + (z1 - z0) * f)))
        rows.append(row)
    for j in range(4):
        for k in range(N): bm.faces.new((rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]))
    o = finish(name, bm, GLASS); sm = o.modifiers.new('thick', 'SOLIDIFY'); sm.thickness = 0.008
    for tag, f in (('_base', 0.0), ('_top', 1.0)):
        sweep(name + tag, [Vector((math.cos(math.pi * k / N) * hw * (1 - 0.12 * f), y0 + bow * math.sin(math.pi * k / N) - rake * f, z0 + (z1 - z0) * f)) for k in range(N + 1)],
              rect(0.022, 0.018), DARK, side=(0, 0, 1), smooth=False)
    for sx in (-1, 1): sweep(f'{name}_post{sx}', [Vector((sx * hw, y0, z0)), Vector((sx * hw * 0.88, y0 - rake, z1))], rect(0.022, 0.022), DARK, side=(0, 1, 0), smooth=False)
def windscreen3(name, y_end=2.03, ph=80, n=28, rows=6):
    """0.15.5 (Nick 21:27: sleeker, more curved, continuous): one wrap-round sheet. Its base runs along the coaming and
    down the cockpit rims; each column is a bulged quadratic curve leaning aft, the top edge falls away to the sides."""
    xf = side_x(OPEN_F, sill(OPEN_F - 0.001)) - 0.006
    side = [Vector((-(side_x(y, sill(y)) - 0.004), y, sill(y) + 0.011)) for y in frange(y_end, OPEN_F - 0.004, 12)]
    path = side + [Vector((x, OPEN_F - 0.002, sill(OPEN_F - 0.001) + 0.004)) for x in frange(side[-1].x * 0.9, -side[-1].x * 0.9, 10)] + [Vector((-p.x, p.y, p.z)) for p in reversed(side)]
    L = [0.0]
    for p0, p1 in zip(path, path[1:]): L.append(L[-1] + (p1 - p0).length)
    def at(d):
        for k in range(len(path) - 1):
            if L[k + 1] >= d: t = (d - L[k]) / max(1e-9, L[k + 1] - L[k]); return path[k].lerp(path[k + 1], t)
        return path[-1]
    bm = bmesh.new(); grid = []; base_c, top_c = [], []
    for i in range(n + 1):
        u = 2 * i / n - 1; f = math.radians(ph * u); sf, cf = math.sin(f), math.cos(f)
        B = at(L[-1] * i / n)
        za, zb_ = 1.68 - 0.42 * sf * sf, B.z + 0.15; T = Vector((B.x * 0.84, B.y - (0.04 + 0.20 * cf * cf), (za + zb_ + math.sqrt((za - zb_) ** 2 + 0.01)) / 2))   # smooth max: no shoulder kink
        Cm = (B + T) / 2 + Vector((sf, cf, 0.35)).normalized() * 0.045
        col = [bm.verts.new(B * (1 - t) ** 2 + Cm * 2 * t * (1 - t) + T * t * t) for t in (j / rows for j in range(rows + 1))]
        grid.append(col); base_c.append(B); top_c.append(T)
    for i in range(n):
        for j in range(rows): bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    edges = [[grid[i][j].co.copy() for j in range(rows + 1)] for i in (0, n)]
    o = finish(name, bm, GLASS); sm = o.modifiers.new('thick', 'SOLIDIFY'); sm.thickness = 0.008
    sweep(name + '_base', base_c, rect(0.012, 0.008), DARK, side=(0, 0, 1))
    sweep(name + '_top', top_c, rect(0.007, 0.006), DARK, side=(0, 0, 1))
    for k, e in enumerate(edges): sweep(f'{name}_edge{k}', e, rect(0.007, 0.006), DARK, side=(1, 0, 0))
windscreen3('windscreen')
# HUD combiner + projector on the glare shield
box('hud_projector', (0, 2.19, 1.452), (0.10, 0.055, 0.032), DARK, bevel=0.008)
box('hud_glass', (0, 2.135, 1.545), (0.13, 0.008, 0.11), GLASS, rot=(R(-22), 0, 0))
for sx in (-1, 1): pipe(f'hud_arm{sx}', [(sx * 0.07, 2.19, 1.465), (sx * 0.072, 2.165, 1.50), (sx * 0.07, 2.142, 1.545)], 0.006, DARK, 4, segs=6)
# instrument binnacle (initial): one MFD with side buttons, analog gauges around it
# 0.15.5 (Nick 21:27): larger portrait MFD in the middle, smaller gauges either side, all inside the cowl skin
PM = Matrix.Translation((0, 2.17, 1.29)) @ Matrix.Rotation(R(-20), 4, 'X')
GAUGES = ((0.125, 0.045), (0.198, 0.045), (0.125, -0.04), (0.198, -0.04)); GR = 0.028
box('panel', (0, 0, 0), (0.50, 0.035, 0.28), CPIT, M=PM, bevel=0.012)
box('mfd', (0, -0.022, 0.0), (0.16, 0.02, 0.27), DARK, M=PM, bevel=0.006)
box('panel_box', (0, 2.25, 1.27), (0.40, 0.08, 0.20), CPIT, bevel=0.01)
box('panel_pedestal', (0, 2.30, 0.78), (0.12, 0.08, 0.70), CPIT, bevel=0.01)
part('x_mfd1', 'cockpit_F', tuple(PM.translation)); screen('mfd_screen', 0.12, 0.24, PM, -0.036, rot=True); part('cockpit_F')
for sx in (-1, 1):
    for k in range(6): box(f'mfd_btn{sx}{k}', (sx * 0.070, -0.034, 0.090 - 0.036 * k), (0.016, 0.008, 0.016), MECH, M=PM)
    for k, (gx, gz) in enumerate(GAUGES):
        revolve(f'gauge{sx}{k}', (sx * gx, -0.018, gz), (0, -1, 0), [(0, GR), (0.010, GR), (0.010, GR - 0.005), (0.005, GR - 0.005), (0.005, 0)], DARK, 16, M=PM)
# exterior detail (pass 2): raised panel seams, side seam, boarding step, antennas
def seam_ring(y, tag, cut=True):
    pts = pod_ring(y); hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb)
    pts = [Vector((p.x * 1.006, y, zm + (p.z - zm) * 1.006)) for p in pts]
    if cut:
        k0 = max(range(NS), key=lambda i: pts[i].z)            # open the ring at the cockpit opening
        lim = sill(y) - 0.02
        run, runs = [], []
        for i in range(NS):
            p = pts[(k0 + i) % NS]
            if p.z < lim: run.append(p)
            elif run: runs.append(run); run = []
        if run: runs.append(run)
        for j, r in enumerate(runs):
            if len(r) > 2: sweep(f'seam_{tag}{j}', r, rect(0.006, 0.0025), DARK, side=(0, 1, 0), smooth=False)
    else:
        sweep(f'seam_{tag}', pts, rect(0.006, 0.0025), DARK, side=(0, 1, 0), smooth=False, closed=True)
for y, tag in ((2.02, 'c'), (1.55, 'd')): seam_ring(y, tag)
for sx in (-1, 1):
    side = []
    for y in frange(2.68, 1.30, 16):
        side.append(Vector((sx * (pod_x(y, 0.80) + 0.003), y, 0.80)))
    sweep(f'seam_side{sx}', side, rect(0.0025, 0.006), DARK, side=(sx, 0, 0), smooth=False)
# boarding step (left) on a short strut
box('board_step', (-0.47, 1.45, 0.47), (0.14, 0.20, 0.018), MECH, bevel=0.004)
for k, yy in enumerate((1.38, 1.52)): tube(f'board_step_strut{k}', (-0.40, yy, 0.47), (-0.33, yy, 0.55), 0.012, FRAME, 8)
# blade antenna under the pod and a GPS puck on the windscreen frame
plate('ant_belly', [(0.05, 0.0), (-0.05, 0.0), (-0.11, -0.15), (-0.06, -0.15)], 0.012, DARK, Matrix.Translation((0, 1.62, 0.39)) @ side_at(0.0), bevel=0.003)   # edge-on, swept back (Nick 21:41)
# pilot controls
part('x_stick', 'cockpit_F', (0, 1.95, 0.47))
pipe('stick', [(0, 1.95, 0.45), (0, 1.90, 0.84), (0, 1.80, 1.19)], 0.015, DARK, 4, segs=8)

# fighter style grip (Nick 2026-11-12 20:57): contoured handle, hat switch, trigger in a guard, weapon release,
# thumb castle switch, pinky lever. Frame GG: z up the grip, +y = finger side (forward)
GG = Matrix.Translation((0, 1.80, 1.185)) @ Matrix.Rotation(R(12), 4, 'X')
revolve('stick_collar', (0, 0, -0.005), (0, 0, 1), [(0, 0.024), (0.012, 0.024), (0.016, 0.018), (0.016, 0)], MECH, 14, M=GG)
ball('stick_grip', (0, 0.002, 0.068), (0.022, 0.027, 0.072), DARK, M=GG, seg=16)
ball('stick_palm', (0, -0.010, 0.060), (0.020, 0.022, 0.052), DARK, M=GG, seg=14)
ball('stick_head', (0, 0.006, 0.142), (0.025, 0.033, 0.026), DARK, M=GG, seg=16)
for k, z in enumerate((0.030, 0.052, 0.074)): tube(f'stick_finger{k}', (-0.017, 0.022, z), (0.017, 0.022, z), 0.006, DARK, 8, M=GG)
box('stick_trigger', (0, 0.034, 0.112), (0.011, 0.010, 0.028), MECH, rot=(R(-15), 0, 0), M=GG, bevel=0.003)
pipe('stick_guard', [(0, 0.024, 0.132), (0, 0.054, 0.118), (0, 0.052, 0.094), (0, 0.026, 0.090)], 0.0035, DARK, 4, segs=6, M=GG)
revolve('stick_hat_base', (0, -0.012, 0.164), (0, -0.25, 1), [(0, 0.011), (0.006, 0.011), (0.006, 0)], MECH, 12, M=GG)
box('stick_hat', (0, -0.012, 0.173), (0.016, 0.016, 0.006), MECH, M=GG, bevel=0.002)
box('stick_hat_x', (0, -0.012, 0.173), (0.026, 0.006, 0.004), MECH, M=GG)
box('stick_hat_y', (0, -0.012, 0.173), (0.006, 0.026, 0.004), MECH, M=GG)
revolve('stick_release', (-0.012, 0.016, 0.164), (0, 0, 1), [(0, 0.008), (0.005, 0.008), (0.005, 0)], MECH, 10, M=GG)
box('stick_castle', (-0.026, -0.006, 0.128), (0.006, 0.014, 0.014), MECH, M=GG, bevel=0.002)
box('stick_pinky', (0, 0.030, 0.012), (0.026, 0.010, 0.016), MECH, rot=(R(10), 0, 0), M=GG, bevel=0.002)
part('cockpit_F')
box('stick_boot', (0, 1.95, 0.47), (0.10, 0.10, 0.05), DARK, bevel=0.015)
box('lconsole_body', (-0.32, 1.82, 1.04), (0.09, 0.40, 0.22), CPIT, bevel=0.01)
box('throttle_box', (-0.32, 1.74, 1.175), (0.07, 0.20, 0.05), MECH, bevel=0.008)
box('throttle_slot', (-0.32, 1.74, 1.201), (0.014, 0.17, 0.004), DARK)
for k, yy in enumerate((1.66, 1.82)): box(f'throttle_mark{k}', (-0.29, yy, 1.201), (0.012, 0.006, 0.003), MECH)
part('x_throttle', 'cockpit_F', (-0.32, 1.74, 1.17))
tube('throttle', (-0.32, 1.74, 1.17), (-0.32, 1.74, 1.29), 0.010, MECH, 8)
# throttle grip (review 2): a lever head canted 15 deg inboard with a thumb ledge; mic hat on top, two small thumb
# buttons and a cursor slider on the inboard face, speed brake slider on the front
TM = Matrix.Translation((-0.32, 1.74, 1.285)) @ Matrix.Rotation(R(15), 4, 'Y')
box('throttle_knob', (0, 0, 0.05), (0.04, 0.06, 0.10), DARK, M=TM, bevel=0.012)
box('throttle_ledge', (0.026, -0.004, 0.022), (0.016, 0.05, 0.008), DARK, M=TM, bevel=0.003)
box('throttle_mic', (0, 0.01, 0.102), (0.012, 0.012, 0.006), MECH, M=TM, bevel=0.002)
for k, yy in enumerate((0.014, -0.012)): revolve(f'throttle_btn{k}', (0.0205, yy, 0.078), (1, 0, 0), [(0, 0.005), (0.003, 0.005), (0.003, 0)], MECH, 10, M=TM)
box('throttle_cursor', (0.0215, 0.0, 0.050), (0.004, 0.018, 0.010), MECH, M=TM, bevel=0.0015)
box('throttle_spdbrk', (0, 0.031, 0.065), (0.012, 0.004, 0.018), MECH, M=TM, bevel=0.0015)
box('throttle_friction', (-0.32, 1.74, 1.205), (0.03, 0.03, 0.012), DARK)
part('cockpit_F')
for sx in (-1, 1):
    # pedals 0.08 further aft so the stock pilot's boots reach them
    box(f'pedal_rest{sx}', (sx * 0.15, 2.32, 0.452), (0.15, 0.30, 0.02), CPIT, bevel=0.004)
    for k in (-1, 1): box(f'pedal_rail{sx}{k}', (sx * 0.15 + k * 0.03, 2.32, 0.468), (0.012, 0.28, 0.012), MECH)
    for k in (-1, 1): box(f'pedal_stop{sx}{k}', (sx * 0.15, 2.32 + k * 0.135, 0.47), (0.08, 0.012, 0.02), DARK)
    part('x_pedal_' + ('L' if sx < 0 else 'R'), 'cockpit_F', (sx * 0.15, 2.34, 0.48))
    box(f'pedal{sx}', (sx * 0.15, 2.34, 0.56), (0.11, 0.02, 0.15), MECH, rot=(R(-25), 0, 0), bevel=0.004)
    box(f'pedal_carriage{sx}', (sx * 0.15, 2.36, 0.478), (0.085, 0.10, 0.03), DARK, bevel=0.004)
    tube(f'pedal_bar{sx}', (sx * 0.15, 2.38, 0.49), (sx * 0.15, 2.35, 0.55), 0.01, MECH, 6)
    box(f'pedal_strap{sx}', (sx * 0.15, 2.325, 0.60), (0.115, 0.03, 0.02), DARK, rot=(R(-25), 0, 0))
    box(f'pedal_heel{sx}', (sx * 0.15, 2.295, 0.505), (0.10, 0.03, 0.02), DARK, rot=(R(-25), 0, 0))
    part('cockpit_F')
# ---- cockpit detail pass: gauge needles and bezels, standby compass, side consoles, harness
for sx in (-1, 1):
    for k, (gx, gz) in enumerate(GAUGES):
        # live needle: its own pseudo-part pivoted on the gauge centre, modelled pointing up; the plugin turns it
        gid = ('asi', 'alt', 'vsi', 'rotor')[k] if sx < 0 else ('rpm', 'fuel', 'oil', 'volt')[k]
        NM = PM @ Matrix.Translation((sx * gx, -0.027, gz))
        part(f'x_g_{gid}', 'cockpit_F', tuple(NM.translation))
        box(f'gauge_needle{sx}{k}', (0, 0, 0.009), (0.0035, 0.002, 0.019), MECH, M=NM)
        part('cockpit_F')
        revolve(f'gauge_bezel{sx}{k}', (sx * gx, -0.018, gz), (0, -1, 0), [(0, GR + 0.003), (0.012, GR + 0.003), (0.012, GR)], MECH, 16, M=PM, cap=False)
box('panel_lamp_bezel', (-0.16, -0.02, 0.105), (0.12, 0.012, 0.024), MECH, M=PM, bevel=0.003)
for k in range(3): box(f'panel_lamp{k}', (-0.196 + 0.036 * k, -0.027, 0.105), (0.03, 0.004, 0.016), DARK, M=PM)
box('panel_masterarm', (0.16, -0.022, 0.105), (0.05, 0.012, 0.03), MECH, M=PM, bevel=0.003)
box('panel_masterarm_guard', (0.16, -0.032, 0.105), (0.04, 0.006, 0.022), DARK, M=PM)
# standby compass on a stalk above the binnacle
tube('compass_stalk', (0.15, 2.185, 1.41), (0.15, 2.175, 1.445), 0.010, DARK, 6)
box('compass', (0.15, 2.17, 1.462), (0.055, 0.045, 0.036), DARK, bevel=0.010)
# left console beside the throttle: fuel / mags / lights switch strip; right console: radio head
box('lconsole', (-0.32, 1.93, 1.155), (0.08, 0.16, 0.012), DARK, bevel=0.003)
for k in range(4):
    tube(f'lconsole_sw{k}', (-0.32, 1.87 + 0.03 * k, 1.16), (-0.32, 1.875 + 0.03 * k, 1.19), 0.0035, MECH, 5)
box('fuel_selector', (-0.32, 1.99, 1.17), (0.03, 0.03, 0.012), MECH, bevel=0.004)
box('rconsole', (0.32, 1.82, 1.04), (0.09, 0.40, 0.22), CPIT, bevel=0.01)
box('radio_head', (0.32, 1.78, 1.165), (0.07, 0.14, 0.03), DARK, bevel=0.004)
for k in range(2): revolve(f'radio_knob{k}', (0.32, 1.74 + 0.08 * k, 1.18), (0, 0, 1), [(0, 0.012), (0.014, 0.012), (0.016, 0)], MECH, 10)
for k in range(3): tube(f'rconsole_sw{k}', (0.32, 1.90 + 0.03 * k, 1.15), (0.32, 1.905 + 0.03 * k, 1.18), 0.0035, MECH, 5)
# pilot seat on the pod floor / keel
def rifle_on_seat(tag, P, side=-1):
    # service rifle clamped vertically to the outboard side of the seat back, muzzle up. Stock Ibis rifle shape, painted
    # in the seat 3 guns' FDE scheme (Nick 2026-11-12 21:23) with a few small additions: sling, muzzle device, optic on a
    # rail along the carry handle, magazine base plate
    a = R(LEAN_PIL); M = Matrix.Translation(P + Vector((side * 0.29, -0.13, -0.16))) @ Matrix.Rotation(a, 4, 'X')
    for o in RIFLE:
        part_obj = o.copy(); me = o.data.copy(); part_obj.data = me
        me.materials.clear()
        for mn in (FDE_, BRONZE_, BLACK_): me.materials.append(mat(mn))
        for f in me.polygons:
            c = f.center
            if c.z > 0.716 or c.z < 0.04: k = 2                                   # muzzle / buttpad
            elif c.y > 0.03 and 0.20 < c.z < 0.315: k = 2                         # magazine
            elif c.y > 0.064 and 0.36 < c.z < 0.66: k = 1                         # carry handle
            elif abs(c.x) < 0.0075 and -0.022 < c.y < 0.021 and 0.41 < c.z < 0.51: k = 2   # trigger
            else: k = 0
            f.material_index = k
        me.transform(M); coll('WORK').objects.link(part_obj)
        part_obj.name = f'{tag}_rifle'; PARTS[CUR[0]]['objs'].append(part_obj)
    def L(*v): return M @ Vector(v)
    revolve(f'{tag}_rifle_muzzle', L(0, -0.049, 0.784), M.to_3x3() @ Vector((0, 0, 1)), [(0, 0.013), (0.035, 0.013), (0.035, 0.0)], BLACK_, 10)
    box(f'{tag}_rifle_rail', (0, 0.112, 0.50), (0.022, 0.008, 0.20), BLACK_, M=M)
    for k in range(9): box(f'{tag}_rifle_rail_rib{k}', (0, 0.117, 0.42 + 0.02 * k), (0.024, 0.004, 0.008), BLACK_, M=M)
    box(f'{tag}_rifle_optic', (0, 0.135, 0.50), (0.026, 0.034, 0.055), BLACK_, M=M, bevel=0.005)
    box(f'{tag}_rifle_optic_hood', (0, 0.135, 0.535), (0.03, 0.038, 0.012), FDE_, M=M, bevel=0.003)
    box(f'{tag}_rifle_magplate', (0, 0.166, 0.258), (0.03, 0.008, 0.10), FDE_, M=M)
    sw = [(0, -0.135, 0.06), (0, -0.165, 0.20), (0, -0.175, 0.40), (0, -0.165, 0.58), (0, -0.135, 0.68)]
    sweep(f'{tag}_rifle_sling', crom([L(*v) for v in sw], 4), rect(0.025, 0.004), FABRIC_, side=tuple(M.to_3x3() @ Vector((1, 0, 0))), smooth=False)
    for k, z in enumerate((0.06, 0.68)): box(f'{tag}_rifle_swivel{k}', (0, -0.13, z), (0.03, 0.012, 0.012), BLACK_, M=M)
    for dz in (0.10, 0.45): box(f'{tag}_rifle_clamp{dz}', P + Vector((side * 0.26, -0.13 - dz * math.sin(a), -0.16 + dz * math.cos(a))), (0.07, 0.05, 0.03), DARK, rot=(a, 0, 0))
P1_SEAT = True
# five-point harness on the stock pilot figure (Nick 2026-11-12 11:19): shoulder straps from the seat back over the
# shoulders to the buckle, lap belts from the seat sides, crotch strap from the pan. Points are relative to the hip point.
STRAP = rect(0.044, 0.006)
import numpy as _np
def _ref_pts(kind):
    """crew figure vertices relative to its hip point (Blender axes)"""
    if kind == 'pilot':
        o = load_obj(D + 'COIN_crew.obj', 'x', 'TMP', only=['renderer_pilot'])[0]
        V = _np.array([tuple(v.co) for v in o.data.vertices]) + _np.array((0, -3.06, 0.36)); bpy.data.objects.remove(o); return V
    V = []
    for ln in open(D + 'UL2_soldier_unity.obj'):
        if ln.startswith('v '): x, y, z = map(float, ln.split()[1:4]); V.append((x, z, y))
    return _np.array(V)
REFV = {}
def surf_z(kind, x, y, r=0.025, zmax=0.72):
    V = REFV.setdefault(kind, _ref_pts(kind)); m = (abs(V[:, 0] - x) < r) & (abs(V[:, 1] - y) < r) & (V[:, 2] < zmax)
    return float(V[m, 2].max()) if m.any() else None
def surf_y(kind, x, z, r=0.025, ymax=0.25):
    V = REFV.setdefault(kind, _ref_pts(kind)); m = (abs(V[:, 0] - x) < r) & (abs(V[:, 2] - z) < r) & (V[:, 1] < ymax)
    return float(V[m, 1].max()) if m.any() else None
G_ = 0.014   # strap stand-off from the figure surface
def harness5(tag, P):
    k = 'pilot'
    for sx in (-1, 1):
        pts = [(sx * 0.13, -0.31, 0.46)]
        for y in (-0.24, -0.16, -0.08, -0.01): pts.append((sx * 0.15, y, (surf_z(k, sx * 0.15, y, zmax=0.60) or 0.55) + G_))
        for x, z in ((0.14, 0.46), (0.12, 0.38), (0.095, 0.30), (0.065, 0.23), (0.04, 0.17)): pts.append((sx * x, (surf_y(k, sx * x, z) or 0.08) + G_, z))
        pts.append((sx * 0.02, 0.0, 0.0)); pts[-1] = (sx * 0.02, pts[-2][1], 0.14)
        sweep(f'{tag}_hn_sh{sx}', crom([P + Vector(v) for v in pts], 4), STRAP, DARK, side=(1, 0, 0), smooth=False)
        lap = [(sx * 0.22, -0.10, -0.07), (sx * 0.22, 0.02, 0.03)]
        for x, y in ((0.19, 0.08), (0.15, 0.11), (0.10, 0.13)): lap.append((sx * x, y, (surf_z(k, sx * x, y, zmax=0.30) or 0.10) + G_))
        lap.append((sx * 0.03, lap[-1][1] + 0.01, 0.14))
        sweep(f'{tag}_hn_lap{sx}', crom([P + Vector(v) for v in lap], 4), STRAP, DARK, side=(0, 0, 1), smooth=False)
    cz = [(0, 0.12, -0.14)] + [(0, (surf_y(k, 0, z, ymax=0.30) or 0.2) + G_, z) for z in (-0.05, 0.03, 0.10)]
    sweep(f'{tag}_hn_crotch', crom([P + Vector(v) for v in cz], 4), STRAP, DARK, side=(1, 0, 0), smooth=False)
    by = max(cz[-1][1], (surf_y(k, 0, 0.14) or 0.1) + G_) + 0.01
    revolve(f'{tag}_hn_buckle', P + Vector((0, by, 0.14)), (0, 1, 0.15), [(0, 0.0), (0, 0.045), (0.018, 0.045), (0.022, 0.03), (0.022, 0.0)], MECH, 16)
def lapbelt(tag, P):
    k = 'soldier'
    for sx in (-1, 1):
        lap = [(sx * 0.25, -0.12, -0.08), (sx * 0.25, -0.03, -0.02)]
        for x, y in ((0.21, 0.02), (0.16, 0.05), (0.10, 0.07)): lap.append((sx * x, y, (surf_z(k, sx * x, y, zmax=0.12) or 0.05) + G_))
        lap.append((sx * 0.03, lap[-1][1] + 0.02, lap[-1][2]))
        sweep(f'{tag}_hn_lap{sx}', crom([P + Vector(v) for v in lap], 4), STRAP, DARK, side=(0, 0, 1), smooth=False)
    z = max(surf_z(k, 0.0, 0.09, zmax=0.12) or 0.05, 0.0) + G_
    box(f'{tag}_hn_buckle', P + Vector((0, 0.09, z)), (0.08, 0.05, 0.016), MECH, bevel=0.006)

bucket('seat1', Q1, lean=LEAN_PIL)
for sx in (-1, 1): box(f'seat1_base{sx}', (sx * 0.15, Q1.y + 0.05, (Q1.z - 0.17 + 0.44) / 2), (0.05, 0.36, Q1.z - 0.17 - 0.44), FRAME)
rifle_on_seat('seat1', Q1)
_c = CUR[0]; part('x_harness1', 'cockpit_F', tuple(P1)); harness5('seat1', P1); part(_c)
# targeting pod: small stabilised ball behind the nose gear (lases a single target)
TP = Vector((0, 1.05, 0.27))   # 0.15.5: aft to mid belly (Nick 21:30); detail pass after the Cricket's turret ball
TR = 0.13
revolve('tpod_flange', (0, TP.y, pod_at(TP.y)[1] + 0.02), (0, 0, -1), [(0, 0.13), (0.012, 0.13), (0.02, 0.115), (0.02, 0)], MECH, 24)
for k in range(8):
    a = 2 * math.pi * (k + 0.5) / 8; revolve(f'tpod_bolt{k}', (0.118 * math.cos(a), TP.y + 0.118 * math.sin(a), pod_at(TP.y)[1] + 0.002), (0, 0, -1), [(0, 0.006), (0.006, 0.006), (0.006, 0)], DARK, 6)
revolve('tpod_az', (0, TP.y, pod_at(TP.y)[1]), (0, 0, -1), [(0, 0.105), (0.05, 0.105), (0.058, 0.098), (0.058, 0)], DARK, 24)
revolve('tpod_az_seam', (0, TP.y, pod_at(TP.y)[1] - 0.03), (0, 0, -1), [(0, 0.107), (0.006, 0.107), (0.006, 0)], MECH, 24)
for sx in (-1, 1):   # yoke cheeks holding the elevation axis
    plate(f'tpod_cheek{sx}', [(-0.075, 0.0), (0.075, 0.0), (0.06, -0.125), (0.0, -0.165), (-0.06, -0.125)], 0.022, MECH, Matrix.Translation((0, TP.y, pod_at(TP.y)[1] - 0.05)) @ side_at(sx * (TR + 0.018)), bevel=0.006)
    revolve(f'tpod_trunnion{sx}', (sx * (TR + 0.03), TP.y, TP.z), (sx, 0, 0), [(0, 0.04), (0.012, 0.04), (0.018, 0.03), (0.018, 0)], DARK, 16)
ball('tpod_ball', TP, (TR, TR, TR), MECH, seg=24)
revolve('tpod_seam', TP, (1, 0, 0), [(-0.004, TR + 0.002), (0.004, TR + 0.002)], DARK, 28, cap=False)
# sensor face: a flat window panel turned 20 deg down, big EO lens, IR lens, laser and rangefinder ports
WD = Vector((0, math.cos(R(20)), -math.sin(R(20))))
WC = TP + WD * (TR - 0.022)
revolve('tpod_window', WC, WD, [(0, 0.098), (0.026, 0.098), (0.030, 0.090), (0.030, 0)], DARK, 24)
revolve('tpod_eo', WC + WD * 0.031 + Vector((-0.032, 0, 0.012)), WD, [(0, 0.044), (0.004, 0.044), (0.006, 0.0)], SCREEN, 20)
revolve('tpod_eo_ring', WC + WD * 0.029 + Vector((-0.032, 0, 0.012)), WD, [(0, 0.050), (0.006, 0.050), (0.006, 0.044)], MECH, 20, cap=False)
revolve('tpod_ir', WC + WD * 0.031 + Vector((0.045, 0, 0.022)), WD, [(0, 0.028), (0.004, 0.028), (0.006, 0.0)], SCREEN, 16)
revolve('tpod_laser', WC + WD * 0.031 + Vector((0.040, 0.012, -0.035)), WD, [(0, 0.015), (0.006, 0.015), (0.006, 0.0)], SCREEN, 12)
revolve('tpod_lrf', WC + WD * 0.031 + Vector((-0.005, 0.014, -0.058)), WD, [(0, 0.010), (0.004, 0.010), (0.004, 0.0)], SCREEN, 10)
part('gear_F', 'cockpit_F', (0, 2.16, 0.50))
NY, NR = 2.10, 0.24
tube('nose_steer', (0, 2.16, 0.72), (0, 2.16, 0.51), 0.034, MECH, 14)
revolve('nose_collar', (0, 2.16, 0.51), (0, 0, 1), [(0, 0.05), (0.04, 0.05), (0.04, 0)], MECH, 14)
box('nose_crown', (0, 2.155, 0.505), (0.22, 0.07, 0.035), MECH, bevel=0.008)
for sx in (-1, 1):
    pipe(f'nose_fork{sx}', [(sx * 0.10, 2.155, 0.50), (sx * 0.10, 2.13, 0.37), (sx * 0.10, NY, NR)], 0.019, MECH, 5, segs=8)
    tube(f'nose_damper{sx}', (sx * 0.10, 2.17, 0.49), (sx * 0.10, 2.15, 0.37), 0.026, MECH, 10)
tube('nose_axle', (-0.125, NY, NR), (0.125, NY, NR), 0.013, MECH, 8)
wheel('wheel_F', (0, NY, NR), NR, 0.14)
arc = [Vector((0, NY + math.cos(a) * (NR + 0.04), NR + math.sin(a) * (NR + 0.04))) for a in frange(R(30), R(150), 10)]
sweep('nose_mudguard', arc, rect(0.17, 0.008), SKIN, side=(1, 0, 0))

NOSE_SPLIT = 2.58
part('nose', 'cockpit_F', (0, NOSE_SPLIT, 0.75))
NOSE_GLASS = 2.975     # smaller lamp window: the front half of the old glass dome is hull skin (Nick 2026-11-12 12:42)
build_pod(frange(NOSE_GLASS, NOSE_SPLIT, 10), 'nose_shell', cut=False, cap_front=False)
build_pod(frange(3.05, NOSE_GLASS, 10), 'nose_glass', m=GLASS, cut=False, cap_front=False)
hw_, zb_, zt_ = pod_at(NOSE_GLASS); zm_ = zb_ + 0.46 * (zt_ - zb_)
sweep('nose_glass_frame', [Vector((p.x * 1.01, p.y, zm_ + (p.z - zm_) * 1.01)) for p in pod_ring(NOSE_GLASS)], rect(0.012, 0.014), DARK, side=(0, 1, 0), closed=True, smooth=False)
# landing light inside the clear fairing: reflector bowl, lamp, mounting bulkhead
LL = Vector((0, NOSE_GLASS + 0.008, zm_ - 0.005))
box('lamp_bulkhead', (0, NOSE_GLASS - 0.012, zm_), (hw_ * 1.6, 0.012, (zt_ - zb_) * 0.8), DARK)
revolve('landing_light', LL, (0, 1, -0.12), [(0, 0.0), (0, 0.030), (0.012, 0.034), (0.03, 0.036), (0.03, 0.0)], MECH, 18)
revolve('landing_lamp', LL + Vector((0, 0.027, -0.003)), (0, 1, -0.12), [(0, 0.0), (0, 0.032), (0.004, 0.0)], SCREEN, 18)
def top_z(x, y):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); u = min(abs(x) / hw, 0.999)
    return zm + (zt - zm) * (1 - u ** 2.1) ** (1 / 2.1) + 0.004
HP = []
for x0, y0, x1, y1 in ((-0.11, 2.60, 0.11, 2.60), (0.11, 2.60, 0.08, 2.69), (0.08, 2.69, -0.08, 2.69), (-0.08, 2.69, -0.11, 2.60)):
    for t in frange(0, 1, 6)[:-1]: x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t; HP.append(Vector((x, y, top_z(x, y))))
sweep('nose_hatch', HP, rect(0.006, 0.0025), DARK, side=(0, 0, 1), smooth=False, closed=True)
for sx in (-1, 1): revolve(f'static_port{sx}', (sx * pod_at(2.62)[0], 2.62, 0.78), (sx, 0, 0), [(0, 0.012), (0.004, 0.012), (0.004, 0)], DARK, 10)
pipe('pitot', [(pod_at(2.56)[0] - 0.01, 2.56, 0.84), (pod_at(2.56)[0] + 0.02, 2.66, 0.84), (pod_at(2.56)[0] + 0.02, 2.88, 0.84)], 0.008, MECH, 3, segs=6)

# ================================================================ frame (root UL2Airtrike = centre section)
part('UL2Airtrike', None, (0, 0.0, KZ))
SQ = rrect(0.10, 0.10, 0.014, 2)
KEEL = [(0, 1.30, 0.50), (0, 1.00, 0.56), (0, 0.70, KZ), (0, -0.30, KZ)]
sweep('keel_C', crom(KEEL, 6), SQ, FRAME, smooth=False)
RAIL_Z = 0.74
for sx in (-1, 1):
    sweep(f'rail_C{sx}', crom([(sx * 0.27, 1.02, 0.80), (sx * 0.29, 0.75, RAIL_Z), (sx * 0.29, -0.30, RAIL_Z)], 5), circ(0.027, 10), FRAME, smooth=True)
    for k, y in enumerate((0.75, 0.10)):
        tube(f'post_C{sx}{k}', (sx * 0.29, y, RAIL_Z), (sx * 0.04, y, KZ - 0.01), 0.018, FRAME, 8)
    # gunner footrests
    tube(f'footrest2{sx}', (sx * 0.12, 0.98, 0.70), (sx * 0.30, 0.98, 0.70), 0.016, DARK, 8)
    tube(f'footrest2_post{sx}', (sx * 0.29, 0.98, 0.70), (sx * 0.29, 0.86, RAIL_Z), 0.014, FRAME, 8)
    box(f'footplate2{sx}', (sx * 0.24, 1.04, 0.70), (0.14, 0.14, 0.01), MECH)
for y in (0.75, 0.10, -0.28): box(f'cross_C{y}', (0, y, RAIL_Z), (0.58, 0.035, 0.035), FRAME)
box('keel_joint_F', (0, 1.04, 0.565), (0.11, 0.10, 0.11), MECH, rot=(R(-11), 0, 0), bevel=0.01)
# stub pylons (Ibis-style fuselage pylons); moved forward DYP so the launchers clear the main gear (Nick 2026-11-12);
# span extended (hardpoint at x HPX) and the pylon now stands ON TOP of the stub: the AT145 rack is mounted on top,
# flipped upside down so its mounting face sits on the pylon (Nick 2026-11-12 12:38)
DYP, HPX = 0.45, 0.86
for sx in (-1, 1):
    wing(f'stub{sx}', [((sx * 0.08, 0.36 + DYP, KZ - 0.02), (0, -0.40, 0), (0, 0, 1)), ((sx * (HPX + 0.05), 0.32 + DYP, KZ - 0.02), (0, -0.32, 0), (0, 0, 1))], SKIN, t=0.16)
    tube(f'stub_strut{sx}', (sx * 0.29, 0.46 + DYP, RAIL_Z), (sx * (HPX - 0.18), 0.20 + DYP, KZ - 0.01), 0.014, FRAME, 8)
    tube(f'stub_strut_b{sx}', (sx * 0.29, 0.02 + DYP, RAIL_Z), (sx * (HPX - 0.22), 0.16 + DYP, KZ - 0.01), 0.014, FRAME, 8)
    # beefed-up built-in pylon (Nick 2026-11-12 21:35): taller and longer with a launch rail on top, so the AGM-48 and
    # IRM-S2 sit straight on it (custom mounts without the stock launch pylon); hardpoint at the rail top, z PYT
    plate(f'pylon{sx}', [(0.30, 0), (-0.50, 0), (-0.40, 0.14), (0.20, 0.14)], 0.085, SKIN, Matrix.Translation((0, 0.20 + DYP, KZ - 0.02)) @ side_at(sx * HPX), bevel=0.012)
    box(f'pylon_rail{sx}', (sx * HPX, 0.15 + DYP, KZ + 0.13), (0.045, 0.62, 0.02), MECH, bevel=0.005)
    for yk in (0.40, 0.00, -0.30): box(f'pylon_lug{sx}{yk}', (sx * HPX, 0.20 + DYP + yk * 0.8 - 0.05, KZ + 0.12), (0.06, 0.03, 0.012), DARK)
    box(f'pylon_panel{sx}', (sx * (HPX + 0.043), 0.12 + DYP, KZ + 0.05), (0.004, 0.22, 0.07), MECH, bevel=0.002)
    for k in (-1, 1): tube(f'sway{sx}{k}', (sx * HPX - 0.06, 0.20 + DYP + 0.20 * k - 0.05, KZ + 0.10), (sx * HPX + 0.06, 0.20 + DYP + 0.20 * k - 0.05, KZ + 0.10), 0.009, MECH, 6)

# ---- gunner seat (cockpit_R)
part('cockpit_R', 'UL2Airtrike', (0, P2.y, RAIL_Z))
bucket('seat2', Q2, lean=LEAN_PIL)
rifle_on_seat('seat2', Q2)
_c = CUR[0]; part('x_harness2', 'cockpit_R', tuple(P2)); harness5('seat2', P2); part(_c)
for sx in (-1, 1):
    tube(f'seat2_leg_f{sx}', (sx * 0.17, Q2.y + 0.22, Q2.z - 0.17), (sx * 0.29, Q2.y + 0.30, RAIL_Z + 0.02), 0.016, FRAME, 8)
    tube(f'seat2_leg_r{sx}', (sx * 0.17, Q2.y - 0.12, Q2.z - 0.18), (sx * 0.29, Q2.y - 0.22, RAIL_Z + 0.02), 0.016, FRAME, 8)
for P, nm in ((Q2, 'seat2'),):
    for sx in (-1, 1):
        tube(f'{nm}_diag{sx}', (sx * 0.29, P.y + 0.30, RAIL_Z + 0.02) if nm == 'seat2' else (sx * 0.29, P.y + 0.32, RAIL_Z + 0.02), (sx * 0.17, P.y - 0.12, P.z - 0.18), 0.012, FRAME, 8)
    box(f'{nm}_crossbar', (0, P.y + 0.05, P.z - 0.163), (0.36, 0.03, 0.03), FRAME)
# gunner MFD post and hand grips
tube('mfd2_post', (0.08, 0.98, 0.80), (0, 0.98, 1.24), 0.017, FRAME, 8)
GM = Matrix.Translation((0, 0.99, 1.33)) @ Matrix.Rotation(R(-12), 4, 'X')
box('mfd2', (0, 0, 0), (0.25, 0.04, 0.20), DARK, M=GM, bevel=0.008)
part('x_mfd2', 'cockpit_R', tuple(GM.translation)); screen('mfd2_screen', 0.18, 0.15, GM, -0.024); part('cockpit_R')
box('inst2', (0, 0, -0.145), (0.25, 0.04, 0.08), DARK, M=GM, bevel=0.006)
part('x_inst2', 'cockpit_R', tuple(GM @ Vector((0, 0, -0.145)))); screen('inst2_screen', 0.22, 0.055, GM @ Matrix.Translation((0, 0, -0.145)), -0.024); part('cockpit_R')
for sx in (-1, 1):
    for k in range(4): box(f'mfd2_btn{sx}{k}', (sx * 0.108, -0.022, 0.055 - 0.037 * k), (0.016, 0.008, 0.016), MECH, M=GM)
    pipe(f'grip2_arm{sx}', [(sx * 0.05, 0.97, 1.20), (sx * 0.15, 0.84, 1.36), (sx * 0.20, 0.72, 1.45)], 0.014, FRAME, 4, segs=8)
    tube(f'grip2{sx}', (sx * 0.20, 0.68, 1.42), (sx * 0.20, 0.64, 1.55), 0.018, DARK, 10)
    box(f'grip2_trigger{sx}', (sx * 0.20, 0.68, 1.52), (0.02, 0.025, 0.035), MECH)
box('grip2_bar', (0, 0.70, 1.45), (0.40, 0.025, 0.025), FRAME)
# gunner detail: arming box on the grip bar, intercom box and cord on the MFD post
box('grip2_armbox', (0, 0.70, 1.435), (0.10, 0.04, 0.03), DARK, bevel=0.004)
for k in range(3): tube(f'grip2_sw{k}', (-0.03 + 0.03 * k, 0.70, 1.45), (-0.03 + 0.03 * k, 0.705, 1.47), 0.003, MECH, 5)
box('ics_box', (0.04, 0.97, 1.05), (0.05, 0.035, 0.07), DARK, bevel=0.004)
revolve('ics_knob', (0.04, 0.95, 1.07), (0, -1, 0), [(0, 0.01), (0.01, 0.01), (0.012, 0)], MECH, 8)
# ammo box at the gunner's feet (right), feeds the side gun
box('ammo_box', (0.30, 0.80, 0.66), (0.11, 0.26, 0.20), MECH, bevel=0.008)
box('ammo_lid', (0.30, 0.80, 0.765), (0.12, 0.27, 0.014), DARK)

# ================================================================ rear frame section (frame_R) + seat 3 + mast
part('frame_R', 'UL2Airtrike', (0, -0.30, KZ))
sweep('keel_R', [(0, -0.30, KZ), (0, -1.10, KZ)], SQ, FRAME, smooth=False)
for sx in (-1, 1):
    sweep(f'rail_R{sx}', [(sx * 0.29, -0.30, RAIL_Z), (sx * 0.29, -0.80, RAIL_Z)], circ(0.027, 10), FRAME)
    pipe(f'rail_R_end{sx}', [(sx * 0.29, -0.80, RAIL_Z), (sx * 0.26, -1.00, RAIL_Z - 0.03), (sx * 0.06, -1.10, KZ + 0.01)], 0.027, FRAME, 4)
    tube(f'post_R{sx}', (sx * 0.29, -0.62, RAIL_Z), (sx * 0.04, -0.62, KZ - 0.01), 0.018, FRAME, 8)
    # passenger footrest bar (raised)
    tube(f'footrest3_post{sx}', (sx * 0.29, -0.30, RAIL_Z), (sx * 0.29, -0.12, 0.80), 0.016, FRAME, 8)
    tube(f'footrest3{sx}', (sx * 0.14, -0.11, 0.80), (sx * 0.40, -0.11, 0.80), 0.016, DARK, 8)
box('cross_R', (0, -0.62, RAIL_Z), (0.58, 0.035, 0.035), FRAME)
box('mast_shoe', (0, -1.08, KZ + 0.02), (0.20, 0.18, 0.12), MECH, bevel=0.012)
box('fuel_tank', (0, -0.82, 0.88), (0.40, 0.22, 0.26), SKIN, bevel=0.04)
revolve('fuel_cap', (0.12, -0.82, 1.01), (0, 0, 1), [(0, 0.03), (0.02, 0.03), (0.02, 0)], DARK, 10)
box('battery', (-0.17, -0.45, 0.68), (0.14, 0.14, 0.10), DARK, bevel=0.008)
box('battery_tray', (-0.17, -0.45, 0.625), (0.30, 0.18, 0.012), FRAME)

part('seat3', 'frame_R', (0, P3.y, P3.z - 0.15))
bucket('seat3', Q3, width=0.54, lean=LEAN_SOL)
lapbelt('seat3', P3)
for sx in (-1, 1): tube(f'seat3_diag{sx}', (sx * 0.29, P3.y + 0.32, RAIL_Z + 0.02), (sx * 0.17, Q3.y - 0.12, Q3.z - 0.18), 0.012, FRAME, 8)
box('seat3_crossbar', (0, Q3.y + 0.05, Q3.z - 0.163), (0.36, 0.03, 0.03), FRAME)
for sx in (-1, 1):
    tube(f'seat3_leg_f{sx}', (sx * 0.12, Q3.y + 0.12, Q3.z - 0.19), (sx * 0.29, P3.y + 0.02, RAIL_Z + 0.02), 0.017, FRAME, 8)
    tube(f'seat3_leg_r{sx}', (sx * 0.16, Q3.y - 0.12, Q3.z - 0.18), (sx * 0.10, -0.92, 1.05), 0.017, FRAME, 8)
box('seat3_mast_bracket', (0, -0.94, P3.z + 0.10), (0.16, 0.20, 0.08), MECH, bevel=0.01)

# ---- mast: twin square tubes, near vertical, braces
part('mast', 'frame_R', MAST_B)
MD = HEAD - MAST_B
for sx in (-1, 1):
    sweep(f'mast_tube{sx}', [MAST_B + Vector((sx * 0.045, 0, 0.04)), HEAD + Vector((sx * 0.040, 0, -0.22))], rrect(0.045, 0.10, 0.008, 2), FRAME, smooth=False)
    tube(f'mast_brace{sx}', (sx * 0.29, -0.98, RAIL_Z), MAST_B + MD * 0.33 + Vector((sx * 0.05, 0.02, 0)), 0.018, FRAME, 8)
for k in range(6):
    p = MAST_B + MD * (0.10 + 0.16 * k)
    box(f'mast_web{k}', p, (0.09, 0.08, 0.035), MECH)
for sx in (-1, 1): tube(f'ctrl_rod{sx}', (sx * 0.09, -1.30, 0.80), HEAD + Vector((sx * 0.15, 0.10, -0.26)), 0.008, MECH, 6)
# gimbal head (fixed to the mast): cheek plates, roll + pitch pivots, control horns, pre-rotator pinion
for sx in (-1, 1):
    box(f'mast_cheek{sx}', HEAD + Vector((sx * 0.075, 0, -0.27)), (0.012, 0.15, 0.16), MECH, bevel=0.004)
    box(f'ctrl_horn{sx}', HEAD + Vector((sx * 0.115, 0.07, -0.24)), (0.09, 0.025, 0.02), MECH, bevel=0.004)
    ball(f'rod_end{sx}', HEAD + Vector((sx * 0.15, 0.09, -0.25)), (0.016,) * 3, DARK, seg=8)
tube('roll_bolt', HEAD + Vector((0, -0.09, -0.22)), HEAD + Vector((0, 0.09, -0.22)), 0.014, DARK, 8)
box('gimbal_block', HEAD + Vector((0, 0, -0.20)), (0.12, 0.10, 0.07), MECH, bevel=0.008)
tube('pitch_bolt', HEAD + Vector((-0.09, 0, -0.19)), HEAD + Vector((0.09, 0, -0.19)), 0.013, DARK, 8)
box('prerot_pinion', HEAD + Vector((0.12, -0.05, -0.09)), (0.07, 0.07, 0.08), MECH, bevel=0.008)
box('rotor_brake', HEAD + Vector((-0.12, 0.04, -0.06)), (0.05, 0.06, 0.04), DARK, bevel=0.006)
pipe('prerot_shaft', [(0.10, -1.52, 1.95), (0.10, -1.38, 2.30), (0.12, -1.32, 2.88)], 0.012, DARK, 5, segs=6)
box('mast_top', HEAD + Vector((0, 0, -0.34)), (0.14, 0.14, 0.05), MECH, bevel=0.01)

# ---- rotor head (visual hub; spins in game) and two blades split root / mid / tip
part('rotorHead', 'mast', HEAD)
revolve('head_bearing', HEAD + Vector((0, 0, -0.17)), (0, 0, 1), [(0, 0.05), (0.02, 0.07), (0.09, 0.07), (0.10, 0.055), (0.13, 0.055), (0.13, 0)], MECH, 18)
revolve('prerot_ring', HEAD + Vector((0, 0, -0.06)), (0, 0, 1), [(0, 0.03), (0, 0.12), (0.014, 0.12), (0.014, 0.03)], DARK, 24)
for sy in (-1, 1): plate(f'teeter_tower{sy}', [(-0.04, 0), (0.04, 0), (0.025, 0.12), (-0.025, 0.12)], 0.014, MECH,
                         Matrix.Translation(HEAD + Vector((0, sy * 0.045, -0.05))) @ Matrix.Rotation(R(90), 4, 'X'))
tube('teeter_bolt', HEAD + Vector((0, -0.075, 0.04)), HEAD + Vector((0, 0.075, 0.04)), 0.012, MECH, 10)
box('hub_bar', HEAD + Vector((0, 0, 0.05)), (1.20, 0.11, 0.032), MECH, bevel=0.005)
for sx in (-1, 1):
    if False: tube(f'hub_bolt{sx}', HEAD + Vector((sx * (0.45 + 0.04 * k), 0.03 * (-1) ** k, 0.02)), HEAD + Vector((sx * (0.45 + 0.04 * k), 0.03 * (-1) ** k, 0.08)), 0.008, DARK, 6)
CONE = R(1.5)
def bpt(sx, r): return HEAD + Vector((sx * r, 0, 0.05 + max(0, r - 0.6) * math.tan(CONE)))
for sx, tag in ((1, 'R'), (-1, 'L')):
    ch = Vector((0, -sx * 0.21, 0)); up = Vector((0, 0, 1))
    for nm, r0, r1 in (('root', 0.55, 1.90), ('mid', 1.90, 3.40), ('tip', 3.40, ROTOR_R)):
        part(f'blade_{tag}_{nm}', 'rotorHead' if nm == 'root' else f'blade_{tag}_' + ('root' if nm == 'mid' else 'mid'), bpt(sx, r0))
        wing(f'blade_{tag}_{nm}_w', [(bpt(sx, r0) - ch * 0.25, ch, up), (bpt(sx, r1) - ch * 0.25, ch, up)], MECH, t=0.12, n=8)
        if nm == 'root': box(f'blade_{tag}_grip', bpt(sx, 0.60), (0.20, 0.16, 0.045), MECH, bevel=0.005)
        if nm == 'tip': box(f'blade_{tag}_tipcap', bpt(sx, ROTOR_R) - ch * 0.25 + ch * 0.5, (0.02, 0.21, 0.026), DARK)

# ================================================================ engine: core + left + right
part('engine_core', 'mast', (0, -1.78, 1.74))
EY, EZ = -1.78, 1.74
box('crankcase', (0, EY, EZ), (0.32, 0.60, 0.36), MECH, bevel=0.04)
box('sump', (0, EY + 0.02, EZ - 0.22), (0.24, 0.46, 0.09), MECH, bevel=0.025)
revolve('gearbox', (0, EY - 0.27, EZ), (0, -1, 0), [(0, 0.12), (0.03, 0.14), (0.10, 0.12), (0.13, 0.07), (0.15, 0.07), (0.15, 0.09), (0.165, 0.09), (0.165, 0)], MECH, 20)
box('airbox', (0, EY + 0.02, EZ + 0.22), (0.30, 0.32, 0.09), DARK, bevel=0.02)
revolve('air_filter', (0, EY + 0.18, EZ + 0.22), (0, 1, 0), [(0, 0.045), (0.02, 0.06), (0.12, 0.06), (0.14, 0.04), (0.14, 0)], DARK, 14)
revolve('turbo', (0.16, EY + 0.22, EZ - 0.24), (0, 1, 0), [(0, 0.03), (0.01, 0.06), (0.06, 0.06), (0.07, 0.04), (0.10, 0.04), (0.11, 0.05), (0.14, 0.05), (0.15, 0)], MECH, 14)
box('oil_tank', (-0.20, EY + 0.30, EZ - 0.20), (0.10, 0.10, 0.24), DARK, bevel=0.03)
# engine mount ring + struts to the mast
for sx in (-1, 1):
    for dz in (-0.16, 0.16):
        tube(f'eng_mount{sx}{dz}', (sx * 0.05, -1.40, EZ + dz * 1.2), (sx * 0.12, EY + 0.20, EZ + dz), 0.014, FRAME, 8)
box('engine_bed', (0, EY + 0.10, EZ - 0.29), (0.34, 0.62, 0.035), FRAME, bevel=0.006)
for sx in (-1, 1):
    tube(f'bed_strut_f{sx}', (sx * 0.15, EY + 0.38, EZ - 0.29), (sx * 0.05, -1.40, 0.80), 0.016, FRAME, 8)
    tube(f'bed_strut_r{sx}', (sx * 0.15, EY - 0.18, EZ - 0.29), (sx * 0.03, -1.68, 0.56), 0.016, FRAME, 8)
part('propHub', 'engine_core', PROP)
revolve('spinner', PROP + Vector((0, 0.05, 0)), (0, -1, 0), [(0, 0.09), (0.03, 0.10), (0.11, 0.09), (0.19, 0.05), (0.24, 0.0)], DARK, 20)
for k in range(3):
    a = R(90 + 120 * k); d = Vector((math.cos(a), 0, math.sin(a))); t = Vector((-math.sin(a), 0, math.cos(a))); st = []
    for rr, chd, tw in [(0.10, 0.075, 55), (0.22, 0.135, 42), (0.45, 0.145, 28), (0.68, 0.115, 19), (0.82, 0.075, 15), (PROP_R, 0.03, 13)]:
        cdir = (t * math.cos(R(tw)) + Vector((0, 1, 0)) * math.sin(R(tw))); nrm = d.cross(cdir).normalized()
        st.append((PROP + d * rr - cdir * chd * 0.35, cdir * chd, nrm))
    wing(f'prop_blade{k}', st, DARK, t=0.11)
for sx, tag in ((1, 'R'), (-1, 'L')):
    part(f'engine_{tag}', 'engine_core', (sx * 0.15, EY, EZ))
    for k, cy in enumerate((EY + 0.13, EY - 0.13)):
        tube(f'cyl{tag}{k}', (sx * 0.16, cy, EZ), (sx * 0.32, cy, EZ), 0.082, MECH, 16)
        for q in range(6): revolve(f'fin{tag}{k}{q}', (sx * (0.175 + 0.024 * q), cy, EZ), (sx, 0, 0), [(0, 0.082), (0, 0.106), (0.006, 0.106), (0.006, 0.082)], MECH, 16)
        box(f'head{tag}{k}', (sx * 0.355, cy, EZ), (0.07, 0.21, 0.21), MECH, bevel=0.015)
        box(f'valvecover{tag}{k}', (sx * 0.405, cy, EZ), (0.035, 0.18, 0.18), DARK, bevel=0.012)
        pipe(f'intake{tag}{k}', [(sx * 0.32, cy, EZ + 0.10), (sx * 0.28, cy, EZ + 0.20), (sx * 0.13, EY, EZ + 0.24)], 0.022, MECH, 5, segs=8)
        pipe(f'exhaust{tag}{k}', [(sx * 0.32, cy, EZ - 0.10), (sx * 0.29, cy, EZ - 0.22), (sx * 0.14, EY - 0.05, EZ - 0.30)], 0.019, MECH, 5, segs=8)
        tube(f'plug{tag}{k}', (sx * 0.32, cy + 0.05, EZ + 0.09), (sx * 0.30, cy + 0.05, EZ + 0.16), 0.008, DARK, 6)
    box(f'radiator{tag}', (sx * 0.24, EY + 0.33, EZ - 0.05), (0.20, 0.05, 0.24), DARK, bevel=0.01)
    for q in range(8): box(f'radfin{tag}{q}', (sx * 0.24, EY + 0.36, EZ - 0.15 + 0.028 * q), (0.18, 0.012, 0.006), MECH)
    pipe(f'coolant{tag}', [(sx * 0.33, EY + 0.13, EZ + 0.08), (sx * 0.30, EY + 0.25, EZ + 0.06), (sx * 0.24, EY + 0.31, EZ + 0.04)], 0.012, DARK, 4, segs=6)
revolve('muffler', (-0.20, EY - 0.05, EZ - 0.34), (1, 0, 0), [(0, 0.03), (0.03, 0.07), (0.40, 0.07), (0.43, 0.03), (0.43, 0)], MECH, 14, M=None) if False else None
part('engine_core')
revolve('muffler', (-0.20, EY - 0.06, EZ - 0.33), (1, 0, 0), [(0, 0.03), (0.03, 0.07), (0.40, 0.07), (0.43, 0.03), (0.43, 0)], MECH, 14)
pipe('tailpipe', [(0.23, EY - 0.06, EZ - 0.33), (0.28, EY - 0.16, EZ - 0.36), (0.28, EY - 0.30, EZ - 0.40)], 0.022, MECH, 4, segs=8)

# ================================================================ tail boom (dips under the prop) + tail
part('tailboom_F', 'frame_R', (0, -1.40, KZ))
BOOM = [(0, -1.40, KZ), (0, -1.75, 0.52), (0, -2.15, 0.44)]
sweep('boom_F', crom(BOOM, 6), SQ, FRAME, smooth=False)
for sx in (-1, 1): tube(f'boom_brace{sx}', (sx * 0.26, -1.25, RAIL_Z - 0.03), (sx * 0.03, -1.95, 0.48), 0.016, FRAME, 8)
part('tailboom_R', 'tailboom_F', (0, -2.15, 0.44))
BOOM2 = [(0, -2.15, 0.44), (0, -2.50, 0.56), (0, -2.85, 0.82), (0, -3.30, 0.96)]
sweep('boom_R', crom(BOOM2, 8), [(x * 0.9, y * 0.9) for x, y in SQ], FRAME, smooth=False)
box('tail_saddle', (0, -3.10, 0.98), (0.14, 0.50, 0.05), MECH, bevel=0.008)
tube('tail_skid_strut', (0, -3.12, 0.92), (0, -3.30, 0.62), 0.016, FRAME, 8)
box('tail_skid', (0, -3.31, 0.60), (0.05, 0.16, 0.035), DARK, rot=(R(-8), 0, 0), bevel=0.01)
TZ = 1.02; X = Vector((1, 0, 0)); Z = Vector((0, 0, 1))
# horizontal stabiliser: inner + tip (with end plates) per side; elevator inner + tip
for sx, tag in ((1, 'R'), (-1, 'L')):
    part(f'hstab_{tag}', 'tailboom_R', (sx * 0.05, -2.92, TZ))
    wing(f'hstab_{tag}_w', [((sx * 0.04, -2.88, TZ), (0, -0.48, 0), Z), ((sx * 0.52, -2.90, TZ), (0, -0.46, 0), Z)], SKIN, t=0.10)
    for xk in (0.16, 0.42): tube(f'elev_hinge_{tag}{xk}', (sx * (xk - 0.02), -3.36, TZ), (sx * (xk + 0.02), -3.36, TZ), 0.011, MECH, 8)
    part(f'elevator_{tag}', f'hstab_{tag}', (sx * 0.05, -3.36, TZ))
    wing(f'elevator_{tag}_w', [((sx * 0.06, -3.362, TZ), (0, -0.20, 0), Z), ((sx * 0.52, -3.362, TZ), (0, -0.20, 0), Z)], SKIN, t=0.08)
    part(f'hstabTip_{tag}', f'hstab_{tag}', (sx * 0.52, -2.92, TZ))
    wing(f'hstabTip_{tag}_w', [((sx * 0.52, -2.90, TZ), (0, -0.46, 0), Z), ((sx * 0.98, -2.94, TZ), (0, -0.42, 0), Z)], SKIN, t=0.10)
    TO = math.radians(6); EX = Vector((math.cos(TO), -sx * math.sin(TO), 0))   # end plates toed out 6 deg
    wing(f'endplate_{tag}', [((sx * 0.99, -2.86, TZ - 0.24), (-sx * 0.44 * math.sin(TO), -0.44 * math.cos(TO), 0), EX), ((sx * 0.99, -2.96, TZ + 0.04), (-sx * 0.36 * math.sin(TO), -0.36 * math.cos(TO), 0), EX)], SKIN, t=0.10)
    for xk in (0.62, 0.86): tube(f'elevtip_hinge_{tag}{xk}', (sx * (xk - 0.02), -3.36, TZ), (sx * (xk + 0.02), -3.36, TZ), 0.011, MECH, 8)
    part(f'elevatorTip_{tag}', f'hstabTip_{tag}', (sx * 0.52, -3.36, TZ))
    wing(f'elevatorTip_{tag}_w', [((sx * 0.52, -3.362, TZ), (0, -0.20, 0), Z), ((sx * 0.972, -3.362, TZ), (0, -0.18, 0), Z)], SKIN, t=0.08)
# centre fin: lower + upper; rudder lower + upper
part('vstab', 'tailboom_R', (0, -2.95, TZ))
wing('vstab_w', [((0, -2.96, 0.80), (0, -0.42, 0), X), ((0, -3.06, 1.50), (0, -0.33, 0), X)], SKIN, t=0.10)
part('rudder', 'vstab', (0, -3.385, 0.80))
wing('rudder_w', [((0, -3.382, 0.80), (0, -0.29, 0), X), ((0, -3.392, 1.50), (0, -0.26, 0), X)], SKIN, t=0.09)
part('vstabTip', 'vstab', (0, -2.95, 1.50))
wing('vstabTip_w', [((0, -3.06, 1.50), (0, -0.33, 0), X), ((0, -3.14, 1.70), (0, -0.25, 0), X)], SKIN, t=0.10)
revolve('strobe', (0, -3.20, 1.695), (0, 0, 1), [(0, 0.02), (0.03, 0.02), (0.05, 0)], DARK, 10)
part('rudderTip', 'vstabTip', (0, -3.392, 1.50))
wing('rudderTip_w', [((0, -3.392, 1.50), (0, -0.26, 0), X), ((0, -3.392, 1.68), (0, -0.22, 0), X)], SKIN, t=0.09)
part('tailboom_R')
for k in range(3): tube(f'rudder_hinge{k}', (0, -3.386, 0.88 + 0.26 * k), (0, -3.386, 0.94 + 0.26 * k), 0.013, MECH, 8)
for sx in (-1, 1): pipe(f'rudder_cable{sx}', [(sx * 0.03, -1.40, KZ + 0.06), (sx * 0.035, -1.75, 0.58), (sx * 0.04, -2.15, 0.50), (sx * 0.045, -2.50, 0.62), (sx * 0.05, -2.85, 0.88), (sx * 0.05, -3.25, 0.98)], 0.003, MECH, 4, segs=4)

# ================================================================ main gear: composite bow halves
for sx, tag in ((1, 'R'), (-1, 'L')):
    part(f'gear_{tag}', 'frame_R', (sx * 0.06, GEAR_Y, KZ - 0.06 + LIFT))
    leg = crom([(sx * 0.04, GEAR_Y, KZ - 0.07 + LIFT), (sx * 0.40, GEAR_Y - 0.01, KZ - 0.06 + LIFT), (sx * 0.74, GEAR_Y - 0.02, 0.50), (sx * 0.89, GEAR_Y - 0.02, WR + 0.03)], 8)
    sweep(f'bow_{tag}', leg, rrect(0.16, 0.032, 0.012, 2), FRAME, side=(0, 1, 0), smooth=False, scale=[1.0 - 0.40 * i / (len(leg) - 1) for i in range(len(leg))])
    box(f'bow_clamp_{tag}', (sx * 0.10, GEAR_Y, KZ - 0.06 + LIFT), (0.10, 0.20, 0.06), MECH, bevel=0.008)
    tube(f'axle_{tag}', (sx * 0.87, GEAR_Y - 0.02, WR), (sx * (TRACK + 0.14), GEAR_Y - 0.02, WR), 0.018, MECH, 10)
    wheel(f'wheel_{tag}', (sx * TRACK, GEAR_Y - 0.02, WR), WR, 0.19)
    revolve(f'disc_{tag}', (sx * (TRACK - 0.115), GEAR_Y - 0.02, WR), (sx, 0, 0), [(0, 0.03), (0, 0.10), (0.008, 0.10), (0.008, 0.03)], MECH, 18)
    box(f'caliper_{tag}', (sx * (TRACK - 0.115), GEAR_Y + 0.07, WR + 0.05), (0.04, 0.06, 0.07), DARK, bevel=0.008)
    revolve(f'spat_{tag}', (sx * TRACK, GEAR_Y - 0.02, WR), (1, 0, 0), [(-0.12, 0), (-0.12, WR + 0.035)], SKIN, 1) if False else None
    # mud guard over the wheel
    arc = [Vector((sx * TRACK, GEAR_Y - 0.02 + math.cos(a) * (WR + 0.05), WR + math.sin(a) * (WR + 0.05))) for a in frange(R(25), R(150), 10)]
    sweep(f'mudguard_{tag}', arc, rect(0.22, 0.008), SKIN, side=(1, 0, 0), smooth=True)
    tube(f'guard_arm_{tag}', (sx * (TRACK - 0.12), GEAR_Y - 0.02, WR), (sx * (TRACK - 0.12), GEAR_Y + 0.11, WR + 0.335), 0.009, MECH, 6)

# ================================================================ Ka-50 style side gun mount (right) and gun
part('gunmount_R', 'UL2Airtrike', (0.30, 1.40, 0.69))
GX, GY, GZ = 0.45, 1.40, 0.70   # moved forward 0.25 for pylon clearance (Nick 2026-11-12)
OUTL = [(-0.30, 0.0), (-0.17, -0.23), (0.17, -0.23), (0.30, 0.0), (0.17, 0.23), (-0.17, 0.23)]
OUT_X = 0.58
def ring_at(x, k): return [Vector((x, GY + a * k, GZ + b * k)) for a, b in OUTL]
def ringS(x, ka, kb): return [Vector((x, GY + a * ka, GZ + b * kb)) for a, b in OUTL]
REC = 0.25    # recess depth
loft('sponson', [ring_at(0.30, 1.12), ring_at(0.44, 1.10), ring_at(OUT_X, 1.0), ringS(OUT_X, 0.84, 0.84), ringS(OUT_X - REC, 0.84, 0.84)], SKIN, smooth=False)
plate('mount_recess', [(a * 0.84, b * 0.84) for a, b in OUTL], 0.02, DARK, Matrix.Translation((0, GY, GZ)) @ side_at(OUT_X - REC + 0.01))
sweep('mount_lip', [Vector((OUT_X + 0.005, GY + a * 0.92, GZ + b * 0.92)) for a, b in OUTL], rect(0.02, 0.035), MECH, side=(1, 0, 0), smooth=False, closed=True)
for i, (a, b) in enumerate(OUTL): tube(f'mount_rivet{i}', (OUT_X + 0.01, GY + a * 0.96, GZ + b * 0.96), (OUT_X + 0.02, GY + a * 0.96, GZ + b * 0.96), 0.011, DARK, 6)
for sz in (-1, 1):   # streamlined fairings fore and aft; the barrel runs out through a slot in the front one
    plate(f'sponson_nose{sz}', [(0.30, sz * 0.0), (0.17, sz * 0.23), (0.62, sz * 0.05), (0.62, sz * 0.012), (0.30, sz * 0.012)], 0.17, SKIN, Matrix.Translation((0, GY, GZ)) @ side_at(0.455), bevel=0.006)
plate('sponson_tail', [(-0.17, -0.23), (-0.30, 0.0), (-0.17, 0.23), (-0.50, 0.03), (-0.50, -0.03)], 0.20, SKIN, Matrix.Translation((0, GY, GZ)) @ side_at(0.45), bevel=0.008)
for k, yy in enumerate((1.12, 1.52)):
    tube(f'sponson_strut{k}', (0.29, yy, RAIL_Z), (0.36, yy, GZ + 0.10), 0.015, FRAME, 8)
    tube(f'sponson_strut_lo{k}', (0.06, yy, KZ - 0.04), (0.34, yy, GZ - 0.12), 0.015, FRAME, 8)
# trunnion ring + gun (the gun elevates in the ring)
# cradle ring: the receiver passes through it, trunnion pins carry it on the recess wall
revolve('trunnion_ring', (GX, GY + 0.02, GZ), (0, 1, 0), [(0, 0.125), (0.09, 0.125), (0.09, 0.095), (0, 0.095)], MECH, 28)
for k in range(10):
    a = 2 * math.pi * k / 10
    tube(f'trunnion_bolt{k}', (GX + 0.11 * math.cos(a), GY + 0.11, GZ + 0.11 * math.sin(a)), (GX + 0.11 * math.cos(a), GY + 0.122, GZ + 0.11 * math.sin(a)), 0.007, DARK, 6)
tube('trunnion_shaft', (OUT_X - REC + 0.02, GY + 0.065, GZ), (GX - 0.12, GY + 0.065, GZ), 0.03, MECH, 12)
tube('trunnion_pin', (GX + 0.12, GY + 0.065, GZ), (OUT_X - 0.01, GY + 0.065, GZ), 0.022, MECH, 10)
part('gun_R', 'gunmount_R', (GX, GY, GZ))
GM2 = Matrix.Translation((GX, GY, GZ))
box('gun_receiver', (0.0, -0.02, 0), (0.10, 0.44, 0.12), DARK, M=GM2, bevel=0.01)
box('gun_feedcover', (0.0, 0.0, 0.07), (0.11, 0.24, 0.035), DARK, M=GM2, bevel=0.006)
box('gun_backplate', (0.0, -0.265, 0), (0.08, 0.06, 0.10), DARK, M=GM2, bevel=0.01)
box('gun_solenoid', (0.0, -0.15, -0.085), (0.07, 0.14, 0.05), MECH, M=GM2, bevel=0.006)
box('gun_trunnion_block', (0.0, 0.25, 0), (0.09, 0.10, 0.09), DARK, M=GM2, bevel=0.008)
revolve('gun_barrel', (0.0, 0.29, 0), (0, 1, 0), [(0, 0.028), (0.10, 0.028), (0.12, 0.022), (0.55, 0.022), (0.57, 0.018), (1.10, 0.016), (1.12, 0.026), (1.15, 0.030), (1.27, 0.030), (1.29, 0.020), (1.29, 0)], DARK, 14, M=GM2)
revolve('gun_recoil', (0.0, 0.36, -0.05), (0, 1, 0), [(0, 0.013), (0.34, 0.013), (0.34, 0)], MECH, 8, M=GM2)
part('gunmount_R')
CH = crom([(0.31, 0.82, 0.78), (0.33, 0.95, 0.84), (0.40, 1.15, 0.80), (GX, GY - 0.10, GZ + 0.08)], 6)
sweep('feed_chute', CH, rect(0.09, 0.03), MECH, side=(0, 1, 0), smooth=False)

# ================================================================ Nick 2026-11-12: smaller gun housing, bigger engine and prop
HOUSING = ('sponson', 'mount_recess', 'mount_lip', 'mount_rivet', 'sponson_nose', 'sponson_tail', 'trunnion')
HM = Matrix.Translation((0.30, GY, GZ)) @ Matrix.Diagonal((0.95, 0.85, 0.85, 1)) @ Matrix.Translation((-0.30, -GY, -GZ))
for o in PARTS['gunmount_R']['objs']:
    if o.name.startswith(HOUSING): o.data.transform(HM)
ENG_K = 1.2; ESP = Vector((0, EY, EZ - 0.29))          # grows up, out and along from the engine bed
EM = Matrix.Translation(ESP) @ Matrix.Scale(ENG_K, 4) @ Matrix.Translation(-ESP)
for n in ('engine_core', 'engine_L', 'engine_R', 'propHub'):
    for o in PARTS[n]['objs']:
        if not o.name.startswith(('eng_mount', 'engine_bed', 'bed_strut')): o.data.transform(EM)
    PARTS[n]['pivot'] = EM @ PARTS[n]['pivot']
PROP = EM @ PROP; PROP_R *= ENG_K
# ---- engine lowered along the mast (Nick 2026-11-12 11:19); the prop keeps ~8 cm over the tail boom. The bed struts
# stretch down to their fixed frame points and the pre-rotator shaft stretches up to the rotor head.
DZ_ENG = 0.14
for n in ('engine_core', 'engine_L', 'engine_R', 'propHub'):
    for o in PARTS[n]['objs']:
        if o.name.startswith('bed_strut'):
            zs = [v.co.z for v in o.data.vertices]; z0, z1 = min(zs), max(zs)
            for v in o.data.vertices: v.co.z -= DZ_ENG * (v.co.z - z0) / max(z1 - z0, 1e-6)
        else: o.data.transform(Matrix.Translation((0, 0, -DZ_ENG)))
    PARTS[n]['pivot'] = PARTS[n]['pivot'] - Vector((0, 0, DZ_ENG))
for n, d in PARTS.items():
    for o in d['objs']:
        if o.name == 'prerot_shaft':
            for v in o.data.vertices:
                if v.co.z < 2.30: v.co.z -= DZ_ENG * min(1.0, (2.30 - v.co.z) / 0.35)
PROP = PROP - Vector((0, 0, DZ_ENG)); EZ = EZ - DZ_ENG
# ---- taller fin and rudder (Nick 2026-11-12 11:26): scaled proportionally about the root mid-chord until the tip
# sits just above the top of the engine
eng_top = max((o.matrix_world @ v.co).z for n in ('engine_core', 'engine_L', 'engine_R') for o in PARTS[n]['objs'] for v in o.data.vertices)
FIN = ('vstab', 'vstabTip', 'rudder', 'rudderTip')
fin_top = max(v.co.z for n in FIN for o in PARTS[n]['objs'] for v in o.data.vertices)
FC = Vector((0, -3.17, 0.80)); FIN_K = (eng_top + 0.06 - FC.z) / (fin_top - FC.z)
FM = Matrix.Translation(FC) @ Matrix.Scale(FIN_K, 4) @ Matrix.Translation(-FC)
for n in FIN:
    for o in PARTS[n]['objs']: o.data.transform(FM)
    PARTS[n]['pivot'] = FM @ PARTS[n]['pivot']
for o in PARTS['tailboom_R']['objs']:
    if o.name.startswith('rudder_hinge'): o.data.transform(FM)
print('fin scale %.3f engine top %.3f strobe %s' % (FIN_K, eng_top, tuple(round(c, 3) for c in FM @ Vector((0, -3.20, 1.695)))))

# ================================================================ forward shift of the aft group, main gear; ride height
AFT = ['mast', 'rotorHead', 'engine_core', 'engine_L', 'engine_R', 'propHub', 'tailboom_F', 'tailboom_R', 'vstab', 'vstabTip', 'rudder', 'rudderTip'] + \
      [n for n in PARTS if n.startswith(('blade_', 'hstab', 'elevator'))]
def shift(names, v):
    for n in names:
        for o in PARTS[n]['objs']: o.data.transform(Matrix.Translation(v))
        PARTS[n]['pivot'] = PARTS[n]['pivot'] + v
shift(AFT, Vector((0, DY_AFT, 0)))
shift(['gear_L', 'gear_R'], Vector((0, DY_GEAR, 0)))
shift([n for n in PARTS if n not in ('gear_L', 'gear_R', 'gear_F')], Vector((0, 0, LIFT)))
for o in PARTS['gear_L']['objs'] + PARTS['gear_R']['objs']: pass

# ================================================================ shorter body (Nick 2026-11-12 10:58): everything ahead of seat 3 moves aft by GAP23
# (seat 3 keeps most of its legroom for the gun side swap). Long frame tubes that cross the cut are stretched, not moved.
GAP23, YC, Z0, Z1 = 0.12, -0.05, -0.30, 0.10
def ymap(y):
    if y <= Z0: return y
    if y >= Z1: return y - GAP23
    return y - GAP23 * (y - Z0) / (Z1 - Z0)
for n, d in PARTS.items():
    for o in d['objs']:
        ys = [v.co.y for v in o.data.vertices]
        if not ys: continue
        lo_, hi_ = min(ys), max(ys)
        if hi_ - lo_ > 0.5 and lo_ < YC < hi_:
            for v in o.data.vertices: v.co.y = ymap(v.co.y)
        elif (lo_ + hi_) / 2 > YC: o.data.transform(Matrix.Translation((0, -GAP23, 0)))
    if n != 'UL2Airtrike' and d['pivot'].y > YC: d['pivot'] = d['pivot'] - Vector((0, GAP23, 0))
P1 = P1 - Vector((0, GAP23, 0)); P2 = P2 - Vector((0, GAP23, 0))

# ================================================================ crew reference (test fit, not exported)
D = '/mnt/user-data/uploads/Nuclear Option Mods/UL2Art/ingame_ref/'
cr = load_obj(D + 'COIN_crew.obj', 'x', 'CREWREF', only=['renderer_pilot'])
pil = cr[0]; pil.name = 'REF_pilot'
pil.matrix_world = Matrix.Translation(P1 + Vector((0, 0, LIFT)) - Vector((0, 3.06, -0.36)))
g = pil.copy(); g.data = pil.data; g.name = 'REF_gunner'; coll('CREWREF').objects.link(g)
g.matrix_world = Matrix.Translation(P2 + Vector((0, 0, LIFT)) - Vector((0, 3.06, -0.36)))
# one soldier out of the Ibis 8-man troop mesh for seat 3
tr = load_obj(D + 'ibis_troops_F.obj', 'x', 'TMP')[0]
bm = bmesh.new(); bm.from_mesh(tr.data); bm.verts.ensure_lookup_table(); seen = set(); keep = set()
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; comp = []; seen.add(v.index)
    while st:
        a = st.pop(); comp.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen: seen.add(b.index); st.append(b)
    c = sum((q.co for q in comp), Vector()) / len(comp)
    if 0.0 < c.x < 0.55 and c.y > 0.2: keep.update(q.index for q in comp)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
feet = [v.co for v in bm.verts if v.co.z < lo.z + 0.12]; fy = sum((f.y for f in feet)) / len(feet); cy = (lo.y + hi.y) / 2
facing = 1 if fy > cy else -1
print('soldier bounds', lo, hi, 'feet y', fy, 'facing', facing)
if facing < 0: bmesh.ops.rotate(bm, verts=bm.verts, cent=(lo + hi) / 2, matrix=Matrix.Rotation(math.pi, 3, 'Z'))
lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
# pelvis estimate: 0.30 ahead of the back, 0.52 above the lowest point (same as the stock pilot)
pel = Vector(((lo.x + hi.x) / 2, lo.y + 0.30, lo.z + 0.52))
me = bpy.data.meshes.new('REF_soldier'); bm.to_mesh(me); bm.free()
so = bpy.data.objects.new('REF_soldier', me); coll('CREWREF').objects.link(so); me.materials.append(mat('UL2_crewref'))
for p in me.polygons: p.use_smooth = True
so.matrix_world = Matrix.Translation(P3 + Vector((0, 0, LIFT)) - pel)
bpy.data.objects.remove(tr)
print('soldier height', hi.z - lo.z, 'depth', hi.y - lo.y)

# ================================================================ join + report
if '--prejoin' in sys.argv: bpy.ops.wm.save_as_mainfile(filepath=OUT.replace('.blend', '_prejoin.blend'))
parts = join_parts('PARTS')
tot = 0
for n, o in sorted(parts.items()):
    t = tri_count([o]); tot += t; print(f'{n:22s} {t:7d}  parent={o.parent.name if o.parent else "-"}')
print('TOTAL tris', tot, 'parts', len(parts))
lo = Vector((9, 9, 9)); hi = -lo
for o in parts.values():
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
print('SIZE L %.2f W %.2f H %.2f  (y %.2f..%.2f, z %.2f..%.2f)' % (hi.y - lo.y, hi.x - lo.x, hi.z - lo.z, lo.y, hi.y, lo.z, hi.z))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
