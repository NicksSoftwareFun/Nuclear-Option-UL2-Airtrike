"""UL-2 Airtrike modelling library (headless bpy). Nose +Y, up +Z, right +X, metres, ground z=0.
Every helper adds geometry to the CURRENT game part. join_parts() merges each part into one object
named like the game part, with its pivot, parent and material slots."""
import bpy, bmesh, math
from mathutils import Vector, Matrix
R = math.radians

# ---------------------------------------------------------------- materials (grey until texture passes)
MATS = {}
SLOT_GREY = {'UL2_enginternal': 0.30, 'UL2_skin': 0.52, 'UL2_frame': 0.62, 'UL2_mech': 0.40, 'UL2_dark': 0.16, 'UL2_tyre': 0.10,
             'UL2_seat': 0.30, 'UL2_cockpit': 0.22, 'UL2_glass': None, 'UL2_screen': 0.06, 'UL2_crewref': 0.75}
# FDE weapon scheme (same colours as the seat 3 guns, look.py): sRGB values
SLOT_RGB = {'UL2_white': (232, 234, 232), 'UL2_propblur': (40, 42, 45), 'UL2_red': (200, 36, 28), 'UL2_rotorblur': (40, 42, 45), 'UL2_fde': (176, 150, 110), 'UL2_bronze': (146, 108, 80), 'UL2_black': (34, 34, 33), 'UL2_fabric': (166, 134, 94)}
def mat(name):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    if name in SLOT_RGB:
        b.inputs['Base Color'].default_value = tuple((c / 255) ** 2.2 for c in SLOT_RGB[name]) + (1,); b.inputs['Roughness'].default_value = 0.6
        MATS[name] = m; return m
    g = SLOT_GREY[name]
    if g is None:
        b.inputs['Base Color'].default_value = (0.85, 0.9, 0.95, 1); b.inputs['Roughness'].default_value = 0.02
        b.inputs['Alpha'].default_value = 0.18
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    else:
        b.inputs['Base Color'].default_value = (g, g, g, 1); b.inputs['Roughness'].default_value = 0.55
    MATS[name] = m; return m
SKIN, FRAME, MECH, DARK, TYRE, SEAT, CPIT, GLASS, SCREEN = ('UL2_skin', 'UL2_frame', 'UL2_mech', 'UL2_dark', 'UL2_tyre',
                                                             'UL2_seat', 'UL2_cockpit', 'UL2_glass', 'UL2_screen')
FDE_, BRONZE_, BLACK_, FABRIC_ = 'UL2_fde', 'UL2_bronze', 'UL2_black', 'UL2_fabric'

# ---------------------------------------------------------------- parts
PARTS = {}          # name -> dict(parent, pivot, objs)
CUR = [None]
def part(name, parent=None, pivot=(0, 0, 0)):
    if name not in PARTS: PARTS[name] = dict(parent=parent, pivot=Vector(pivot), objs=[])
    CUR[0] = name
def coll(name):
    c = bpy.data.collections.get(name)
    if not c: c = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c

def finish(name, bm, m, smooth=True, bevel=0.0, auto=True):
    me = bpy.data.meshes.new(name); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); me.materials.append(mat(m)); coll('WORK').objects.link(o)
    if bevel:
        b = o.modifiers.new('bevel', 'BEVEL'); b.width = bevel; b.segments = 2; b.limit_method = 'ANGLE'; b.angle_limit = R(40)
    if smooth and auto:
        try: o.modifiers.new('ws', 'WEIGHTED_NORMAL'); me.set_sharp_from_angle(angle=R(40)) if hasattr(me, 'set_sharp_from_angle') else None
        except Exception: pass
    PARTS[CUR[0]]['objs'].append(o); return o

def rotm(rot): return Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')

