# python3 crop.py <session-captures-dir> [name ...]
# Cuts src/cap from the full-window DPR 3 captures that cap.js writes for session.json (L-*/D-* names; not kept,
# they are re-made by running the session). Box in device pixels of the 3x capture: (x0, y0, x1, y1).
# k=3 keeps the capture's own pixels; k=2 downsamples to 2 px per CSS px (Lanczos), for the screens shown in frames.
# Nothing is ever upsampled.
import sys, os
from PIL import Image
C=sys.argv[1].rstrip('/')+'/'
O=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'cap')+'/'
def css(x0,y0,x1,y1): return (round(x0*3),round(y0*3),round(x1*3),round(y1*3))
SPEC = [
  # full screens for the device frames (2x)
  ('desk-quotes-light', 'L-desk-quotes', css(0,0,1440,900), 2),
  ('desk-quotes-dark',  'D-desk-quotes', css(0,0,1440,900), 2),
  ('tab-preview-light', 'L-tab-preview', css(0,0,820,1180), 2),
  ('tab-preview-dark',  'D-tab-preview', css(0,0,820,1180), 2),
  ('phone-preview-dark',  'D-phone-preview', css(0,0,390,844), 2),
  ('phone-preview-light', 'L-phone-preview', css(0,0,390,844), 2),
  ('phone-quotes-dark',   'D-phone-quotes',  css(0,0,390,844), 2),
  ('phone-catalog-light', 'L-phone-catalog', css(0,0,390,844), 2),
  ('phone-catalog-dark',  'D-phone-catalog', css(0,0,390,844), 2),
  # focused crops (3x)
  ('list-light',    'L-list',         css(170,0,1046,980), 3),
  ('doc-kitchen',   'L-desk-preview', (1934,799,3993,3348), 3),
  ('preview-bar',   'L-desk-preview', css(1080,170,1426,230), 3),
  ('editor-dark',   'D-desk-editor',  css(446,64,1150,1080), 3),
  # the other quotes' documents, for the sheets (3x, cropped below their footer)
  ('doc-office',    'L-sheet-office',   (1934,799,3993,3060), 3),
  ('doc-bathroom',  'L-sheet-bathroom', (1934,799,3993,3012), 3),
  ('doc-drain',     'L-sheet-drain',    (1934,799,3993,3012), 3),
  ('doc-water',     'L-sheet-water',    (1934,799,3993,3012), 3),
  # desktop split pair, settings
  # mosaic tiles (3x), each a whole element with a margin of its own page around it
  ('m-rail',        'L-desk-quotes',  css(0,0,156,258), 3),
  ('m-stat-quoted', 'D-desk-quotes',  css(180,69,592,162), 3),
  ('m-stat-sent',   'L-desk-quotes',  css(592,69,1004,162), 3),
  ('m-stat-rate',   'D-desk-quotes',  css(1004,69,1416,162), 3),
  ('m-pills',       'L-list',         css(170,162,728,246), 3),
  ('m-card-kitchen','D-desk-quotes',  css(162,758.33,529,859.33), 3),
  ('m-card-water',  'L-desk-quotes',  css(162,656.67,529,757.67), 3),
  ('m-docband',     'L-desk-preview', (1934,799,3993,1268), 3),
  ('m-totals',      'L-desk-preview', css(1080,842,1314,950), 3),
  ('m-tiles',       'D-desk-catalog', css(160,105,530,388), 3),
  ('cat-tiles',     'L-desk-catalog', css(160,105,530,665), 3),
  ('cat-list',      'L-desk-catalog', css(560,80,1424,846), 3),
  ('m-newquote',    'L-desk-quotes',  css(996,0,1424,48), 3),
  ('m-lines',       'D-desk-quotes',  css(566,292,1023,568), 3),
]
only=set(sys.argv[2:])
for name, src, box, k in SPEC:
    if only and name not in only: continue
    im = Image.open(C+src+'.png').convert('RGB').crop(box)
    if k == 2: im = im.resize((round(im.width*2/3), round(im.height*2/3)), Image.LANCZOS)
    out = f'{O}{name}@{k}x.png'
    im.save(out, optimize=True)
    print(f'{name}@{k}x', im.size, 'css', round(im.width/k), 'x', round(im.height/k))
