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
LIFT = 0.12                      # ride height added at the end to everything but the gear
DY_AFT, DY_GEAR = 0.30, 0.43     # aft group (mast, engine, tail) and main gear shifted forward at the end
KZ = 0.60                        # keel top-ish
MAST_B = Vector((0, -1.38, KZ)); HEAD = Vector((0, -1.27, 3.02))
PROP = Vector((0, -2.22, 1.74)); PROP_R = 0.86
ROTOR_R = 4.6
WR, TRACK, GEAR_Y = 0.27, 1.00, -1.08

def bucket(tag, P, width=0.46, back=0.66, pan=0.40, lean=14):
    a = R(lean); up = Vector((0, -math.sin(a), math.cos(a)))
    c0 = P + Vector((0, 0.30, -0.13)); c1 = P + Vector((0, -0.12, -0.15)); c2 = c1 + up * back
    path = crom([c0, P + Vector((0, 0.08, -0.15)), c1 + Vector((0, 0.02, 0.02)), c1 + up * 0.25, c2], 4)
    w = width / 2; t = 0.03
    sec = [(-w, 0.10), (-w, -0.0), (w, -0.0), (w, 0.10), (w - t, 0.10), (w - t, t), (-w + t, t), (-w + t, 0.10)]
    sweep(tag + '_shell', path, sec, SEAT, side=(1, 0, 0), smooth=False)
    box(tag + '_cushion', P + Vector((0, 0.08, -0.115)), (width - 0.08, 0.36, 0.05), SEAT, bevel=0.02)
    box(tag + '_backpad', P + Vector((0, -0.10, 0.20)) + up * 0.0, (width - 0.09, 0.05, 0.42), SEAT, rot=(-a, 0, 0), bevel=0.02)
    hr = c2 - up * 0.04
    box(tag + '_headrest', hr + Vector((0, 0.03, 0)), (0.24, 0.07, 0.16), SEAT, rot=(-a, 0, 0), bevel=0.02)
    for sx in (-1, 1):   # harness
        box(f'{tag}_strap{sx}', P + Vector((sx * 0.09, -0.08, 0.38)), (0.045, 0.012, 0.42), DARK, rot=(-a, 0, 0))
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
def screen(name, w, h, M, y=0.0):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    vs = [bm.verts.new(M @ Vector(p)) for p in ((-w / 2, y, -h / 2), (w / 2, y, -h / 2), (w / 2, y, h / 2), (-w / 2, y, h / 2))]
    f = bm.faces.new(vs)
    for lp, t in zip(f.loops, ((1, 0), (0, 0), (0, 1), (1, 1))): lp[uv].uv = t
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    if me.polygons[0].normal.dot(M.to_3x3() @ Vector((0, -1, 0))) < 0: me.flip_normals()
    o = bpy.data.objects.new(name, me); me.materials.append(mat(SCREEN)); coll('WORK').objects.link(o)
    PARTS[CUR[0]]['objs'].append(o); return o

# ================================================================ nose pod (cockpit_F + nose)
POD = [  # y, half width, bottom z, top z
    (2.965, 0.030, 0.735, 0.765), (2.945, 0.135, 0.640, 0.865), (2.895, 0.215, 0.560, 0.945), (2.815, 0.278, 0.500, 1.015),
    (2.70, 0.322, 0.450, 1.075), (2.55, 0.356, 0.413, 1.135), (2.40, 0.378, 0.390, 1.180), (2.20, 0.392, 0.375, 1.215),
    (2.00, 0.398, 0.372, 1.215), (1.80, 0.394, 0.375, 1.185), (1.60, 0.378, 0.385, 1.140), (1.40, 0.345, 0.405, 1.075),
    (1.30, 0.305, 0.428, 1.025), (1.22, 0.235, 0.462, 0.950), (1.17, 0.120, 0.520, 0.850), (1.15, 0.030, 0.575, 0.745)]
def pod_at(y):
    for a, b in zip(POD, POD[1:]):
        if a[0] >= y >= b[0]:
            t = (a[0] - y) / (a[0] - b[0]); return [a[k] + (b[k] - a[k]) * t for k in (1, 2, 3)]
    return list((POD[0] if y > POD[0][0] else POD[-1])[1:])
