# Draws the WinSqueeze icon: two-bar VU (Squeezebox-style segmented L/R bars on a dark glass tile)
import sys
from PIL import Image, ImageDraw, ImageFilter
size = 1024
transparent = lambda: Image.new("RGBA", (size, size), (0, 0, 0, 0))
mask = Image.new("L", (size, size), 0); ImageDraw.Draw(mask).rounded_rectangle([40, 40, size - 40, size - 40], radius=220, fill=255)
gradient = Image.new("RGBA", (size, size)); gradient_draw = ImageDraw.Draw(gradient)
for row in range(size): blend = row / size; gradient_draw.line([(0, row), (size, row)], fill=(int(26 - 18 * blend), int(30 - 20 * blend), int(40 - 26 * blend), 255))
tile = transparent(); tile.paste(gradient, (0, 0), mask)
highlight = transparent(); ImageDraw.Draw(highlight).ellipse([-200, -560, size + 200, 380], fill=(255, 255, 255, 26))
tile = Image.alpha_composite(tile, Image.composite(highlight, transparent(), mask))
segments, lit_levels = 7, (6, 4)
bar_width, gap, top, bottom = 230, 26, 190, 834
segment_height = (bottom - top - gap * (segments - 1)) / segments
bars, glow = transparent(), transparent()
for bar_index, lit in enumerate(lit_levels):
    left = size // 2 - 40 - bar_width if bar_index == 0 else size // 2 + 40
    for segment in range(segments):
        segment_bottom = bottom - segment * (segment_height + gap); box = [left, segment_bottom - segment_height, left + bar_width, segment_bottom]
        hot = segment >= segments - 1; on = segment < lit
        colour = ((255, 90, 70, 255) if hot else (94, 231, 255, 255)) if on else ((255, 90, 70, 40) if hot else (94, 231, 255, 34))
        ImageDraw.Draw(bars).rounded_rectangle(box, radius=26, fill=colour)
        if on: ImageDraw.Draw(glow).rounded_rectangle(box, radius=26, fill=colour)
tile = Image.alpha_composite(tile, Image.composite(glow.filter(ImageFilter.GaussianBlur(28)), transparent(), mask))
tile = Image.alpha_composite(tile, bars)
tile.save(sys.argv[1] + ".png")
tile.resize((256, 256), Image.LANCZOS).save(sys.argv[1] + "-256.png")
tile.save(sys.argv[1] + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
