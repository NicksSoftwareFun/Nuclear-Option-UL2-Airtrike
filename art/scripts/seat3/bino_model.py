"""Seat 3 spotter kit: original laser-rangefinder binoculars (military LRF binocular class, looks only: the game already
lases visible targets) plus the chest radio and EUD (Nick 2026-11-11 / 2026-11-12 16:14).
Binocular frame: +Y = line of sight, origin midway between the rear faces of the eyecups, metres.
    import bino_model as bn; bn.build_bino(M); wR, wL = bn.build_hands(M); bn.build_chest(place_fn) """
import math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from ul2lib import *
from mathutils import Vector, Matrix
import mg_model as mg

IPD = 0.033                    # half eye spacing
BW, BY0, BY1, BZ0, BZ1 = 0.074, 0.048, 0.190, -0.036, 0.040     # main housing half width, y and z extents
OBJ_X, OBJ_Z, OBJ_R = 0.040, -0.002, 0.028                    # objectives


def build_bino(M):
    Mm = Matrix(M)
    # eyecups (rubber, flared, slightly cupped) and diopter rings on the eyepiece tubes
    for sx in (-1, 1):
        x = sx * IPD
        revolve(f'bino_eyecup{sx}', (x, 0.000, 0), (0, 1, 0), [(0, 0.012), (0, 0.022), (0.004, 0.024), (0.016, 0.021), (0.026, 0.019), (0.026, 0.0)], SEAT, segs=20, M=M)
        tube(f'bino_eyepiece{sx}', (x, 0.024, 0), (x, BY0 + 0.004, 0), 0.0185, DARK, segs=18, M=M)
        for k in range(3):
            tube(f'bino_focus{sx}_{k}', (x, 0.030 + k * 0.005, 0), (x, 0.033 + k * 0.005, 0), 0.0200, SEAT, segs=18, M=M)
        tube(f'bino_eyeglass{sx}', (x, 0.008, 0), (x, 0.009, 0), 0.012, SCREEN, segs=16, M=M)
    # main housing: one wide body with rounded edges, a raised top spine, the rear face stepped in around the eyepieces
    box('bino_body', (0, (BY0 + BY1) / 2, (BZ0 + BZ1) / 2), (2 * BW, BY1 - BY0, BZ1 - BZ0), DARK, bevel=0.014, M=M)
    box('bino_spine', (0, (BY0 + BY1) / 2 - 0.006, BZ1 + 0.004), (0.050, BY1 - BY0 - 0.040, 0.012), DARK, bevel=0.005, M=M)
    # rubber armour on both sides where the hands hold it, with grip ribs
    for sx in (-1, 1):
        box(f'bino_armour{sx}', (sx * (BW + 0.002), (BY0 + BY1) / 2 + 0.006, (BZ0 + BZ1) / 2), (0.010, BY1 - BY0 - 0.030, BZ1 - BZ0 - 0.010), SEAT, bevel=0.004, M=M)
        for k in range(5):
            box(f'bino_grip{sx}_{k}', (sx * (BW + 0.0075), BY0 + 0.040 + k * 0.020, (BZ0 + BZ1) / 2), (0.002, 0.006, BZ1 - BZ0 - 0.020), SEAT, M=M)
    # objectives: sunshades, lens with a coated look, front ring
    for sx in (-1, 1):
        c = Vector((sx * OBJ_X, BY1, OBJ_Z))
        revolve(f'bino_shade{sx}', tuple(c - Vector((0, 0.004, 0))), (0, 1, 0), [(0, OBJ_R + 0.004), (0.010, OBJ_R + 0.004), (0.010, OBJ_R + 0.001), (0.004, OBJ_R)], DARK, segs=24, M=M)
        revolve(f'bino_armour_rim{sx}', tuple(c + Vector((0, 0.006, 0))), (0, 1, 0), [(0, OBJ_R + 0.001), (0, OBJ_R + 0.006), (0.004, OBJ_R + 0.005), (0.006, OBJ_R + 0.002), (0.006, OBJ_R + 0.001)], SEAT, segs=24, M=M)
        tube(f'bino_lensring{sx}', tuple(c + Vector((0, -0.001, 0))), tuple(c + Vector((0, 0.000, 0))), OBJ_R - 0.0005, MECH, segs=24, M=M)
        tube(f'bino_lens{sx}', tuple(c + Vector((0, 0.000, 0))), tuple(c + Vector((0, 0.0008, 0))), OBJ_R - 0.004, MECH, segs=24, M=M)
    # laser plate between and above the objectives with transmitter and receiver apertures
    LP = Vector((0, BY1 + 0.003, OBJ_Z + OBJ_R + 0.004))
    box('bino_laserplate', tuple(LP), (0.050, 0.008, 0.024), DARK, bevel=0.003, M=M)
    for nm, x, r in (('tx', -0.016, 0.006), ('rx', 0.012, 0.0085)):
        c = LP + Vector((x, 0.004, 0))
        revolve(f'bino_{nm}_ring', tuple(c), (0, 1, 0), [(0, 0), (0, r + 0.002), (0.002, r + 0.002), (0.002, 0)], MECH, segs=18, M=M)
        tube(f'bino_{nm}_glass', tuple(c + Vector((0, 0.0018, 0))), tuple(c + Vector((0, 0.0024, 0))), r, SCREEN, segs=16, M=M)
    # top controls: three rubber buttons at the front right (range / mode / display), power knob at the rear; inset display
    for k in range(3):
        tube(f'bino_btn{k}', (0.030, BY1 - 0.070 + k * 0.015, BZ1 + 0.000), (0.030, BY1 - 0.070 + k * 0.015, BZ1 + 0.003), 0.005, SEAT, segs=14, M=M)
    tube('bino_btn_power', (-0.030, BY0 + 0.020, BZ1 - 0.001), (-0.030, BY0 + 0.020, BZ1 + 0.005), 0.0065, MECH, segs=14, M=M)
    box('bino_bezel', (0, BY0 + 0.060, BZ1 + 0.0102), (0.032, 0.044, 0.0012), DARK, M=M)
    box('bino_display', (0, BY0 + 0.060, BZ1 + 0.0106), (0.028, 0.040, 0.0008), SCREEN, M=M)
    # left side battery cap, bottom tripod plate with screw boss, lanyard loops
    revolve('bino_batt_cap', (-(BW + 0.006), BY1 - 0.030, -0.004), (-1, 0, 0), [(0, 0), (0, 0.014), (0.003, 0.014), (0.004, 0.012), (0.004, 0)], MECH, segs=18, M=M)
    for k in range(8):
        a_ = math.radians(k * 45); box(f'bino_batt_kn{k}', (-(BW + 0.008), BY1 - 0.030 + 0.0142 * math.cos(a_), -0.004 + 0.0142 * math.sin(a_)), (0.003, 0.002, 0.002), MECH, M=M)
    box('bino_mount', (0, (BY0 + BY1) / 2, BZ0 - 0.003), (0.050, 0.070, 0.006), MECH, bevel=0.002, M=M)
    tube('bino_screw', (0, (BY0 + BY1) / 2, BZ0 - 0.006), (0, (BY0 + BY1) / 2, BZ0 - 0.008), 0.008, DARK, segs=12, M=M)
    for sx in (-1, 1):       # closed strap loops at the rear upper corners
        x = sx * (BW - 0.014)
        for dy in (-0.007, 0.007): box(f'bino_loop{sx}{dy > 0}', (x, BY0 + 0.016 + dy, BZ1 + 0.003), (0.004, 0.003, 0.010), DARK, M=M)
        tube(f'bino_loopbar{sx}', (x, BY0 + 0.0075, BZ1 + 0.008), (x, BY0 + 0.0245, BZ1 + 0.008), 0.002, DARK, segs=8, M=M)
        # armour wraps 15 mm onto the top and bottom, thumb pads under the body
        for zz in (BZ1 + 0.001, BZ0 - 0.001):
            box(f'bino_armour_wrap{sx}{zz > 0}', (sx * (BW - 0.006), (BY0 + BY1) / 2 + 0.006, zz), (0.016, BY1 - BY0 - 0.030, 0.004), SEAT, bevel=0.0015, M=M)
        box(f'bino_armour_thumb{sx}', (sx * 0.036, BY1 - 0.060, BZ0 - 0.002), (0.030, 0.020, 0.004), SEAT, bevel=0.0015, M=M)
    # eyecup fold line
    for sx in (-1, 1): tube(f'bino_eyecup_fold{sx}', (sx * IPD, 0.006, 0), (sx * IPD, 0.0075, 0), 0.0235, SEAT, segs=20, M=M)


