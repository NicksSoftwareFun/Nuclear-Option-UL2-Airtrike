"""Pack a seat3_bake JSON into the two binary files the game side reads:
  <out>/UL2_s3_<kind>_mesh.bytes  (Unity builder: skinned mesh, bones, reference pose, materials)
  <out>/UL2_s3_<kind>_pose.bytes  (plugin, embedded: heading x elevation pose table)
Usage: python3 seat3_pack.py <bake.json> <outdir>"""
import json, struct, sys, os
d = json.load(open(sys.argv[1])); out = sys.argv[2]; k = d['kind']


def s(b, t):
    e = t.encode('utf-8'); b += struct.pack('<i', len(e)) + e


m = bytearray()
nv = len(d['verts']); m += struct.pack('<i', nv)
for v in d['verts']: m += struct.pack('<3f', *v)
for v in d['normals']: m += struct.pack('<3f', *v)
for v in d['uvs']: m += struct.pack('<2f', *v)
for w in d['weights']:
    w = (w + [[0, 0.0]] * 4)[:4]
    for i, x in w: m += struct.pack('<if', i, x)
m += struct.pack('<i', len(d['bones']))
for n in d['bones']: s(m, n)
for r in d['ref']: m += struct.pack('<7f', *r)
m += struct.pack('<i', len(d['submeshes']))
for sm in d['submeshes']:
    s(m, sm['mat']); m += struct.pack('<i', len(sm['tris'])); m += struct.pack(f'<{len(sm["tris"])}i', *sm['tris'])
m += struct.pack('<i', len(d['mats']))
for key, v in d['mats'].items():
    s(m, key); m += struct.pack('<5f', *v['color'], v['smooth'], v['metal'])
open(os.path.join(out, f'UL2_s3_{k}_mesh.bytes'), 'wb').write(m)

p = bytearray(); s(p, k); p += struct.pack('<3f', *d['arcs'])
p += struct.pack('<i', len(d['bones']))
for n in d['bones']: s(p, n)
p += struct.pack('<i', len(d['A'])) + struct.pack(f'<{len(d["A"])}f', *d['A'])
p += struct.pack('<i', len(d['E'])) + struct.pack(f'<{len(d["E"])}f', *d['E'])
for row in d['table']:
    for cell in row:
        for b in cell: p += struct.pack('<7f', *b)
open(os.path.join(out, f'UL2_s3_{k}_pose.bytes'), 'wb').write(p)
print(k, 'mesh', len(m), 'pose', len(p))
