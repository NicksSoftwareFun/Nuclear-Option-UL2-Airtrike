"""Seat 3 light machine gun (original design in the spirit of a modern .338 belt-fed LMG) and the
harness support arm. Gun frame: origin under the receiver above the pistol grip, +Y muzzle, +Z up,
bore at z 0.045. build_gun(M) / build_arm(hip, elbow, post_top) add to the current ul2lib part."""
import math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from ul2lib import *
from mathutils import Vector, Matrix

BORE = 0.060
GRIP = Vector((0, 0.0, -0.055))       # pistol grip centre (under the rear of the receiver)
FORE = Vector((0, 0.35, -0.075))      # vertical foregrip centre under the handguard (left hand, Nick 2026-11-12)
MOUNT = Vector((0, 0.11, -0.012))     # support arm gimbal under the receiver, ahead of the trigger guard
REC_Y0, REC_Y1, HG_Y1 = -0.10, 0.30, 0.64


def build_gun(M):
    """original belt-fed LMG after the modern .338 class (Nick's reference photos): one long flat-sided upper with a
    skeletonised handguard, full-length top rail, box sight, belt box under the feed, stock folded along the right side"""
    W, H = 0.064, 0.100
    # one-piece upper: receiver + handguard, flat sides, chamfered top edges
    sweep('mg_upper', [(0, REC_Y0, 0.05), (0, HG_Y1, 0.05)], [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2 - 0.012), (W / 2 - 0.012, H / 2),
          (-W / 2 + 0.012, H / 2), (-W / 2, H / 2 - 0.012)], MECH, side=(1, 0, 0), smooth=False, M=M)
    box('mg_lower', (0, 0.02, -0.004), (0.054, 0.26, 0.016), MECH, bevel=0.003, M=M)                       # trigger housing strip
    box('mg_rail', (0, (REC_Y0 + HG_Y1) / 2, 0.106), (0.024, HG_Y1 - REC_Y0 - 0.02, 0.012), DARK, M=M)
    for k in range(22):
        box(f'mg_rail_slot{k}', (0, REC_Y0 + 0.02 + k * 0.032, 0.113), (0.026, 0.010, 0.004), DARK, M=M)
    # handguard windows (top row long, lower row short) and the rivet line, both sides
    for sx in (-1, 1):
        for k in range(5):
            box(f'mg_win{sx}{k}', (sx * (W / 2 + 0.0005), 0.36 + k * 0.055, 0.075), (0.003, 0.040, 0.020), DARK, M=M)
            box(f'mg_slot{sx}{k}', (sx * (W / 2 + 0.0005), 0.37 + k * 0.055, 0.040), (0.003, 0.030, 0.008), DARK, M=M)
        for k in range(12):
            ball(f'mg_rivet{sx}{k}', (sx * (W / 2 + 0.001), REC_Y0 + 0.04 + k * 0.06, 0.012), (0.003, 0.004, 0.004), DARK, M=M, seg=6)
        box(f'mg_feedwin{sx}', (sx * (W / 2 + 0.0005), 0.21, 0.050), (0.003, 0.11, 0.050), DARK, M=M)          # feed tray window
    box('mg_charger', (-W / 2 - 0.012, 0.25, 0.075), (0.022, 0.03, 0.016), DARK, bevel=0.003, M=M)           # left charging handle
    box('mg_cover_latch', (0, 0.135, 0.103), (0.030, 0.02, 0.012), DARK, M=M)
    # box sight at the rear of the rail
    box('mg_sight', (0, 0.02, 0.150), (0.050, 0.075, 0.062), DARK, bevel=0.006, M=M)
    box('mg_sight_hood', (0, 0.02, 0.183), (0.054, 0.080, 0.008), DARK, M=M)
    box('mg_sight_glass', (0, 0.02, 0.152), (0.040, 0.078, 0.038), SCREEN, M=M)
    # barrel, gas block / front sight post, bipod yoke, long suppressor
    tube('mg_barrel', (0, HG_Y1, BORE), (0, 0.91, BORE), 0.016, DARK, segs=14, M=M)
    box('mg_gasblock', (0, HG_Y1 + 0.03, BORE - 0.005), (0.034, 0.035, 0.045), DARK, bevel=0.004, M=M)
    box('mg_fsight', (0, HG_Y1 + 0.03, BORE + 0.035), (0.010, 0.012, 0.035), DARK, M=M)
    revolve('mg_can', (0, 0.88, BORE), (0, 1, 0), [(0, 0), (0, 0.018), (0.035, 0.037), (0.17, 0.037), (0.18, 0.030), (0.18, 0.0)], DARK, segs=18, M=M)
    # folded bipod under the handguard
    box('mg_bipod_yoke', (0, HG_Y1 + 0.02, 0.0), (0.040, 0.026, 0.022), DARK, bevel=0.003, M=M)
    for sx in (-1, 1):
        tube(f'mg_bipod{sx}', (sx * 0.013, HG_Y1 + 0.01, -0.004), (sx * 0.013, 0.47, -0.006), 0.0075, DARK, segs=8, M=M)
        box(f'mg_bipod_foot{sx}', (sx * 0.013, 0.47, -0.006), (0.014, 0.022, 0.012), DARK, M=M)
    # vertical foregrip on the handguard bottom rail
    box('mg_vg_clamp', (0, FORE.y, -0.006), (0.030, 0.05, 0.012), DARK, M=M)
    tube('mg_vg', (0, FORE.y, -0.010), (0, FORE.y, -0.142), 0.017, DARK, segs=12, M=M)
    tube('mg_vg_cap', (0, FORE.y, -0.142), (0, FORE.y, -0.150), 0.020, DARK, segs=12, M=M)
    # pistol grip + trigger guard + trigger
    box('mg_grip', tuple(GRIP + Vector((0, -0.008, -0.012))), (0.034, 0.044, 0.12), DARK, rot=(R(-18), 0, 0), bevel=0.007, M=M)
    box('mg_guard', (0, 0.055, -0.024), (0.008, 0.06, 0.006), DARK, M=M)
    box('mg_guard_f', (0, 0.083, -0.014), (0.008, 0.006, 0.022), DARK, M=M)
    box('mg_trigger', (0, 0.045, -0.012), (0.006, 0.006, 0.018), DARK, rot=(R(15), 0, 0), M=M)
    # skeleton stock folded forward along the right side (hinge at the rear of the upper)
    SX = W / 2 + 0.016
    box('mg_hinge', (W / 2 + 0.006, REC_Y0 + 0.005, 0.05), (0.014, 0.02, 0.07), DARK, M=M)
    plate('mg_stock', [(REC_Y0 + 0.01, 0.095), (REC_Y0 + 0.24, 0.095), (REC_Y0 + 0.26, 0.075), (REC_Y0 + 0.26, -0.02), (REC_Y0 + 0.20, -0.02),
                       (REC_Y0 + 0.11, 0.035), (REC_Y0 + 0.01, 0.035)], 0.026, DARK, Matrix(M) @ side_at(SX), bevel=0.004)
    box('mg_buttpad', (SX, REC_Y0 + 0.268, 0.038), (0.030, 0.016, 0.12), DARK, bevel=0.004, M=M)
    box('mg_cheekrest', (SX, REC_Y0 + 0.13, 0.100), (0.028, 0.16, 0.012), DARK, bevel=0.003, M=M)
    # belt box (fabric pouch) under the feed, belt link chute up into the left of the feed tray
    box('mg_pouch', (0, 0.22, -0.072), (0.105, 0.14, 0.11), SEAT, bevel=0.012, M=M)
    box('mg_pouch_flap', (-0.0535, 0.20, -0.072), (0.004, 0.04, 0.09), SEAT, M=M)
    box('mg_pouch_rail', (0, 0.22, -0.016), (0.060, 0.14, 0.012), DARK, M=M)
    # belt: rounds parallel to the bore, rising from the box mouth up the left side into the feed tray
    for k in range(5):
        z = -0.012 + k * 0.018; x = -W / 2 - 0.010 - 0.010 * (1 - k / 4.0) ** 2
        tube(f'mg_round{k}', (x, 0.165, z), (x, 0.235, z), 0.0075, MECH, segs=8, M=M)
        tube(f'mg_tip{k}', (x, 0.235, z), (x, 0.258, z), 0.004, MECH, segs=6, M=M)
        tube(f'mg_link{k}', (x - 0.002, 0.20, z - 0.008), (x - 0.002, 0.20, z + 0.008), 0.0045, DARK, segs=6, M=M)
    # gimbal for the support arm
    box('mg_mount_rail', (0, MOUNT.y, -0.004), (0.026, 0.05, 0.012), DARK, M=M)
    ball('mg_gimbal', tuple(MOUNT + Vector((0, 0, -0.012))), (0.016, 0.016, 0.016), MECH, M=M, seg=12)


