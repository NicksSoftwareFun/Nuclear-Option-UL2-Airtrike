"""Seat 3 light machine gun (original design in the spirit of a modern .338 belt-fed LMG) and the
harness support arm. Gun frame: origin under the receiver above the pistol grip, +Y muzzle, +Z up,
bore at z 0.045. build_gun(M) / build_arm(hip, elbow, post_top) add to the current ul2lib part."""
import math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from ul2lib import *
from mathutils import Vector, Matrix

BORE = 0.060
GRIP = Vector((0, -0.01, -0.06))      # pistol grip centre (under the rear of the receiver)
FORE = Vector((0, 0.33, -0.075))      # vertical foregrip centre under the handguard (left hand, Nick 2026-11-12)
MOUNT = Vector((0, 0.11, -0.012))     # support arm gimbal under the receiver, ahead of the trigger guard
REC_Y0, REC_Y1, HG_Y1 = -0.10, 0.30, 0.64
W = 0.064
GUN_CUTTERS = []
import bpy
GRIP_TOP, GRIP_BOT = Vector((0, 0.008, -0.012)), Vector((0, -0.032, -0.118))   # pistol grip axis (raked)
VG_TOP, VG_BOT = Vector((0, 0.33, -0.012)), Vector((0, 0.33, -0.142))         # foregrip axis


def _bullet(name, x, y0, z, M):
    """one .338-class round lying parallel to the bore (case + shoulder + projectile)"""
    revolve(name, (x, y0, z), (0, 1, 0), [(0, 0), (0, 0.0072), (0.052, 0.0072), (0.060, 0.0050), (0.066, 0.0045), (0.068, 0.0044),
                                         (0.090, 0.0030), (0.096, 0.0), ], MECH, segs=8, M=M)


