"""潮閘的圖示：萊姆綠圓角底 + 深綠錶環（限速錶）+ 被壓住的水滴。

純 Python 畫，4 倍超取樣後縮小，輸出 PNG 與多尺寸 ICO（BMP 影格，相容性最好）。
"""

import math
import os
import struct
import zlib

DESIGN = 1024.0
SS = 4

LIME_TOP = (230, 250, 150)
LIME_BOTTOM = (198, 231, 88)
INK = (23, 32, 22)
DROP = (60, 150, 230)
WHITE = (255, 255, 255)
BLUSH = (250, 168, 188)


def fill_polygon(buf, width, points, color, alpha=255, gradient=None):
    if len(points) < 3:
        return

    ys = [p[1] for p in points]
    top = max(0, int(min(ys)))
    bottom = min(width, int(max(ys)) + 1)
    count = len(points)

    for y in range(top, bottom):
        yc = y + 0.5
        crossings = []
        for index in range(count):
            x1, y1 = points[index]
            x2, y2 = points[(index + 1) % count]
            if (y1 <= yc < y2) or (y2 <= yc < y1):
                crossings.append(x1 + (yc - y1) * (x2 - x1) / (y2 - y1))

        if len(crossings) < 2:
            continue

        crossings.sort()
        row_color = color
        if gradient is not None:
            ratio = y / max(1.0, width - 1)
            row_color = tuple(
                int(gradient[0][c] + (gradient[1][c] - gradient[0][c]) * ratio) for c in range(3)
            )

        for index in range(0, len(crossings) - 1, 2):
            xa = max(0, int(crossings[index] + 0.5))
            xb = min(width, int(crossings[index + 1] + 0.5))
            if xb <= xa:
                continue
            start = (y * width + xa) * 4
            span = xb - xa
            if alpha >= 255:
                buf[start:start + span * 4] = bytes((row_color[0], row_color[1], row_color[2], 255)) * span
            else:
                ratio = alpha / 255.0
                for offset in range(span):
                    pos = start + offset * 4
                    buf[pos] = int(buf[pos] * (1 - ratio) + row_color[0] * ratio)
                    buf[pos + 1] = int(buf[pos + 1] * (1 - ratio) + row_color[1] * ratio)
                    buf[pos + 2] = int(buf[pos + 2] * (1 - ratio) + row_color[2] * ratio)


def round_rect(buf, width, x0, y0, x1, y1, radius, color, alpha=255, gradient=None):
    r = min(radius, (x1 - x0) / 2, (y1 - y0) / 2)
    points = []
    for cx, cy, start, end in [
        (x1 - r, y0 + r, -90, 0),
        (x1 - r, y1 - r, 0, 90),
        (x0 + r, y1 - r, 90, 180),
        (x0 + r, y0 + r, 180, 270),
    ]:
        steps = 10
        for step in range(steps + 1):
            angle = math.radians(start + (end - start) * step / steps)
            points.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    fill_polygon(buf, width, points, color, alpha, gradient)


def ellipse(buf, width, cx, cy, rx, ry, color, alpha=255):
    points = []
    steps = 56
    for step in range(steps):
        angle = 2 * math.pi * step / steps
        points.append((cx + rx * math.cos(angle), cy + ry * math.sin(angle)))
    fill_polygon(buf, width, points, color, alpha)


def bezier(p0, p1, p2, p3, steps=48):
    out = []
    for step in range(steps + 1):
        t = step / steps
        mt = 1 - t
        x = mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0]
        y = mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1]
        out.append((x, y))
    return out


def stroke(buf, width, points, thickness, color):
    for x, y in points:
        ellipse(buf, width, x, y, thickness / 2, thickness / 2, color)


def arc_band(buf, width, cx, cy, outer, inner, start_deg, sweep_deg, color):
    points = []
    steps = 96
    for step in range(steps + 1):
        angle = math.radians(start_deg + sweep_deg * step / steps)
        points.append((cx + outer * math.cos(angle), cy + outer * math.sin(angle)))
    for step in range(steps, -1, -1):
        angle = math.radians(start_deg + sweep_deg * step / steps)
        points.append((cx + inner * math.cos(angle), cy + inner * math.sin(angle)))
    fill_polygon(buf, width, points, color)