OPEN_F, OPEN_A = 2.30, 1.17      # cockpit opening y range
def sill(y):                     # opening edge height
    keys = [(2.30, 1.195), (2.14, 1.085), (1.95, 1.050), (1.65, 1.030), (1.38, 0.990), (1.25, 0.925), (1.17, 0.860)]
    for a, b in zip(keys, keys[1:]):
        if a[0] >= y >= b[0]: t = (a[0] - y) / (a[0] - b[0]); return a[1] + (b[1] - a[1]) * t
    return 1.195 if y > 2.30 else 0.800
NS = 40
def pod_ring(y):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); pts = []
    for i in range(NS):
        a = 2 * math.pi * i / NS; c, s = math.cos(a), math.sin(a)
        e = 2.6 if s < 0 else 2.1
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
ys = sorted(set([round(y, 4) for y in frange(2.70, 1.15, 40)] + [OPEN_F, OPEN_A]), reverse=True)
build_pod(ys, 'pod_shell')
# rim along the cut edge
rim = [Vector((-pod_ring_x, y, sill(y))) for y, pod_ring_x in []]
def side_x(y, z):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); s = min(1, (z - zm) / (zt - zm)); return hw * (1 - abs(s) ** 2.1) ** (1 / 2.1)
edge = [Vector((side_x(y, sill(y)) - 0.004, y, sill(y))) for y in frange(OPEN_F - 0.01, OPEN_A + 0.01, 24)]
sweep('rim_R', edge, circ(0.016, 8), DARK, side=(0, 0, 1))
sweep('rim_L', [Vector((-p.x, p.y, p.z)) for p in edge], circ(0.016, 8), DARK, side=(0, 0, 1))
# cowl lip in front of the opening (glare shield edge)
hw, zb, zt = pod_at(OPEN_F)
lip = [Vector((math.cos(a) * side_x(OPEN_F, sill(OPEN_F)) * 0.98, OPEN_F, sill(OPEN_F) + (zt - sill(OPEN_F)) * math.sin(a))) for a in frange(0, math.pi, 14)]
sweep('cowl_lip', lip, circ(0.016, 8), DARK, side=(0, 1, 0))
# pod floor, ring frames (internal structure), keel shoe
box('pod_floor', (0, 1.95, 0.43), (0.56, 0.95, 0.025), CPIT)
for k, y in enumerate((2.55, 1.95, 1.45)):
    r = pod_ring(y); sec = [Vector((p.x * 0.94, y, 0.5 * (pod_at(y)[1] + pod_at(y)[2]) + (p.z - 0.5 * (pod_at(y)[1] + pod_at(y)[2])) * 0.94)) for p in r]
    sec = [p for p in sec if not (OPEN_A < y < OPEN_F and p.z > sill(y) - 0.03)]
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
windscreen(2.31, 1.55, 0.20, 'windscreen')
# HUD combiner + projector on the glare shield
box('hud_projector', (0, 2.16, 1.225), (0.13, 0.12, 0.055), DARK, bevel=0.01)
box('hud_glass', (0, 2.10, 1.33), (0.15, 0.008, 0.14), GLASS, rot=(R(-22), 0, 0))
for sx in (-1, 1): box(f'hud_arm{sx}', (sx * 0.08, 2.11, 1.30), (0.01, 0.016, 0.15), DARK, rot=(R(-22), 0, 0))
# instrument binnacle (initial): one MFD with side buttons, analog gauges around it
PM = Matrix.Translation((0, 2.18, 1.07)) @ Matrix.Rotation(R(-20), 4, 'X')
box('panel', (0, 0, 0), (0.62, 0.035, 0.24), CPIT, M=PM, bevel=0.01)
box('mfd', (0, -0.022, 0.0), (0.21, 0.02, 0.19), DARK, M=PM, bevel=0.006)
part('x_mfd1', 'cockpit_F', tuple(PM.translation)); screen('mfd_screen', 0.15, 0.15, PM, -0.036); part('cockpit_F')
for sx in (-1, 1):
    for k in range(5): box(f'mfd_btn{sx}{k}', (sx * 0.093, -0.034, 0.066 - 0.033 * k), (0.018, 0.008, 0.018), MECH, M=PM)
    for k, (gx, gz) in enumerate(((0.152, 0.05), (0.234, 0.05), (0.152, -0.055), (0.234, -0.055))):
        revolve(f'gauge{sx}{k}', (sx * gx, -0.018, gz), (0, -1, 0), [(0, 0.038), (0.012, 0.038), (0.012, 0.032), (0.006, 0.032), (0.006, 0)], DARK, 16, M=PM)
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
        hw, zb, zt = pod_at(y); side.append(Vector((sx * (hw + 0.002), y, zb + 0.46 * (zt - zb) - 0.02)))
    sweep(f'seam_side{sx}', side, rect(0.0025, 0.006), DARK, side=(sx, 0, 0), smooth=False)