def build_gun(M):
    """original belt-fed LMG after the modern .338 class (Nick's reference photos), detail pass 2: profiled one-piece upper
    with angled front/rear, skeletonised handguard windows, lightening pockets, feed cover, real belt of rounds,
    vertical foregrip, forward-folded bipod, folded skeleton stock, box optic on a QD mount"""
    Mm = Matrix(M)
    # ---- upper: side profile extruded across the width, chamfered
    prof = [(REC_Y0, 0.004), (REC_Y0, 0.078), (REC_Y0 + 0.03, 0.100), (HG_Y1 - 0.03, 0.100), (HG_Y1, 0.080), (HG_Y1, 0.030),
            (HG_Y1 - 0.05, 0.016), (0.31, 0.016), (0.29, 0.0), (REC_Y0 + 0.02, 0.0)]
    plate('mg_upper', prof, W, MECH, Mm @ side_at(0), bevel=0.004)
    # side detail: windows, M-LOK slots and receiver pockets are CUT into the walls (boolean), with a dark core behind them
    up = PARTS[CUR[0]]['objs'][-1]
    box('mg_core', (0, (0.30 + HG_Y1) / 2, 0.052), (W - 0.016, HG_Y1 - 0.32, 0.068), SCREEN, M=M)
    cutters = []
    for sx in (-1, 1):
        X = sx * W / 2
        for k in range(5):
            y = 0.345 + k * 0.056
            cutters.append(plate(f'mg_cw{sx}{k}', [(y, 0.060), (y + 0.042, 0.060), (y + 0.034, 0.088), (y + 0.008, 0.088)], 0.020, DARK, Mm @ side_at(X)))
            cutters.append(box(f'mg_cs{sx}{k}', (X, y + 0.020, 0.044), (0.020, 0.030, 0.008), DARK, M=M))
            cutters.append(box(f'mg_cs2{sx}{k}', (X, y + 0.020, 0.028), (0.020, 0.025, 0.007), DARK, M=M))
        for k, (a_, b_) in enumerate(((REC_Y0 + 0.02, 0.06), (0.07, 0.11))):
            cutters.append(plate(f'mg_cp{sx}{k}', [(a_, 0.046), (b_, 0.046), (b_ - 0.008, 0.084), (a_ + 0.008, 0.084)], 0.008, DARK, Mm @ side_at(X)))
        # raised ribs full length
        for zr in (0.030, 0.075):
            if zr == 0.075: segs_ = ((REC_Y0 + 0.03, 0.115), (0.29, 0.335))
            else: segs_ = ((REC_Y0 + 0.03, HG_Y1 - 0.06),)
            for j, (ya, yb) in enumerate(segs_):
                box(f'mg_rib{sx}{zr}{j}', (sx * (W / 2 + 0.0015), (ya + yb) / 2, zr), (0.003, yb - ya, 0.005), MECH, bevel=0.001, M=M)
        for k in range(15):
            yk = REC_Y0 + 0.03 + k * 0.045
            if yk < HG_Y1 - 0.03 and not (0.10 < yk < 0.30):
                tube(f'mg_rivet{sx}{k}', (sx * W / 2, yk, 0.018), (sx * (W / 2 + 0.003), yk, 0.018), 0.003, DARK, segs=6, M=M)
        tube(f'mg_qd{sx}', (sx * W / 2, HG_Y1 - 0.08, 0.026), (sx * (W / 2 + 0.006), HG_Y1 - 0.08, 0.026), 0.0075, DARK, segs=10, M=M)
        box(f'mg_selector{sx}', (sx * (W / 2 + 0.003), 0.010, 0.012), (0.004, 0.022, 0.008), DARK, rot=(R(-20), 0, 0), M=M)
    for c_ in cutters:
        PARTS[CUR[0]]['objs'].remove(c_); c_.hide_render = True; c_.hide_viewport = True; c_.display_type = 'WIRE'
        bo = up.modifiers.new('cut_' + c_.name, 'BOOLEAN'); bo.operation = 'DIFFERENCE'; bo.object = c_; bo.solver = 'EXACT'
    for _ in range(len(cutters)): bpy.context.view_layer.objects.active = up
    # move the booleans ahead of the bevel so the cut edges get chamfered too
    bv = up.modifiers.get('bevel')
    if bv:
        up.modifiers.remove(bv); bv = up.modifiers.new('bevel', 'BEVEL'); bv.width = 0.003; bv.segments = 2; bv.limit_method = 'ANGLE'; bv.angle_limit = R(40)
    GUN_CUTTERS[:] = cutters
    # feed tray window (left) and ejection port (right)
    plate('mg_feedwin', [(0.13, 0.024), (0.29, 0.024), (0.29, 0.082), (0.13, 0.082)], 0.002, DARK, Mm @ side_at(-W / 2 - 0.0008))
    plate('mg_ejport', [(0.15, 0.006), (0.27, 0.006), (0.27, 0.022), (0.15, 0.022)], 0.002, DARK, Mm @ side_at(W / 2 + 0.0008))
    # charging handle in its slot (left)
    plate('mg_chslot', [(0.31, 0.058), (0.56, 0.058), (0.56, 0.066), (0.31, 0.066)], 0.002, DARK, Mm @ side_at(-W / 2 - 0.0008))
    box('mg_charger', (-W / 2 - 0.012, 0.335, 0.062), (0.024, 0.016, 0.014), DARK, bevel=0.003, M=M)
    ball('mg_charger_knob', (-W / 2 - 0.026, 0.335, 0.062), (0.008, 0.010, 0.010), DARK, M=M, seg=10)
    # feed cover: raised hump with ribs, hinge at the front, latch at the rear
    plate('mg_cover', [(0.11, 0.100), (0.30, 0.100), (0.30, 0.112), (0.13, 0.116), (0.11, 0.110)], W - 0.008, MECH, Mm @ side_at(0), bevel=0.003)
    for k in range(4):
        box(f'mg_cover_rib{k}', (0, 0.15 + k * 0.035, 0.117), (W - 0.02, 0.006, 0.004), DARK, M=M)
    tube('mg_cover_hinge', (-W / 2 + 0.006, 0.30, 0.104), (W / 2 - 0.006, 0.30, 0.104), 0.006, DARK, segs=10, M=M)
    box('mg_cover_latch', (0, 0.105, 0.112), (0.034, 0.016, 0.012), DARK, bevel=0.002, M=M)
    # picatinny rail: base + teeth, rear section and handguard section
    for nm, a, b in (('r', REC_Y0 + 0.035, 0.105), ('f', 0.31, HG_Y1 - 0.035)):
        box(f'mg_rail_{nm}', (0, (a + b) / 2, 0.106), (0.022, b - a, 0.012), DARK, M=M)
        n = int((b - a) / 0.010)
        for k in range(n // 2):
            box(f'mg_rail_{nm}{k}', (0, a + 0.006 + k * 0.020, 0.1135), (0.024, 0.010, 0.004), DARK, M=M)
    # holographic sight on a QD rail clamp: body, open window under a hood, front/rear glass, side buttons, throw lever
    O = Vector((0, -0.005, 0.146))
    box('mg_optic_clamp', O + Vector((0, 0, -0.030)), (0.034, 0.080, 0.012), DARK, bevel=0.002, M=M)
    box('mg_optic_lever', O + Vector((0.022, -0.010, -0.028)), (0.006, 0.034, 0.008), DARK, bevel=0.002, M=M)
    box('mg_optic_body', O + Vector((0, 0.0, -0.014)), (0.052, 0.090, 0.022), DARK, bevel=0.005, M=M)
    for sx in (-1, 1):
        plate(f'mg_optic_wall{sx}', [(-0.040, -0.004), (0.040, -0.004), (0.036, 0.034), (-0.034, 0.034)], 0.007, DARK, Mm @ Matrix.Translation(O) @ side_at(sx * 0.0225), bevel=0.002)
    box('mg_optic_hood', O + Vector((0, 0.002, 0.036)), (0.052, 0.078, 0.008), DARK, bevel=0.003, M=M)
    box('mg_optic_glassF', O + Vector((0, 0.030, 0.014)), (0.038, 0.002, 0.034), SCREEN, M=M)
    box('mg_optic_glassR', O + Vector((0, -0.030, 0.014)), (0.038, 0.002, 0.034), SCREEN, M=M)
    for k in range(3):
        tube(f'mg_optic_btn{k}', O + Vector((-0.026, -0.022 + k * 0.014, -0.012)), O + Vector((-0.031, -0.022 + k * 0.014, -0.012)), 0.0035, MECH, segs=8, M=M)
    tube('mg_optic_batt', O + Vector((0.026, 0.020, -0.012)), O + Vector((0.033, 0.020, -0.012)), 0.009, MECH, segs=12, M=M)
    # folded front backup sight on the rail front
    box('mg_bus', (0, HG_Y1 - 0.05, 0.118), (0.020, 0.030, 0.008), DARK, bevel=0.002, M=M)
    # barrel nut, barrel with a few fluting rings, gas block with front post, barrel handle (folded left)
    tube('mg_bnut', (0, HG_Y1 - 0.004, BORE), (0, HG_Y1 + 0.016, BORE), 0.024, DARK, segs=16, M=M)
    tube('mg_barrel', (0, HG_Y1, BORE), (0, 0.91, BORE), 0.0145, DARK, segs=16, M=M)
    for k in range(3):
        tube(f'mg_bring{k}', (0, 0.73 + k * 0.03, BORE), (0, 0.736 + k * 0.03, BORE), 0.0158, DARK, segs=16, M=M)
    box('mg_gasblock', (0, HG_Y1 + 0.045, BORE - 0.008), (0.036, 0.040, 0.048), DARK, bevel=0.005, M=M)
    tube('mg_gastube', (0, HG_Y1 + 0.02, BORE - 0.024), (0, HG_Y1 + 0.07, BORE - 0.024), 0.0075, DARK, segs=10, M=M)
    box('mg_fpost_base', (0, HG_Y1 + 0.045, BORE + 0.022), (0.016, 0.022, 0.012), DARK, M=M)
    for sx in (-1, 1):
        box(f'mg_fpost_ear{sx}', (sx * 0.007, HG_Y1 + 0.045, BORE + 0.040), (0.003, 0.014, 0.024), DARK, M=M)
    box('mg_fpost', (0, HG_Y1 + 0.045, BORE + 0.038), (0.003, 0.004, 0.020), DARK, M=M)
    tube('mg_bhandle_pivot', (-0.018, HG_Y1 + 0.03, BORE), (-0.030, HG_Y1 + 0.03, BORE), 0.007, DARK, segs=8, M=M)
    pipe('mg_bhandle', [(-0.030, HG_Y1 + 0.03, BORE), (-0.040, HG_Y1 + 0.06, BORE + 0.005), (-0.040, HG_Y1 + 0.13, BORE + 0.005), (-0.030, HG_Y1 + 0.16, BORE)],
         0.0065, DARK, n=4, segs=8, M=M)
    # suppressor: mount collar, can with grooves and a front cap
    revolve('mg_can', (0, 0.88, BORE), (0, 1, 0), [(0, 0), (0, 0.020), (0.025, 0.022), (0.035, 0.037), (0.168, 0.037), (0.180, 0.031), (0.182, 0.008), (0.182, 0.0)],
            DARK, segs=24, M=M)
    for k in range(4):
        tube(f'mg_can_groove{k}', (0, 0.94 + k * 0.03, BORE), (0, 0.943 + k * 0.03, BORE), 0.0378, MECH, segs=24, M=M)
    tube('mg_can_lock', (0, 0.88, BORE), (0, 0.905, BORE), 0.024, MECH, segs=16, M=M)
    # bipod: clamp on the gas block, legs folded FORWARD under the barrel (telescoping, with feet)
    box('mg_bipod_clamp', (0, HG_Y1 + 0.045, BORE - 0.045), (0.040, 0.030, 0.020), DARK, bevel=0.003, M=M)
    for sx in (-1, 1):
        a = Vector((sx * 0.017, HG_Y1 + 0.055, BORE - 0.052)); b = Vector((sx * 0.020, HG_Y1 + 0.20, BORE - 0.050)); c = Vector((sx * 0.022, HG_Y1 + 0.30, BORE - 0.049))
        tube(f'mg_bipod_up{sx}', a, b, 0.0115, DARK, segs=10, M=M)
        tube(f'mg_bipod_lo{sx}', b - Vector((0, 0.01, 0)), c, 0.0090, MECH, segs=10, M=M)
        tube(f'mg_bipod_lock{sx}', b - Vector((0, 0.008, 0)), b + Vector((0, 0.006, 0)), 0.0095, DARK, segs=10, M=M)
        box(f'mg_bipod_foot{sx}', c + Vector((0, 0.008, -0.004)), (0.012, 0.022, 0.012), DARK, bevel=0.003, M=M)
    # vertical foregrip on the bottom rail
    box('mg_vg_clamp', (0, VG_TOP.y, 0.008), (0.030, 0.050, 0.014), DARK, bevel=0.002, M=M)
    revolve('mg_vg', tuple(VG_TOP), (0, 0, -1), [(0, 0), (0, 0.015), (0.010, 0.016), (0.120, 0.018), (0.126, 0.021), (0.130, 0.021), (0.132, 0.0)], DARK, segs=14, M=M)
    for k in range(5):
        tube(f'mg_vg_rib{k}', (0, VG_TOP.y, VG_TOP.z - 0.030 - k * 0.018), (0, VG_TOP.y, VG_TOP.z - 0.034 - k * 0.018), 0.0178, MECH, segs=14, M=M)
    # pistol grip (ergonomic profile with finger swells), trigger guard loop, trigger
    gp = [(0.012, -0.004), (-0.012, -0.004), (-0.026, -0.030), (-0.048, -0.112), (-0.040, -0.122), (-0.008, -0.118), (-0.004, -0.104),
          (-0.008, -0.090), (0.000, -0.076), (-0.004, -0.062), (0.004, -0.048), (0.008, -0.030), (0.012, -0.016)]
    plate('mg_grip', gp, 0.032, DARK, Mm @ side_at(0), bevel=0.006)
    plate('mg_guard', [(0.012, -0.004), (0.090, -0.004), (0.090, -0.014), (0.072, -0.034), (0.020, -0.034), (0.012, -0.026)], 0.010, DARK, Mm @ side_at(0), bevel=0.002)
    plate('mg_guard_hole', [(0.026, -0.008), (0.070, -0.008), (0.064, -0.028), (0.028, -0.028)], 0.0104, SCREEN, Mm @ side_at(0))
    box('mg_trigger', (0, 0.036, -0.016), (0.006, 0.006, 0.020), MECH, rot=(R(15), 0, 0), M=M)
    # skeleton stock folded forward along the right side: frame bars, buttpad, cheek riser, hinge knuckle
    SX = W / 2 + 0.016; Ss = Mm @ side_at(SX)
    tube('mg_hinge', (W / 2, REC_Y0 + 0.012, 0.030), (W / 2 + 0.03, REC_Y0 + 0.012, 0.030), 0.010, DARK, segs=10, M=M)
    tube('mg_hinge2', (W / 2, REC_Y0 + 0.012, 0.070), (W / 2 + 0.03, REC_Y0 + 0.012, 0.070), 0.010, DARK, segs=10, M=M)
    Y0 = REC_Y0 + 0.02
    plate('mg_stock_top', [(Y0, 0.082), (Y0 + 0.25, 0.082), (Y0 + 0.25, 0.100), (Y0, 0.100)], 0.024, DARK, Ss, bevel=0.003)
    plate('mg_stock_bot', [(Y0, 0.012), (Y0 + 0.07, 0.012), (Y0 + 0.24, -0.020), (Y0 + 0.24, -0.004), (Y0 + 0.07, 0.028), (Y0, 0.028)], 0.020, DARK, Ss, bevel=0.003)
    plate('mg_stock_diag', [(Y0 + 0.08, 0.082), (Y0 + 0.10, 0.082), (Y0 + 0.20, 0.004), (Y0 + 0.18, -0.002)], 0.016, DARK, Ss, bevel=0.002)
    plate('mg_stock_front', [(Y0 + 0.23, 0.100), (Y0 + 0.26, 0.100), (Y0 + 0.26, -0.026), (Y0 + 0.23, -0.020)], 0.034, DARK, Ss, bevel=0.004)
    plate('mg_buttpad', [(Y0 + 0.26, 0.098), (Y0 + 0.275, 0.098), (Y0 + 0.275, -0.026), (Y0 + 0.26, -0.026)], 0.036, SEAT, Ss, bevel=0.004)
    for k in range(5):
        box(f'mg_butt_rib{k}', (SX, Y0 + 0.2765, 0.085 - k * 0.026), (0.034, 0.003, 0.006), DARK, M=M)
    plate('mg_cheek', [(Y0 + 0.05, 0.100), (Y0 + 0.19, 0.100), (Y0 + 0.19, 0.112), (Y0 + 0.07, 0.116)], 0.026, DARK, Ss, bevel=0.003)
    # belt box (fabric pouch) under the feed with lid flap, straps, side pocket and buckle
    P = Vector((0, 0.215, -0.072))
    box('mg_pouch', P, (0.105, 0.140, 0.110), SEAT, bevel=0.012, M=M)
    box('mg_pouch_lid', P + Vector((0.004, 0, 0.056)), (0.100, 0.144, 0.010), SEAT, bevel=0.004, M=M)
    for sy in (-1, 1):
        box(f'mg_pouch_strap{sy}', P + Vector((0.0535, sy * 0.040, 0.0)), (0.004, 0.022, 0.112), DARK, M=M)
    box('mg_pouch_pocket', P + Vector((-0.0545, 0.0, -0.010)), (0.008, 0.070, 0.070), SEAT, bevel=0.004, M=M)
    box('mg_pouch_buckle', P + Vector((0.056, 0.040, 0.030)), (0.004, 0.026, 0.016), MECH, M=M)
    box('mg_pouch_rail', (0, 0.215, -0.012), (0.060, 0.140, 0.014), DARK, M=M)
    # belt: rounds in links leaving the box mouth on the left and climbing into the feed tray
    for k in range(6):
        z = -0.012 + k * 0.017; x = -W / 2 - 0.009 - 0.012 * (1 - k / 5.0) ** 2
        _bullet(f'mg_round{k}', x, 0.160, z, M)
        box(f'mg_link{k}', (x - 0.002, 0.185, z), (0.017, 0.012, 0.016), DARK, M=M)
    # flexible feed chute from the box mouth up the left side into the feed port
    sweep('mg_chute', crom([(-W / 2 - 0.024, 0.205, -0.022), (-W / 2 - 0.022, 0.205, 0.010), (-W / 2 - 0.010, 0.205, 0.040), (-W / 2 + 0.004, 0.205, 0.058)], 6),
          rect(0.008, 0.052), DARK, side=(0, 1, 0), smooth=False, M=M)
    box('mg_feedport', (-W / 2 - 0.002, 0.205, 0.058), (0.008, 0.06, 0.020), DARK, bevel=0.002, M=M)
    # support arm interface: rail clamp + U-yoke holding a ball (the arm post rises into it)
    box('mg_mount_rail', (0, MOUNT.y, -0.004), (0.030, 0.06, 0.012), DARK, bevel=0.002, M=M)
    for sx in (-1, 1):
        box(f'mg_yoke{sx}', (sx * 0.026, MOUNT.y, -0.034), (0.008, 0.040, 0.050), DARK, bevel=0.002, M=M)
    box('mg_yoke_top', (0, MOUNT.y, -0.012), (0.060, 0.040, 0.008), DARK, bevel=0.002, M=M)
    ball('mg_gimbal', tuple(MOUNT + Vector((0, 0, -0.040))), (0.0175, 0.0175, 0.0175), MECH, M=M, seg=14)
    tube('mg_yoke_pin', (-0.030, MOUNT.y, -0.040), (0.030, MOUNT.y, -0.040), 0.005, MECH, segs=8, M=M)


def _glove_wrap(tag, top, bot, side, fwd, rg, M, arm_dir, thumb='top'):
    """modelled gloved hand closed around a cylindrical grip (top->bot axis): a mitten-like palm/back block on the
    wrist side, 4 flattened two-segment fingers wrapping around the front (index longest, little 15 % shorter, 2 mm gaps),
    a knuckle ridge, the thumb, and a cuff wider than the wrist aimed along arm_dir (toward the elbow).
    Returns the wrist point (gun frame) for the arm IK."""
    top, bot = Vector(top), Vector(bot); ax = (bot - top).normalized()
    eo = Vector((side, 0, 0)); eo = (eo - ax * eo.dot(ax)).normalized()
    ef = Vector(fwd); ef = (ef - ax * ef.dot(ax) - eo * ef.dot(eo)).normalized()
    ring = lambda c, r, th: c + r * (math.cos(math.radians(th)) * eo + math.sin(math.radians(th)) * ef)
    def seg(name, p, q, h, t):
        d = (q - p); u = d.normalized(); rad = (p + q) / 2; n = (rad - (top + ax * (rad - top).dot(ax))); n = (n - u * n.dot(u)); n.normalize()
        Rb = Matrix((u, ax, n)).transposed()
        if Rb.determinant() < 0: Rb = Matrix((u, -ax, n)).transposed()
        box(name, tuple((p + q) / 2), (d.length + 0.006, h, t), SEAT, rot=tuple(Rb.to_euler()), bevel=0.0045, M=M)
    T_ = 0.016; H_ = 0.019; PITCH = 0.021
    for k in range(4):
        c = top + ax * (0.020 + k * PITCH); sc_ = 1.0 - 0.05 * k
        r1 = rg + T_ / 2
        a0, a1, a2 = -10, 75, 75 + 85 * sc_
        p0, p1, p2 = ring(c, r1 + 0.004, a0), ring(c, r1 + 0.001, a1), ring(c, r1, a2)
        seg(f'{tag}_f{k}a', p0, p1, H_, T_); seg(f'{tag}_f{k}b', p1, p2, H_ - 0.001, T_ - 0.001)
        ball(f'{tag}_k{k}', tuple(ring(c, r1 + 0.006, a0 - 8)), (0.010, 0.010, 0.010), SEAT, M=M, seg=8)
    L4 = 0.020 + 3 * PITCH + H_
    # palm / back-of-hand block on the wrist side
    cb = top + ax * (L4 / 2 - 0.006); pb_ = ring(cb, rg + 0.022, -40)
    u1 = (ring(cb, 1, -40) - cb).normalized(); u2 = u1.cross(ax).normalized()
    Rb = Matrix((u1, ax, u2)).transposed()
    if Rb.determinant() < 0: Rb = Matrix((u1, -ax, u2)).transposed()
    box(f'{tag}_back', tuple(pb_), (0.030, L4 + 0.004, 0.056), SEAT, rot=tuple(Rb.to_euler()), bevel=0.010, M=M)
    tube(f'{tag}_ridge', tuple(ring(top, rg + 0.016, -12) + ax * 0.012), tuple(ring(top, rg + 0.016, -12) + ax * (L4 - 0.004)), 0.0085, SEAT, segs=8, M=M)
    # thumb
    if thumb == 'top':        # pistol grip: over the top of the grip and down the far side
        tp = [ring(top + ax * 0.012, rg + 0.016, a) for a in (-60, -105, -150, -190)]
    else:                     # foregrip: across the front face
        tp = [ring(top + ax * 0.004, rg + 0.016, a) for a in (-40, 10, 60, 100)]
    pipe(f'{tag}_thumb', [tuple(q) for q in tp], 0.010, SEAT, n=4, segs=8, M=M)
    # cuff, wider than the wrist, aimed along the forearm
    ad = Vector(arm_dir).normalized(); wr0 = pb_ + ad * 0.035
    tube(f'{tag}_wrist', tuple(pb_), tuple(wr0), 0.026, SEAT, segs=12, M=M)
    tube(f'{tag}_cuff', tuple(wr0), tuple(wr0 + ad * 0.035), 0.031, DARK, segs=14, M=M)
    return wr0 + ad * 0.035


def build_hands(M):
    """the gunner's gloved hands, modelled closed on the pistol grip (right) and the vertical foregrip (left).
    They ride with the gun; the soldier's own hands are removed and the arm IK targets the returned cuff points."""
    wR = _glove_wrap('glove_R', GRIP_TOP, GRIP_BOT, 1, (0, 1, 0), 0.018, M, (0.30, -0.92, -0.18), 'top')
    wL = _glove_wrap('glove_L', VG_TOP + Vector((0, 0, -0.008)), VG_BOT, -1, (0, 1, 0), 0.017, M, (-0.35, -0.70, -0.60), 'front')
    return wR, wL


def link(name, a, b, w=0.024):
    """one parallelogram arm section: two bars stacked vertically 0.05 apart (18 mm), knuckle blocks at both ends,
    a diagonal gas-spring housing with its rod and a tension knob"""
    a, b = Vector(a), Vector(b); d = (b - a); u = d.normalized(); up = Vector((0, 0, 1))
    side = u.cross(up); side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0)); nrm = side.cross(u).normalized()
    for k, off in enumerate((nrm * 0.025, -nrm * 0.025)):
        tube(f'{name}_bar{k}', a + off + u * 0.025, b + off - u * 0.025, 0.009, FRAME, segs=10)
    for e_, nm in ((a, 'a'), (b, 'b')):
        box(f'{name}_knuckle_{nm}', tuple(e_ + u * (0.028 if nm == 'a' else -0.028)), (0.040, 0.040, 0.075), DARK,
            rot=tuple(Matrix((side, u, nrm)).transposed().to_euler()), bevel=0.006)
    s0 = a + u * 0.05 - nrm * 0.022; s1 = a + u * (d.length * 0.62) + nrm * 0.022
    tube(f'{name}_spring', s0, s0 + (s1 - s0).normalized() * 0.16, 0.016, DARK, segs=12)
    tube(f'{name}_rod', s0 + (s1 - s0).normalized() * 0.15, b - u * 0.05 + nrm * 0.022, 0.007, MECH, segs=8)
    tube(f'{name}_knob', s0 - (s1 - s0).normalized() * 0.012, s0 + (s1 - s0).normalized() * 0.004, 0.015, MECH, segs=12)
    return side, nrm