HOLD_Y0, HOLD_Y1 = 0.058, 0.175


def build_hands(M):
    """gloves round the rubber-armoured sides: fingers over the top, thumbs underneath; returns the cuff points"""
    out = {}
    for s, sx in (('R', 1), ('L', -1)):
        top = Vector((sx * (BW - 0.016), HOLD_Y0, -0.004)); bot = Vector((sx * (BW - 0.016), HOLD_Y1, -0.004))
        out[s] = mg._glove_wrap(f'glove_{s}', top, bot, sx, (0, 0, 1), 0.040, M, (sx * 0.35, -0.85, -0.40), 'top')
    return out['R'], out['L']


def build_chest(snap):
    """spotter chest kit (Nick 16:50): a large two-channel manpack radio (PRC-162 class, original) carried upright in an
    open-top pouch on the chest, its base resting on the lap; controls and keypad on the front-top, two antenna ports,
    side handles, battery box; PTT on the right strap, a small EUD on a fold-down mount above it, cables.
    Rest-pose body frame: +Y forward, +Z up, -X the soldier's left. snap(p, dir, gap) pushes a point out of the vest."""
    RX, W, D, Z0, Z1 = -0.015, 0.220, 0.095, 0.086, 0.370          # radio centre x, width, depth, base and top height
    Y0 = max(snap(Vector((x, 0.0, z)), (0, 1, 0), 0.006).y for x in (RX - W / 2 + 0.01, RX, RX + W / 2 - 0.01) for z in (0.12, 0.20, 0.28, 0.36))
    C = Vector((RX, Y0 + D / 2, (Z0 + Z1) / 2)); H = Z1 - Z0; YF = Y0 + D        # body centre, front face y
    def P(x, y, z): return (RX + x, y, z)
    # pouch: open top, covers the lower two thirds, MOLLE webbing rows, straps up to the shoulder harness
    PZ1 = Z0 + 0.125
    box('kit_radio_pouch', P(0, C.y, (Z0 - 0.006 + PZ1) / 2), (W + 0.016, D + 0.014, PZ1 - Z0 + 0.006), SEAT, bevel=0.010)
    for k in range(3):
        box(f'kit_pouch_web{k}', P(0, YF + 0.0075, Z0 + 0.030 + k * 0.040), (W + 0.010, 0.004, 0.022), SEAT, bevel=0.002)
        for j in range(4):
            box(f'kit_pouch_tack{k}{j}', P(-W / 2 + 0.030 + j * (W - 0.060) / 3, YF + 0.0095, Z0 + 0.030 + k * 0.040), (0.004, 0.002, 0.024), SEAT)
    for sx in (-1, 1):
        top = snap(Vector((RX + sx * 0.085, 0.0, 0.445)), (0, 1, 0), 0.004)
        pipe(f'kit_pouch_strap{sx}', [P(sx * (W / 2 - 0.012), C.y, PZ1 - 0.010), P(sx * (W / 2 - 0.016), Y0 + 0.012, Z1 + 0.030), tuple(top)], 0.006, SEAT, n=5, segs=6)
        box(f'kit_pouch_buckle{sx}', P(sx * (W / 2 - 0.014), Y0 + 0.020, Z1 + 0.010), (0.022, 0.006, 0.014), MECH, bevel=0.002)
    # radio body: main chassis, battery box at the base (seam + latches), side handles (rails) the full height
    box('radio_body', tuple(C + Vector((0, 0, 0.020))), (W, D, H - 0.040), SEAT, bevel=0.008)
    box('radio_batt', tuple(Vector((C.x, C.y, Z0 + 0.020))), (W - 0.004, D - 0.004, 0.040), SEAT, bevel=0.006)
    box('radio_batt_seam', tuple(Vector((C.x, C.y, Z0 + 0.041))), (W + 0.002, D + 0.002, 0.003), DARK)
    for sx in (-1, 1):
        box(f'radio_batt_latch{sx}', P(sx * (W / 2 + 0.001), C.y, Z0 + 0.036), (0.006, 0.030, 0.016), MECH, bevel=0.002)
        x = sx * (W / 2 + 0.010)
        pipe(f'radio_rail{sx}', [P(x * 0.86, C.y - 0.020, Z1 - 0.004), P(x, C.y - 0.020, Z1 - 0.020), P(x, C.y - 0.020, PZ1 + 0.010), P(x * 0.86, C.y - 0.020, PZ1 - 0.004)], 0.0055, DARK, n=6, segs=8)
        for k in range(3):
            box(f'radio_fin{sx}{k}', P(sx * (W / 2 + 0.001), C.y + 0.012, PZ1 + 0.030 + k * 0.016), (0.004, D - 0.040, 0.006), DARK)
    # front control face (upper third): recessed panel, display, 4x4 keypad, function keys, labels plate
    FZ = Z1 - 0.058
    box('radio_panel', P(0, YF + 0.0005, FZ), (W - 0.024, 0.003, 0.090), DARK, bevel=0.003)
    box('radio_screen', P(-0.040, YF + 0.0025, FZ + 0.020), (0.070, 0.002, 0.034), SCREEN, bevel=0.001)
    box('radio_screen_bezel', P(-0.040, YF + 0.0020, FZ + 0.020), (0.080, 0.002, 0.042), MECH, bevel=0.002)
    for r in range(4):
        for c in range(4):
            box(f'radio_key{r}{c}', P(0.022 + c * 0.017, YF + 0.003, FZ + 0.030 - r * 0.017), (0.013, 0.004, 0.012), MECH, bevel=0.002)
    for k in range(4):
        box(f'radio_fkey{k}', P(-0.068 + k * 0.019, YF + 0.003, FZ - 0.020), (0.014, 0.004, 0.008), MECH, bevel=0.0015)
    box('radio_plate', P(-0.040, YF + 0.0025, FZ - 0.036), (0.070, 0.0015, 0.010), MECH, bevel=0.001)
    # top: volume and channel knobs with guards, two antenna ports (VHF whip, UHF/SATCOM tape over the shoulder),
    # side connectors with dust caps
    for k, (dx, r) in enumerate(((-0.060, 0.011), (-0.028, 0.013))):
        tube(f'radio_knob{k}', P(dx, YF - 0.022, Z1), P(dx, YF - 0.022, Z1 + 0.016), r, MECH, segs=16)
        for j in range(8):
            a = math.radians(j * 45)
            box(f'radio_knob_rib{k}{j}', P(dx + (r + 0.0005) * math.cos(a), YF - 0.022 + (r + 0.0005) * math.sin(a), Z1 + 0.008), (0.002, 0.002, 0.014), MECH, rot=(0, 0, a))
    box('radio_guard', P(-0.044, YF - 0.006, Z1 + 0.010), (0.070, 0.004, 0.020), DARK, bevel=0.002)
    ants = []
    for k, dx in enumerate((0.010, 0.088)):
        b = Vector(P(dx, C.y - 0.006, Z1))
        tube(f'radio_port{k}', tuple(b), tuple(b + Vector((0, 0, 0.014))), 0.010, MECH, segs=14)
        tube(f'radio_antbase{k}', tuple(b + Vector((0, 0, 0.014))), tuple(b + Vector((0, 0, 0.050))), 0.0075, DARK, segs=12)
        ants.append(b + Vector((0, 0, 0.050)))
    a0 = ants[0]
    pipe('radio_ant0', [tuple(a0), tuple(a0 + Vector((0, -0.010, 0.120))), tuple(a0 + Vector((-0.010, -0.040, 0.260))), tuple(a0 + Vector((-0.020, -0.090, 0.380)))], 0.0040, DARK, n=8, segs=6)
    a1 = ants[1]
    pipe('radio_ant1', [tuple(a1), tuple(a1 + Vector((0.012, -0.010, 0.060))), tuple(a1 + Vector((0.030, -0.050, 0.110))), tuple(a1 + Vector((0.045, -0.120, 0.120)))], 0.0050, DARK, n=6, segs=6)
    for k in range(2):
        z = Z1 - 0.040 - k * 0.040
        tube(f'radio_conn{k}', P(W / 2, C.y + 0.010, z), P(W / 2 + 0.012, C.y + 0.010, z), 0.009, MECH, segs=14)
        tube(f'radio_conn_cap{k}', P(W / 2 + 0.012, C.y + 0.010, z), P(W / 2 + 0.016, C.y + 0.010, z), 0.010, DARK, segs=14)
    # PTT on the right shoulder strap, cable from the radio's side connector up to it
    P0 = snap(Vector((0.100, 0.0, 0.445)), (0, 1, 0), 0.008)
    box('radio_ptt', tuple(P0), (0.042, 0.022, 0.056), DARK, bevel=0.006)
    tube('radio_ptt_btn', tuple(P0 + Vector((0, 0.011, 0.004))), tuple(P0 + Vector((0, 0.017, 0.004))), 0.013, MECH, segs=14)
    c0 = Vector(P(W / 2 + 0.016, C.y + 0.010, Z1 - 0.040))
    pipe('radio_cable', [tuple(c0), tuple(c0 + Vector((0.020, 0.0, 0.020))), tuple(P0 + Vector((0.010, -0.004, -0.060))), tuple(P0 + Vector((0, 0, -0.028)))], 0.0045, DARK, n=6, segs=6)
    # EUD: rugged phone on a fold-down mount on the left upper chest above the radio, cable to the second connector
    E0 = snap(Vector((-0.105, 0.0, 0.385)), (0, 1, 0), 0.006)
    E0 = Vector((E0.x, max(E0.y, Y0 + 0.010), E0.z))
    box('eud_mount', tuple(E0), (0.084, 0.012, 0.036), SEAT, bevel=0.004)
    tube('eud_hinge', tuple(E0 + Vector((-0.038, 0.010, -0.012))), tuple(E0 + Vector((0.038, 0.010, -0.012))), 0.005, MECH, segs=10)
    tilt = math.radians(-12)
    Hm = Matrix.Translation(E0 + Vector((0, 0.014, -0.012))) @ Matrix.Rotation(tilt, 4, 'X')
    box('eud_case', tuple(Hm @ Vector((0, 0.008, 0.062))), (0.080, 0.014, 0.135), DARK, rot=(tilt, 0, 0), bevel=0.006)
    box('eud_bezel', tuple(Hm @ Vector((0, 0.0155, 0.062))), (0.072, 0.002, 0.124), DARK, rot=(tilt, 0, 0), bevel=0.002)
    box('eud_screen', tuple(Hm @ Vector((0, 0.0168, 0.064))), (0.062, 0.001, 0.108), SCREEN, rot=(tilt, 0, 0))
    for k, x in enumerate((-0.041, 0.041)):
        box(f'eud_strap{k}', tuple(Hm @ Vector((x, 0.006, 0.062))), (0.006, 0.020, 0.030), DARK, rot=(tilt, 0, 0), bevel=0.001)
    c1 = Vector(P(W / 2 + 0.016, C.y + 0.010, Z1 - 0.080))
    pipe('radio_cable_eud', [tuple(c1), tuple(c1 + Vector((0.010, 0.012, -0.020))), tuple(Vector(P(0, YF + 0.012, PZ1 + 0.004))), tuple(E0 + Vector((0.030, 0.010, -0.030)))], 0.0035, DARK, n=7, segs=6)
