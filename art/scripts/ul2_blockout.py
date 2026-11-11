"""UL-2 Airtrike blockout. Usage: python3 -I ul2_blockout.py -- <out.blend>
Nose +Y, up +Z, right +X, metres, ground z=0, origin under the rotor mast foot.
Variant collections: ENG_A (open prop), ENG_B (ducted fan), PAX_SPOTTER, PAX_MG, PAX_GL, STORES.
"""
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene

# ------------------------------------------------------------------ layout numbers
SCALE_NOTE = 'tub stretched for 3 tandem seats; rotor 9.2 m (real 8.4)'
Z_FLOOR, Z_SILL = 0.42, 0.96
HW = 0.50                       # tub half width
SEATS = [(1.85, 0.64), (0.95, 0.68), (0.15, 0.86)]   # (y, seat pan z): pilot, gunner, passenger
NOSE_Y, TUB_AFT_Y = 3.25, -0.45
HEAD = Vector((0, 0.22, 3.00))  # rotor head
ROTOR_R = 4.6
PROP_Y, PROP_Z, PROP_R = -1.50, 1.36, 0.86
KEEL_Z = 0.36

# ------------------------------------------------------------------ helpers
MATS = {}
def mat(name, rgb, rough=0.6, alpha=1.0, metal=0.0):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, alpha); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*rgb, 1); b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal; b.inputs['Alpha'].default_value = alpha
    if alpha < 1:
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    MATS[name] = m; return m

SKIN = mat('skin', (0.23, 0.29, 0.19)); SKIN2 = mat('skin_dark', (0.13, 0.17, 0.12))
FRAME = mat('frame', (0.62, 0.64, 0.66), 0.35, metal=0.8); DARK = mat('dark', (0.05, 0.05, 0.055), 0.5)
TYRE = mat('tyre', (0.03, 0.03, 0.03), 0.9); GLASS = mat('glass', (0.75, 0.86, 0.9), 0.05, 0.28)
ENGM = mat('engine', (0.30, 0.31, 0.33), 0.45, metal=0.6); RED = mat('red', (0.55, 0.06, 0.05))
SEATM = mat('seat', (0.16, 0.17, 0.13), 0.9); GUNM = mat('gun', (0.09, 0.095, 0.10), 0.4, metal=0.7)
UNI = mat('uniform', (0.33, 0.33, 0.22), 0.9); VEST = mat('vest', (0.20, 0.21, 0.15), 0.9)
HELM = mat('helmet', (0.24, 0.25, 0.18), 0.6); FACE = mat('visor', (0.04, 0.04, 0.05), 0.2)
BLADE = mat('blade', (0.10, 0.10, 0.11), 0.4); ORD = mat('ordnance', (0.27, 0.29, 0.27), 0.6)
BRASS = mat('ammo', (0.50, 0.42, 0.18), 0.4, metal=0.8); SCREEN = mat('screen', (0.05, 0.25, 0.12), 0.3)

def coll(name):
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name); SC.collection.children.link(c)
    return c
CUR = [coll('AIRFRAME')]
def use(name): CUR[0] = coll(name)

def obj_from_bm(name, bm, m, smooth=True):
    me = bpy.data.meshes.new(name); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); me.materials.append(m); CUR[0].objects.link(o); return o

def tube(name, p0, p1, r, m, segs=12, r1=None, caps=True):
    p0, p1 = Vector(p0), Vector(p1); d = p1 - p0; L = d.length
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, segments=segs, radius1=r, radius2=r if r1 is None else r1, depth=L)
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((p0 + p1) / 2) @ rot, verts=bm.verts)
    return obj_from_bm(name, bm, m)

def path(name, pts, r, m, segs=10):
    return [tube(f'{name}_{i}', pts[i], pts[i + 1], r, m, segs) for i in range(len(pts) - 1)]

