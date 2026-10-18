"""Draws the app icon (a folder with a sextant) and writes src/FolderDiskSize.App/Assets/app.ico.

Needs: pip install cairosvg pillow. Sizes 48 px and up get the detailed sextant; smaller ones a bolder, simpler one.
"""
import io
import math
import os

import cairosvg
from PIL import Image

def svg(detail):
    px, py, r = 128, 84, 112
    def pt(deg, rad):
        a = math.radians(90 + deg)  # 0 = straight down
        return px + rad*math.cos(a), py + rad*math.sin(a)
    L = pt(-30, r); R = pt(30, r)
    Li = pt(-30, r-16); Ri = pt(30, r-16)
    sw = 9 if detail else 16
    ticks = ""
    if detail:
        for d in range(-27, 28, 6):
            a, b = pt(d, r-14), pt(d, r-3)
            ticks += f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#F4E3B5" stroke-width="2.4"/>'
    arm = pt(10, r-2)
    band = (f'<path d="M{L[0]:.1f},{L[1]:.1f} A{r},{r} 0 0 1 {R[0]:.1f},{R[1]:.1f} L{Ri[0]:.1f},{Ri[1]:.1f} '
            f'A{r-16},{r-16} 0 0 0 {Li[0]:.1f},{Li[1]:.1f} Z" fill="#1F3A5F"/>')
    frame = f'<path d="M{Li[0]:.1f},{Li[1]:.1f} L{px},{py} L{Ri[0]:.1f},{Ri[1]:.1f}" fill="none" stroke="#1F3A5F" stroke-width="{sw}" stroke-linejoin="round" stroke-linecap="round"/>'
    scope = ('<rect x="96" y="128" width="58" height="15" rx="5" fill="#1F3A5F"/>'
             '<rect x="150" y="125" width="14" height="21" rx="4" fill="#1F3A5F"/>' if detail else '')
    mirror = '<rect x="117" y="70" width="22" height="9" rx="2" fill="#7FB3E0" stroke="#1F3A5F" stroke-width="3"/>' if detail else ''
    armline = f'<line x1="{px}" y1="{py}" x2="{arm[0]:.1f}" y2="{arm[1]:.1f}" stroke="#D2452F" stroke-width="{7 if detail else 13}" stroke-linecap="round"/>'
    pivot = f'<circle cx="{px}" cy="{py}" r="{10 if detail else 14}" fill="#D2452F" stroke="#1F3A5F" stroke-width="{4 if detail else 6}"/>'
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
<defs>
 <linearGradient id="front" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFD66B"/><stop offset="1" stop-color="#F5B730"/></linearGradient>
</defs>
<path d="M14,52 Q14,38 28,38 L92,38 Q102,38 108,46 L118,58 L228,58 Q242,58 242,72 L242,206 Q242,222 226,222 L30,222 Q14,222 14,206 Z" fill="#D9931F"/>
<rect x="14" y="74" width="228" height="150" rx="16" fill="url(#front)"/>
<rect x="14" y="74" width="228" height="10" rx="5" fill="#FFE49A" opacity="0.8"/>
<g transform="translate(0,18)">{band}{ticks}{frame}{scope}{armline}{mirror}{pivot}</g>
</svg>'''

sizes=[16,20,24,32,40,48,64,128,256]
imgs=[]
for s in sizes:
    src = svg(s>=48)
    png = cairosvg.svg2png(bytestring=src.encode(), output_width=s, output_height=s)
    im = Image.open(io.BytesIO(png)).convert('RGBA'); imgs.append(im)
out = os.path.join(os.path.dirname(__file__), '..', 'src', 'FolderDiskSize.App', 'Assets', 'app.ico')
imgs[-1].save(out, sizes=[(s,s) for s in sizes], append_images=imgs[:-1])
print('wrote', os.path.normpath(out))
