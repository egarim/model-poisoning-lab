"""Tile contact-sheet PNGs: rows = creatures, columns = moods. python3 tools/grid.py <dir>"""
import sys, pathlib
from PIL import Image, ImageDraw
d = pathlib.Path(sys.argv[1]); S = 230
archs = ['keeper','trickster','seer','wild','healer','shadow','wanderer','flame']
for form in ['latam', 'slavic']:
    g = Image.new('RGB', (S * 6, S * 4 + 0), (16, 13, 24)); dr = ImageDraw.Draw(g)
    for i, a in enumerate(archs):
        for j, mood in enumerate(['neutral', 'happy', 'worried']):
            im = Image.open(d / f'{a}-{form}-{mood}.png').resize((S, S))
            x = (i % 2) * 3 * S + j * S; y = (i // 2) * S
            g.paste(im, (x, y)); dr.text((x + 6, y + 4), f'{a} {mood}', fill=(200, 190, 220))
    g.save(d / f'sheet-{form}.png')
row = Image.new('RGB', (S * 5, S)); [row.paste(Image.open(d / f'stage{n}.png').resize((S, S)), ((n - 1) * S, 0)) for n in range(1, 6)]
row.save(d / 'sheet-stages.png'); print('sheets written')