# boarding step (left) on a short strut
box('board_step', (-0.47, 1.45, 0.47), (0.14, 0.20, 0.018), MECH, bevel=0.004)
for k, yy in enumerate((1.38, 1.52)): tube(f'board_step_strut{k}', (-0.40, yy, 0.47), (-0.33, yy, 0.55), 0.012, FRAME, 8)
# blade antenna under the pod and a GPS puck on the windscreen frame
plate('ant_belly', [(0.0, 0.0), (0.10, 0.0), (0.05, -0.16), (-0.01, -0.16)], 0.012, DARK, Matrix.Translation((0, 1.62, 0.39)) @ Matrix.Rotation(R(90), 4, 'Z') @ side_at(0.0), bevel=0.003)
# pilot controls
part('x_stick', 'cockpit_F', (0, 1.95, 0.47))
pipe('stick', [(0, 1.95, 0.45), (0, 1.90, 0.80), (0, 1.80, 1.12)], 0.015, DARK, 4, segs=8)

box('stick_grip', (0, 1.79, 1.18), (0.045, 0.05, 0.12), DARK, rot=(R(12), 0, 0), bevel=0.014)
part('cockpit_F')
box('stick_boot', (0, 1.95, 0.47), (0.10, 0.10, 0.05), DARK, bevel=0.015)
box('throttle_box', (-0.31, 1.72, 1.06), (0.06, 0.22, 0.06), MECH, bevel=0.01)
part('x_throttle', 'cockpit_F', (-0.31, 1.73, 1.08))
tube('throttle', (-0.31, 1.73, 1.08), (-0.31, 1.77, 1.20), 0.009, MECH, 6); ball('throttle_knob', (-0.31, 1.775, 1.21), (0.022,) * 3, DARK, seg=10)
part('cockpit_F')
for sx in (-1, 1):
    box(f'pedal{sx}', (sx * 0.15, 2.42, 0.56), (0.11, 0.02, 0.15), MECH, rot=(R(-25), 0, 0), bevel=0.004)
    tube(f'pedal_bar{sx}', (sx * 0.15, 2.46, 0.44), (sx * 0.15, 2.43, 0.52), 0.01, MECH, 6)
# ---- cockpit detail pass: gauge needles and bezels, standby compass, side consoles, harness
for sx in (-1, 1):
    for k, (gx, gz) in enumerate(((0.152, 0.05), (0.234, 0.05), (0.152, -0.055), (0.234, -0.055))):
        # live needle: its own pseudo-part pivoted on the gauge centre, modelled pointing up; the plugin turns it
        gid = ('asi', 'alt', 'vsi', 'rotor')[k] if sx < 0 else ('rpm', 'fuel', 'oil', 'volt')[k]
        NM = PM @ Matrix.Translation((sx * gx, -0.027, gz))
        part(f'x_g_{gid}', 'cockpit_F', tuple(NM.translation))
        box(f'gauge_needle{sx}{k}', (0, 0, 0.012), (0.004, 0.002, 0.026), MECH, M=NM)
        part('cockpit_F')
        revolve(f'gauge_bezel{sx}{k}', (sx * gx, -0.018, gz), (0, -1, 0), [(0, 0.041), (0.014, 0.041), (0.014, 0.038)], MECH, 16, M=PM, cap=False)
