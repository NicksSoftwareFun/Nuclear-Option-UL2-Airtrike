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
POLE_R, POLE_L = Vector((0.45, -0.20, -0.70)), Vector((-0.35, 0.25, -0.75))  # torso frame


def _rz(deg): return Matrix.Rotation(math.radians(-deg), 4, 'Z')     # + = turn right (clockwise from above)


class Gunner:
    def __init__(self, place, rifle_out=True):
        with bpy.data.libraries.load(RIG_BLEND) as (src, dst):
            dst.objects = [n for n in src.objects if n in ('UL2_soldier', 'UL2_soldier_rig')]
        for o in dst.objects: bpy.context.scene.collection.objects.link(o)
        self.so = bpy.data.objects['UL2_soldier']; self.ao = bpy.data.objects['UL2_soldier_rig']
        self.ao.matrix_world = place; self.place = place.copy()
        self._strip()
        self.pb = self.ao.pose.bones
        for n in ('spine', 'chest', 'head'): self.pb[n].rotation_mode = 'XYZ'
        # spine twist sign: measure the chest yaw produced by +10 deg on the spine bone
        self.pb['spine'].rotation_euler = (0, math.radians(10), 0); bpy.context.view_layer.update()
        f = (self.ao.matrix_world.inverted() @ self._chest_mat()).to_3x3() @ Vector((0, 1, 0))
        self.tw_sign = 1 if math.atan2(f.x, f.y) > 0 else -1          # + yaw = toward +x (right)
        self.pb['spine'].rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
        # gun + gloves built once in gun space, parented to an empty
        self.gun = bpy.data.objects.new('GUN', None); bpy.context.scene.collection.objects.link(self.gun)
        part('seat3_mg'); n0 = len(ul2lib.PARTS['seat3_mg']['objs'])
        mg.build_gun(Matrix.Identity(4)); self.wR, self.wL = mg.build_hands(Matrix.Identity(4))
        for o in ul2lib.PARTS['seat3_mg']['objs'][n0:] + list(mg.GUN_CUTTERS):
            o.parent = self.gun
        self.gun_objs = ul2lib.PARTS['seat3_mg']['objs'][n0:]
        # arm IK
        self.ik = {}; self.pole = {}
        for s in 'RL':
            e = bpy.data.objects.new(f'ik_{s}', None); bpy.context.scene.collection.objects.link(e)
            p = bpy.data.objects.new(f'pole_{s}', None); bpy.context.scene.collection.objects.link(p)
            c = self.pb[f'forearm_{s}'].constraints.new('IK'); c.target = e; c.chain_count = 2; c.pole_target = p; c.pole_angle = 0
            self.ik[s] = e; self.pole[s] = p
        self._harness()
        self.arm_objs = []

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
        box('hb_socket', tuple(SOCKET), (0.08, 0.07, 0.08), DARK, bevel=0.008)
        for k in range(2): tube(f'hb_bolt{k}', tuple(SOCKET + Vector((0.035, -0.015 + 0.03 * k, -0.03))), tuple(SOCKET + Vector((0.040, -0.015 + 0.03 * k, -0.03))), 0.006, MECH, segs=8)
        tube('hb_hinge', tuple(HINGE - Vector((0, 0, 0.04))), tuple(HINGE + Vector((0, 0, 0.04))), 0.022, MECH, segs=14)
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

    # ------------------------------------------------------------------ pose
    def pose(self, A, E, T=None, head=True):
        if T is None: T = max(-50.0, min(65.0, 20.0 + 0.5 * A))
        self.A, self.E, self.T = A, E, T
        self.pb['spine'].rotation_euler = (0, math.radians(T) * self.tw_sign, 0)
        bpy.context.view_layer.update()
        TF = self.place @ _rz(T)                                       # torso frame (pelvis origin, yawed)
        rho = A - T
        lift = max(0.0, E) / 60.0
        GMt = Matrix.Translation(Vector((0, -0.11 * lift, 0.20 * lift))) @ Matrix.Translation(H_T) @ _rz(rho) @ Matrix.Translation(G_T - H_T)
        PIV = Vector((0, 0.16, 0.0))                                   # elevation axis through the grip line
        GM = TF @ GMt @ Matrix.Translation(PIV) @ Matrix.Rotation(math.radians(E), 4, 'X') @ Matrix.Translation(-PIV)
        self.gun.matrix_world = GM; self.GM = GM
        self.ik['R'].location = GM @ self.wR; self.ik['L'].location = GM @ self.wL
        self.pole['R'].location = TF @ POLE_R; self.pole['L'].location = TF @ POLE_L
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
        hinge = P @ HINGE
        post_top = self.GM @ (mg.MOUNT + Vector((0, 0, -0.028)))
        post_bot = post_top - (self.GM.to_3x3() @ Vector((0, 0, 1))).normalized() * 0.08
        d = post_bot - hinge; L = d.length; L_ = min(L, L1 + L2 - 0.005); self.arm_len = L
        h = math.sqrt(max(0.0, L1 * L1 - (L_ / 2) ** 2))
        # elbow forced outboard (away from the body centre), to the rear and up
        cen = P @ Vector((0, -0.03, hinge.z - P.translation.z))
        out = (hinge + post_bot) / 2 - cen; out.z = 0
        n = (1.6 * out.normalized() + (P.to_3x3() @ Vector((0, -0.3, 0))) + Vector((0, 0, 0.8)))
        n = (n - d.normalized() * n.dot(d.normalized())).normalized()
        elbow = hinge + d.normalized() * (L_ / 2) + n * h
        mg.build_arm(hinge, elbow, post_bot, post_top)
        self.arm_objs = list(ul2lib.PARTS['seat3_arm']['objs'])

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