def box(name, c, size, m, rot=(0, 0, 0), smooth=False):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
    M = Matrix.Translation(c) @ (Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')) @ Matrix.Diagonal((*size, 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return obj_from_bm(name, bm, m, smooth)

def ball(name, c, radii, m, rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1)
    M = Matrix.Translation(c) @ (Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[0], 4, 'X')) @ Matrix.Diagonal((*radii, 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return obj_from_bm(name, bm, m)

def prism(name, pts_xy_plane, thick, m, to_world):
    """Flat plate from a 2D outline, extruded +-thick/2, placed by matrix to_world (outline in local XY)."""
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, -thick / 2)) for x, y in pts_xy_plane]
    hi = [bm.verts.new((x, y, thick / 2)) for x, y in pts_xy_plane]
    n = len(lo); bm.faces.new(lo); bm.faces.new(hi[::-1])
    for i in range(n): bm.faces.new((lo[i], hi[i], hi[(i + 1) % n], lo[(i + 1) % n]))
    bmesh.ops.transform(bm, matrix=to_world, verts=bm.verts)
    return obj_from_bm(name, bm, m, False)

def fin(name, base, root, tip, height, sweep, m, thick=0.035, lateral=0.0):
    """Vertical fin in the YZ plane. base = bottom leading-edge point; chord runs toward -Y."""
    M = Matrix.Translation(base) @ Matrix.Rotation(math.radians(90), 4, 'Y') @ Matrix.Rotation(math.radians(90), 4, 'Z')
    # local x -> world -Y?  build explicitly instead:
    bm = bmesh.new(); b = Vector(base)
    out = [(0, 0), (-root, 0), (-sweep - tip, height), (-sweep, height)]
    lo = [bm.verts.new((b.x - thick / 2 + lateral * z / height, b.y + y, b.z + z)) for y, z in out]
    hi = [bm.verts.new((b.x + thick / 2 + lateral * z / height, b.y + y, b.z + z)) for y, z in out]
    bm.faces.new(lo); bm.faces.new(hi[::-1])
    for i in range(4): bm.faces.new((lo[i], hi[i], hi[(i + 1) % 4], lo[(i + 1) % 4]))
    return obj_from_bm(name, bm, m, False)

# ------------------------------------------------------------------ tub (open cockpit hull)
NB, NT = 22, 10   # points on the belly U and on the top arc
def ring(y, hw, zsill, depth, crown, e=0.62):
    pts = []
    for i in range(NB + 1):                      # left sill -> belly -> right sill
        t = math.pi * i / NB; c, s = math.cos(t), math.sin(t)
        pts.append(Vector((-hw * math.copysign(abs(c) ** e, c), y, zsill - depth * abs(s) ** e)))
    for i in range(1, NT):                       # right sill -> crown -> left sill
        t = math.pi * i / NT
        pts.append(Vector((hw * math.cos(t), y, zsill + crown * math.sin(t))))
    return pts

def tub():
    D = Z_SILL - Z_FLOOR
    # y, half width, sill z, depth, crown, top closed to the next section?
    S = [(NOSE_Y, 0.03, 0.66, 0.04, 0.03, True), (3.15, 0.12, 0.70, 0.13, 0.08, True), (2.95, 0.25, 0.76, 0.27, 0.14, True),
         (2.70, 0.36, 0.84, 0.40, 0.19, True), (2.45, 0.44, 0.91, 0.50, 0.24, True), (2.28, 0.47, 0.95, 0.55, 0.27, False),
         (2.00, HW, Z_SILL, 0.56, 0.0, False), (1.50, HW, Z_SILL, 0.55, 0.0, False),
         (1.42, HW, Z_SILL + 0.02, 0.56, 0.24, True), (1.30, HW, Z_SILL + 0.02, 0.55, 0.24, False),   # hoop between pilot and gunner
         (1.22, HW, Z_SILL, 0.52, 0.0, False), (0.62, HW, Z_SILL + 0.03, 0.46, 0.0, False),
         (0.56, HW, Z_SILL + 0.05, 0.46, 0.10, True), (0.48, HW, Z_SILL + 0.05, 0.45, 0.10, False),   # low divider before seat 3
         (0.42, HW, Z_SILL + 0.05, 0.44, 0.0, False), (-0.20, 0.46, Z_SILL + 0.10, 0.38, 0.0, False),
         (TUB_AFT_Y, 0.38, Z_SILL + 0.12, 0.30, 0.0, False), (TUB_AFT_Y - 0.01, 0.38, Z_SILL + 0.12, 0.30, 0.02, True)]
    bm = bmesh.new(); rings = []
    for (y, hw, zs, d, cr, _) in S: rings.append([bm.verts.new(p) for p in ring(y, hw, zs, d, max(cr, 0.001))])
    n = len(rings[0])
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(n):
            j = (i + 1) % n
            top = i >= NB                         # faces on the top arc
            if top and not S[k][5]: continue
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rings[0]); bm.faces.new(rings[-1][::-1])
    o = obj_from_bm('tub', bm, SKIN)
    sm = o.modifiers.new('thick', 'SOLIDIFY'); sm.thickness = 0.025; sm.offset = -1
    # floor and bulkheads
    box('floor', (0, 1.30, Z_FLOOR + 0.05), (0.80, 2.2, 0.02), DARK)
    box('floor_aft', (0, 0.0, 0.66), (0.74, 0.9, 0.02), DARK)
    box('bulkhead_aft', (0, TUB_AFT_Y + 0.03, 0.90), (0.70, 0.03, 0.36), SKIN2)