box('panel_lamp_bezel', (0, -0.02, 0.112), (0.19, 0.012, 0.024), MECH, M=PM, bevel=0.003)
for k in range(5): box(f'panel_lamp{k}', (-0.072 + 0.036 * k, -0.027, 0.112), (0.03, 0.004, 0.016), DARK, M=PM)
box('panel_masterarm', (0.17, -0.022, 0.108), (0.05, 0.012, 0.03), MECH, M=PM, bevel=0.003)
box('panel_masterarm_guard', (0.17, -0.032, 0.108), (0.04, 0.006, 0.022), DARK, M=PM)
# standby compass on a stalk above the binnacle
tube('compass_stalk', (0.22, 2.19, 1.19), (0.22, 2.18, 1.25), 0.008, DARK, 6)
box('compass', (0.22, 2.18, 1.27), (0.06, 0.05, 0.045), DARK, bevel=0.012)
# left console beside the throttle: fuel / mags / lights switch strip; right console: radio head
box('lconsole', (-0.26, 1.88, 1.03), (0.07, 0.20, 0.04), CPIT, bevel=0.006)
for k in range(5):
    tube(f'lconsole_sw{k}', (-0.26, 1.81 + 0.035 * k, 1.05), (-0.26, 1.82 + 0.035 * k, 1.075), 0.0035, MECH, 5)
box('fuel_selector', (-0.26, 1.97, 1.055), (0.03, 0.03, 0.012), DARK, bevel=0.004)
box('rconsole', (0.25, 1.82, 1.03), (0.08, 0.26, 0.04), CPIT, bevel=0.006)
box('radio_head', (0.25, 1.80, 1.06), (0.06, 0.12, 0.022), DARK, bevel=0.004)
for k in range(2): revolve(f'radio_knob{k}', (0.25, 1.76 + 0.08 * k, 1.07), (0, 0, 1), [(0, 0.012), (0.014, 0.012), (0.016, 0)], MECH, 10)
for k in range(3): tube(f'rconsole_sw{k}', (0.25, 1.90 + 0.025 * k, 1.05), (0.25, 1.91 + 0.025 * k, 1.075), 0.0035, MECH, 5)
# pedal toe straps
for sx in (-1, 1): box(f'pedal_strap{sx}', (sx * 0.15, 2.405, 0.60), (0.115, 0.03, 0.02), DARK, rot=(R(-25), 0, 0))
# pilot seat on the pod floor / keel
def rifle_on_seat(tag, P, side=-1):
    # service rifle clamped vertically to the outboard side of the seat back, muzzle up
    a = R(14); M = Matrix.Translation(P + Vector((side * 0.29, -0.13, -0.16))) @ Matrix.Rotation(-a, 4, 'X')
    for o in RIFLE:
        part_obj = o.copy(); part_obj.data = o.data.copy(); part_obj.data.transform(M); coll('WORK').objects.link(part_obj)
        part_obj.name = f'{tag}_rifle'; PARTS[CUR[0]]['objs'].append(part_obj)
    for dz in (0.10, 0.45): box(f'{tag}_rifle_clamp{dz}', P + Vector((side * 0.26, -0.13 - dz * math.sin(a), -0.16 + dz * math.cos(a))), (0.07, 0.05, 0.03), DARK, rot=(-a, 0, 0))
P1_SEAT = True

bucket('seat1', P1)
for sx in (-1, 1): box(f'seat1_base{sx}', (sx * 0.15, P1.y + 0.05, (P1.z - 0.17 + 0.44) / 2), (0.05, 0.36, P1.z - 0.17 - 0.44), FRAME)
rifle_on_seat('seat1', P1)
# targeting pod: small stabilised ball behind the nose gear (lases a single target)
TP = Vector((0, 1.80, 0.295))
revolve('tpod_mount', (0, 1.80, 0.445), (0, 0, -1), [(0, 0.075), (0.03, 0.07), (0.06, 0.055), (0.06, 0)], MECH, 18)
revolve('tpod_yoke_ring', (0, 1.80, 0.387), (0, 0, -1), [(0, 0.10), (0.025, 0.10), (0.025, 0.0)], DARK, 22)
ball('tpod_ball', TP, (0.115, 0.115, 0.115), MECH, seg=20)
revolve('tpod_window', TP + Vector((0, 0.088, -0.02)), (0, 1, -0.2), [(0, 0.062), (0.03, 0.062), (0.03, 0.0)], DARK, 16)
revolve('tpod_lens', TP + Vector((0, 0.119, -0.026)), (0, 1, -0.2), [(0, 0.05), (0.002, 0)], SCREEN, 16)
revolve('tpod_laser', TP + Vector((0.07, 0.072, 0.03)), (0, 1, -0.2), [(0, 0.022), (0.03, 0.022), (0.03, 0.0)], DARK, 10)
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

