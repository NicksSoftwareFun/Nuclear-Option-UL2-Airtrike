"""Preview look for renders: stock crew textures on the pilot refs and the seat 3 soldier (local reference copies from
the game, preview only), and the machine gun finish after Nick's MG 338 photo: bronze cerakote receiver and handguard,
flat dark earth polymer furniture, black barrel, can and small parts, coyote fabric belt pouch, brass rounds.
    import look; look.crew(gunner); look.gun(gunner.gun_objs)"""
import bpy, re

TEX = '/tmp/ul2crewtex/'      # pilot_b/n, soldier1_b/n PNGs and pilot_uv.obj staged from the game reference export


def _srgb(c): return tuple(((x / 255.0 + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)


def _img_mat(name, b, n=None, rough=0.75):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    bsdf = nt.nodes['Principled BSDF']; bsdf.inputs['Roughness'].default_value = rough
    tb = nt.nodes.new('ShaderNodeTexImage'); tb.image = bpy.data.images.load(TEX + b); nt.links.new(tb.outputs['Color'], bsdf.inputs['Base Color'])
    if n:
        tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(TEX + n); tn.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value = 0.8
        nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    return m


def crew(gunner=None, refs=('REF_pilot', 'REF_gunner')):
    """stock pilot texture on the seat 1-2 refs (UVs from the game mesh, same vertex order), soldier texture on seat 3"""
    uv = [tuple(map(float, l.split()[1:3])) for l in open(TEX + 'pilot_uv.obj') if l.startswith('vt ')]
    pm = _img_mat('crew_pilot', 'pilot_b_PLACEHOLDER.png', 'pilot_n_PLACEHOLDER.png')
    done = set()
    for n in refs:
        o = bpy.data.objects.get(n)
        if not o: continue
        me = o.data
        if me.name not in done and len(me.vertices) == len(uv):
            L = me.uv_layers.new(name='UVMap') if not me.uv_layers else me.uv_layers[0]
            for poly in me.polygons:
                for li in poly.loop_indices: L.data[li].uv = uv[me.loops[li].vertex_index]
            done.add(me.name)
        me.materials.clear(); me.materials.append(pm)
    if gunner is not None:
        sm = _img_mat('crew_soldier', 'soldier1_b_PLACEHOLDER.png', 'soldier1_n_PLACEHOLDER.png')
        me = gunner.so.data
        for i in range(len(me.materials)): me.materials[i] = sm
        if not me.materials: me.materials.append(sm)


def _paint(name, rgb, rough, metal=0.0, wear=0.0, noise=0.06, scale=180.0):
    """flat colour with a subtle fine tone noise; wear lightens edges a little (Cycles pointiness)"""
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; L = nt.links
    b = nt.nodes['Principled BSDF']; b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = scale; nz.inputs['Detail'].default_value = 4
    ramp = nt.nodes.new('ShaderNodeValToRGB'); c = _srgb(rgb)
    ramp.color_ramp.elements[0].color = tuple(x * (1 - noise) for x in c[:3]) + (1,); ramp.color_ramp.elements[1].color = tuple(min(1, x * (1 + noise)) for x in c[:3]) + (1,)
    L.new(nz.outputs['Fac'], ramp.inputs['Fac'])
    if wear > 0:
        geo = nt.nodes.new('ShaderNodeNewGeometry'); wr = nt.nodes.new('ShaderNodeValToRGB')
        wr.color_ramp.elements[0].position = 0.52; wr.color_ramp.elements[1].position = 0.60
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
        L.new(geo.outputs['Pointiness'], wr.inputs['Fac']); L.new(wr.outputs['Color'], mix.inputs['Factor'])
        mix.inputs['Factor'].default_value = 0
        L.new(ramp.outputs['Color'], mix.inputs[6]); mix.inputs[7].default_value = tuple(min(1, x * (1 + wear)) for x in c[:3]) + (1,)
        L.new(mix.outputs[2], b.inputs['Base Color'])
    else:
        L.new(ramp.outputs['Color'], b.inputs['Base Color'])
    return m


BRONZE = dict(rgb=(146, 108, 80), rough=0.42, metal=0.35, wear=0.35)      # cerakote receiver/handguard/rails/bipod legs
FDE = dict(rgb=(176, 150, 110), rough=0.72, wear=0.15)                    # polymer stock, grips, trigger guard, laser module
BLACK = dict(rgb=(34, 34, 33), rough=0.48, metal=0.4, wear=0.6)           # barrel, can, bipod clamp, links, chute, small steel
STEEL = dict(rgb=(58, 55, 51), rough=0.40, metal=0.6, wear=0.3)           # knobs, grooves, screws
RUBBER = dict(rgb=(28, 28, 28), rough=0.9)
FABRIC = dict(rgb=(166, 134, 94), rough=0.97, noise=0.12, scale=600.0)   # coyote belt pouch
BRASS = dict(rgb=(196, 152, 78), rough=0.30, metal=1.0)
CASE = dict(rgb=(228, 228, 224), rough=0.55)                             # white polymer case (True Velocity style)
COPPER = dict(rgb=(196, 120, 72), rough=0.28, metal=1.0)                 # copper projectile
OPTIC = dict(rgb=(132, 122, 96), rough=0.6, wear=0.2)

RULES = [  # (regex on the base name, finish); first match wins; SCREEN-material parts (glass, core, holes) keep theirs
    (r'^mg_round\d*_base', BLACK), (r'^mg_round\d*_case', CASE), (r'^mg_round\d*_proj', COPPER),
    (r'^mg_pouch_(strap|buckle)', BLACK), (r'^mg_pouch_rail', BRONZE), (r'^mg_pouch', FABRIC),
    (r'^mg_(buttpad)', RUBBER),
    (r'^mg_(stock_|cheek|grip|vg$|vg_rib|guard)', FDE),
    (r'^mg_bipod_(up|lo)', BRONZE),
    (r'^mg_(barrel|bhandle|bipod|bnut|bring|can$|can_lock|chute|fpost$|fpost_ear|gasblock|gastube|gimbal|link|yoke|trigger|selector|charger_knob|butt_rib|cover_hinge|qd|hinge|feedport|chslot|ejport|feedwin|mount_rail)', BLACK),
    (r'^mg_can_groove', STEEL),
    (r'^mg_optic', OPTIC),
    (r'^ngal_(illum$|collar|cap|shutter|body|crown|clamp|jaw|pad)', FDE),
    (r'^ngal_', STEEL),
    (r'^mg_', BRONZE),
    # grenade launcher (same scheme): bronze frame and handguard, black cylinder/barrel/rails, FDE furniture
    (r'^gl_nose', dict(rgb=(176, 146, 72), rough=0.35, metal=0.7)),
    (r'^gl_buttpad', RUBBER),
    (r'^gl_(stock|grip$|grip_rib|vg|guard|rcover)', FDE),
    (r'^gl_(recv|rplate|fplate|strap|lstrap|hinge_lug|hg_[tblrc]|latch)', BRONZE),
    (r'^gl_(hub_rib|ratchet|sight_knob|sight_drum|safety|trigger|qd|stock_lever|screw|sling|hinge_pin|axis_btn|lock_lever|pin\d|bus_f_post)', STEEL),
    (r'^gl_', BLACK),
    # spotter kit: FDE binocular body with black rubber armour and eyecups, ranger-green radio, black EUD
    (r'^bino_lens$', dict(rgb=(38, 46, 62), rough=0.08, metal=0.3)),
    (r'^bino_laserplate', BLACK),
    (r'^bino_(eyecup|armour|grip|focus)', RUBBER),
    (r'^bino_(btn|batt_cap|mount|screw)', STEEL),
    (r'^bino_', FDE),
    (r'^radio_(key|fkey|knob|port|conn$|conn\d|batt_latch|screen_bezel|plate)', STEEL),
    (r'^radio_', dict(rgb=(40, 42, 41), rough=0.65, metal=0.2, wear=0.4)),   # PRC-162 class: black/dark grey chassis
    (r'^eud_(case|bezel|strap|hinge)', BLACK),
    (r'^eud_', dict(rgb=(62, 64, 52), rough=0.9, noise=0.10, scale=500.0)),
    (r'^kit_', dict(rgb=(62, 64, 52), rough=0.95, noise=0.10, scale=500.0)),
]


def gun(objs):
    for o in objs:
        if o.type != 'MESH' or not o.data.materials: continue
        if o.data.materials[0] and o.data.materials[0].name.startswith('UL2_screen'): continue
        base = re.sub(r'[-\d]+$', '', o.name.split('.')[0])
        for rx, fin in RULES:
            if re.match(rx, base):
                key = 'gun_' + str(sorted(fin.items())).__hash__().__str__()[-8:]
                m = _paint(key, **fin)
                me = o.data
                if me.users > 1: me = o.data = me.copy()
                for i in range(len(me.materials)): me.materials[i] = m
                break


WEB = dict(rgb=(62, 64, 52), rough=0.95, noise=0.10, scale=500.0)     # harness webbing, ranger green
ARMB = dict(rgb=(30, 30, 30), rough=0.45, metal=0.5, wear=0.5)        # support arm links, black anodised
GLOVE = dict(rgb=(92, 80, 62), rough=0.85, noise=0.08, scale=400.0)   # coyote gloves, darker cuffs


def _set(objs, fin, keep_screen=True):
    m = _paint('gear_' + str(abs(hash(str(sorted(fin.items())))))[-8:], **fin)
    for o in objs:
        if o.type != 'MESH' or not o.data.materials: continue
        if keep_screen and o.data.materials[0] and o.data.materials[0].name.startswith('UL2_screen'): continue
        me = o.data
        if me.users > 1: me = o.data = me.copy()
        for i in range(len(me.materials)): me.materials[i] = m


def gear(g):
    """harness, gloves and the support arm (re-applied after every pose, the arm is rebuilt each time)"""
    if getattr(g, 'kind', 'mg') == 'spot': gun(g.harness_objs)
    else: _set(g.harness_objs, WEB)
    _set([o for o in g.gun_objs if o.name.startswith('glove_') and '_cuff' not in o.name], GLOVE)
    _set([o for o in g.gun_objs if '_cuff' in o.name], WEB)
    _set(g.arm_objs, ARMB)
    if not getattr(g, '_look_wrapped', False):
        orig = g._arm
        def _arm2():
            orig(); _set(g.arm_objs, ARMB)
        g._arm = _arm2; g._look_wrapped = True