def windshield(name, y0, zbase, hw, height, rake, top_scale=0.55):
    bm = bmesh.new(); lo, hi = [], []
    for i in range(13):
        t = math.radians(8 + 164 * i / 12); c, s = math.cos(t), math.sin(t)
        lo.append(bm.verts.new((hw * c, y0 + 0.30 * s, zbase + 0.02 * s)))
        hi.append(bm.verts.new((hw * top_scale * c, y0 + 0.30 * top_scale * s - rake, zbase + height)))
    for i in range(12): bm.faces.new((lo[i], lo[i + 1], hi[i + 1], hi[i]))
    return obj_from_bm(name, bm, GLASS)

# ------------------------------------------------------------------ crew
def seat(i, y, z):
    box(f'seat{i}_pan', (0, y + 0.05, z - 0.03), (0.44, 0.46, 0.06), SEATM)
    box(f'seat{i}_back', (0, y - 0.24, z + 0.30), (0.44, 0.06, 0.66), SEATM, rot=(math.radians(-12), 0, 0))

def figure(tag, y, z, lean=12, head_yaw=0, arms='lap', helmet=HELM):
    """Seated figure, hips at (0, y-0.1, z+0.10). Returns handy points."""
    hip = Vector((0, y - 0.10, z + 0.10)); a = math.radians(lean)
    up = Vector((0, -math.sin(a), math.cos(a)))
    chest = hip + up * 0.42; neck = hip + up * 0.56; head = hip + up * 0.72
    box(f'{tag}_pelvis', hip, (0.34, 0.24, 0.20), UNI)
    box(f'{tag}_torso', hip + up * 0.30, (0.38, 0.22, 0.46), VEST, rot=(-a, 0, 0))
    ball(f'{tag}_head', head, (0.10, 0.115, 0.12), UNI)
    ball(f'{tag}_helmet', head + Vector((0, -0.01, 0.03)), (0.125, 0.14, 0.125), helmet, rot=(0, 0, math.radians(head_yaw)))
    box(f'{tag}_visor', head + Matrix.Rotation(math.radians(head_yaw), 3, 'Z') @ Vector((0, 0.105, -0.005)), (0.17, 0.04, 0.07), FACE, rot=(0, 0, math.radians(head_yaw)))
    for sx in (-1, 1):
        knee = hip + Vector((sx * 0.13, 0.40, 0.08)); foot = knee + Vector((sx * 0.02, 0.20, -0.30))
        tube(f'{tag}_thigh{sx}', hip + Vector((sx * 0.10, 0.02, -0.02)), knee, 0.075, UNI)
        tube(f'{tag}_shin{sx}', knee, foot, 0.06, UNI)
        box(f'{tag}_boot{sx}', foot + Vector((0, 0.06, 0.0)), (0.10, 0.24, 0.08), DARK)
    sh = [chest + Vector((sx * 0.22, 0, 0.06)) for sx in (-1, 1)]
    return dict(hip=hip, chest=chest, head=head, sh=sh, up=up)

