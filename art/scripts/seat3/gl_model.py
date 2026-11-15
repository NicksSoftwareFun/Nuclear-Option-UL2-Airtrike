"""Seat 3 grenadier weapon: an original 6-shot 40 mm revolver grenade launcher in the M32A1 class (Nick 2026-11-11 /
2026-11-12 16:14), with a vertical foregrip under the handguard for the hold. Gun frame like the MG: +Y along the bore,
origin at the top of the pistol grip, metres.
    import gl_model as gl; gl.build_gl(M); wR, wL = gl.build_hands(M)"""
import math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from ul2lib import *
from mathutils import Vector, Matrix
import mg_model as mg

CZ = 0.020                    # cylinder axis height
CR = 0.070                    # cylinder outer radius
CPR = 0.046                   # chamber pitch radius (6 x 40 mm)
CY0, CY1 = 0.072, 0.212       # cylinder rear / front faces
BORE = CZ + CPR               # barrel axis (top chamber)
FP0, FP1 = 0.214, 0.232       # front frame plate
HG0, HG1 = 0.240, 0.470       # handguard
MUZ = 0.505
GRIP_TOP, GRIP_BOT = Vector((0, 0.008, -0.012)), Vector((0, -0.032, -0.118))
VG_Y = 0.255
VG_TOP, VG_BOT = Vector((0, VG_Y, BORE - 0.044)), Vector((0, VG_Y, BORE - 0.174))
SIGHT = Vector((-0.040, 0.110, BORE + 0.064))   # tube reflex sight on a left side mount (axis centre)
STOCK_TUBE = (Vector((0, 0.000, 0.064)), Vector((0, -0.190, 0.064)))
BUTT_Y = -0.230


def _plate_yz(name, prof, x0, x1, m, M, bevel=0.003):
    plate(name, prof, x1 - x0, m, M @ side_at((x0 + x1) / 2), bevel=bevel)


def _frame_plate(name, y0, y1, M, m=DARK):
    """frame plate: a rounded hub around the cylinder axis joined to a ring round the barrel (side outline in XZ,
    extruded along Y)"""
    pts = []
    for a in range(200, 341, 20): pts.append((0.030 * math.cos(math.radians(a)), CZ + 0.030 * math.sin(math.radians(a))))
    for a in range(-20, 201, 20): pts.append((0.040 * math.cos(math.radians(a)), BORE + 0.006 + 0.034 * math.sin(math.radians(a))))
    pts = pts[::-1]
    R = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))      # outline (x, z), thickness along y
    plate(name, pts, y1 - y0, m, M @ Matrix.Translation((0, (y0 + y1) / 2, 0)) @ R, bevel=0.003)


