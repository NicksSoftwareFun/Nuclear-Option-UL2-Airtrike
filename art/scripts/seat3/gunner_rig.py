"""Seat 3 machine gunner rig for preview renders: the rigged stock soldier, the LMG with modelled gloved hands, the
harness (hip belt, back struts, shoulder yoke, straps) and the articulated support arm, posed by gun heading and
elevation the same way the plugin will drive it.

    g = Gunner(place)            # place = 4x4 world matrix of the soldier pelvis (rig origin)
    g.pose(A, E)                 # A heading deg (0 forward, + right), E elevation deg (+ up)

Heading split: the torso yaws T = clamp(20 + 0.5 A, -50, 65); the rest of the heading (A - T) swings the gun about a
point in front of the chest. Elevation tilts the gun about its grip line. Arm IK: two-bone on each arm to the glove
cuffs; harness arm: two links hip hinge -> elbow (forced outboard, rear and up) -> post under the gun's U-yoke."""
import bpy, bmesh, math, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
import ul2lib; from ul2lib import part, box, tube, pipe, ball, plate, side_at, DARK, MECH, SEAT, FRAME
import mg_model as mg

RIG_BLEND = '/mnt/project-files/ul2/art/blend/ul2_soldier_rig.blend'
G_T = Vector((0.05, 0.17, 0.34))          # gun origin in the torso frame: heads-up hold (Nick 13:01), gun forward on the arm, optic well below the eyes
H_T = Vector((0.0, 0.02, 0.33))          # swing point for the gun's yaw relative to the torso
SOCKET = Vector((0.17, 0.06, 0.15)); HINGE = Vector((0.20, 0.10, 0.19))   # waist, front-right of the belt (Nick 12:38)   # harness arm root (hips frame)
L1 = L2 = 0.26
RAIL = [Vector(q) for q in ((0.20, 0.10, 0.19), (0.15, 0.15, 0.19), (0.075, 0.18, 0.19), (0.0, 0.19, 0.19), (-0.075, 0.18, 0.19), (-0.15, 0.15, 0.19), (-0.20, 0.10, 0.19))]
POST_L = 0.15            # 2026-11-14: longer post so the links run below the gun and the hands
ARM_SCALE = (1.14, 1.06)        # upper arm, forearm length factors
WRIST_SINK = 0.040
LEAN_MAX = 24.0
GL_SHOULDER = Vector((0.150, 0.000, 0.430)); GL_CANT = 0.0   # right shoulder pocket in the torso frame   # grenadier: launcher grip in the torso frame, shouldered
POLE_R_SPOT, POLE_L_SPOT = Vector((0.45, 0.05, -0.20)), Vector((-0.45, 0.05, -0.20))
POLE_R, POLE_L = Vector((0.45, -0.20, -0.70)), Vector((-0.35, 0.25, -0.75))  # torso frame


def _rz(deg): return Matrix.Rotation(math.radians(-deg), 4, 'Z')     # + = turn right (clockwise from above)