def arm(tag, shoulder, hand, bend=Vector((0, -0.05, -0.12))):
    elbow = (Vector(shoulder) + Vector(hand)) / 2 + bend
    tube(tag + '_upper', shoulder, elbow, 0.05, UNI); tube(tag + '_fore', elbow, hand, 0.042, UNI)
    ball(tag + '_hand', hand, (0.045, 0.045, 0.045), DARK)

# ------------------------------------------------------------------ build: airframe
tub()
windshield('windshield_pilot', 2.26, 1.15, 0.44, 0.52, 0.34)
windshield('windshield_gunner', 1.33, 1.18, 0.42, 0.50, 0.30)
box('panel_pilot', (0, 2.33, 1.05), (0.62, 0.05, 0.30), DARK, rot=(math.radians(-20), 0, 0))
box('mfd_pilot', (0, 2.305, 1.05), (0.20, 0.012, 0.16), SCREEN, rot=(math.radians(-20), 0, 0))
for sx in (-1, 1):
    for k in range(3): ball(f'gauge{sx}{k}', (sx * (0.17 + 0.0 * k) + sx * 0.06 * (k % 2), 2.30, 0.98 + 0.075 * k), (0.032, 0.012, 0.032), FRAME, rot=(math.radians(-20), 0, 0))
box('panel_gunner', (0, 1.31, 1.09), (0.36, 0.04, 0.26), DARK, rot=(math.radians(-15), 0, 0))
box('mfd_gunner', (0, 1.288, 1.09), (0.22, 0.012, 0.17), SCREEN, rot=(math.radians(-15), 0, 0))
for i, (y, z) in enumerate(SEATS, 1): seat(i, y, z)
tube('stick', (0, 2.12, Z_FLOOR + 0.08), (0, 2.08, 0.94), 0.014, DARK)

# pilot and gunner
p = figure('pilot', *SEATS[0]); arm('pilot_armR', p['sh'][1], (0.03, 2.09, 0.96)); arm('pilot_armL', p['sh'][0], (-0.30, 2.05, 0.92))
g = figure('gunner', *SEATS[1]); arm('gunner_armR', g['sh'][1], (0.16, 1.22, 1.06)); arm('gunner_armL', g['sh'][0], (-0.16, 1.22, 1.06))
box('ammo_box', (0.30, 1.42, Z_FLOOR + 0.20), (0.22, 0.34, 0.24), ORD)
box('ammo_box_lid', (0.30, 1.42, Z_FLOOR + 0.33), (0.24, 0.36, 0.02), DARK)

# keel, mast, rotor
KEEL = [(0, 2.0, KEEL_Z + 0.04), (0, -0.4, KEEL_Z), (0, -2.95, KEEL_Z + 0.04)]
path('keel', KEEL, 0.05, FRAME)
MAST_FOOT_F, MAST_FOOT_R = Vector((0, -0.50, 1.10)), Vector((0, -0.95, 1.70))
for sx in (-1, 1):
    tube(f'mast_front{sx}', MAST_FOOT_F + Vector((sx * 0.30, 0, 0)), HEAD + Vector((sx * 0.05, 0.04, -0.10)), 0.028, FRAME)
    tube(f'mast_rear{sx}', MAST_FOOT_R + Vector((sx * 0.18, 0, 0)), HEAD + Vector((sx * 0.05, -0.06, -0.10)), 0.024, FRAME)
    tube(f'mast_base{sx}', (sx * 0.30, -0.50, 1.10), (sx * 0.10, -0.45, KEEL_Z), 0.028, FRAME)
    tube(f'eng_mount{sx}', (sx * 0.18, -0.95, 1.70), (sx * 0.16, -1.25, 1.10), 0.02, FRAME)
    tube(f'eng_mount_low{sx}', (sx * 0.16, -1.25, 1.10), (sx * 0.06, -0.9, KEEL_Z), 0.022, FRAME)