def build_gl(M):
    Mm = Matrix(M)
    # ---- receiver block over the grip (hammer housing) + top strap over the cylinder to the front plate
    box('gl_recv', (0, 0.012, 0.050), (0.052, 0.090, 0.090), DARK, bevel=0.015, M=M)
    box('gl_recv_side', (0, 0.020, 0.030), (0.058, 0.050, 0.030), DARK, bevel=0.004, M=M)
    _frame_plate('gl_rplate', 0.056, CY0 - 0.002, Mm)
    box('gl_strap', (0, (0.040 + FP1) / 2, CZ + CR + 0.014), (0.030, FP1 - 0.040, 0.022), DARK, bevel=0.003, M=M)
    for k, y in enumerate((0.064, 0.222)):
        for sx in (-1, 1): tube(f'gl_screw{k}{sx}', (sx * 0.0405, y, BORE + 0.010), (sx * 0.0425, y, BORE + 0.010), 0.003, MECH, segs=8, M=M)
    box('gl_rail_t', (0, (0.030 + FP1) / 2, BORE + 0.054), (0.022, FP1 - 0.030, 0.010), DARK, M=M)
    for k in range(int((FP1 - 0.035) / 0.020)):
        box(f'gl_rail_t{k}', (0, 0.040 + k * 0.020, BORE + 0.0605), (0.024, 0.010, 0.004), DARK, M=M)
    # ---- cylinder: body, six flutes between the chambers, front chamber mouths with rounds, rear ratchet, axis spring housing
    revolve('gl_cyl', (0, CY0, CZ), (0, 1, 0), [(0, 0), (0, CR - 0.004), (0.004, CR), (CY1 - CY0 - 0.004, CR), (CY1 - CY0, CR - 0.004), (CY1 - CY0, 0)], MECH, segs=36, M=M)
    for k in range(6):
        a = math.radians(30 + 60 * k); d = Vector((math.cos(a), 0, math.sin(a)))
        c = Vector((0, (CY0 + CY1) / 2, CZ)) + d * (CR - 0.002)
        Rf = Matrix.Rotation(math.radians(30), 4, d) @ Matrix.Rotation(-a, 4, 'Y')
        box(f'gl_flute{k}', tuple(c), (0.010, (CY1 - CY0 - 0.030) / math.cos(math.radians(30)), 0.008), DARK, rot=tuple(Rf.to_euler()), M=M)
        a2 = math.radians(90 + 60 * k); d2 = Vector((math.cos(a2), 0, math.sin(a2))); ch = Vector((0, CY1, CZ)) + d2 * CPR
        if k == 0: continue                       # top chamber sits behind the barrel
        tube(f'gl_mouth{k}', tuple(ch - Vector((0, 0.002, 0))), tuple(ch + Vector((0, 0.0012, 0))), 0.0205, SCREEN, segs=16, M=M)
        revolve(f'gl_nose{k}', tuple(ch - Vector((0, 0.010, 0))), (0, 1, 0), [(0, 0), (0, 0.0185), (0.004, 0.0185), (0.009, 0.015), (0.012, 0.008), (0.013, 0)], MECH, segs=14, M=M)
        tube(f'gl_ratchet{k}', tuple(Vector((0, CY0 - 0.004, CZ)) + d * 0.024), tuple(Vector((0, CY0 + 0.001, CZ)) + d * 0.024), 0.006, DARK, segs=8, M=M)
    revolve('gl_hub', (0, CY1, CZ), (0, 1, 0), [(0, 0), (0, 0.022), (0.050, 0.022), (0.054, 0.018), (0.054, 0)], DARK, segs=20, M=M)
    for k in range(4):
        tube(f'gl_hub_rib{k}', (0, CY1 + 0.006 + k * 0.010, CZ), (0, CY1 + 0.010 + k * 0.010, CZ), 0.0235, MECH, segs=20, M=M)
    _frame_plate('gl_fplate', FP0, FP1, Mm)
    half = [(0.064 * math.cos(math.radians(a)), CZ + 0.064 * math.sin(math.radians(a))) for a in range(0, 181, 15)]
    Rxz = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
    plate('gl_fplate_shield', half[::-1], 0.008, DARK, Mm @ Matrix.Translation((0, CY1 + 0.006, 0)) @ Rxz, bevel=0.002)
    # frame hinge pin (the barrel group swings open for loading) and the cylinder release latch
    # lower frame strap under the cylinder's lower left, joining the rear frame to the front plate, with the hinge knuckle
    box('gl_lstrap', (-0.016, (0.062 + FP0) / 2 + 0.004, CZ - CR - 0.011), (0.018, FP0 - 0.062 + 0.012, 0.010), DARK, bevel=0.003, M=M)
    tube('gl_hinge', (-0.036, FP0 + 0.004, CZ - CR - 0.011), (0.004, FP0 + 0.004, CZ - CR - 0.011), 0.011, DARK, segs=14, M=M)
    tube('gl_hinge_pin', (-0.039, FP0 + 0.004, CZ - CR - 0.011), (-0.036, FP0 + 0.004, CZ - CR - 0.011), 0.006, MECH, segs=10, M=M)
    box('gl_hinge_lug', (-0.012, FP0 + 0.004, CZ - CR + 0.004), (0.024, 0.020, 0.030), DARK, bevel=0.004, M=M)
    box('gl_latch', (-0.029, 0.046, 0.024), (0.010, 0.030, 0.012), MECH, bevel=0.003, M=M)
    # ---- barrel with a quad-rail handguard (vented, rails on all four faces), muzzle ring
    tube('gl_barrel', (0, FP1, BORE), (0, MUZ, BORE), 0.0255, DARK, segs=24, M=M)
    tube('gl_bore', (0, MUZ - 0.004, BORE), (0, MUZ + 0.0005, BORE), 0.0205, SCREEN, segs=20, M=M)
    revolve('gl_muzzle', (0, MUZ - 0.020, BORE), (0, 1, 0), [(0, 0.0255), (0, 0.030), (0.016, 0.030), (0.020, 0.027), (0.020, 0.0255)], DARK, segs=24, M=M)
    hgc = Vector((0, (HG0 + HG1) / 2, BORE)); L = HG1 - HG0
    for nm, off, sz in (('t', (0, 0, 0.029), (0.040, L, 0.010)), ('b', (0, 0, -0.029), (0.040, L, 0.010)),
                        ('l', (-0.029, 0, 0), (0.010, L, 0.040)), ('r', (0.029, 0, 0), (0.010, L, 0.040))):
        box(f'gl_hg_{nm}', tuple(hgc + Vector(off)), sz, MECH, bevel=0.002, M=M)
    for sx, sz_ in ((-1, 1), (1, 1), (-1, -1), (1, -1)):
        box(f'gl_hg_c{sx}{sz_}', tuple(hgc + Vector((sx * 0.022, 0, sz_ * 0.022))), (0.014, L, 0.014), MECH, rot=(0, math.radians(45), 0), bevel=0.002, M=M)
    for nm, off, fn in (('t', (0, 0, 0.0365), 'z'), ('b', (0, 0, -0.0365), 'z'), ('l', (-0.0365, 0, 0), 'x'), ('r', (0.0365, 0, 0), 'x')):
        sz = (0.020, L - 0.010, 0.008) if fn == 'z' else (0.008, L - 0.010, 0.020)
        box(f'gl_hgr_{nm}', tuple(hgc + Vector(off)), sz, DARK, M=M)
        for k in range(int((L - 0.02) / 0.020)):
            o2 = Vector(off) * 1.13; tsz = (0.022, 0.010, 0.004) if fn == 'z' else (0.004, 0.010, 0.022)
            box(f'gl_hgr_{nm}{k}', tuple(hgc + o2 + Vector((0, -L / 2 + 0.012 + k * 0.020, 0))), tsz, DARK, M=M)
    for k in range(5):                   # vents in the corner bevels
        for sx in (-1, 1):
            box(f'gl_vent{sx}_{k}', tuple(hgc + Vector((sx * 0.0285, -L / 2 + 0.035 + k * 0.040, 0.021))), (0.004, 0.022, 0.010), SCREEN, rot=(0, sx * math.radians(45), 0), M=M)
    revolve('gl_hg_band', (0, HG1 - 0.004, BORE), (0, 1, 0), [(0, 0.026), (0, 0.035), (0.015, 0.035), (0.015, 0.026)], DARK, segs=24, M=M)
    tube('gl_sling_f', (-0.040, HG1 - 0.012, BORE - 0.010), (-0.040, HG1 - 0.012, BORE + 0.010), 0.006, MECH, segs=10, M=M)
    # ---- vertical foregrip on the bottom rail (Nick 16:14)
    box('gl_vg_clamp', (0, VG_Y, VG_TOP.z + 0.004), (0.030, 0.050, 0.014), DARK, bevel=0.002, M=M)
    revolve('gl_vg', tuple(VG_TOP), (0, 0, -1), [(0, 0), (0, 0.015), (0.010, 0.016), (0.120, 0.018), (0.126, 0.021), (0.130, 0.021), (0.132, 0.0)], DARK, segs=14, M=M)
    for k in range(4):
        tube(f'gl_vg_rib{k}', (0, VG_Y, VG_TOP.z - 0.030 - k * 0.018), (0, VG_Y, VG_TOP.z - 0.034 - k * 0.018), 0.0178, MECH, segs=14, M=M)
    # ---- reflex sight on the top rail and a folding leaf range sight at the front of the strap
    S = SIGHT
    box('gl_sight_mount', (-0.024, S.y, BORE + 0.056), (0.030, 0.060, 0.012), DARK, bevel=0.002, M=M)
    box('gl_sight_arm', (-0.040, S.y, S.z - 0.012), (0.012, 0.040, 0.026), DARK, bevel=0.002, M=M)
    revolve('gl_sight', tuple(S + Vector((0, -0.055, 0))), (0, 1, 0), [(0, 0.016), (0, 0.020), (0.008, 0.020), (0.010, 0.0175), (0.100, 0.0175), (0.102, 0.020), (0.110, 0.020), (0.110, 0.016)], DARK, segs=24, M=M)
    tube('gl_sight_glass', tuple(S + Vector((0, 0.050, 0))), tuple(S + Vector((0, 0.051, 0))), 0.016, SCREEN, segs=20, M=M)
    tube('gl_sight_glassR', tuple(S + Vector((0, -0.050, 0))), tuple(S + Vector((0, -0.049, 0))), 0.016, SCREEN, segs=20, M=M)
    revolve('gl_sight_drum', tuple(S + Vector((-0.018, -0.005, 0))), (-1, 0, 0), [(0, 0), (0, 0.0175), (0.020, 0.0175), (0.022, 0.015), (0.022, 0)], MECH, segs=20, M=M)
    for k in range(8):
        a_ = math.radians(k * 45); box(f'gl_sight_tick{k}', tuple(S + Vector((-0.039, -0.005 + 0.0176 * math.cos(a_), 0.0176 * math.sin(a_)))), (0.004, 0.002, 0.002), DARK, M=M)
    tube('gl_sight_knob', tuple(S + Vector((0.010, -0.030, 0.016))), tuple(S + Vector((0.010, -0.030, 0.026))), 0.006, MECH, segs=12, M=M)
    # ---- pistol grip, trigger guard and trigger, safety
    plate('gl_grip', [(0.022, 0.010), (-0.018, 0.010), (-0.052, -0.116), (-0.044, -0.130), (-0.008, -0.130), (0.006, -0.090), (0.024, -0.040)], 0.034, DARK, Mm @ side_at(0.0), bevel=0.008)
    for k in range(4):
        box(f'gl_grip_rib{k}', (0, -0.012 - k * 0.008, -0.040 - k * 0.022), (0.035, 0.004, 0.010), MECH, rot=(math.radians(-20), 0, 0), M=M)
    pts = [(0.000, -0.010), (0.010, -0.050), (0.060, -0.052), (0.068, -0.012)]
    pipe('gl_guard', [(0, y, z) for y, z in pts], 0.0070, DARK, n=4, segs=8, M=M)
    box('gl_trigger', (0, 0.032, -0.014), (0.008, 0.008, 0.034), MECH, rot=(math.radians(12), 0, 0), bevel=0.002, M=M)
    tube('gl_safety', (-0.026, 0.004, 0.024), (-0.031, 0.004, 0.024), 0.0075, MECH, segs=12, M=M)
    box('gl_safety_lever', (-0.032, 0.004, 0.014), (0.004, 0.008, 0.025), MECH, bevel=0.0015, M=M)
    # ---- collapsible stock: buffer tube, sliding butt with cheek rest, rubber pad, QD sling points
    a, b = STOCK_TUBE
    tube('gl_tube', tuple(a), tuple(b), 0.0155, DARK, segs=16, M=M)
    tube('gl_tube_nut', tuple(a + Vector((0, -0.040, 0))), tuple(a + Vector((0, -0.055, 0))), 0.020, DARK, segs=14, M=M)
    prof = [(BUTT_Y, 0.086), (BUTT_Y + 0.130, 0.082), (BUTT_Y + 0.140, 0.056), (BUTT_Y + 0.100, 0.034),
            (BUTT_Y + 0.040, -0.004), (BUTT_Y + 0.008, -0.030), (BUTT_Y, -0.034)]
    _plate_yz('gl_stock', prof, -0.021, 0.021, DARK, Mm, bevel=0.006)
    box('gl_stock_cheek', (0, BUTT_Y + 0.070, 0.087), (0.040, 0.120, 0.012), DARK, bevel=0.004, M=M)
    box('gl_buttpad', (0, BUTT_Y - 0.008, 0.026), (0.044, 0.016, 0.122), SEAT, bevel=0.006, M=M)
    for k in range(5):
        box(f'gl_butt_rib{k}', (0, BUTT_Y - 0.0165, -0.030 + k * 0.025), (0.040, 0.002, 0.004), DARK, M=M)
    box('gl_stock_lever', (0, BUTT_Y + 0.160, 0.064 - 0.018), (0.008, 0.025, 0.005), MECH, bevel=0.002, M=M)
    tube('gl_sling_s', (-0.022, BUTT_Y + 0.060, 0.050), (-0.027, BUTT_Y + 0.060, 0.050), 0.010, MECH, segs=12, M=M)
    tube('gl_sling_r', (-0.018, BUTT_Y + 0.018, -0.022), (0.018, BUTT_Y + 0.018, -0.022), 0.005, MECH, segs=8, M=M)
    for k, p in enumerate(((0.024, -0.020, 0.066), (0.024, 0.300, BORE - 0.030))):
        tube(f'gl_qd{k}', p, (p[0] + 0.006, p[1], p[2]), 0.008, MECH, segs=10, M=M)
    # ---- pass 3 (realism): stock position holes, flip-up backup iron sights, FDE rail covers, axis release,
    #      receiver pins and side plate screws, cylinder detent notches, barrel lock lever, sling loop
    for k in range(5):
        tube(f'gl_tube_hole{k}', (0, -0.110 - k * 0.016, 0.064 - 0.0145), (0, -0.110 - k * 0.016, 0.064 - 0.017), 0.003, SCREEN, segs=8, M=M)
    rz = BORE + 0.066                                            # top of the strap rail / handguard top rail
    box('gl_bus_r_base', (0, 0.046, BORE + 0.068), (0.026, 0.020, 0.008), DARK, bevel=0.0015, M=M)
    _plate_yz('gl_bus_r', [(0.038, 0.000), (0.054, 0.000), (0.052, 0.026), (0.040, 0.026)], -0.009, 0.009, DARK, Mm @ Matrix.Translation((0, 0, BORE + 0.072)), bevel=0.0015)
    tube('gl_bus_r_ap', (0, 0.044, BORE + 0.090), (0, 0.050, BORE + 0.090), 0.0035, SCREEN, segs=10, M=M)
    box('gl_bus_f_base', (0, HG1 - 0.024, BORE + 0.048), (0.026, 0.022, 0.008), DARK, bevel=0.0015, M=M)
    for sx in (-1, 1):
        box(f'gl_bus_f_ear{sx}', (sx * 0.010, HG1 - 0.024, BORE + 0.066), (0.004, 0.012, 0.030), DARK, bevel=0.001, M=M)
    box('gl_bus_f_post', (0, HG1 - 0.024, BORE + 0.068), (0.003, 0.004, 0.026), MECH, M=M)
    for nm, off, sz in (('l', (-0.0405, 0, 0), (0.006, 0.090, 0.024)), ('r', (0.0405, 0, 0), (0.006, 0.090, 0.024))):
        box(f'gl_rcover_{nm}', tuple(hgc + Vector(off) + Vector((0, 0.050, 0))), sz, DARK, bevel=0.002, M=M)
        for k in range(6):
            box(f'gl_rcover_{nm}{k}', tuple(hgc + Vector(off) * 1.07 + Vector((0, 0.012 + k * 0.014, 0))), (0.003, 0.004, 0.020), DARK, M=M)
    tube('gl_axis_btn', (0.034, FP1 + 0.002, CZ), (0.040, FP1 + 0.002, CZ), 0.006, MECH, segs=12, M=M)
    box('gl_lock_lever', (0.036, FP0 - 0.010, CZ - 0.040), (0.006, 0.040, 0.010), MECH, rot=(math.radians(-25), 0, 0), bevel=0.002, M=M)
    for sx in (-1, 1):
        for k, (y, z) in enumerate(((0.000, 0.070), (0.040, 0.070), (0.000, 0.026), (0.030, 0.008))):
            tube(f'gl_pin{sx}{k}', (sx * 0.0255, y, z), (sx * 0.0275, y, z), 0.0035, MECH, segs=8, M=M)
        box(f'gl_recv_plate{sx}', (sx * 0.0265, 0.020, 0.048), (0.002, 0.050, 0.030), DARK, bevel=0.001, M=M)
    for k in range(6):
        a = math.radians(60 * k); d = Vector((math.cos(a), 0, math.sin(a)))
        box(f'gl_detent{k}', tuple(Vector((0, CY0 + 0.010, CZ)) + d * (CR - 0.0005)), (0.006, 0.010, 0.003), SCREEN, rot=(0, -a + math.pi / 2, 0), M=M)
    pipe('gl_sling_loop', [(-0.046, HG1 - 0.020, BORE), (-0.054, HG1 - 0.012, BORE), (-0.046, HG1 - 0.004, BORE)], 0.002, MECH, n=4, segs=6, M=M)


def build_hands(M):
    """gloved fists modelled on the pistol grip (right) and the vertical foregrip (left); returns the cuff points"""
    wR = mg._glove_wrap('glove_R', GRIP_TOP, GRIP_BOT, 1, (0, 1, 0), 0.018, M, (0.30, -0.92, -0.18), 'top')
    wL = mg._glove_wrap('glove_L', VG_TOP + Vector((0, 0, -0.008)), VG_BOT, -1, (0, 1, 0), 0.017, M, (-0.35, -0.70, -0.60), 'front')
    return wR, wL
