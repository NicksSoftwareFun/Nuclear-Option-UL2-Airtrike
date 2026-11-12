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
    """radio in a pouch on the left chest with its antenna over the shoulder, PTT on the right strap, EUD phone in a
    fold-down chest mount at the sternum, cables. snap(p, dir, gap) pushes a rest-pose body point out of the vest."""
    # radio pouch + radio (handheld manpack-radio class): body, top knobs, keypad face, antenna base and whip
    R0 = snap(Vector((-0.085, 0.0, 0.36)), (0, 1, 0), 0.004)
    box('kit_radio_pouch', tuple(R0 + Vector((0, 0.020, 0))), (0.078, 0.042, 0.120), SEAT, bevel=0.008)
    box('kit_pouch_flap', tuple(R0 + Vector((0, 0.022, 0.050))), (0.082, 0.046, 0.024), SEAT, bevel=0.006)
    box('radio_body', tuple(R0 + Vector((0, 0.020, 0.088))), (0.064, 0.036, 0.060), SEAT, bevel=0.006)
    box('radio_face', tuple(R0 + Vector((0, 0.039, 0.088))), (0.046, 0.002, 0.040), MECH, bevel=0.001)
    for k, dx in enumerate((-0.016, 0.010)):
        tube(f'radio_knob{k}', tuple(R0 + Vector((dx, 0.020, 0.118))), tuple(R0 + Vector((dx, 0.020, 0.132))), 0.008 if k == 0 else 0.006, MECH, segs=12)
    A0 = R0 + Vector((0.018, 0.016, 0.118))
    tube('radio_antbase', tuple(A0), tuple(A0 + Vector((0, 0, 0.022))), 0.007, DARK, segs=10)
    pipe('radio_ant', [tuple(A0 + Vector((0, 0, 0.020))), tuple(A0 + Vector((-0.010, -0.010, 0.090))), tuple(A0 + Vector((-0.030, -0.050, 0.170))), tuple(A0 + Vector((-0.045, -0.110, 0.210)))], 0.0045, DARK, n=6, segs=6)
    # PTT on the right shoulder strap and its cable across the chest
    P0 = snap(Vector((0.090, 0.0, 0.44)), (0, 1, 0), 0.006)
    box('radio_ptt', tuple(P0), (0.040, 0.020, 0.050), DARK, bevel=0.006)
    tube('radio_ptt_btn', tuple(P0 + Vector((0, 0.010, 0.004))), tuple(P0 + Vector((0, 0.016, 0.004))), 0.012, MECH, segs=14)
    mid = snap(Vector((0.0, 0.0, 0.33)), (0, 1, 0), 0.010)
    pipe('radio_cable', [tuple(R0 + Vector((0.020, 0.010, 0.060))), tuple(mid + Vector((-0.04, 0, 0))), tuple(mid + Vector((0.04, 0, 0.04))), tuple(P0 + Vector((0, 0, -0.026)))], 0.004, DARK, n=6, segs=6)
    # EUD: rugged phone in a fold-down chest mount, folded flat against the vest
    E0 = snap(Vector((0.020, 0.0, 0.30)), (0, 1, 0), 0.006)
    box('eud_mount', tuple(E0), (0.090, 0.012, 0.040), SEAT, bevel=0.004)
    tube('eud_hinge', tuple(E0 + Vector((-0.040, 0.010, 0.012))), tuple(E0 + Vector((0.040, 0.010, 0.012))), 0.005, MECH, segs=10)
    tilt = Matrix.Rotation(math.radians(-8), 4, 'X')
    H = Matrix.Translation(E0 + Vector((0, 0.012, 0.012))) @ tilt
    box('eud_case', tuple(H @ Vector((0, 0.008, 0.085))), (0.086, 0.014, 0.165), DARK, rot=(math.radians(-8), 0, 0), bevel=0.006)
    box('eud_bezel', tuple(H @ Vector((0, 0.0155, 0.085))), (0.078, 0.002, 0.152), DARK, rot=(math.radians(-8), 0, 0), bevel=0.002)
    box('eud_screen', tuple(H @ Vector((0, 0.0168, 0.087))), (0.068, 0.001, 0.136), SCREEN, rot=(math.radians(-8), 0, 0))
    for k, x in enumerate((-0.044, 0.044)):
        box(f'eud_strap{k}', tuple(H @ Vector((x, 0.006, 0.085))), (0.006, 0.020, 0.030), DARK, rot=(math.radians(-8), 0, 0), bevel=0.001)
    pipe('radio_cable_eud', [tuple(E0 + Vector((-0.040, 0.008, 0.0))), tuple(E0 + Vector((-0.070, 0.016, 0.020))), tuple(R0 + Vector((0.020, 0.030, -0.040)))], 0.0035, DARK, n=5, segs=6)
