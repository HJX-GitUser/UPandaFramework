"""Regenerate the Log Console SVG masters and PNG textures (Python 3, resvg-py).

From the Unity project root:
  python -m pip install resvg-py Pillow
  python Assets/upanda-framework/Editor/PLogger/LogConsoleIconDesign/generate_icons.py

PNG dimensions and .meta files are preserved. All shapes are original vector geometry.
"""
from pathlib import Path
import argparse
import hashlib
import json
import sys

PACKAGE = Path(__file__).resolve().parents[3]
PROJECT = PACKAGE.parents[1]
DESIGN = Path(__file__).resolve().parent
IMAGES = PACKAGE / 'Runtime/Composition/LogConsole/Icons'
SLATE = '#425570'
BLUE = '#377FE6'
PALE = '#E7F0FD'


def path(d, color=SLATE, width=1.8, fill='none'):
    return f'<path d="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>'


def rect(x,y,w,h,r=2,fill='none',stroke=SLATE,sw=1.8):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'


def circle(x,y,r,fill='none',stroke=SLATE,sw=1.8):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'


def glyphs():
    g = {}
    g['back'] = path('M19 12H5 M11 6l-6 6 6 6', BLUE, 2.1)
    g['close'] = path('M6 6l12 12 M18 6L6 18', '#D95D6C', 2.2)
    g['search'] = circle(10.3,10.3,6.2,PALE,BLUE)+path('M15 15l5 5',SLATE,2.3)
    g['clear'] = path('M5 7h14 M9 7V4h6v3 M7 7l1 13h8l1-13',SLATE,1.8,PALE)+path('M10 10v6 M14 10v6',BLUE,1.7)
    g['copy'] = rect(8,7,12,14,2,PALE,BLUE)+path('M15 4H5a2 2 0 0 0-2 2v11')+path('M11 11h6 M11 15h4',BLUE,1.5)
    g['Save'] = path('M5 3h12l4 4v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2',SLATE,1.8,PALE)+rect(7,3,9,6,0,'#FFFFFF',BLUE,1.5)+rect(7,14,10,7,1,'#FFFFFF',SLATE,1.5)+path('M13 5v2',BLUE,1.4)
    g['collapse'] = path('M3 7l9-4 9 4-9 4z',BLUE,1.8,PALE)+path('M3 12l9 4 9-4 M3 17l9 4 9-4')
    g['date'] = rect(3,5,18,16,2,PALE,SLATE)+path('M3 10h18 M8 3v4 M16 3v4',BLUE,1.8)+rect(7,13,3,3,.6,BLUE,'none')+path('M14 14h3 M8 19h2 M14 18h3',SLATE,1.4)
    g['fps'] = path('M5 19a9 9 0 1 1 14 0',SLATE,1.9)+path('M5 19h14',SLATE)+path('M12 5v2 M5.5 8l1.5 1 M18.5 8L17 9',BLUE,1.6)+circle(12,14,1.4,BLUE,'none')+path('M12 14l4-4',BLUE,2.1)
    g['chart'] = path('M4 4v16h17')+path('M7 15l4-5 4 2 5-7',BLUE,2)+circle(11,10,1.4,BLUE,'none')+circle(20,5,1.4,BLUE,'none')
    g['memory'] = rect(6,6,12,12,2,PALE,SLATE)+rect(9,9,6,6,1,'#FFFFFF',BLUE,1.5)+path('M8 3v3 M12 3v3 M16 3v3 M8 18v3 M12 18v3 M16 18v3 M3 8h3 M3 12h3 M3 16h3 M18 8h3 M18 12h3 M18 16h3',BLUE,1.5)
    g['graphicCard'] = rect(3,6,18,12,2,PALE,SLATE)+circle(12,12,3.5,'#FFFFFF',BLUE,1.5)+path('M12 8.5v7 M8.5 12h7 M6 18v3 M9 18v2 M12 18v2 M15 18v2 M18 18v2 M1 7v10',SLATE,1.4)
    g['ComputerIcon'] = rect(3,4,18,13,2,PALE,SLATE)+path('M9 21h6 M12 17v4',SLATE)+path('M7 12l3-3 3 3 4-5',BLUE,1.7)
    g['buildFrom'] = rect(2,3,16,12,2,PALE,SLATE)+path('M7 19h6 M10 15v4',SLATE,1.6)+path('M15 19h7 M19 16l3 3-3 3',BLUE,1.9)+path('M6 7l-2 2 2 2 M13 7l2 2-2 2',BLUE,1.3)
    g['software'] = rect(3,3,18,18,2.5,PALE,SLATE)+path('M3 8h18',SLATE,1.6)+circle(6,5.5,.65,BLUE,'none')+circle(8.5,5.5,.65,BLUE,'none')+path('M9 12l-3 3 3 3 M15 12l3 3-3 3',BLUE,1.8)
    g['UnityIcon'] = path('M12 2.5l9 5v9l-9 5-9-5v-9z',SLATE,1.8,PALE)+path('M3.5 7.5l8.5 5 8.5-5 M12 12.5v9',BLUE,1.8)+path('M8 5l9 5',SLATE,1.4)
    g['clearOnSceneLoaded'] = path('M9 3l7 4v7l-7 4-7-4V7z',SLATE,1.6,PALE)+path('M2.5 7l6.5 4 6.5-4 M9 11v7',SLATE,1.5)+path('M15 21h4a3 3 0 0 0 0-6h-2 M19 12l-3 3 3 3',BLUE,1.9)
    g['timer_1'] = path('M7 3h10 M7 21h10 M8 3v4c0 2 2 3 4 5-2 2-4 3-4 5v4 M16 3v4c0 2-2 3-4 5 2 2 4 3 4 5v4',SLATE,1.7)+path('M10 6h4l-2 3z',BLUE,1,BLUE)+path('M10 18l2-2 2 2z',BLUE,1,BLUE)
    g['user'] = circle(12,7.5,4,PALE,BLUE)+path('M4 21v-2a8 6 0 0 1 16 0v2',SLATE,1.9,PALE)
    g['info'] = circle(12,12,9,PALE,BLUE)+circle(12,7.5,1.1,BLUE,'none')+path('M12 11v6',BLUE,2)
    g['log_icon'] = circle(12,12,9.5,BLUE,'none')+circle(12,7.2,1.1,'#FFFFFF','none')+path('M12 11v6','#FFFFFF',2.1)
    g['warning_icon'] = path('M10.2 3.9a2.1 2.1 0 0 1 3.6 0l8 14a2.1 2.1 0 0 1-1.8 3.1H4a2.1 2.1 0 0 1-1.8-3.1z','#B87617',.8,'#F7BA4B')+path('M12 8v6','#513812',2)+circle(12,17,1,'#513812','none')
    g['error_icon'] = path('M8 2.5h8l5.5 5.5v8L16 21.5H8L2.5 16V8z','#D64D61',.7,'#EB6072')+path('M8.5 8.5l7 7 M15.5 8.5l-7 7','#FFFFFF',2.1)
    return g