def box(name, c, size, m, rot=(0, 0, 0), bevel=0.0, M=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
    T = Matrix.Translation(c) @ rotm(rot) @ Matrix.Diagonal((*size, 1))
    if M is not None: T = M @ T
    bmesh.ops.transform(bm, matrix=T, verts=bm.verts)
    return finish(name, bm, m, False, bevel)

def ball(name, c, radii, m, rot=(0, 0, 0), M=None, seg=16):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(6, seg // 2), radius=1)
    T = Matrix.Translation(c) @ rotm(rot) @ Matrix.Diagonal((*radii, 1))
    if M is not None: T = M @ T
    bmesh.ops.transform(bm, matrix=T, verts=bm.verts)
    return finish(name, bm, m)

def basis(axis):
    u = Vector(axis).normalized(); ref = Vector((0, 0, 1)) if abs(u.z) < 0.9 else Vector((1, 0, 0))
    v = u.cross(ref).normalized(); w = u.cross(v); return u, v, w

def revolve(name, origin, axis, prof, m, segs=16, smooth=True, M=None, cap=True):
    """prof = [(distance along axis, radius)]"""
    u, v, w = basis(axis); o = Vector(origin); bm = bmesh.new(); rings = []
    for a, r in prof:
        if r < 1e-5: rings.append([bm.verts.new(o + u * a)]); continue
        rings.append([bm.verts.new(o + u * a + (v * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r) for k in range(segs)])
    for i in range(len(rings) - 1):
        A, B = rings[i], rings[i + 1]
        for k in range(segs):
            j = (k + 1) % segs
            if len(A) == 1 and len(B) == 1: continue
            if len(A) == 1: bm.faces.new((A[0], B[k], B[j]))
            elif len(B) == 1: bm.faces.new((A[k], B[0], A[j]))
            else: bm.faces.new((A[k], B[k], B[j], A[j]))
    if cap:
        for ring in (rings[0], rings[-1]):
            if len(ring) > 1: bm.faces.new(ring)
    if M is not None: bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return finish(name, bm, m, smooth)

def tube(name, p0, p1, r, m, segs=10, r1=None, M=None):
    p0, p1 = Vector(p0), Vector(p1)
    return revolve(name, p0, p1 - p0, [(0, r), ((p1 - p0).length, r if r1 is None else r1)], m, segs, M=M)

def crom(pts, n=8):
    P = [Vector(p) for p in pts]; P = [P[0]] + P + [P[-1]]; out = []
    for i in range(1, len(P) - 2):
        for k in range(n):
            t = k / n; t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * P[i]) + (-P[i - 1] + P[i + 1]) * t + (2 * P[i - 1] - 5 * P[i] + 4 * P[i + 1] - P[i + 2]) * t2 + (-P[i - 1] + 3 * P[i] - 3 * P[i + 1] + P[i + 2]) * t3))
    out.append(P[-2]); return out

def sweep(name, pts, sec, m, side=(1, 0, 0), smooth=True, closed=False, M=None, scale=None):
    P = [Vector(p) for p in pts]; N = len(P); bm = bmesh.new(); rings = []
    for i in range(N):
        a = P[(i - 1) % N] if (closed or i > 0) else P[i]; b = P[(i + 1) % N] if (closed or i < N - 1) else P[i]
        t = (b - a).normalized(); s = Vector(side); s = (s - t * s.dot(t)).normalized(); n = t.cross(s)
        k = scale[i] if scale else 1.0
        rings.append([bm.verts.new(P[i] + s * x * k + n * y * k) for x, y in sec])
    L = len(sec)
    for i in range(N if closed else N - 1):
        A, B = rings[i], rings[(i + 1) % N]
        for k in range(L): bm.faces.new((A[k], A[(k + 1) % L], B[(k + 1) % L], B[k]))
    if not closed: bm.faces.new(rings[0]); bm.faces.new(rings[-1][::-1])
    if M is not None: bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return finish(name, bm, m, smooth)