class Gunner:
    def __init__(self, place, rifle_out=True, kind='mg'):
        self.kind = kind
        with bpy.data.libraries.load(RIG_BLEND) as (src, dst):
            dst.objects = [n for n in src.objects if n in ('UL2_soldier', 'UL2_soldier_rig')]
        for o in dst.objects: bpy.context.scene.collection.objects.link(o)
        self.so = bpy.data.objects['UL2_soldier']; self.ao = bpy.data.objects['UL2_soldier_rig']
        self.ao.matrix_world = place; self.place = place.copy()
        self._strip()
        self._lengthen_arms()
        self.pb = self.ao.pose.bones
        for n in ('spine', 'chest', 'head'): self.pb[n].rotation_mode = 'XYZ'
        # proportions (Nick 16:51 OK): the stock soldier's arms are short for shouldered and raised holds; lengthen them
        # spine twist sign: measure the chest yaw produced by +10 deg on the spine bone
        self.pb['spine'].rotation_euler = (0, math.radians(10), 0); bpy.context.view_layer.update()
        f = (self.ao.matrix_world.inverted() @ self._chest_mat()).to_3x3() @ Vector((0, 1, 0))
        self.tw_sign = 1 if math.atan2(f.x, f.y) > 0 else -1          # + yaw = toward +x (right)
        self.pb['spine'].rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
        # side lean (Nick 19:37): roll on the spine bone's Z, applied before the twist (ZYX), +lean = toward +x (right)
        self.pb['spine'].rotation_mode = 'ZYX'
        h0 = self.place.inverted() @ (self.ao.matrix_world @ self.pb['head'].head)
        self.pb['spine'].rotation_euler = (0, 0, math.radians(10)); bpy.context.view_layer.update()
        h1 = self.place.inverted() @ (self.ao.matrix_world @ self.pb['head'].head)
        self.lean_sign = 1 if (h1.x - h0.x) > 0 else -1
        self.pb['spine'].rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
        self.lean = 0.0
        # gun + gloves built once in gun space, parented to an empty
        self.gun = bpy.data.objects.new('GUN', None); bpy.context.scene.collection.objects.link(self.gun)
        part('seat3_mg'); n0 = len(ul2lib.PARTS['seat3_mg']['objs'])
        if kind == 'gl':
            import gl_model as km; km.build_gl(Matrix.Identity(4)); self.wR, self.wL = km.build_hands(Matrix.Identity(4))
        elif kind == 'spot':
            import bino_model as km; km.build_bino(Matrix.Identity(4)); self.wR, self.wL = km.build_hands(Matrix.Identity(4))
        else:
            mg.build_gun(Matrix.Identity(4)); self.wR, self.wL = mg.build_hands(Matrix.Identity(4))
        for o in ul2lib.PARTS['seat3_mg']['objs'][n0:] + (list(mg.GUN_CUTTERS) if kind == 'mg' else []):
            o.parent = self.gun
        self.gun_objs = ul2lib.PARTS['seat3_mg']['objs'][n0:]
        self._post_ik = []; self._prev_el = {}; self._prev_hand = {}
        # arm IK
        self.ik = {}; self.pole = {}
        for s in 'RL':
            e = bpy.data.objects.new(f'ik_{s}', None); bpy.context.scene.collection.objects.link(e)
            p = bpy.data.objects.new(f'pole_{s}', None); bpy.context.scene.collection.objects.link(p)
            c = self.pb[f'forearm_{s}'].constraints.new('IK'); c.target = e; c.chain_count = 2; c.pole_target = p; c.pole_angle = 0
            self.ik[s] = e; self.pole[s] = p
        self.arm_objs = []; self.harness_objs = []
        if kind == 'mg': self._harness()
        elif kind == 'spot': self._chest_kit()
        self._eye_setup()

    # ------------------------------------------------------------------ setup helpers
    def _strip(self):
        """drop the troop rifle between the knees and the soldier's own open hands (replaced by the modelled gloves)"""
        so = self.so; bm = bmesh.new(); bm.from_mesh(so.data); bm.verts.ensure_lookup_table(); seen = set(); kill = set()
        for v in bm.verts:
            if v.index in seen: continue
            st = [v]; comp = []; seen.add(v.index)
            while st:
                q = st.pop(); comp.append(q)
                for e in q.link_edges:
                    w = e.other_vert(q)
                    if w.index not in seen: seen.add(w.index); st.append(w)
            lo = [min(q.co[i] for q in comp) for i in range(3)]; hi = [max(q.co[i] for q in comp) for i in range(3)]
            if lo[0] > -0.02 and hi[0] < 0.07 and lo[1] > 0.15 and hi[2] < 0.25 and lo[2] < -0.1: kill.update(comp)
        dl = bm.verts.layers.deform.active; hidx = [so.vertex_groups[f'hand_{q}'].index for q in 'LR']
        kill.update(v for v in bm.verts if sum(v[dl].get(i, 0) for i in hidx) > 0.5)
        bmesh.ops.delete(bm, geom=list(kill), context='VERTS'); bm.to_mesh(so.data); bm.free()

    def _lengthen_arms(self):
        """proportions (Nick 16:51 OK): lengthen the upper arms and forearms in the REST pose, moving the bones and the
        skinned vertices together (weights blend the shift), so IK works on the real lengths"""
        import bpy as _b
        print('so parent', self.so.parent.name if self.so.parent else None, tuple(round(x, 3) for x in self.so.matrix_local.translation))
        arm = self.ao.data; vg = {g.name: g.index for g in self.so.vertex_groups}
        _b.context.view_layer.objects.active = self.ao; _b.ops.object.mode_set(mode='EDIT')
        eb = arm.edit_bones; shifts = {}
        for q in 'LR':
            ua, fa, hd = eb[f'upperarm_{q}'], eb[f'forearm_{q}'], eb[f'hand_{q}']
            d1 = (ua.tail - ua.head) * (ARM_SCALE[0] - 1.0); d2 = (fa.tail - fa.head) * (ARM_SCALE[1] - 1.0)
            ua.tail += d1; fa.head += d1; fa.tail += d1 + d2; hd.head += d1 + d2; hd.tail += d1 + d2
            shifts[q] = (d1.copy(), d2.copy())
        _b.ops.object.mode_set(mode='OBJECT')
        me = self.so.data
        for v in me.vertices:
            w = {g.group: g.weight for g in v.groups}
            for q in 'LR':
                d1, d2 = shifts[q]
                wf = w.get(vg.get(f'forearm_{q}', -1), 0.0); wh = w.get(vg.get(f'hand_{q}', -1), 0.0)
                if wf or wh: v.co += d1 * (wf + wh) + d2 * wh
        me.update()

    def _chest_mat(self):
        b = self.ao.data.bones['chest']; return self.ao.matrix_world @ self.pb['chest'].matrix @ b.matrix_local.inverted()

    def _snap(self, bvh, p, d, gap=0.012):
        """push a rig-space point out of the (rest) body along d until it sits gap outside the surface"""
        d = Vector(d).normalized(); hit = bvh.ray_cast(p + d * 0.4, -d, 0.8)
        return (hit[0] + d * gap) if hit[0] is not None else p

    def _harness(self):
        """rigid hip belt (hips) + back struts, shoulder yoke and straps (chest), built on the rest body and bone-parented"""
        dg = bpy.context.evaluated_depsgraph_get(); me = self.so.evaluated_get(dg).to_mesh()
        bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons]); self.so.evaluated_get(dg).to_mesh_clear()
        part('seat3_harness'); n0 = 0
        # hip belt: ring at z 0.12 around the pelvis, snapped to the body
        ring = []
        for k in range(16):
            a = 2 * math.pi * k / 16; d = Vector((math.sin(a), math.cos(a), 0))
            ring.append(self._snap(bvh, Vector((0, -0.03, 0.12)), d, 0.010))
        sweep_pts = [tuple(q) for q in ring] + [tuple(ring[0])]
        ul2lib.sweep('hb_belt', sweep_pts, ul2lib.rect(0.012, 0.06), SEAT, side=(0, 0, 1), smooth=False)
        hips_objs = list(ul2lib.PARTS['seat3_harness']['objs'])
        # socket block bolted to the belt on the right hip
        # 2026-11-14 (Nick: the support clips through the gun and the body): the arm root runs on a rail round the front
        # of the belt, hip to hip, so a gun swung to the left side no longer drags the arm across the lap
        for sx in (1, -1):
            sk = Vector((sx * SOCKET.x, SOCKET.y, SOCKET.z))
            box(f'hb_socket{sx}', tuple(sk), (0.06, 0.06, 0.07), DARK, bevel=0.008)
            for k in range(2): tube(f'hb_bolt{sx}{k}', tuple(sk + Vector((sx * 0.028, -0.015 + 0.03 * k, -0.025))), tuple(sk + Vector((sx * 0.033, -0.015 + 0.03 * k, -0.025))), 0.006, MECH, segs=8)
        for dz in (-0.022, 0.022):
            pipe(f'hb_rail{dz > 0}', [tuple(q + Vector((0, 0, dz))) for q in RAIL], 0.0075, MECH, n=6, segs=8)
        hips_objs = list(ul2lib.PARTS['seat3_harness']['objs'])
        # back struts from the belt up to a shoulder yoke, straps over the shoulders to the chest plate
        st = []
        for sx in (-1, 1):
            lo_ = self._snap(bvh, Vector((sx * 0.10, -0.05, 0.16)), (0, -1, 0), 0.012)
            hi_ = self._snap(bvh, Vector((sx * 0.12, -0.05, 0.50)), (0, -1, 0), 0.016)
            mid = self._snap(bvh, Vector((sx * 0.11, -0.05, 0.33)), (0, -1, 0), 0.016)
            pipe(f'hb_strut{sx}', [tuple(lo_), tuple(mid), tuple(hi_)], 0.0125, FRAME, n=4, segs=4)
            hit = bvh.ray_cast(Vector((sx * 0.13, -0.06, 0.40)), Vector((0, 0, 1)), 0.3)
            sh = (hit[0] + Vector((0, 0, 0.010))) if hit[0] is not None else Vector((sx * 0.13, -0.06, 0.54))
            fr = self._snap(bvh, Vector((sx * 0.11, -0.05, 0.42)), (0, 1, 0), 0.010)
            fr2 = self._snap(bvh, Vector((sx * 0.10, -0.05, 0.30)), (0, 1, 0), 0.010)
            ul2lib.sweep(f'hb_strap{sx}', ul2lib.crom([tuple(hi_), tuple(sh), tuple(fr), tuple(fr2)], 5), ul2lib.rect(0.05, 0.008), SEAT, side=(1, 0, 0), smooth=False)
            box(f'hb_strapclip{sx}', tuple(fr2), (0.056, 0.014, 0.03), MECH, bevel=0.003)
            st.append(hi_)
        tube('hb_yoke', tuple(st[0]), tuple(st[1]), 0.014, FRAME, segs=10)
        box('hb_yoke_plate', tuple((st[0] + st[1]) / 2 + Vector((0, -0.01, -0.02))), (0.14, 0.02, 0.07), DARK, bevel=0.006)
        chest_objs = [o for o in ul2lib.PARTS['seat3_harness']['objs'] if o not in hips_objs]
        for objs, bone in ((hips_objs, 'hips'), (chest_objs, 'chest')):
            for o in objs:
                mw = self.ao.matrix_world @ o.matrix_world
                o.parent = self.ao; o.parent_type = 'BONE'; o.parent_bone = bone
                bpy.context.view_layer.update()
                o.matrix_world = mw
        self.harness_objs = list(ul2lib.PARTS['seat3_harness']['objs'])

    def _chest_kit(self):
        """spotter: radio, PTT and EUD on the vest (rest pose), bone-parented to the chest"""
        import bino_model as bn
        dg = bpy.context.evaluated_depsgraph_get(); me = self.so.evaluated_get(dg).to_mesh()
        bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons]); self.so.evaluated_get(dg).to_mesh_clear()
        part('seat3_kit'); bn.build_chest(lambda p, d, gap: self._snap(bvh, p, d, gap))
        for o in ul2lib.PARTS['seat3_kit']['objs']:
            mw = self.ao.matrix_world @ o.matrix_world
            o.parent = self.ao; o.parent_type = 'BONE'; o.parent_bone = 'chest'
            bpy.context.view_layer.update(); o.matrix_world = mw
        self.harness_objs = list(ul2lib.PARTS['seat3_kit']['objs'])

    def _eye_setup(self):
        """eye point (between the eyes, on the face surface) in head-bone space, measured on the rest body"""
        dg = bpy.context.evaluated_depsgraph_get(); me = self.so.evaluated_get(dg).to_mesh()
        bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons]); self.so.evaluated_get(dg).to_mesh_clear()
        hb = self.ao.data.bones['head']; z = hb.head_local.z + 0.115
        hit = bvh.ray_cast(Vector((0.0, 0.6, z)), Vector((0, -1, 0)), 1.0)
        eye = Vector((0.0, (hit[0].y if hit[0] is not None else 0.08) + 0.010, z))
        self.eye_h = hb.matrix_local.inverted() @ eye
        self.fwd_h = (hb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0))).normalized()
        self.up_h = (hb.matrix_local.to_3x3().inverted() @ Vector((0, 0, 1))).normalized()

    def _aim_head(self, dw, clamp=85):
        """turn the head so its rest gaze points along world direction dw: the neck bone (skeleton v2) takes 40% of the
        turn, the head the rest"""
        ph = self.pb['head']; ph.rotation_mode = 'QUATERNION'; ph.rotation_quaternion = (1, 0, 0, 0)
        pn = self.pb.get('neck')
        if pn is not None:
            pn.rotation_mode = 'QUATERNION'; pn.rotation_quaternion = (1, 0, 0, 0); bpy.context.view_layer.update()
            AW = self.ao.matrix_world.to_3x3(); hb = self.ao.data.bones['head']
            g0 = (self.pb['head'].matrix.to_3x3() @ (hb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0)))).normalized()
            d = (AW.inverted() @ dw).normalized(); q = g0.rotation_difference(d)
            qa = min(q.angle, math.radians(clamp)) * 0.4
            Mn = pn.matrix.copy(); R3 = Quaternion(q.axis, qa).to_matrix() @ Mn.to_3x3(); M4 = R3.to_4x4(); M4.translation = Mn.translation; pn.matrix = M4
        bpy.context.view_layer.update()
        AW = self.ao.matrix_world.to_3x3(); hb = self.ao.data.bones['head']
        fwd_l = hb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0))
        Mh = self.pb['head'].matrix.copy(); g0 = (Mh.to_3x3() @ fwd_l).normalized()
        d = (AW.inverted() @ dw).normalized(); q = g0.rotation_difference(d)
        if q.angle > math.radians(clamp): q = Quaternion(q.axis, math.radians(clamp))
        R3 = q.to_matrix() @ Mh.to_3x3(); M4 = R3.to_4x4(); M4.translation = Mh.translation; ph.matrix = M4
        bpy.context.view_layer.update()

    def _level_head(self, dw):
        """take the roll out of the head after _aim_head: its up vector goes to world up (about the gaze line)"""
        ph = self.pb['head']; AWi = self.ao.matrix_world.to_3x3().inverted()
        H = self._head_world(); f = (H.to_3x3() @ self.fwd_h).normalized(); u = (H.to_3x3() @ self.up_h)
        u = (u - f * u.dot(f)).normalized(); w = Vector((0, 0, 1)); w = (w - f * w.dot(f))
        if w.length < 1e-4: return
        w.normalize(); ang = math.atan2(u.cross(w).dot(f), u.dot(w))
        Rw = Quaternion(f, ang).to_matrix(); Ra = AWi @ Rw @ self.ao.matrix_world.to_3x3()
        Mh = ph.matrix.copy(); M4 = (Ra @ Mh.to_3x3()).to_4x4(); M4.translation = Mh.translation; ph.matrix = M4
        bpy.context.view_layer.update()

    def _head_world(self):
        return self.ao.matrix_world @ self.pb['head'].matrix

    def _pose_kit(self, A, E, T):
        """grenadier: launcher shouldered, head on the sight line. spotter: head on the target, binoculars at the eyes."""
        TF = self._torso(T, self._lean_for(A)); dw = self.place.to_3x3() @ _rz(A).to_3x3() @ Vector((0, math.cos(math.radians(E)), math.sin(math.radians(E))))
        if self.kind == 'spot':
            # 2026-11-14 (Nick: not looking where he holds them): head turned onto the target with NO roll, binoculars on
            # exactly the same line (target direction, level), eyecups on the eyes
            self._aim_head(dw)
            self._level_head(dw)
            H = self._head_world(); eye = H @ self.eye_h
            f = dw.normalized(); u = Vector((0, 0, 1)); u = (u - f * u.dot(f)).normalized(); r = f.cross(u)
            GM = Matrix((r, f, u)).transposed().to_4x4(); GM.translation = eye + f * 0.006
        else:
            # butt in the right shoulder pocket: the launcher yaws and pitches about that point (Nick 16:50: no chest clipping)
            import gl_model as gl
            rho = A - T
            BUTT = Vector((0, gl.BUTT_Y - 0.016, 0.030))
            GM = TF @ Matrix.Translation(GL_SHOULDER) @ _rz(rho) @ Matrix.Rotation(math.radians(E), 4, 'X') @ Matrix.Rotation(math.radians(GL_CANT), 4, 'Y') @ Matrix.Translation(-BUTT)
            import gl_model as gl
            bore = (GM.to_3x3() @ Vector((0, 1, 0))).normalized()
            self._aim_head(bore, clamp=70)
            # gaze converges on the sight line 8 m out instead of running parallel beside it (Nick 18:18: head read as skewed left)
            eye = self._head_world() @ self.eye_h; aim = GM @ gl.SIGHT + bore * 8.0
            dh = (aim - eye).normalized()
            # head pitch limited (Nick 19:19): a short launcher needs no big head tilt or lean during the swap
            el_ = math.asin(max(-1.0, min(1.0, dh.z))); lim = math.radians(25.0)
            if el_ > lim:
                hz = Vector((dh.x, dh.y, 0.0)).normalized(); dh = hz * math.cos(lim) + Vector((0, 0, math.sin(lim)))
            self._aim_head(dh, clamp=70)
        self.gun.matrix_world = GM; self.GM = GM
        self.ik['R'].location = GM @ self.wR; self.ik['L'].location = GM @ self.wL
        self._shrug()
        self._sink_wrists(GM)
        self._post_ik.append(GM)
        self._settle_elbows(TF); self.arm_len = 0.0
        return {s: ((self.ao.matrix_world @ self.pb[f'hand_{s}'].head) - self.ik[s].location).length for s in 'RL'}

    def _torso(self, T, lean):
        """twist T and side lean on the spine bone; returns the torso frame (pelvis origin, yawed, leaned about the spine base)"""
        self.pb['spine'].rotation_euler = (0, math.radians(T) * self.tw_sign, math.radians(lean) * self.lean_sign)
        bpy.context.view_layer.update(); self.lean = lean
        piv = self.ao.data.bones['spine'].head_local
        return self.place @ _rz(T) @ Matrix.Translation(piv) @ Matrix.Rotation(math.radians(lean), 4, 'Y') @ Matrix.Translation(-piv)

    def _lean_for(self, A):
        """lean toward the firing side for forward shots so the gun clears the seat 2 crewman; none overhead or to the rear"""
        a = abs(A); s_ = 1 if A > 0 else -1
        f = min(1.0, max(0.0, (a - 8.0) / 22.0)) * min(1.0, max(0.0, (105.0 - a) / 45.0))
        return s_ * LEAN_MAX * f

    def _shrug(self):
        """collarbones (skeleton v2): each shoulder rolls a little toward its hand target (raise / reach forward), a quarter
        of the way, at most 20 deg, so raised and forward holds do not have to come from the arm alone"""
        AW = self.ao.matrix_world; AWi = AW.inverted()
        for s in 'RL':
            pb = self.pb.get(f'clavicle_{s}')
            if pb is None: return
            pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0); bpy.context.view_layer.update()
            h = AW @ pb.head; t = AW @ pb.tail; d0 = (t - h).normalized(); dt = (self.ik[s].location - h).normalized()
            q = d0.rotation_difference(dt); ang = min(q.angle * 0.25, math.radians(20.0))
            if ang < 1e-4: continue
            Rw = Quaternion(q.axis, ang).to_matrix()
            Ra = AWi.to_3x3() @ Rw @ AW.to_3x3()
            M = pb.matrix.copy(); hd = M.translation.copy(); R3 = Ra @ M.to_3x3(); M4 = R3.to_4x4(); M4.translation = hd; pb.matrix = M4
        bpy.context.view_layer.update()

    def _sink_wrists(self, GM):
        """wrist sink (Nick 18:13): the sleeve ends ~3 cm short of the wrist joint (the soldier's own hands are stripped),
        so after a first solve the wrist target is pushed past the glove cuff along the forearm until the sleeve meets it"""
        bpy.context.view_layer.update()
        for s in 'RL':
            el = self.ao.matrix_world @ self.pb[f'forearm_{s}'].head; wr = self.ao.matrix_world @ self.pb[f'hand_{s}'].head
            d = (wr - el).normalized()
            self.ik[s].location = GM @ (self.wR if s == 'R' else self.wL) + d * WRIST_SINK
        bpy.context.view_layer.update()

    def _body_bvh(self):
        """posed body surface without the arms and hands (torso, legs, head, kit), world space, for elbow clearance"""
        if getattr(self, '_bb_key', None) == round(self.T, 3): return self._bb
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get(); ev = self.so.evaluated_get(dg); me = ev.to_mesh(); W = self.so.matrix_world
        gi = {self.so.vertex_groups[f'{b}_{q}'].index for b in ('upperarm', 'forearm', 'hand') for q in 'LR' if f'{b}_{q}' in self.so.vertex_groups}
        src = self.so.data.vertices
        arm = [sum(g.weight for g in src[i].groups if g.group in gi) > 0.3 for i in range(len(src))]
        polys = [list(p.vertices) for p in me.polygons if not any(arm[v] for v in p.vertices)]
        self._bb = BVHTree.FromPolygons([W @ v.co for v in me.vertices], polys); ev.to_mesh_clear()
        self._bb_key = round(self.T, 3); return self._bb

    def _settle_elbows(self, TF):
        """elbow constraint (Nick 16:51): for each arm try a set of pole targets and keep the one that reaches the hand
        target with the elbow lowest, so the elbow stays below the shoulder"""
        out = {}
        for s, sx in (('R', 1), ('L', -1)):
            cands = [(sx * 0.45, 0.05, -0.60), (sx * 0.35, -0.25, -0.70), (sx * 0.60, 0.20, -0.40), (sx * 0.15, 0.30, -0.70),
                     (sx * 0.55, -0.10, -0.20), (sx * 0.25, 0.10, -0.90), (-sx * 0.10, 0.25, -0.70), (sx * 0.45, -0.20, -0.70),
                     (sx * 0.45, 0.05, 0.90), (-sx * 0.30, 0.05, 0.90), (sx * 0.10, -0.40, 0.60), (-sx * 0.45, 0.05, -0.60), (sx * 0.0, -0.50, -0.30),
                     (sx * 0.80, 0.00, -0.30), (sx * 0.80, -0.30, -0.40), (-sx * 0.80, 0.00, -0.30), (-sx * 0.80, -0.30, -0.40),
                     (sx * 0.70, 0.30, -0.30), (-sx * 0.70, 0.30, -0.30), (sx * 0.80, 0.00, 0.60), (-sx * 0.80, 0.00, 0.60)]
            body = self._body_bvh()
            best = None
            for c in cands:
                self.pole[s].location = TF @ Vector(c); bpy.context.view_layer.update()
                sh = self.ao.matrix_world @ self.pb[f'upperarm_{s}'].head; el = self.ao.matrix_world @ self.pb[f'forearm_{s}'].head
                miss = ((self.ao.matrix_world @ self.pb[f'hand_{s}'].head) - self.ik[s].location).length
                # body clearance (Nick 17:52): the elbow must sit outside the torso. In the torso frame the chest is
                # about +-0.19 wide and its front about 0.13 forward; inside that box the penalty grows with depth
                loc, nrm, _i, dist = body.find_nearest(el)
                sd = (el - loc).dot(nrm)                  # signed distance to the posed body (arms excluded), <0 = inside
                inside = max(0.0, 0.05 - sd) if sd < 0.05 else 0.0
                drop = sh.z - el.z
                score = 3.0 * max(0.0, 0.06 - drop) - 0.15 * drop + 4.0 * miss + 8.0 * inside + (0.5 if inside > 0.01 else 0.0)
                # frame-to-frame continuity (Nick 19:19: joints flipping during the swap): penalise elbow jumps
                pe = self._prev_el.get(s)
                if pe is not None:
                    hm = (self.ik[s].location - self._prev_hand[s]).length if s in self._prev_hand else 0.0
                    score += 25.0 * max(0.0, (el - pe).length - 0.02 - 1.2 * hm)
                if best is None or score < best[0]: best = (score, c, miss, el.z - sh.z, inside)
            self.pole[s].location = TF @ Vector(best[1]); out[s] = best
            bpy.context.view_layer.update(); self._prev_el[s] = self.ao.matrix_world @ self.pb[f'forearm_{s}'].head
            self._prev_hand[s] = self.ik[s].location.copy()
        bpy.context.view_layer.update()
        self.elbow_drop = {s: round(-out[s][3], 3) for s in out}; self.elbow_inside = {s: round(out[s][4], 3) for s in out}
        if self._post_ik: self._wrist_bridge(self._post_ik.pop()); self._post_ik.clear()
        return out

    def _neck_gaiter(self):
        """neck gaiter (Nick 19:22): the stock soldier's thin neck was never meant to turn, so a thick fabric gaiter runs
        from inside the collar (chest frame) up under the helmet (head frame), bending with the head, rebuilt each pose"""
        import bmesh as _bm
        if not hasattr(self, 'neck_obj'):
            me = bpy.data.meshes.new('neck_gaiter'); self.neck_obj = bpy.data.objects.new('neck_gaiter', me); bpy.context.scene.collection.objects.link(self.neck_obj)
            me.materials.append(bpy.data.materials['wrist_gaiter'])
        AW = self.ao.matrix_world; hb = self.ao.data.bones['head']; cb = self.ao.data.bones['chest']
        Hm = AW @ self.pb['head'].matrix; Cm = AW @ self.pb['chest'].matrix
        # rest-space anchors: collar ring centre just below the head joint, top ring under the helmet rim
        base_r = hb.head_local + Vector((0, 0.010, -0.045)); top_r = hb.head_local + Vector((0, 0.012, 0.070))
        b = Cm @ (cb.matrix_local.inverted() @ base_r); t = Hm @ (hb.matrix_local.inverted() @ top_r)
        zb = (Cm.to_3x3() @ (cb.matrix_local.to_3x3().inverted() @ Vector((0, 0, 1)))).normalized()
        zt = (Hm.to_3x3() @ (hb.matrix_local.to_3x3().inverted() @ Vector((0, 0, 1)))).normalized()
        yb = (Cm.to_3x3() @ (cb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0)))).normalized()
        yt = (Hm.to_3x3() @ (hb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0)))).normalized()
        bm = _bm.new(); N = 16; rings = []; L = (t - b).length
        for i, (f, rx, ry) in enumerate(((0.0, 0.066, 0.060), (0.33, 0.062, 0.058), (0.66, 0.058, 0.056), (1.0, 0.055, 0.054))):
            c = b.lerp(t, f) + (zb * (1 - f) + zt * f) * 0.0
            z = (zb * (1 - f) + zt * f).normalized(); y = (yb * (1 - f) + yt * f); y = (y - z * y.dot(z)).normalized(); x = y.cross(z)
            rings.append([bm.verts.new(c + x * math.cos(2 * math.pi * k / N) * rx + y * math.sin(2 * math.pi * k / N) * ry) for k in range(N)])
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(N): bm.faces.new((r0[k], r0[(k + 1) % N], r1[(k + 1) % N], r1[k]))
        bm.to_mesh(self.neck_obj.data); bm.free()
        for pg in self.neck_obj.data.polygons: pg.use_smooth = True

    def _wrist_bridge(self, GM):
        """wrist gaiter (Nick 18:13): a tapered sleeve from inside the cut sleeve end to the glove cuff, rebuilt each pose,
        so the glove always reads as attached to the forearm whatever the angle between the cuff and the forearm"""
        import bmesh as _bm
        if not hasattr(self, 'wrist_objs'):
            mat = bpy.data.materials.new('wrist_gaiter'); mat.use_nodes = True
            mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.048, 0.052, 0.034, 1); mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
            self.wrist_objs = []
            for s in 'RL':
                me = bpy.data.meshes.new(f'wrist_{s}'); o = bpy.data.objects.new(f'wrist_{s}', me); bpy.context.scene.collection.objects.link(o)
                me.materials.append(mat); self.wrist_objs.append(o)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get(); ev = self.so.evaluated_get(dg); me = ev.to_mesh(); W = self.so.matrix_world
        src = self.so.data.vertices
        self._neck_gaiter()
        for o, s in zip(self.wrist_objs, 'RL'):
            el = self.ao.matrix_world @ self.pb[f'forearm_{s}'].head; wr = self.ao.matrix_world @ self.pb[f'hand_{s}'].head
            d = (wr - el).normalized()
            fi = self.so.vertex_groups[f'forearm_{s}'].index
            fv = [W @ me.vertices[i].co for i in range(len(src)) if any(g.group == fi and g.weight > 0.5 for g in src[i].groups)]
            pm = max((v - el).dot(d) for v in fv); ring = [v for v in fv if (v - el).dot(d) > pm - 0.02]
            se = sum(ring, Vector()) / len(ring)                                      # sleeve end centre on the posed mesh
            a = se - d * 0.025; b = GM @ (self.wR if s == 'R' else self.wL)          # 2.5 cm inside the sleeve -> cuff end
            ax = (b - a); L = ax.length
            if L < 1e-4: continue
            z = ax.normalized(); x = z.orthogonal().normalized(); y = z.cross(x)
            bm = _bm.new(); N = 12; rings = []
            for t, r in ((0.0, 0.030), (0.5, 0.029), (1.0, 0.027)):
                c = a + ax * t
                rings.append([bm.verts.new(c + (x * math.cos(2 * math.pi * k / N) + y * math.sin(2 * math.pi * k / N)) * r) for k in range(N)])
            for r0, r1 in zip(rings, rings[1:]):
                for k in range(N): bm.faces.new((r0[k], r0[(k + 1) % N], r1[(k + 1) % N], r1[k]))
            # 0.15.53 (review: you could see into the hollow sleeve from the front): a closing disc across the cut sleeve end
            rr = sum(((v - se) - d * (v - se).dot(d)).length for v in ring) / len(ring) * 0.97
            cc = se - d * 0.004; cr = [bm.verts.new(cc + (x * math.cos(2 * math.pi * k / N) + y * math.sin(2 * math.pi * k / N)) * rr) for k in range(N)]
            ctr = bm.verts.new(cc)
            for k in range(N): bm.faces.new((ctr, cr[k], cr[(k + 1) % N]))
            bm.to_mesh(o.data); bm.free(); o.data.update()
            for pg in o.data.polygons: pg.use_smooth = True
        ev.to_mesh_clear()

    # ------------------------------------------------------------------ pose
    def pose(self, A, E, T=None, head=True):
        if T is None:
            T = max(-80.0, min(98.0, A + 22.0)) if self.kind == 'gl' else (max(-50.0, min(65.0, A)) if self.kind == 'spot' else max(-50.0, min(65.0, 20.0 + 0.5 * A)))   # spotter squares up to the target; grenadier: bladed, left shoulder forward
        self.A, self.E, self.T = A, E, T
        if self.kind != 'mg': return self._pose_kit(A, E, T)
        TF = self._torso(T, self._lean_for(A))                         # torso frame (pelvis origin, yawed, leaned)
        rho = A - T
        lift = max(0.0, E) / 60.0
        GMt = Matrix.Translation(Vector((0, -0.11 * lift, 0.20 * lift))) @ Matrix.Translation(H_T) @ _rz(rho) @ Matrix.Translation(G_T - H_T)
        PIV = Vector((0, 0.16, 0.0))                                   # elevation axis through the grip line
        GM = TF @ GMt @ Matrix.Translation(PIV) @ Matrix.Rotation(math.radians(E), 4, 'X') @ Matrix.Translation(-PIV)
        self.gun.matrix_world = GM; self.GM = GM
        self.ik['R'].location = GM @ self.wR; self.ik['L'].location = GM @ self.wL
        self._shrug()
        self._sink_wrists(GM)
        self._post_ik.append(GM)
        self.pole['R'].location = TF @ POLE_R; self.pole['L'].location = TF @ POLE_L
        self._settle_elbows(TF)
        if head:
            bpy.context.view_layer.update()
            hb = self.ao.data.bones['head']; ph = self.pb['head']; ph.rotation_mode = 'QUATERNION'; ph.rotation_quaternion = (1, 0, 0, 0)
            bpy.context.view_layer.update()
            AW = self.ao.matrix_world.to_3x3()
            fwd_l = hb.matrix_local.to_3x3().inverted() @ Vector((0, 1, 0))           # rest gaze in head-bone space
            Mh = self.pb['head'].matrix.copy()                                           # armature space, chest-posed
            g0 = (Mh.to_3x3() @ fwd_l).normalized()
            hw = self.ao.matrix_world @ Mh.translation + AW @ (Mh.to_3x3() @ Vector((0, 0.10, 0)))   # roughly the eyes
            # heads-up: the head turns with the heading and dips a little for low aim, never looks up (Nick 13:01)
            hp = math.radians(max(-25.0, min(0.0, 0.8 * E)))
            dw = self.place.to_3x3() @ _rz(A).to_3x3() @ Vector((0, math.cos(hp), math.sin(hp)))
            d = (AW.inverted() @ dw).normalized()
            q = g0.rotation_difference(d)
            if q.angle > math.radians(85): q = Quaternion(q.axis, math.radians(85))
            R3 = q.to_matrix() @ Mh.to_3x3(); M4 = R3.to_4x4(); M4.translation = Mh.translation; ph.matrix = M4
        bpy.context.view_layer.update()
        self._arm()
        bpy.context.view_layer.update()
        miss = {s: ((self.ao.matrix_world @ self.pb[f'hand_{s}'].head) - self.ik[s].location).length for s in 'RL'}
        return miss

    def _arm(self):
        for o in self.arm_objs: bpy.data.objects.remove(o)
        part('seat3_arm'); ul2lib.PARTS['seat3_arm']['objs'] = []
        P = self.place
        hl = self._hinge_local(self.A); hinge = P @ hl
        hr = P.to_3x3() @ Vector((0, 0, 1))
        tube('arm_carriage', tuple(hinge - hr * 0.035), tuple(hinge + hr * 0.035), 0.024, DARK, segs=14)
        tube('arm_hinge', tuple(hinge - hr * 0.045), tuple(hinge + hr * 0.045), 0.016, MECH, segs=12)
        post_top = self.GM @ (mg.MOUNT + Vector((0, 0, -0.028)))
        post_bot = post_top - (self.GM.to_3x3() @ Vector((0, 0, 1))).normalized() * POST_L
        d = post_bot - hinge; L = d.length; L_ = min(L, L1 + L2 - 0.005); self.arm_len = L
        h = math.sqrt(max(0.0, L1 * L1 - (L_ / 2) ** 2))
        # elbow forced outboard (away from the body centre), to the rear and up
        cen = P @ Vector((0, -0.03, hinge.z - P.translation.z))
        out = (hinge + post_bot) / 2 - cen; out.z = 0
        n = (1.6 * out.normalized() + (P.to_3x3() @ Vector((0, -0.3, 0))) + Vector((0, 0, 0.8)))
        n = (n - d.normalized() * n.dot(d.normalized())).normalized()
        # 2026-11-14 (Nick: the support clips through the gun and the body): the elbow may swing anywhere round the
        # hinge-post line; keep the swing that leaves the links furthest from the posed soldier and the gun, staying near
        # the old outboard/rear/up preference and the previous pose's elbow so neighbouring poses blend smoothly
        du = d.normalized(); bvhs = self._clear_bvhs()
        def clear(e):
            m = 9.0
            for a_, b_, s0, s1 in ((hinge, e, 0.25, 1.0), (e, post_bot, 0.0, 0.65)):
                for k in range(9):
                    t = s0 + (s1 - s0) * k / 8.0; q = a_ + (b_ - a_) * t
                    for bv in bvhs:
                        r = bv.find_nearest(q)
                        if r[0] is not None: m = min(m, r[3])
            return m
        prev = getattr(self, '_prev_arm_el', None); best = None
        for k in range(24):
            nn = Quaternion(du, math.radians(15 * k)) @ n
            e = hinge + du * (L_ / 2) + nn * h
            c = clear(e); score = min(c, 0.09) - 0.010 * (1 - nn.dot(n)) - (0.08 * (e - prev).length if prev is not None else 0.0)
            if best is None or score > best[0]: best = (score, e, c)
        elbow = best[1]; self._prev_arm_el = elbow.copy(); self.arm_clear = best[2]
        mg.build_arm(hinge, elbow, post_bot, post_top)
        self.arm_objs = list(ul2lib.PARTS['seat3_arm']['objs'])

    def _hinge_local(self, A):
        """arm root on the belt rail: right hip for right-side aim, left hip for left-side aim, sliding across the front
        through the forward zone (A from +10 to -40 deg)"""
        f = max(0.0, min(1.0, (10.0 - A) / 50.0)); f = f * f * (3 - 2 * f)
        t = f * (len(RAIL) - 1); i = min(int(t), len(RAIL) - 2); u = t - i
        return RAIL[i].lerp(RAIL[i + 1], u)

    def _clear_bvhs(self):
        """posed soldier (whole body) and the gun, world space, for the support arm clearance"""
        bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get(); out = []
        ev = self.so.evaluated_get(dg); me = ev.to_mesh(); W = self.so.matrix_world
        out.append(BVHTree.FromPolygons([W @ v.co for v in me.vertices], [list(p.vertices) for p in me.polygons])); ev.to_mesh_clear()
        V, F = [], []
        for o in self.gun_objs:
            if o.type != 'MESH': continue
            W = o.matrix_world; b0 = len(V); V += [W @ v.co for v in o.data.vertices]; F += [[b0 + i for i in p.vertices] for p in o.data.polygons]
        # the yoke and the post top really do meet the gun: leave out gun parts within 6 cm of the mount
        mnt = self.GM @ mg.MOUNT
        F = [f for f in F if min((V[i] - mnt).length for i in f) > 0.06]
        if F: out.append(BVHTree.FromPolygons(V, F))
        return out

    def clearance(self, other):
        """min distance from the gun objects (world) to another mesh object (e.g. the seat 2 crew)"""
        dg = bpy.context.evaluated_depsgraph_get()
        def bvh(o):
            me = o.evaluated_get(dg).to_mesh(); M = o.matrix_world
            t = BVHTree.FromPolygons([M @ v.co for v in me.vertices], [list(p.vertices) for p in me.polygons]); o.evaluated_get(dg).to_mesh_clear(); return t
        T = bvh(other); best = 9.0
        for o in self.gun_objs:
            me = o.evaluated_get(dg).to_mesh(); M = o.matrix_world
            for v in list(me.vertices)[::3]:
                r = T.find_nearest(M @ v.co)
                if r[3] is not None and r[3] < best: best = r[3]
            o.evaluated_get(dg).to_mesh_clear()
        return best