box('rotor_head', HEAD + Vector((0, 0, -0.06)), (0.16, 0.20, 0.14), DARK)
tube('rotor_teeter', HEAD + Vector((0, -0.12, 0.05)), HEAD + Vector((0, 0.12, 0.05)), 0.03, FRAME)
box('rotor_hub_bar', HEAD + Vector((0, 0, 0.09)), (1.1, 0.16, 0.035), FRAME)
for sx in (-1, 1):
    box(f'rotor_blade{sx}', HEAD + Vector((sx * (0.5 + (ROTOR_R - 0.5) / 2), 0, 0.09 + 0.06)), (ROTOR_R - 0.5, 0.20, 0.022), BLADE, rot=(0, math.radians(-sx * 1.8), 0))
tube('prerotator', (0.10, -0.80, 1.62), HEAD + Vector((0.10, -0.03, -0.14)), 0.012, DARK)

# tail
box('hstab', (0, -2.62, 0.44), (2.00, 0.62, 0.04), SKIN)
fin('fin_centre', (0, -2.15, 0.44), 0.78, 0.42, 1.10, 0.30, SKIN)
fin('rudder', (0, -2.72, 0.48), 0.24, 0.18, 1.00, 0.06, SKIN2, thick=0.03)
for sx in (-1, 1):
    fin(f'fin_tip{sx}', (sx * 1.00, -2.32, 0.22), 0.52, 0.34, 0.56, 0.14, SKIN)

# landing gear
WR = 0.27
for sx in (-1, 1):
    path(f'gear_main{sx}', [(0, -0.62, KEEL_Z), (sx * 0.55, -0.68, 0.44), (sx * 1.00, -0.72, WR)], 0.035, FRAME)
    tube(f'wheel_main{sx}', (sx * 0.97, -0.72, WR), (sx * 1.19, -0.72, WR), WR, TYRE, segs=20)
    tube(f'hub_main{sx}', (sx * 0.96, -0.72, WR), (sx * 1.20, -0.72, WR), 0.11, FRAME, segs=12)
    tube(f'nose_fork{sx}', (sx * 0.07, 1.92, 0.44), (sx * 0.07, 2.00, 0.20), 0.018, FRAME)
tube('wheel_nose', (-0.06, 2.00, 0.20), (0.06, 2.00, 0.20), 0.20, TYRE, segs=20)

# side gun: 12.7 mm, 2A42 style, right side, Ka-50 layout
GX, GZ = 0.70, 0.66
box('gun_cradle', (0.60, 1.55, GZ), (0.16, 0.60, 0.26), SKIN2)
tube('gun_pivot', (0.50, 1.55, GZ), (0.80, 1.55, GZ), 0.10, GUNM, segs=16)
box('gun_receiver', (GX, 1.50, GZ), (0.13, 0.95, 0.17), GUNM)
box('gun_feed', (GX, 1.42, GZ + 0.11), (0.15, 0.24, 0.07), GUNM)
tube('gun_barrel_jacket', (GX, 1.95, GZ), (GX, 2.45, GZ), 0.038, GUNM)
tube('gun_barrel', (GX, 2.45, GZ), (GX, 3.05, GZ), 0.022, GUNM)
tube('gun_muzzle', (GX, 3.05, GZ), (GX, 3.22, GZ), 0.036, GUNM)
path('gun_chute', [(0.36, 1.42, Z_FLOOR + 0.34), (0.50, 1.42, 0.86), (0.62, 1.42, 0.84), (GX, 1.42, GZ + 0.13)], 0.045, BRASS)

# stub pylons (clone of the Ibis fuselage pylons)
for sx in (-1, 1):
    box(f'pylon_stub{sx}', (sx * 0.74, 0.62, 0.50), (0.58, 0.42, 0.05), SKIN)
    box(f'pylon{sx}', (sx * 0.96, 0.62, 0.44), (0.07, 0.50, 0.12), SKIN2)