def circ(r, n=10): return [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n)) for k in range(n)]
def rect(w, h): return [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
def rrect(w, h, r, n=3):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(n + 1):
            a = R(a0 + 90 * k / n); pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts
def pipe(name, pts, r, m, n=6, side=(1, 0, 0), segs=10, M=None): return sweep(name, crom(pts, n), circ(r, segs), m, side, M=M)

def naca(t, n=7):
    xs = [(0.5 * (1 - math.cos(math.pi * k / n))) for k in range(n + 1)]
    yt = lambda x: 5 * t * (0.2969 * math.sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4)
    up = [(x, yt(x)) for x in xs]; lo = [(x, -yt(x)) for x in xs[-2:0:-1]]
    return up + lo

def wing(name, stations, m, t=0.12, n=7, M=None):
    """stations = [(LE point, chord vector, thickness unit vector)]"""
    prof = naca(t, n); bm = bmesh.new(); rings = []
    for le, cv, nv in stations:
        le, cv, nv = Vector(le), Vector(cv), Vector(nv); c = cv.length
        rings.append([bm.verts.new(le + cv * x + nv * y * c) for x, y in prof])
    L = len(prof)
    for i in range(len(rings) - 1):
        for k in range(L): bm.faces.new((rings[i][k], rings[i][(k + 1) % L], rings[i + 1][(k + 1) % L], rings[i + 1][k]))
    bm.faces.new(rings[0]); bm.faces.new(rings[-1][::-1])
    if M is not None: bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return finish(name, bm, m)

def plate(name, outline, thick, m, M, bevel=0.0):
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, -thick / 2)) for x, y in outline]; hi = [bm.verts.new((x, y, thick / 2)) for x, y in outline]
    n = len(lo); bm.faces.new(lo); bm.faces.new(hi[::-1])
    for i in range(n): bm.faces.new((lo[i], hi[i], hi[(i + 1) % n], lo[(i + 1) % n]))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return finish(name, bm, m, False, bevel)

YZ = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # outline (y, z) in the side plane, thickness along X
def side_at(x): return Matrix.Translation((x, 0, 0)) @ YZ

def loft(name, sections, m, closed_ends=True, smooth=True):
    """sections: list of equal-length point lists (closed loops)."""
    bm = bmesh.new(); rings = [[bm.verts.new(Vector(p)) for p in s] for s in sections]; L = len(sections[0])
    for i in range(len(rings) - 1):
        for k in range(L): bm.faces.new((rings[i][k], rings[i][(k + 1) % L], rings[i + 1][(k + 1) % L], rings[i + 1][k]))
    if closed_ends: bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    return finish(name, bm, m, smooth)

def mirror_x(fn):
    """call fn(sx) for sx in (-1, 1)"""
    for sx in (-1, 1): fn(sx)

def wheel(tag, c, r, w, hub_r=None, spokes=5, segs=28):
    c = Vector(c); hub_r = hub_r or r * 0.55
    prof = [(-w / 2 + 0.012, hub_r * 1.02), (-w / 2, r * 0.70), (-w / 2 + 0.006, r * 0.90), (-w * 0.30, r * 0.985), (0, r),
            (w * 0.30, r * 0.985), (w / 2 - 0.006, r * 0.90), (w / 2, r * 0.70), (w / 2 - 0.012, hub_r * 1.02)]
    revolve(tag + '_tyre', c, (1, 0, 0), prof, TYRE, segs, cap=False)
    revolve(tag + '_rim', c, (1, 0, 0), [(-w * 0.40, 0.0), (-w * 0.40, r * 0.22), (-w * 0.46, hub_r * 0.75), (-w * 0.46, hub_r * 1.04),
                                         (w * 0.46, hub_r * 1.04), (w * 0.46, hub_r * 0.75), (w * 0.40, r * 0.22), (w * 0.40, 0.0)], MECH, 20)
    for s in (-1, 1):
        for k in range(spokes):
            a = 2 * math.pi * k / spokes; d = Vector((0, math.cos(a), math.sin(a)))
            tube(f'{tag}_lug{s}{k}', c + Vector((s * w * 0.40, 0, 0)) + d * r * 0.13, c + Vector((s * w * 0.45, 0, 0)) + d * r * 0.13, 0.007, DARK, 6)

