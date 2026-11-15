from PIL import Image, ImageDraw
import math
S=1024; PX=128.0; ZC=-0.25
def P(x,z): return (S/2+x*PX, S/2-(z-ZC)*PX)
def poly(d,pts,f=255): d.polygon([P(*p) for p in pts],fill=f)
def rect(d,x0,x1,z0,z1,f=255): poly(d,[(x0,z0),(x1,z0),(x1,z1),(x0,z1)],f)
def circ(d,x,z,r,f=255,w=0):
    a=P(x-r,z+r); b=P(x+r,z-r)
    if w: d.ellipse([a,b],outline=f,width=int(w*PX))
    else: d.ellipse([a,b],fill=f)
def bar(d,x,z,ang,L,ch,f=255,round_=True):
    ca,sa=math.cos(ang),math.sin(ang); dx,dz=-sa*ch/2,ca*ch/2
    for s in (1,-1):
        ex,ez=x+s*ca*L,z+s*sa*L
        poly(d,[(x+dx,z+dz),(ex+dx*0.7,ez+dz*0.7),(ex-dx*0.7,ez-dz*0.7),(x-dx,z-dz)],f)
        if round_: circ(d,ex,ez,ch*0.35,f)
def pod(d,wscale=1.0,f=255):
    prof=[(2.95,0.0),(2.85,0.18),(2.6,0.34),(2.2,0.44),(1.6,0.47),(0.8,0.46),(0.0,0.40),(-0.3,0.36)]
    pts=[(w*wscale,z) for z,w in prof]+[(-w*wscale,z) for z,w in reversed(prof)]
    poly(d,pts,f)
def body(d,wb=1.5,prop=1.05,stubs=True,wheels=True,tail=1.0):
    pod(d,wb)
    poly(d,[(0.36*wb,-0.3),(0.42*wb,-1.0),(0.36*wb,-1.85),(-0.36*wb,-1.85),(-0.42*wb,-1.0),(-0.36*wb,-0.3)])
    rect(d,-0.2*tail,0.2*tail,-1.9,-3.15)                       # boom
    poly(d,[(-1.0*tail,-2.62),(1.0*tail,-2.62),(1.0*tail,-3.2),(-1.0*tail,-3.2)])   # tailplane
    rect(d,0.86*tail,1.12*tail,-2.4,-3.5); rect(d,-1.12*tail,-0.86*tail,-2.4,-3.5)  # end plates
    rect(d,-0.1,0.1,-3.1,-3.55)                                    # centre fin/rudder
    bar(d,0,-2.03,0.0,prop,0.26); circ(d,0,-2.03,0.24)          # pusher prop + spinner
    if stubs: poly(d,[(0.3,0.85),(1.15,0.74),(1.15,0.36),(0.3,0.30)]); poly(d,[(-0.3,0.85),(-1.15,0.74),(-1.15,0.36),(-0.3,0.30)])
    if wheels: rect(d,0.85,1.25,-0.3,-1.0); rect(d,-1.25,-0.85,-0.3,-1.0)
def seats(d,f=0):
    for z in (2.0,1.2,0.4): poly(d,[(-0.24,z+0.24),(0.24,z+0.24),(0.27,z-0.22),(-0.27,z-0.22)],f)

def opt(n):
    im=Image.new('L',(S,S),0); d=ImageDraw.Draw(im)
    if n=='A':   # full silhouette, two-blade rotor at 35 deg, thick enough to read
        body(d); bar(d,0,-0.97,math.radians(35),3.75,0.46); circ(d,0,-0.97,0.42)
    elif n=='B': # rotor shown as a disc ring round the airframe, one blade across it
        body(d); circ(d,0,-0.25,3.75,255,0.26); bar(d,0,-0.97,math.radians(35),3.7,0.36,round_=False); circ(d,0,-0.97,0.3)
    elif n=='C': # bold minimal cross: chunky body, rotor straight across, oversized pusher prop
        body(d,wb=1.7,prop=1.25,wheels=False,tail=1.15); bar(d,0,-0.97,0.0,3.6,0.55); circ(d,0,-0.97,0.45)
    elif n=='D': # A plus stock-style cut-outs: three tandem open seats in the pod
        body(d,wb=1.75); bar(d,0,-0.97,math.radians(35),3.75,0.46); circ(d,0,-0.97,0.42); seats(d)
        circ(d,0,-0.97,0.16,0)
    elif n=='E': # C body + D rotor and hub circle (Nick 03:43)
        body(d,wb=1.7,prop=1.25,wheels=False,tail=1.15); bar(d,0,-0.97,math.radians(35),3.75,0.46); circ(d,0,-0.97,0.42); circ(d,0,-0.97,0.16,0)
    a=im.resize((256,256),Image.LANCZOS)
    out=Image.new('RGBA',(256,256),(255,255,255,0)); out.putalpha(a); return out

import sys
opts={k:opt(k) for k in 'ABCDE'}
for k,v in opts.items(): v.save(f'/tmp/claude-0/icons/UL2_icon_map_{k}.png')
cur=Image.open('/mnt/user-data/uploads/Nuclear Option Mods/_renders/icons/ul2_current.png').convert('RGBA').resize((256,256))
ref=Image.open('/mnt/user-data/uploads/Nuclear Option Mods/_renders/icons/UtilityHelo1.png').convert('RGBA'); ref.thumbnail((256,256))
cols=[('Now',cur),('C',opts['C']),('D',opts['D']),('New',opts['E'])]
W=290; sh=Image.new('RGBA',(W*4,290+120),(38,56,46,255)); dd=ImageDraw.Draw(sh)
from PIL import ImageFont
try: fnt=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',30)
except: fnt=None
for i,(t,im) in enumerate(cols):
    sh.alpha_composite(im,(i*W+17,10))
    sm=im.resize((40,40),Image.LANCZOS); sh.alpha_composite(sm,(i*W+30,300)); 
    sm2=im.resize((28,28),Image.LANCZOS).rotate(-60,expand=True,resample=Image.BICUBIC); sh.alpha_composite(sm2,(i*W+95,298))
    dd.text((i*W+180,310),t,fill=(255,255,255,255),font=fnt)
sh.save('/tmp/claude-0/icons/options.png')