use('STORES')
tube('rocket_pod', (-0.96, 0.15, 0.29), (-0.96, 1.05, 0.29), 0.12, ORD, segs=16)
tube('rocket_pod_nose', (-0.96, 1.05, 0.29), (-0.96, 1.20, 0.29), 0.12, DARK, segs=16, r1=0.08)
ball('pab125', (0.96, 0.62, 0.28), (0.115, 0.62, 0.115), ORD)
for k in range(4):
    a = math.radians(45 + 90 * k)
    box(f'pab_fin{k}', (0.96 + 0.10 * math.cos(a), 0.08, 0.28 + 0.10 * math.sin(a)), (0.14, 0.22, 0.012), ORD, rot=(0, -a, 0))

# ------------------------------------------------------------------ engine options
def engine_core():
    box('crankcase', (0, -0.98, 1.38), (0.42, 0.66, 0.42), ENGM)
    for sx in (-1, 1):
        for k in (0, 1):
            tube(f'cyl{sx}{k}', (sx * 0.21, -0.82 - 0.30 * k, 1.38), (sx * 0.45, -0.82 - 0.30 * k, 1.38), 0.11, ENGM)
            box(f'valve_cover{sx}{k}', (sx * 0.48, -0.82 - 0.30 * k, 1.38), (0.06, 0.23, 0.22), RED)
        tube(f'exhaust{sx}', (sx * 0.38, -0.98, 1.26), (sx * 0.15, -0.98, 1.04), 0.03, FRAME)
    tube('muffler', (-0.27, -0.98, 1.02), (0.27, -0.98, 1.02), 0.085, FRAME)
    box('gearbox', (0, -1.36, PROP_Z), (0.24, 0.12, 0.26), ENGM)
    box('radiator', (0, -0.70, 1.72), (0.50, 0.07, 0.22), DARK)
    box('oil_tank', (0, -0.62, 1.22), (0.34, 0.10, 0.30), DARK)

def blades(n, r, chord, y, m, tag, twist=18):
    for k in range(n):
        a = 2 * math.pi * k / n + 0.3
        c = Vector((math.cos(a), 0, math.sin(a)))
        Mx = Matrix.Translation(Vector((0, y, PROP_Z)) + c * (0.08 + (r - 0.08) / 2)) @ Matrix.Rotation(-a, 4, 'Y') @ Matrix.Rotation(math.radians(twist), 4, 'X')
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
        bmesh.ops.transform(bm, matrix=Mx @ Matrix.Diagonal((r - 0.08, 0.018, chord, 1)), verts=bm.verts)
        obj_from_bm(f'{tag}_blade{k}', bm, m, False)

use('ENG_A')   # real-world: bare Rotax-style flat four, open 3-blade pusher prop
engine_core()
tube('prop_hub', (0, -1.44, PROP_Z), (0, -1.60, PROP_Z), 0.07, FRAME, r1=0.03)
blades(3, PROP_R, 0.13, PROP_Y, BLADE, 'prop')

use('ENG_B')   # modernised: cowled engine, Ibis-style ducted pusher fan
engine_core()
ball('cowl', (0, -0.98, 1.40), (0.54, 0.50, 0.40), SKIN)
box('cowl_intake', (0, -0.56, 1.50), (0.50, 0.10, 0.18), DARK)
DR, DC = 0.68, 0.46
bm = bmesh.new(); prof = [(DR + 0.07, 0.0), (DR + 0.085, -0.12), (DR + 0.06, -DC), (DR + 0.005, -DC), (DR, -0.14), (DR + 0.02, 0.0)]
N = 40; rr = []
for k in range(N):
    a = 2 * math.pi * k / N
    rr.append([bm.verts.new((r * math.cos(a), PROP_Y + 0.20 + dy, PROP_Z + r * math.sin(a))) for r, dy in prof])
for k in range(N):
    a, b = rr[k], rr[(k + 1) % N]
    for i in range(len(prof)): bm.faces.new((a[i], a[(i + 1) % len(prof)], b[(i + 1) % len(prof)], b[i]))