part('nose', 'cockpit_F', (0, 2.70, 0.75))
build_pod(frange(2.965, 2.70, 10), 'nose_shell', cut=False, cap_front=False)
def top_z(x, y):
    hw, zb, zt = pod_at(y); zm = zb + 0.46 * (zt - zb); u = min(abs(x) / hw, 0.999)
    return zm + (zt - zm) * (1 - u ** 2.1) ** (1 / 2.1) + 0.004
HP = []
for x0, y0, x1, y1 in ((-0.12, 2.73, 0.12, 2.73), (0.12, 2.73, 0.10, 2.86), (0.10, 2.86, -0.10, 2.86), (-0.10, 2.86, -0.12, 2.73)):
    for t in frange(0, 1, 6)[:-1]: x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t; HP.append(Vector((x, y, top_z(x, y))))
sweep('nose_hatch', HP, rect(0.006, 0.0025), DARK, side=(0, 0, 1), smooth=False, closed=True)
for sx in (-1, 1): revolve(f'static_port{sx}', (sx * 0.33, 2.66, 0.80), (sx, 0, 0), [(0, 0.012), (0.004, 0.012), (0.004, 0)], DARK, 10)
revolve('landing_light', (0, 2.87, 0.555), (0, 1, -0.45), [(0, 0.045), (0.02, 0.045), (0.024, 0.036), (0.025, 0)], MECH, 14)
pipe('pitot', [(0.18, 2.84, 0.80), (0.20, 2.95, 0.80), (0.20, 3.10, 0.80)], 0.008, MECH, 3, segs=6)

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
    box(f'footplate2{sx}', (sx * 0.30, 1.13, 0.716), (0.16, 0.07, 0.01), MECH)
for y in (0.75, 0.10, -0.28): box(f'cross_C{y}', (0, y, RAIL_Z - 0.06), (0.56, 0.035, 0.035), FRAME)
box('keel_joint_F', (0, 1.04, 0.565), (0.11, 0.10, 0.11), MECH, rot=(R(-11), 0, 0), bevel=0.01)
# stub pylons (Ibis-style fuselage pylons)
for sx in (-1, 1):
    wing(f'stub{sx}', [((sx * 0.08, 0.36, KZ - 0.02), (0, -0.40, 0), (0, 0, 1)), ((sx * 0.74, 0.32, KZ - 0.02), (0, -0.32, 0), (0, 0, 1))], SKIN, t=0.16)
    tube(f'stub_strut{sx}', (sx * 0.29, 0.46, RAIL_Z), (sx * 0.55, 0.20, KZ - 0.01), 0.014, FRAME, 8)
    tube(f'stub_strut_b{sx}', (sx * 0.29, 0.02, RAIL_Z), (sx * 0.50, 0.16, KZ - 0.01), 0.014, FRAME, 8)
    plate(f'pylon{sx}', [(0.12, 0), (-0.26, 0), (-0.20, -0.11), (0.06, -0.11)], 0.055, SKIN, Matrix.Translation((0, 0.20, KZ - 0.02)) @ side_at(sx * 0.70), bevel=0.008)
    for k in (-1, 1): tube(f'sway{sx}{k}', (sx * 0.70 - 0.05, 0.20 + 0.10 * k, KZ - 0.13), (sx * 0.70 + 0.05, 0.20 + 0.10 * k, KZ - 0.13), 0.008, MECH, 6)

# ---- gunner seat (cockpit_R)
part('cockpit_R', 'UL2Airtrike', (0, P2.y, RAIL_Z))
bucket('seat2', P2)
rifle_on_seat('seat2', P2)
for sx in (-1, 1):
    tube(f'seat2_leg_f{sx}', (sx * 0.17, P2.y + 0.22, P2.z - 0.17), (sx * 0.29, P2.y + 0.30, RAIL_Z + 0.02), 0.016, FRAME, 8)
    tube(f'seat2_leg_r{sx}', (sx * 0.17, P2.y - 0.12, P2.z - 0.18), (sx * 0.29, P2.y - 0.22, RAIL_Z + 0.02), 0.016, FRAME, 8)