def draw(size, detailed):
    width = size * SS
    k = width / DESIGN
    buf = bytearray(width * width * 4)

    def s(value):
        return value * k

    # 圓角方底
    round_rect(buf, width, 0, 0, width, width, s(232), LIME_TOP, gradient=(LIME_TOP, LIME_BOTTOM))

    # 限速錶環：缺口朝下，兩端做成圓頭
    arc_band(buf, width, s(512), s(512), s(404), s(318), 132, 276, INK)
    for angle_deg in (132, 408):
        angle = math.radians(angle_deg)
        radius = s(361)
        ellipse(buf, width, s(512) + radius * math.cos(angle), s(512) + radius * math.sin(angle),
                s(43), s(43), INK)

    # 水滴：頂端被錶環壓住
    points = []
    points += bezier((s(512), s(292)), (s(384), s(388)), (s(317), s(494)), (s(317), s(596)))
    points += bezier((s(317), s(596)), (s(317), s(712)), (s(404), s(792)), (s(512), s(792)))
    points += bezier((s(512), s(792)), (s(620), s(792)), (s(707), s(712)), (s(707), s(596)))
    points += bezier((s(707), s(596)), (s(707), s(494)), (s(640), s(388)), (s(512), s(292)))
    fill_polygon(buf, width, points, DROP)

    # 表情
    ellipse(buf, width, s(442), s(586), s(44), s(50), WHITE)
    ellipse(buf, width, s(582), s(586), s(44), s(50), WHITE)
    ellipse(buf, width, s(450), s(594), s(21), s(21), INK)
    ellipse(buf, width, s(590), s(594), s(21), s(21), INK)

    if detailed:
        ellipse(buf, width, s(436), s(576), s(8), s(8), WHITE)
        ellipse(buf, width, s(576), s(576), s(8), s(8), WHITE)
        ellipse(buf, width, s(362), s(654), s(32), s(22), BLUSH, alpha=210)
        ellipse(buf, width, s(662), s(654), s(32), s(22), BLUSH, alpha=210)
        smile = bezier((s(482), s(664)), (s(490), s(700)), (s(534), s(700)), (s(542), s(664)), steps=24)
        stroke(buf, width, smile, s(16), INK)

    return downsample(buf, width, size)


def downsample(buf, width, size):
    out = bytearray(size * size * 4)
    step = width // size
    area = step * step
    for y in range(size):
        for x in range(size):
            r = g = b = a = 0
            base_y = y * step
            for dy in range(step):
                row = ((base_y + dy) * width + x * step) * 4
                for dx in range(step):
                    pos = row + dx * 4
                    alpha = buf[pos + 3]
                    r += buf[pos] * alpha
                    g += buf[pos + 1] * alpha
                    b += buf[pos + 2] * alpha
                    a += alpha
            out_pos = (y * size + x) * 4
            if a == 0:
                out[out_pos:out_pos + 4] = b"\x00\x00\x00\x00"
            else:
                out[out_pos] = r // a
                out[out_pos + 1] = g // a
                out[out_pos + 2] = b // a
                out[out_pos + 3] = a // area
    return out


def write_png(path, size, rgba):
    raw = b"".join(b"\x00" + bytes(rgba[y * size * 4:(y + 1) * size * 4]) for y in range(size))

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)))
        handle.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        handle.write(chunk(b"IEND", b""))


def write_ico(path, images):
    entries = []
    payloads = []
    offset = 6 + 16 * len(images)

    for size, rgba in images:
        header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0)
        pixels = bytearray(size * size * 4)
        for y in range(size):
            source = (size - 1 - y) * size * 4
            target = y * size * 4
            for x in range(size):
                src = source + x * 4
                dst = target + x * 4
                pixels[dst] = rgba[src + 2]
                pixels[dst + 1] = rgba[src + 1]
                pixels[dst + 2] = rgba[src]
                pixels[dst + 3] = rgba[src + 3]
        stride = ((size + 31) // 32) * 4
        payload = header + bytes(pixels) + b"\x00" * (stride * size)
        payloads.append(payload)
        entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(payload), offset))
        offset += len(payload)

    with open(path, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, len(images)))
        for entry in entries:
            handle.write(entry)
        for payload in payloads:
            handle.write(payload)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    assets = os.path.join(root, "src", "Tidegate.App", "Assets")
    artifacts = os.path.join(root, "artifacts")
    os.makedirs(assets, exist_ok=True)
    os.makedirs(artifacts, exist_ok=True)

    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [(size, draw(size, detailed=size >= 32)) for size in sizes]
    write_ico(r"D:\CODE\NetTool\src\Tidegate.App\Assets\tidegate.ico", images)

    large = draw(512, detailed=True)
    write_png(r"D:\CODE\NetTool\src\Tidegate.App\Assets\tidegate.png", 512, large)
    write_png(r"D:\CODE\NetTool\artifacts\icon-preview.png", 512, large)

    # 小尺寸放大排在一起，檢查縮小後還看不看得懂
    cell = 64
    strip = bytearray(cell * 4 * cell * 4)
    for index, size in enumerate([16, 24, 32, 48]):
        small = draw(size, detailed=size >= 32)
        ox = index * cell
        for y in range(cell):
            for x in range(cell):
                sx = min(size - 1, x * size // cell)
                sy = min(size - 1, y * size // cell)
                src = (sy * size + sx) * 4
                dst = (y * cell * 4 + ox + x) * 4
                strip[dst:dst + 4] = small[src:src + 4]
    write_png(r"D:\CODE\NetTool\artifacts\icon-small.png", cell * 4, strip)
    print("ok")


main()