obj_from_bm('duct', bm, SKIN)
tube('fan_hub', (0, -1.40, PROP_Z), (0, -1.74, PROP_Z), 0.12, DARK, r1=0.05, segs=16)
blades(7, DR - 0.02, 0.17, PROP_Y - 0.05, BLADE, 'fan', twist=32)
for k in range(4):
    a = math.radians(45 + 90 * k)
    tube(f'stator{k}', (0.12 * math.cos(a), PROP_Y - 0.22, PROP_Z + 0.12 * math.sin(a)), ((DR + 0.03) * math.cos(a), PROP_Y - 0.22, PROP_Z + (DR + 0.03) * math.sin(a)), 0.022, SKIN2)

# ------------------------------------------------------------------ seat 3 passengers
Y3, Z3 = SEATS[2]
use('PAX_SPOTTER')
s = figure('spotter', Y3, Z3, lean=2, head_yaw=-55)
aim = Matrix.Rotation(math.radians(-55), 3, 'Z')
bino = s['head'] + aim @ Vector((0, 0.24, -0.01))
box('bino_body', bino, (0.24, 0.22, 0.10), GUNM, rot=(0, 0, math.radians(-55)))
for sx in (-1, 1): tube(f'bino_lens{sx}', bino + aim @ Vector((sx * 0.07, 0.10, 0)), bino + aim @ Vector((sx * 0.07, 0.20, 0)), 0.042, DARK)
box('bino_laser', bino + aim @ Vector((0, 0.04, 0.07)), (0.07, 0.16, 0.05), GUNM, rot=(0, 0, math.radians(-55)))
arm('spotter_armR', s['sh'][1], bino + aim @ Vector((0.11, -0.04, -0.04)), bend=Vector((0.10, 0.02, -0.14)))
arm('spotter_armL', s['sh'][0], bino + aim @ Vector((-0.11, -0.04, -0.04)), bend=Vector((-0.10, 0.02, -0.14)))
box('radio', s['chest'] + Vector((-0.10, 0.13, 0.02)), (0.10, 0.05, 0.16), DARK)
tube('radio_antenna', s['chest'] + Vector((-0.14, 0.12, 0.10)), s['chest'] + Vector((-0.20, 0.06, 0.62)), 0.006, DARK)
box('eud', s['chest'] + Vector((0.07, 0.15, -0.04)), (0.17, 0.02, 0.11), SCREEN, rot=(math.radians(-35), 0, 0))

use('PAX_MG')
s = figure('mgunner', Y3, Z3, lean=2, head_yaw=-70)
yaw = math.radians(-86); R = Matrix.Rotation(yaw, 3, 'Z'); down = math.radians(-12)
Rg = R @ Matrix.Rotation(down, 3, 'X')
gp = s['chest'] + Vector((0.46, 0.14, 0.02))                      # gun centre, out over the right sill
def gpt(v): return gp + Rg @ Vector(v)
box('mg_receiver', gp, (0.07, 0.62, 0.11), GUNM, rot=(down, 0, yaw))
tube('mg_barrel', gpt((0, 0.31, 0.01)), gpt((0, 0.92, 0.01)), 0.018, GUNM)
tube('mg_suppressor', gpt((0, 0.92, 0.01)), gpt((0, 1.14, 0.01)), 0.030, GUNM)
box('mg_stock', gpt((0, -0.44, -0.01)), (0.05, 0.28, 0.10), GUNM, rot=(down, 0, yaw))
box('mg_optic', gpt((0, 0.02, 0.09)), (0.05, 0.14, 0.06), DARK, rot=(down, 0, yaw))
box('mg_belt_box', gpt((-0.11, 0.05, -0.08)), (0.13, 0.20, 0.16), ORD, rot=(down, 0, yaw))
tube('mg_grip', gpt((0, -0.10, -0.05)), gpt((0, -0.13, -0.17)), 0.02, GUNM)
arm('mg_armR', s['sh'][1], gpt((0, -0.12, -0.12)), bend=Vector((0.06, -0.10, -0.10)))
arm('mg_armL', s['sh'][0], gpt((-0.02, 0.30, -0.07)), bend=Vector((-0.02, 0.10, -0.16)))
# support arm (Steadicam style): hip plate -> two spring links -> gimbal under the gun
hp = s['hip'] + Vector((0.21, 0.06, 0.16)); j1 = hp + Vector((0.14, 0.16, 0.12)); j2 = j1 + Vector((0.12, -0.10, 0.04)); gim = gpt((0, 0.08, -0.08))
box('arm_hip_plate', hp, (0.05, 0.16, 0.14), DARK)
for dz in (0.0, 0.05):
    tube(f'arm_link1_{dz}', hp + Vector((0, 0, dz)), j1 + Vector((0, 0, dz)), 0.014, FRAME)
    tube(f'arm_link2_{dz}', j1 + Vector((0, 0, dz)), j2 + Vector((0, 0, dz)), 0.014, FRAME)