def hinge(name, c, h=0.06, r=0.020):
    c = Vector(c); tube(name, c - Vector((0, 0, h / 2)), c + Vector((0, 0, h / 2)), r, MECH, segs=12)
    tube(name + '_cap', c + Vector((0, 0, h / 2)), c + Vector((0, 0, h / 2 + 0.006)), r * 0.7, DARK, segs=12)


def build_arm(hip, elbow, post_bot, post_top=None):
    """harness hinge (on the hip socket block) -> elbow -> wrist hinge -> post up into the gun's U-yoke ball"""
    hip, elbow, post_bot = Vector(hip), Vector(elbow), Vector(post_bot)
    post_top = Vector(post_top) if post_top is not None else post_bot + Vector((0, 0, 0.08))
    hinge('arm_elbow', elbow, 0.10, 0.024)
    hinge('arm_wrist', post_bot, 0.06, 0.020)
    link('arm_upper', hip, elbow)
    link('arm_lower', elbow, post_bot)
    tube('arm_post', post_bot, post_top, 0.0125, MECH, segs=12)
    tube('arm_post_collar', post_top - Vector((0, 0, 0.022)), post_top - Vector((0, 0, 0.012)), 0.017, DARK, segs=12)
    # cable from the gun down the upper bar into the socket block, with p-clips
    side, nrm = (elbow - hip).normalized().cross(Vector((0, 0, 1))).normalized(), None
    pts = [post_top + Vector((0, 0, 0.01)), post_bot + Vector((0, 0, 0.02)), elbow + Vector((0, 0, 0.05)), hip + Vector((0, 0, 0.06)), hip + Vector((0, 0, 0.02))]
    pipe('arm_cable', [tuple(q) for q in pts], 0.004, DARK, n=5, segs=6)
    for k in range(1, 4):
        q = elbow + (hip - elbow) * (k / 4.0) + Vector((0, 0, 0.05))
        tube(f'arm_clip{k}', q - Vector((0, 0, 0.006)), q + Vector((0, 0, 0.006)), 0.007, MECH, segs=6)
