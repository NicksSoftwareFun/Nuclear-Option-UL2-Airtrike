"""UL-2 with all three seats filled: stock pilot refs in seats 1-2, the rigged seat 3 machine gunner posed by heading A
and elevation E, AT145 quad racks on the stub pylons (below, or on top with --above).
Usage: python3 -I aim_scene.py -- <outdir> still A E [--above]
       python3 -I aim_scene.py -- <outdir> swap            (side-swap frames swap_###.png + clearance log)"""
import bpy, sys, math
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]; out = a[0]; mode = a[1]
bpy.ops.wm.open_mainfile(filepath='/mnt/project-files/ul2/art/blend/ul2_uv.blend')
from render_lib import setup, shot
from gunner_rig import Gunner
# livery
m = bpy.data.materials['UL2_skin']; m.use_nodes = True; nt = m.node_tree
bsdf = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = bpy.data.images.load('/tmp/ul2work/ul2liv/out/UL2_skin_b.png'); nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
# AT145 racks
vs, fs = [], []
for ln in open('/tmp/ul2work/atgm.obj'):
    p = ln.split()
    if not p: continue
    if p[0] == 'v': vs.append(tuple(map(float, p[1:4])))
    elif p[0] == 'f': fs.append(tuple(int(x.split('/')[0]) - 1 for x in p[1:4]))
me = bpy.data.meshes.new('atgm'); me.from_pydata(vs, [], fs); me.update()
for pg in me.polygons: pg.use_smooth = True
mt = bpy.data.materials.new('atgm_mat'); mt.use_nodes = True
mt.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.17, 0.15, 1); me.materials.append(mt)
ABOVE = '--below' not in sys.argv     # design (Nick 2026-11-12 12:38): racks on TOP of the extended stubs, flipped upside down
HPX, HPY, HPZ = 0.86, 0.53, 0.80
for sx in (-1, 1):
    o = bpy.data.objects.new(f'atgm{sx}', me); bpy.context.scene.collection.objects.link(o)
    if ABOVE: o.matrix_world = Matrix.Translation((sx * HPX, HPY, HPZ)) @ Matrix.Rotation(math.pi, 4, 'Y')   # mounting face down on the pylon
    else: o.matrix_world = Matrix.Translation((sx * 0.70, 0.53, 0.59))
# crew: seats 1-2 stock pilot refs stay, seat 3 = rigged gunner (hide the static soldier ref)
bpy.data.objects['REF_soldier'].hide_render = True
c = bpy.data.collections.get('CREWREF'); c.hide_render = False
g = Gunner(Matrix.Translation((0, -0.55, 1.42)))
copilot = bpy.data.objects['REF_gunner']
import look; look.crew(g); look.gun(g.gun_objs); look.gear(g)     # stock crew textures + MG 338 finish (Nick 15:41)
sc, cam, gg = setup(28)
if mode == 'still':
    A, E = float(a[2]), float(a[3])
    miss = g.pose(A, E)
    if '--laser' in sys.argv:   # visual-only green aiming beam from the laser module's visible-laser window (Nick 15:37)
        import mg_model as mg
        bm_ = bpy.data.meshes.new('beam'); o = bpy.data.objects.new('lam_beam', bm_); sc0 = bpy.context.scene; sc0.collection.objects.link(o)
        import bmesh as _b; b = _b.new(); _b.ops.create_cone(b, cap_ends=True, segments=10, radius1=0.003, radius2=0.03, depth=60.0); b.to_mesh(bm_); b.free()
        em = bpy.data.materials.new('beam'); em.use_nodes = True; n = em.node_tree.nodes; n.clear()
        e = n.new('ShaderNodeEmission'); e.inputs['Color'].default_value = (0.1, 1.0, 0.15, 1); e.inputs['Strength'].default_value = 6.0
        tr = n.new('ShaderNodeBsdfTransparent'); mx = n.new('ShaderNodeMixShader'); mx.inputs['Fac'].default_value = 0.55
        out_ = n.new('ShaderNodeOutputMaterial'); L = em.node_tree.links
        L.new(tr.outputs[0], mx.inputs[1]); L.new(e.outputs[0], mx.inputs[2]); L.new(mx.outputs[0], out_.inputs[0]); bm_.materials.append(em)
        win = Vector((0.018, mg.NGAL_Y + 0.0612, 0.1155 + 0.018))
        o.matrix_world = g.GM @ Matrix.Translation(win + Vector((0, 30.0, 0))) @ Matrix.Rotation(math.pi / 2, 4, 'X') @ Matrix.Rotation(math.pi, 4, 'X')
    print('pose', A, E, 'miss', {k: round(v, 3) for k, v in miss.items()}, 'arm', round(g.arm_len, 3), 'copilot clearance', round(g.clearance(copilot), 3))
    tag = ('above_' if ABOVE else '') + ('laser_' if '--laser' in sys.argv else '') + f'{int(A)}_{int(E)}'
    shot(sc, cam, f'{out}/air_{tag}_3q.png', (4.6, 5.6, 3.3), (0, -0.2, 1.35), 42, res=(1600, 1000), scale=1.0)
    shot(sc, cam, f'{out}/air_{tag}_rear.png', (4.4, -5.2, 3.0), (0, -0.3, 1.35), 42, res=(1600, 1000), scale=1.0)
    shot(sc, cam, f'{out}/air_{tag}_close.png', (2.6, 1.6, 2.4), (0.25, -0.45, 1.55), 40, res=(1400, 1000), scale=1.0)
    shot(sc, cam, f'{out}/air_{tag}_toprear.png', (-1.8, -6.2, 5.0), (0.4, -0.3, 1.2), 40, res=(1600, 1000), scale=1.0)
    shot(sc, cam, f'{out}/air_{tag}_topfront.png', (-1.6, 6.4, 4.8), (0.4, -0.4, 1.2), 40, res=(1600, 1000), scale=1.0)
    if ABOVE: shot(sc, cam, f'{out}/air_{tag}_side.png', (-40, -0.2, 1.55), (0, -0.2, 1.55), ortho=8.4, res=(1600, 900))
else:
    keys = [(95, -30), (70, -5), (40, 35), (12, 68), (0, 74), (-12, 69), (-40, 35), (-70, -5), (-95, -30)]
    N = 36; log = []
    def interp(t):
        x = t * (len(keys) - 1); i = min(int(x), len(keys) - 2); f = x - i; f = f * f * (3 - 2 * f)
        return tuple(keys[i][j] * (1 - f) + keys[i + 1][j] * f for j in range(2))
    sc.cycles.samples = 10
    for k in range(N + 1):
        A, E = interp(k / N); miss = g.pose(A, E); cl = g.clearance(copilot)
        log.append(f'{k:02d} A {A:6.1f} E {E:5.1f} T {g.T:5.1f} missR {miss["R"]:.3f} missL {miss["L"]:.3f} arm {g.arm_len:.3f} copilot {cl:.3f}')
        shot(sc, cam, f'{out}/swap_{k:03d}.png', (3.4, 3.0, 3.1), (0, -0.25, 1.75), 40, res=(900, 640), scale=1.0)
    open(f'{out}/swap_log.txt', 'w').write('\n'.join(log)); print('\n'.join(log))
