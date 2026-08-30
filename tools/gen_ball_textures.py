"""Generate pool ball albedo textures (equirectangular, for Godot SphereMesh UVs).

Deterministic and re-runnable:  python tools/gen_ball_textures.py
Outputs game/assets/balls/pool_{0..15}.png  (1024x512).
"""

from __future__ import annotations

import math
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1024, 512
OUT = os.path.join(os.path.dirname(__file__), "..", "game", "assets", "balls")

# WPA color scheme (1/9 yellow, 2/10 blue, 3/11 red, 4/12 purple, 5/13 orange,
# 6/14 green, 7/15 maroon, 8 black).
COLORS = {
    1: (253, 202, 22),
    2: (25, 66, 175),
    3: (223, 34, 30),
    4: (98, 46, 140),
    5: (245, 124, 25),
    6: (22, 116, 56),
    7: (135, 39, 44),
    8: (16, 16, 18),
}
WHITE = (243, 240, 232)
CUE_DOT = (200, 40, 40)

STRIPE_HALF_DEG = 33          # stripe band: +-33 degrees of latitude
NUMBER_CIRCLE_DEG = 17        # angular radius of the white number circle
FONT_PATH = r"C:\Windows\Fonts\arialbd.ttf"


def lat_to_y(lat_deg: float) -> int:
    """Latitude (+90 top .. -90 bottom) to pixel row."""
    return round((90.0 - lat_deg) / 180.0 * H)


def draw_number_circle(draw: ImageDraw.ImageDraw, img: Image.Image, u_center: float, number: int) -> None:
    """White circle with the ball number, centered on the equator at longitude u_center (0..1)."""
    cx = u_center * W
    cy = H / 2
    r_v = NUMBER_CIRCLE_DEG / 180.0 * H          # vertical angular radius in px
    r_h = NUMBER_CIRCLE_DEG / 360.0 * W          # horizontal (same angle, equator: no stretch)

    draw.ellipse([cx - r_h, cy - r_v, cx + r_h, cy + r_v], fill=WHITE)

    font = ImageFont.truetype(FONT_PATH, int(r_v * 1.05))
    text = str(number)
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text((cx - tw / 2 - bbox[0], cy - th / 2 - bbox[1]), text, font=font, fill=(20, 20, 22))


def make_ball(ball_id: int) -> Image.Image:
    img = Image.new("RGB", (W, H), WHITE)
    draw = ImageDraw.Draw(img)

    if ball_id == 0:
        # Cue ball: white with a small red spot (helps read spin).
        r = 10
        draw.ellipse([W * 0.25 - r, H / 2 - r, W * 0.25 + r, H / 2 + r], fill=CUE_DOT)
        return img

    solid = ball_id <= 8
    color = COLORS[ball_id if solid else ball_id - 8]

    if solid:
        draw.rectangle([0, 0, W, H], fill=color)
    else:
        y0 = lat_to_y(STRIPE_HALF_DEG)
        y1 = lat_to_y(-STRIPE_HALF_DEG)
        draw.rectangle([0, y0, W, y1], fill=color)

    for u in (0.25, 0.75):
        draw_number_circle(draw, img, u, ball_id)

    return img


# Snooker balls are plain colours, which makes their rotation invisible in game.
# Real balls carry a small maker's mark; reproducing one lets the player actually
# see the spin they applied. Ids match SnookerBalls: 0 cue, 1 red, 16..21 colours.
SNOOKER_COLORS = {
    0: (242, 237, 224),
    1: (199, 20, 20),
    16: (242, 209, 26),
    17: (16, 107, 41),
    18: (122, 71, 31),
    19: (26, 64, 199),
    20: (240, 140, 166),
    21: (13, 13, 15),
}


def make_snooker_ball(ball_id: int) -> Image.Image:
    color = SNOOKER_COLORS[ball_id]
    img = Image.new("RGB", (W, H), color)
    draw = ImageDraw.Draw(img)

    # Mark colour: dark on light balls, light on dark ones, so it always reads.
    luma = 0.2126 * color[0] + 0.7152 * color[1] + 0.0722 * color[2]
    mark = (28, 28, 30) if luma > 110 else (232, 228, 216)

    if ball_id == 0:
        # Cue ball: the classic spot marking, plus a fine ring, for maximum
        # spin readability on the one ball the player watches most.
        for u in (0.25, 0.75):
            r = 13
            draw.ellipse([u * W - r, H / 2 - r, u * W + r, H / 2 + r], fill=(190, 40, 40))
        return img

    # Maker's mark: a small ring with a dot, on two opposite sides.
    for u in (0.25, 0.75):
        cx, cy = u * W, H / 2
        outer, inner = 17, 11
        draw.ellipse([cx - outer, cy - outer, cx + outer, cy + outer], outline=mark, width=3)
        draw.ellipse([cx - inner / 2, cy - inner / 2, cx + inner / 2, cy + inner / 2], fill=mark)

    return img


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    for ball_id in range(16):
        path = os.path.join(OUT, f"pool_{ball_id}.png")
        make_ball(ball_id).save(path)
        print("wrote", os.path.normpath(path))

    for ball_id in SNOOKER_COLORS:
        path = os.path.join(OUT, f"snooker_{ball_id}.png")
        make_snooker_ball(ball_id).save(path)
        print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