# ---------------------------------------------------------------- reference meshes from Unity OBJ exports
def load_obj(path, name, coll_name, T=None, only=None):
    """Unity (x, y, z) -> Blender (x, z, y)."""
    verts, faces, objs = [], [], []
    cur = None; base = 0
    def flush():
        if cur is None or not faces: return
        me = bpy.data.meshes.new(cur); me.from_pydata(verts, [], faces); me.update()
        o = bpy.data.objects.new(cur, me); coll(coll_name).objects.link(o); me.materials.append(mat('UL2_crewref'))
        for p in me.polygons: p.use_smooth = True
        if T is not None: o.matrix_world = T
        objs.append(o)
    allv = []
    with open(path) as f:
        for line in f:
            if line.startswith('o '):
                flush(); cur = line[2:].strip(); verts, faces = [], []; base = len(allv)
                if only and not any(s in cur for s in only): cur = '__skip__' + cur
            elif line.startswith('v '):
                x, y, z = map(float, line.split()[1:4]); allv.append((x, z, y)); verts.append((x, z, y))
            elif line.startswith('f '):
                i = [int(t) - 1 - base for t in line.split()[1:4]]; faces.append((i[0], i[2], i[1]))
    flush()
    for o in [o for o in objs if o.name.startswith('__skip__')]: bpy.data.objects.remove(o); objs.remove(o)
    return objs

# ---------------------------------------------------------------- join into game parts
NOCOL_KEYS = ('cable', 'hinge', 'wire', 'bolt', 'strap', 'harness', 'rivet', 'antenna')
def join_parts(collection='PARTS'):
    c = coll(collection); out = {}
    for name, P in PARTS.items():
        objs = P['objs']
        if not objs: continue
        for o in objs:
            if o.users_collection[0] != c: pass
        with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs,
                                       object=objs[0]):
            for o in objs:
                for md in list(o.modifiers):
                    try:
                        with bpy.context.temp_override(object=o, active_object=o): bpy.ops.object.modifier_apply(modifier=md.name)
                    except Exception as e: print('modapply', o.name, e)
                # thin hardware (cables, hinge pins, bolts, straps) stays out of the part's convex collider
                if o.type == 'MESH' and len(o.data.vertices):
                    ext = max(max(v.co[i] for v in o.data.vertices) - min(v.co[i] for v in o.data.vertices) for i in range(3)) * max(o.scale)
                    nc = 1 if any(k in o.name for k in NOCOL_KEYS) or ext < 0.05 else 0
                    at = o.data.attributes.get('nocol') or o.data.attributes.new('nocol', 'INT', 'POINT')
                    for d in at.data: d.value = nc
            bpy.ops.object.join()
        o = objs[0]; o.name = name; o.data.name = name
        for cc in list(o.users_collection): cc.objects.unlink(o)
        c.objects.link(o)
        # pivot
        piv = P['pivot']; o.data.transform(Matrix.Translation(-piv)); o.matrix_world = Matrix.Translation(piv)
        # merge duplicate material slots
        names = []; remap = []
        for s in o.data.materials:
            if s.name not in names: names.append(s.name)
            remap.append(names.index(s.name))
        idx = [p.material_index for p in o.data.polygons]
        o.data.materials.clear()
        for n in names: o.data.materials.append(bpy.data.materials[n])
        for p, i in zip(o.data.polygons, idx): p.material_index = remap[i]
        out[name] = o
    for name, P in PARTS.items():
        if name in out and P['parent'] in out:
            ch = out[name]; mw = ch.matrix_world.copy(); ch.parent = out[P['parent']]; ch.matrix_world = mw
    w = bpy.data.collections.get('WORK')
    if w:
        for o in list(w.objects): bpy.data.objects.remove(o)
    return out

def tri_count(objs):
    dg = bpy.context.evaluated_depsgraph_get(); n = 0
    for o in objs:
        me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles(); n += len(me.loop_triangles); o.evaluated_get(dg).to_mesh_clear()
    return n
