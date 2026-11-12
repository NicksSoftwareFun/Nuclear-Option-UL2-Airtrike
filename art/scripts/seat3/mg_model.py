"""Seat 3 light machine gun (original design in the spirit of a modern .338 belt-fed LMG) and the
harness support arm. Gun frame: origin under the receiver above the pistol grip, +Y muzzle, +Z up,
bore at z 0.045. build_gun(M) / build_arm(hip, elbow, post_top) add to the current ul2lib part."""
import math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from ul2lib import *
from mathutils import Vector, Matrix

BORE = 0.045
GRIP = Vector((0, 0.0, -0.06))        # pistol grip centre
FORE = Vector((0, 0.30, -0.035))      # vertical foregrip centre
MOUNT = Vector((0, 0.125, -0.012))    # support arm gimbal under the trunnion


def build_gun(M):
    # receiver + feed cover + rail
    box('mg_receiver', (0, -0.02, 0.042), (0.070, 0.30, 0.100), MECH, bevel=0.004, M=M)
    box('mg_trunnion', (0, 0.15, 0.045), (0.066, 0.05, 0.080), MECH, bevel=0.003, M=M)
    box('mg_cover', (0, 0.015, 0.106), (0.074, 0.17, 0.030), MECH, bevel=0.004, M=M)
    box('mg_cover_latch', (0, -0.072, 0.108), (0.030, 0.018, 0.020), DARK, bevel=0.002, M=M)
    box('mg_rail', (0, -0.005, 0.124), (0.024, 0.32, 0.012), DARK, M=M)
    for k in range(10):
        box(f'mg_rail_slot{k}', (0, -0.15 + k * 0.031, 0.131), (0.026, 0.010, 0.004), DARK, M=M)
    # optic: compact magnified sight with a sunshade
    revolve('mg_optic', (0, -0.085, 0.163), (0, 1, 0), [(0, 0.0), (0, 0.021), (0.025, 0.021), (0.035, 0.018), (0.085, 0.018),
                                                       (0.095, 0.023), (0.12, 0.023), (0.12, 0.0)], DARK, segs=18, M=M)
    box('mg_optic_mount', (0, -0.02, 0.140), (0.030, 0.07, 0.020), DARK, bevel=0.002, M=M)
    box('mg_optic_turret', (0.024, -0.015, 0.163), (0.016, 0.024, 0.024), DARK, bevel=0.002, M=M)
    # handguard (flat-sided, slotted) + barrel + gas block + muzzle brake
    sweep('mg_handguard', [(0, 0.17, BORE), (0, 0.45, BORE)], rrect(0.058, 0.066, 0.012), MECH,
          side=(1, 0, 0), smooth=False, M=M)
    for k in range(5):
        for sx in (-1, 1):
            box(f'mg_hg_slot{k}{sx}', (sx * 0.0295, 0.21 + k * 0.05, BORE), (0.003, 0.030, 0.012), DARK, M=M)
    tube('mg_barrel', (0, 0.45, BORE), (0, 0.60, BORE), 0.0115, DARK, segs=14, M=M)
    box('mg_gasblock', (0, 0.465, BORE - 0.004), (0.030, 0.03, 0.040), DARK, bevel=0.003, M=M)
    revolve('mg_brake', (0, 0.60, BORE), (0, 1, 0), [(0, 0), (0, 0.018), (0.065, 0.018), (0.065, 0.0)], DARK, segs=14, M=M)
    for k in range(3):
        for sx in (-1, 1):
            box(f'mg_brake_port{k}{sx}', (sx * 0.016, 0.612 + k * 0.018, BORE), (0.008, 0.010, 0.022), DARK, M=M)
    box('mg_carry', (0, 0.10, 0.150), (0.016, 0.07, 0.016), DARK, bevel=0.003, M=M)
    tube('mg_carry_post', (0, 0.07, 0.120), (0, 0.07, 0.150), 0.006, DARK, M=M)
    tube('mg_carry_post2', (0, 0.13, 0.120), (0, 0.13, 0.150), 0.006, DARK, M=M)
    # folded bipod under the handguard
    for sx in (-1, 1):
        tube(f'mg_bipod{sx}', (sx * 0.014, 0.43, 0.002), (sx * 0.014, 0.22, 0.004), 0.0065, DARK, segs=8, M=M)
        box(f'mg_bipod_foot{sx}', (sx * 0.014, 0.215, 0.004), (0.012, 0.018, 0.010), DARK, M=M)
    box('mg_bipod_yoke', (0, 0.445, 0.006), (0.040, 0.020, 0.016), DARK, bevel=0.002, M=M)
    # vertical foregrip
    revolve('mg_foregrip', FORE + Vector((0, 0, 0.035)), (0, 0, -1), [(0, 0), (0, 0.015), (0.075, 0.016), (0.085, 0.019), (0.09, 0)],
            DARK, segs=12, M=M)
    # pistol grip + trigger guard + trigger
    box('mg_grip', tuple(GRIP), (0.030, 0.042, 0.10), DARK, rot=(R(-16), 0, 0), bevel=0.006, M=M)
    box('mg_guard', (0, 0.045, -0.024), (0.008, 0.055, 0.006), DARK, M=M)
    box('mg_guard_f', (0, 0.071, -0.012), (0.008, 0.006, 0.026), DARK, M=M)
    box('mg_trigger', (0, 0.040, -0.010), (0.006, 0.006, 0.020), DARK, rot=(R(15), 0, 0), M=M)
    # charging handle (right) and ejection port cover
    box('mg_charger', (0.040, 0.090, 0.070), (0.025, 0.020, 0.014), DARK, bevel=0.002, M=M)
    box('mg_charge_slot', (0.0305, 0.03, 0.070), (0.002, 0.13, 0.008), DARK, M=M)
    box('mg_eject', (0.0305, -0.03, 0.030), (0.002, 0.07, 0.030), DARK, M=M)
    # collapsing stock on a buffer tube, short for harness carry
    tube('mg_buffer', (0, -0.17, 0.062), (0, -0.30, 0.062), 0.017, DARK, segs=12, M=M)
    box('mg_stock', (0, -0.29, 0.042), (0.042, 0.10, 0.075), DARK, bevel=0.008, M=M)
    box('mg_buttpad', (0, -0.345, 0.034), (0.046, 0.014, 0.115), DARK, bevel=0.004, M=M)
    box('mg_cheek', (0, -0.27, 0.083), (0.036, 0.09, 0.016), DARK, bevel=0.004, M=M)
    # belt pouch (left) + flexible feed chute to the feed tray
    box('mg_pouch', (-0.085, -0.005, -0.040), (0.075, 0.15, 0.15), SEAT, bevel=0.012, M=M)
    box('mg_pouch_lid', (-0.085, -0.005, 0.040), (0.080, 0.155, 0.018), SEAT, bevel=0.006, M=M)
    box('mg_pouch_strap', (-0.0455, -0.005, -0.040), (0.004, 0.03, 0.14), DARK, M=M)
    pipe('mg_chute', [(-0.085, 0.03, 0.05), (-0.075, 0.035, 0.085), (-0.050, 0.035, 0.100), (-0.034, 0.035, 0.098)],
         0.012, DARK, n=6, side=(0, 1, 0), segs=8, M=M)
    # gimbal for the support arm
    box('mg_mount_rail', (0, 0.125, -0.004), (0.026, 0.07, 0.012), DARK, M=M)
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