for P, nm in ((P2, 'seat2'),):
    for sx in (-1, 1):
        tube(f'{nm}_diag{sx}', (sx * 0.29, P.y + 0.30, RAIL_Z + 0.02) if nm == 'seat2' else (sx * 0.29, P.y + 0.32, RAIL_Z + 0.02), (sx * 0.17, P.y - 0.12, P.z - 0.18), 0.012, FRAME, 8)
    box(f'{nm}_crossbar', (0, P.y + 0.05, P.z - 0.19), (0.36, 0.03, 0.03), FRAME)
# gunner MFD post and hand grips
tube('mfd2_post', (0.08, 0.98, 0.80), (0, 0.98, 1.24), 0.017, FRAME, 8)
GM = Matrix.Translation((0, 0.99, 1.33)) @ Matrix.Rotation(R(-12), 4, 'X')
box('mfd2', (0, 0, 0), (0.25, 0.04, 0.20), DARK, M=GM, bevel=0.008)
part('x_mfd2', 'cockpit_R', tuple(GM.translation)); screen('mfd2_screen', 0.18, 0.15, GM, -0.024); part('cockpit_R')
for sx in (-1, 1):
    for k in range(4): box(f'mfd2_btn{sx}{k}', (sx * 0.108, -0.022, 0.055 - 0.037 * k), (0.016, 0.008, 0.016), MECH, M=GM)
    pipe(f'grip2_arm{sx}', [(sx * 0.05, 0.97, 1.20), (sx * 0.15, 0.84, 1.36), (sx * 0.20, 0.72, 1.45)], 0.014, FRAME, 4, segs=8)
    tube(f'grip2{sx}', (sx * 0.20, 0.68, 1.42), (sx * 0.20, 0.64, 1.55), 0.018, DARK, 10)
    box(f'grip2_trigger{sx}', (sx * 0.20, 0.68, 1.52), (0.02, 0.025, 0.035), MECH)
box('grip2_bar', (0, 0.70, 1.45), (0.40, 0.025, 0.025), FRAME)
# gunner detail: arming box on the grip bar, intercom box and cord on the MFD post
box('grip2_armbox', (0, 0.70, 1.475), (0.10, 0.04, 0.03), DARK, bevel=0.004)
for k in range(3): tube(f'grip2_sw{k}', (-0.03 + 0.03 * k, 0.70, 1.49), (-0.03 + 0.03 * k, 0.705, 1.51), 0.003, MECH, 5)
box('ics_box', (0.04, 0.97, 1.05), (0.05, 0.035, 0.07), DARK, bevel=0.004)
revolve('ics_knob', (0.04, 0.95, 1.07), (0, -1, 0), [(0, 0.01), (0.01, 0.01), (0.012, 0)], MECH, 8)
# ammo box at the gunner's feet (right), feeds the side gun
box('ammo_box', (0.27, 0.80, 0.66), (0.20, 0.26, 0.20), MECH, bevel=0.008)
box('ammo_lid', (0.27, 0.80, 0.765), (0.21, 0.27, 0.014), DARK)

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
box('cross_R', (0, -0.62, RAIL_Z - 0.06), (0.56, 0.035, 0.035), FRAME)
box('mast_shoe', (0, -1.08, KZ + 0.02), (0.20, 0.18, 0.12), MECH, bevel=0.012)
box('fuel_tank', (0, -0.82, 0.88), (0.40, 0.22, 0.26), SKIN, bevel=0.04)
revolve('fuel_cap', (0.12, -0.82, 1.01), (0, 0, 1), [(0, 0.03), (0.02, 0.03), (0.02, 0)], DARK, 10)
box('battery', (-0.17, -0.45, 0.68), (0.14, 0.14, 0.10), DARK, bevel=0.008)
box('battery_tray', (-0.17, -0.45, 0.625), (0.30, 0.18, 0.012), FRAME)