def texture(name,w,h):
    colors = {'bar':'#E8EDF4','button_active':'#CEDFF5','even_log':'#F4F7FB','odd_log':'#EAF0F7','selected':'#356BC4'}
    if name in colors:
        # Full-bleed surfaces work with the existing 1px GUIStyle borders.
        return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><rect width="{w}" height="{h}" fill="{colors[name]}"/></svg>'
    bg = '<rect width="24" height="24" fill="#E8EDF4"/>'
    if name.endswith('_back'):
        body=bg
    elif name.endswith('_thumb'):
        body=bg+rect(1.5,1.5,21,21,3,'#B5C7DE','#9CB1CD',.7)
    else:
        arrows={'scroller_down_arraw':'M8 10l4 4 4-4','scroller_up_arraw':'M8 14l4-4 4 4','scroller_left_arraw':'M14 8l-4 4 4 4','scroller_right_arraw':'M10 8l4 4-4 4'}
        body=bg+path(arrows[name],SLATE,1.9)
    return svg(body,w,h)


def svg(body,w,h):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 24 24">{body}</svg>\n'


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--vendor',type=Path);args=parser.parse_args()
    if args.vendor: sys.path.insert(0,str(args.vendor.resolve()))
    import resvg_py
    from PIL import Image
    sources=DESIGN/'Sources';sources.mkdir(exist_ok=True)
    definitions=glyphs(); manifest=[]
    for png in sorted(IMAGES.glob('*.png')):
        with Image.open(png) as old: w,h=old.size
        name=png.stem
        source=svg(definitions[name],w,h) if name in definitions else texture(name,w,h)
        if name == "timer_1": source = source.replace('viewBox="0 0 24 24"', 'viewBox="5 1 14 22"')
        (sources/(name+'.svg')).write_text(source,encoding='utf-8')
        png.write_bytes(resvg_py.svg_to_bytes(svg_string=source,skip_system_fonts=True))
        with Image.open(png) as im:
            assert im.size==(w,h) and im.mode=='RGBA', (name,im.size,im.mode)
        manifest.append({'file':png.name,'width':w,'height':h,'sha256':hashlib.sha256(png.read_bytes()).hexdigest()})
    assert len(manifest)==36
    (DESIGN/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(f'Redesigned {len(manifest)} textures in {IMAGES}')

if __name__=='__main__': main()