ball('arm_joint1', j1 + Vector((0, 0, 0.025)), (0.035, 0.035, 0.045), DARK); ball('arm_joint2', j2 + Vector((0, 0, 0.025)), (0.035, 0.035, 0.045), DARK)
tube('arm_post', j2, gim, 0.016, FRAME); ball('arm_gimbal', gim, (0.04, 0.04, 0.04), DARK)
for sx in (-1, 1): box(f'harness_strap{sx}', s['chest'] + Vector((sx * 0.11, 0.115, -0.02)), (0.05, 0.02, 0.40), DARK)
box('harness_belt', s['hip'] + Vector((0, 0.02, 0.12)), (0.41, 0.27, 0.07), DARK)

use('PAX_GL')
s = figure('grenadier', Y3, Z3, lean=2, head_yaw=65)
yaw = math.radians(72); down = math.radians(-8); R = Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(down, 3, 'X')
gp = s['chest'] + Vector((-0.30, 0.22, 0.02))
def gl(v): return gp + R @ Vector(v)
tube('gl_cylinder', gl((0, -0.07, -0.02)), gl((0, 0.12, -0.02)), 0.085, GUNM, segs=18)
tube('gl_barrel', gl((0, 0.12, 0.03)), gl((0, 0.44, 0.03)), 0.030, GUNM)
box('gl_frame', gl((0, -0.02, 0.06)), (0.05, 0.42, 0.05), GUNM, rot=(down, 0, yaw))
box('gl_stock', gl((0, -0.36, 0.02)), (0.045, 0.30, 0.09), GUNM, rot=(down, 0, yaw))
box('gl_sight', gl((0, 0.06, 0.13)), (0.05, 0.10, 0.07), DARK, rot=(down, 0, yaw))
tube('gl_foregrip', gl((0, 0.30, -0.01)), gl((0, 0.30, -0.14)), 0.02, GUNM)
tube('gl_grip', gl((0, -0.14, -0.02)), gl((0, -0.17, -0.13)), 0.02, GUNM)
arm('gl_armL', s['sh'][0], gl((0, 0.30, -0.12)), bend=Vector((-0.08, 0.04, -0.14)))
arm('gl_armR', s['sh'][1], gl((0, -0.16, -0.10)), bend=Vector((0.10, 0.06, -0.14)))
for k in range(6): tube(f'gl_bandolier{k}', s['chest'] + Vector((-0.15 + 0.06 * k, 0.12, -0.10)), s['chest'] + Vector((-0.15 + 0.06 * k, 0.12, 0.0)), 0.022, BRASS)

# ------------------------------------------------------------------ report + save
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in bpy.data.objects:
    if o.users_collection[0].name in ('ENG_B', 'PAX_SPOTTER', 'PAX_GL', 'STORES'): continue
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
print('SIZE L %.2f  W(rotor) %.2f  H %.2f  | tub nose-to-fin %.2f..%.2f' % (hi.y - lo.y, hi.x - lo.x, hi.z - lo.z, lo.y, hi.y))
print('tris', sum(len(o.data.polygons) for o in bpy.data.objects))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