part('seat3', 'frame_R', (0, P3.y, P3.z - 0.15))
bucket('seat3', P3, width=0.44)
for sx in (-1, 1): tube(f'seat3_diag{sx}', (sx * 0.29, P3.y + 0.32, RAIL_Z + 0.02), (sx * 0.17, P3.y - 0.12, P3.z - 0.18), 0.012, FRAME, 8)
box('seat3_crossbar', (0, P3.y + 0.05, P3.z - 0.19), (0.36, 0.03, 0.03), FRAME)
for sx in (-1, 1):
    tube(f'seat3_leg_f{sx}', (sx * 0.16, P3.y + 0.22, P3.z - 0.17), (sx * 0.29, P3.y + 0.32, RAIL_Z + 0.02), 0.017, FRAME, 8)
    tube(f'seat3_leg_r{sx}', (sx * 0.16, P3.y - 0.12, P3.z - 0.18), (sx * 0.10, -0.92, 1.05), 0.017, FRAME, 8)
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
    part(f'elevator_{tag}', f'hstab_{tag}', (sx * 0.05, -3.36, TZ))
    wing(f'elevator_{tag}_w', [((sx * 0.06, -3.37, TZ), (0, -0.20, 0), Z), ((sx * 0.52, -3.37, TZ), (0, -0.20, 0), Z)], SKIN, t=0.08)
    part(f'hstabTip_{tag}', f'hstab_{tag}', (sx * 0.52, -2.92, TZ))
    wing(f'hstabTip_{tag}_w', [((sx * 0.52, -2.90, TZ), (0, -0.46, 0), Z), ((sx * 0.98, -2.94, TZ), (0, -0.42, 0), Z)], SKIN, t=0.10)
    TO = math.radians(6); EX = Vector((math.cos(TO), -sx * math.sin(TO), 0))   # end plates toed out 6 deg
    wing(f'endplate_{tag}', [((sx * 0.99, -2.86, TZ - 0.24), (-sx * 0.44 * math.sin(TO), -0.44 * math.cos(TO), 0), EX), ((sx * 0.99, -2.96, TZ + 0.04), (-sx * 0.36 * math.sin(TO), -0.36 * math.cos(TO), 0), EX)], SKIN, t=0.10)
    part(f'elevatorTip_{tag}', f'hstabTip_{tag}', (sx * 0.52, -3.36, TZ))
    wing(f'elevatorTip_{tag}_w', [((sx * 0.52, -3.37, TZ), (0, -0.20, 0), Z), ((sx * 0.96, -3.37, TZ), (0, -0.18, 0), Z)], SKIN, t=0.08)
# centre fin: lower + upper; rudder lower + upper
part('vstab', 'tailboom_R', (0, -2.95, TZ))
wing('vstab_w', [((0, -2.96, 0.86), (0, -0.42, 0), X), ((0, -3.06, 1.50), (0, -0.33, 0), X)], SKIN, t=0.10)
part('rudder', 'vstab', (0, -3.40, 0.86))
wing('rudder_w', [((0, -3.41, 0.62), (0, -0.29, 0), X), ((0, -3.42, 1.50), (0, -0.26, 0), X)], SKIN, t=0.09)
part('vstabTip', 'vstab', (0, -2.95, 1.50))
wing('vstabTip_w', [((0, -3.06, 1.50), (0, -0.33, 0), X), ((0, -3.14, 1.70), (0, -0.25, 0), X)], SKIN, t=0.10)
revolve('strobe', (0, -3.20, 1.71), (0, 0, 1), [(0, 0.02), (0.03, 0.02), (0.05, 0)], DARK, 10)
part('rudderTip', 'vstabTip', (0, -3.42, 1.50))
wing('rudderTip_w', [((0, -3.42, 1.50), (0, -0.26, 0), X), ((0, -3.42, 1.68), (0, -0.22, 0), X)], SKIN, t=0.09)
part('tailboom_R')
for k in range(3): tube(f'rudder_hinge{k}', (0, -3.40, 0.70 + 0.30 * k), (0, -3.40, 0.76 + 0.30 * k), 0.012, MECH, 8)
for sx in (-1, 1): tube(f'rudder_cable{sx}', (sx * 0.03, -1.40, KZ + 0.05), (sx * 0.05, -3.30, 0.90), 0.003, MECH, 4)

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
    tube(f'guard_arm_{tag}', (sx * (TRACK - 0.12), GEAR_Y - 0.02, WR), (sx * (TRACK - 0.12), GEAR_Y + 0.10, WR + 0.30), 0.009, MECH, 6)

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
CH = crom([(0.27, 0.82, 0.78), (0.33, 0.95, 0.84), (0.40, 1.15, 0.80), (GX, GY - 0.10, GZ + 0.08)], 6)
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
