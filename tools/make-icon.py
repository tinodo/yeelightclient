#!/usr/bin/env python3
"""Draws YeelightClient/icon.png, the icon of the NuGet package, from the same drawing as docs/images/logo.svg.

NuGet takes a PNG or JPEG for an icon (not SVG), of at most 1 MB, and recommends 128 by 128 pixels: this makes 256 by 256, with
transparent corners. The drawing is that of the logo, which is 64 by 64 units: a rounded square with a diagonal gradient, and a light bulb
(the glass, a base under it and three rays above it) in white strokes with round ends. The bulb is the `d` of the path of the logo, as it
is: keep it in line with docs/images/logo.svg and docs/images/favicon.svg when that changes. It only has M, L, C and Z commands with
absolute coordinates, which is all that this reads.

Usage: python tools/make-icon.py [OUTPUT.png]    (default: YeelightClient/icon.png)
Needs Pillow (pip install pillow).
"""
import math
import os
import re
import sys

SIZE = 256
SUPERSAMPLE = 4  # drawn four times as large and scaled down, for smooth edges
UNITS = 64
START = (0xFF, 0xB8, 0x4D)
END = (0xE8, 0x59, 0x0C)
CORNER_RADIUS = 14
STROKE_WIDTH = 4.5
GLYPH = (
    "M28 45C28 41 21.5 39 21.5 33.5C21.5 27.7 26.2 23 32 23C37.8 23 42.5 27.7 42.5 33.5C42.5 39 36 41 36 45Z"  # the glass
    "M28.5 52L35.5 52"  # the base
    "M32 12L32 16M19.2 21.1L16.4 18.3M44.8 21.1L47.6 18.3"  # the rays
)
CURVE_STEPS = 64  # the straight pieces that a curve is cut into
NUMBER = r"-?\d*\.?\d+"
NUMBERS_OF = {"M": 2, "L": 2, "C": 6, "Z": 0}  # how many numbers each command takes


def polylines(path):
    """The lines of a path, as lists of (x, y) in units: a curve is cut into straight pieces, and Z goes back to where its line began."""
    tokens = re.findall(rf"[A-Za-z]|{NUMBER}", path)
    lines = []
    index = 0
    while index < len(tokens):
        command = tokens[index]
        count = NUMBERS_OF.get(command)
        numbers = tokens[index + 1 : index + 1 + count] if count is not None else []
        if count is None or len(numbers) != count or not all(re.fullmatch(NUMBER, number) for number in numbers):
            raise ValueError(f"cannot read '{command}' in the path of the logo: only M, L, C and Z, with absolute coordinates")
        index += 1 + count

        values = [float(number) for number in numbers]
        if command == "M":
            lines.append([(values[0], values[1])])
        elif not lines:
            raise ValueError("the path of the logo has to begin with M")
        elif command == "L":
            lines[-1].append((values[0], values[1]))
        elif command == "C":
            (x0, y0), (x1, y1, x2, y2, x3, y3) = lines[-1][-1], values
            for step in range(1, CURVE_STEPS + 1):
                t = step / CURVE_STEPS
                u = 1 - t
                lines[-1].append(
                    (
                        u**3 * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t**3 * x3,
                        u**3 * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t**3 * y3,
                    )
                )
        else:
            lines[-1].append(lines[-1][0])
    return lines


def draw(size=SIZE):
    from PIL import Image, ImageDraw  # only the drawing needs Pillow: the path above can be read without it

    canvas = size * SUPERSAMPLE
    unit = canvas / UNITS

    # The gradient of the logo runs from the top left to the bottom right corner (objectBoundingBox 0,0 to 1,1).
    gradient = Image.new("RGB", (canvas, canvas))
    pixels = gradient.load()
    for y in range(canvas):
        for x in range(canvas):
            t = (x + y) / (2 * (canvas - 1))
            pixels[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(START, END))

    mask = Image.new("L", (canvas, canvas), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, canvas - 1, canvas - 1], radius=CORNER_RADIUS * unit, fill=255)

    icon = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    icon.paste(gradient, (0, 0), mask)

    # A round pen (stroke-linecap and stroke-linejoin are round in the logo): a disc on every pixel of every line, so that there is no
    # seam where two pieces of a curve meet, and the ends are round.
    pen = ImageDraw.Draw(icon)
    radius = STROKE_WIDTH * unit / 2
    for line in polylines(GLYPH):
        points = [(x * unit, y * unit) for x, y in line]
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            steps = max(1, math.ceil(math.hypot(x1 - x0, y1 - y0)))
            for step in range(steps + 1):
                x = x0 + (x1 - x0) * step / steps
                y = y0 + (y1 - y0) * step / steps
                pen.ellipse([x - radius, y - radius, x + radius, y + radius], fill=(255, 255, 255, 255))

    return icon.resize((size, size), Image.LANCZOS)


def main(arguments):
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    path = arguments[0] if arguments else os.path.join(root, "YeelightClient", "icon.png")
    draw().save(path, optimize=True)
    print(f"{path}: {os.path.getsize(path):,} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
