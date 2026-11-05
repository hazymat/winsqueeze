# Draws the WinSqueeze icon: frosted dark tile, warm VU arc, cyan needle, display-style base
import math, sys
from PIL import Image, ImageDraw, ImageFilter
size = 1024
tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
# Tile gradient
gradient = Image.new("RGBA", (size, size))
gradient_draw = ImageDraw.Draw(gradient)
for row in range(size):
    blend = row / size
    gradient_draw.line([(0, row), (size, row)], fill=(int(34 - 14 * blend), int(38 - 16 * blend), int(52 - 22 * blend), 255))
mask = Image.new("L", (size, size), 0)
ImageDraw.Draw(mask).rounded_rectangle([40, 40, size - 40, size - 40], radius=220, fill=255)
tile.paste(gradient, (0, 0), mask)
draw = ImageDraw.Draw(tile)
# Frosted highlight
highlight = Image.new("RGBA", (size, size), (0, 0, 0, 0))
ImageDraw.Draw(highlight).ellipse([-200, -520, size + 200, 420], fill=(255, 255, 255, 34))
tile = Image.alpha_composite(tile, Image.composite(highlight, Image.new("RGBA", (size, size), (0, 0, 0, 0)), mask))
draw = ImageDraw.Draw(tile)
# Warm meter face glow
center_x, center_y, radius = size // 2, 690, 380
glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([center_x - 330, center_y - 330, center_x + 330, center_y + 330], fill=(255, 170, 60, 70))
glow = glow.filter(ImageFilter.GaussianBlur(70))
tile = Image.alpha_composite(tile, Image.composite(glow, Image.new("RGBA", (size, size), (0, 0, 0, 0)), mask))
draw = ImageDraw.Draw(tile)
# Scale arc with ticks, red zone at the right
for step in range(0, 11):
    angle = math.radians(220 - step * 26)  # 220 deg -> -40 deg... keep inside upper arc
    angle = math.radians(150 - step * 12)
    inner, outer = radius - (70 if step % 5 == 0 else 45), radius
    colour = (255, 196, 110, 255) if step < 8 else (255, 90, 70, 255)
    draw.line([(center_x + inner * math.cos(angle), center_y - inner * math.sin(angle)), (center_x + outer * math.cos(angle), center_y - outer * math.sin(angle))], fill=colour, width=26 if step % 5 == 0 else 18)
draw.arc([center_x - radius, center_y - radius, center_x + radius, center_y + radius], start=-150, end=-30, fill=(255, 196, 110, 200), width=10)
# Needle with cyan glow
needle_angle = math.radians(62)
tip = (center_x + (radius - 20) * math.cos(needle_angle), center_y - (radius - 20) * math.sin(needle_angle))
needle_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
ImageDraw.Draw(needle_glow).line([(center_x, center_y), tip], fill=(94, 231, 255, 200), width=60)
needle_glow = needle_glow.filter(ImageFilter.GaussianBlur(22))
tile = Image.alpha_composite(tile, needle_glow)
draw = ImageDraw.Draw(tile)
draw.line([(center_x, center_y), tip], fill=(140, 240, 255, 255), width=26)
draw.ellipse([center_x - 52, center_y - 52, center_x + 52, center_y + 52], fill=(230, 240, 250, 255))
# Display bar
draw.rounded_rectangle([250, 790, size - 250, 850], radius=30, fill=(94, 231, 255, 210))
tile.save(sys.argv[1] + ".png")
tile.resize((256, 256), Image.LANCZOS).save(sys.argv[1] + "-256.png")
tile.save(sys.argv[1] + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
