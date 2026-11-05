"""Tile species shots: each creature as neutral/happy/worried. python3 tools/species_grid.py <dir>"""
import sys, json, pathlib
from PIL import Image, ImageDraw
d = pathlib.Path(sys.argv[1]); S = 200; keys = json.loads((d / 'list.json').read_text())
per_row = 2; rows = (len(keys) + per_row - 1) // per_row
for part in range(0, rows, 7):
    chunk = keys[part * per_row:(part + 7) * per_row]
    g = Image.new('RGB', (S * 3 * per_row, S * ((len(chunk) + 1) // 2)), (16, 13, 24)); dr = ImageDraw.Draw(g)
    for i, k in enumerate(chunk):
        for j, mood in enumerate(['neutral', 'happy', 'worried']):
            im = Image.open(d / f"{k.replace(':', '-')}-{mood}.png").resize((S, S))
            x = (i % per_row) * 3 * S + j * S; y = (i // per_row) * S
            g.paste(im, (x, y)); dr.text((x + 5, y + 4), f'{k} {mood}', fill=(200, 190, 220))
    g.save(d / f'species-{part // 7}.png')
print('done')