def link(name, a, b, w=0.024):
    """one arm section: twin parallel bars + gas spring between two hinge blocks"""
    a, b = Vector(a), Vector(b); d = (b - a); u = d.normalized(); up = Vector((0, 0, 1))
    side = u.cross(up); side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0)); nrm = side.cross(u).normalized()
    for k, off in enumerate((nrm * 0.016, -nrm * 0.016)):
        tube(f'{name}_bar{k}', a + off + u * 0.02, b + off - u * 0.02, 0.0075, FRAME, segs=8)
    tube(f'{name}_spring', a + u * 0.05 + side * 0.012, a + u * (d.length * 0.62) + side * 0.012, 0.010, DARK, segs=10)
    tube(f'{name}_rod', a + u * (d.length * 0.62) + side * 0.012, b - u * 0.05 + side * 0.012, 0.0045, MECH, segs=8)


def hinge(name, c, h=0.06, r=0.020):
    c = Vector(c); tube(name, c - Vector((0, 0, h / 2)), c + Vector((0, 0, h / 2)), r, MECH, segs=12)
    tube(name + '_cap', c + Vector((0, 0, h / 2)), c + Vector((0, 0, h / 2 + 0.006)), r * 0.7, DARK, segs=12)


def build_arm(hip, elbow, post_top):
    """harness hip socket -> elbow -> vertical post under the gun gimbal (all world points)"""
    hip, elbow, post_top = Vector(hip), Vector(elbow), Vector(post_top)
    post_bot = post_top - Vector((0, 0, 0.10))
    box('arm_hip_plate', hip + Vector((0.0, 0, 0.02)), (0.030, 0.16, 0.20), DARK, bevel=0.006)
    box('arm_hip_socket', hip + Vector((0.022, 0, 0.0)), (0.03, 0.06, 0.09), MECH, bevel=0.006)
    hinge('arm_hip_hinge', hip + Vector((0.035, 0, 0)), 0.08)
    hinge('arm_elbow', elbow, 0.07, 0.022)
    hinge('arm_wrist', post_bot, 0.05, 0.018)
    link('arm_upper', hip + Vector((0.035, 0, 0)), elbow)
    link('arm_lower', elbow, post_bot)
    tube('arm_post', post_bot, post_top, 0.010, MECH, segs=10)
