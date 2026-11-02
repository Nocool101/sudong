# -*- coding: utf-8 -*-
"""生成「速动」应用图标。

用法：
    python tools/make_icon.py <源图路径>     导入模式：用一张美术稿生成应用图标（默认）
    python tools/make_icon.py --alt          备选：生成风格化闪电图标（仅供对比，不会直接覆盖）

【导入模式做的事】
    1. 自动识别圆角方块的真实边界（源图四周往往是纯黑留白，直接用会显示成黑方块）；
    2. 裁成正方形，并按识别到的半径补出「透明圆角」；
    3. 切出「去字标」的图形版（顶部居中的正方形）——
       16/24/32px 里「速动 SUDONG」字标只会糊成灰斑，故小尺寸用图形版；
    4. 输出 assets/logo.png（界面内用，256px）与 assets/app.ico（7 档尺寸，
       ≤32px 用图形版，≥48px 用完整原画）。

【与 tools/MakeIcon 的关系】
两套实现产出同一套设计。本机没有 Python 环境，实际出图走 C# 版（tools/MakeIcon，
渲染用 WPF）；本脚本供有 Python + PIL 的环境使用。改设计时两边需同步。
"""
import io
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_DIR = os.path.join(ROOT, "src", "QuickLauncher.App", "assets")
os.makedirs(OUT_DIR, exist_ok=True)

BASE = 128               # logo.png 输出边长（界面内显示 24~34 DIP，兼顾高分屏）
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
DARK_THRESHOLD = 26       # 近黑像素视为图外留白

# ── 备选方案的配色与几何（与应用 UI 设计系统一致）──────────────
C1 = np.array([0x38, 0xBD, 0xF8], dtype=np.float64)   # 青蓝
C2 = np.array([0x4F, 0x6B, 0xFF], dtype=np.float64)   # 蓝
C3 = np.array([0x8B, 0x5C, 0xF6], dtype=np.float64)   # 紫
BOLT = [
    (15.0, 4.8), (5.4, 15.0), (10.6, 15.0),
    (9.0, 19.2), (18.6, 9.0), (13.2, 9.0),
]


# ══════════════════════════════════════════════════════════════
# 导入模式
# ══════════════════════════════════════════════════════════════

def detect_and_mask(src):
    """识别内容边界 → 裁正方形 → 补透明圆角。返回 (RGBA 图, 边长, 圆角半径)"""
    rgb = src.convert("RGB")
    w, h = rgb.size
    arr = np.asarray(rgb).astype(np.int16)
    content = arr.max(axis=2) > DARK_THRESHOLD
    ys, xs = np.nonzero(content)
    if len(xs) == 0:
        raise SystemExit("图片没有可识别的内容")
    min_x, max_x, min_y, max_y = xs.min(), xs.max(), ys.min(), ys.max()

    side = min(max(max_x - min_x + 1, max_y - min_y + 1), min(w, h))
    cx, cy = (min_x + max_x) // 2, (min_y + max_y) // 2
    sx = max(0, min(cx - side // 2, w - side))
    sy = max(0, min(cy - side // 2, h - side))
    square = rgb.crop((sx, sy, sx + side, sy + side))

    # 沿对角线探测圆角半径
    radius = 0
    px = square.load()
    for d in range(side // 3):
        r, g, b = px[d, d]
        if max(r, g, b) > DARK_THRESHOLD:
            radius = d
            break
    radius = max(0, min(radius, side // 2))
    print("  内容边界: x[%d..%d] y[%d..%d]  取正方形 %dpx @ (%d,%d)  圆角半径 ≈ %dpx"
          % (min_x, max_x, min_y, max_y, side, sx, sy, radius))

    # 圆角遮罩
    mask = Image.new("L", (side, side), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, side - 1, side - 1],
                                            radius=radius, fill=255)
    out = square.convert("RGBA")
    out.putalpha(mask)
    return out, side, radius


def crop_top_square(img, ratio):
    """【已停用】取顶部居中的正方形会切掉圆环两侧。
    现在要求图标完整显示，不再做任何裁剪，保留此函数仅供将来参考。"""
    w, h = img.size
    side = int(round(min(w, h) * ratio))
    x = (w - side) // 2
    return img.crop((x, 0, x + side, side))


def write_multi_ico(images_by_size, path):
    """按尺寸写入 ICO 容器（每档内嵌 PNG）"""
    payloads = []
    for size in ICO_SIZES:
        buf = io.BytesIO()
        images_by_size[size].save(buf, format="PNG")
        payloads.append(buf.getvalue())

    offset = 6 + 16 * len(ICO_SIZES)
    entries = []
    for i, (size, data) in enumerate(zip(ICO_SIZES, payloads)):
        entries.append(struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0, 0, 1, 32, len(data), offset))
        offset += len(data)

    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(ICO_SIZES)))
        for entry in entries:
            f.write(entry)
        for data in payloads:
            f.write(data)


def import_mode(src_path):
    src = Image.open(src_path)
    print("  源图: %dx%d  模式=%s" % (src.width, src.height, src.mode))

    artwork, _, _ = detect_and_mask(src)

    # 完整显示：不裁剪任何内容，各尺寸一律等比缩放到满幅。
    # 用 Image.BOX（面积平均）缩放：从 954px 缩到 16px 是 60 倍的极端缩小，
    # 常规插值缩放会留下硬边色块（马赛克），面积平均得到平滑的真实平均色。
    artwork.resize((BASE, BASE), Image.BOX).save(os.path.join(OUT_DIR, "logo.png"))

    images = {size: artwork.resize((size, size), Image.BOX) for size in ICO_SIZES}
    write_multi_ico(images, os.path.join(OUT_DIR, "app.ico"))

    print("OK ->", os.path.join(OUT_DIR, "logo.png"))
    print("OK ->", os.path.join(OUT_DIR, "app.ico"),
          "(%d bytes)" % os.path.getsize(os.path.join(OUT_DIR, "app.ico")))

# ══════════════════════════════════════════════════════════════
# 备选方案：风格化闪电（与应用 UI 同色系）
# ══════════════════════════════════════════════════════════════

def alt_mode():
    ss, base = 4, BASE
    s = base * ss
    u = s / 24.0

    yy, xx = np.mgrid[0:s, 0:s].astype(np.float64)
    t = (xx / s * 0.5 + yy / s * 0.5)
    grad = np.empty((s, s, 3), dtype=np.float64)
    m1 = t < 0.5
    t1 = (t / 0.5)[..., None]
    t2 = ((t - 0.5) / 0.5)[..., None]
    grad[m1] = C1 * (1 - t1[m1]) + C2 * t1[m1]
    grad[~m1] = C2 * (1 - t2[~m1]) + C3 * t2[~m1]

    d = np.sqrt((xx - s * 0.30) ** 2 + (yy - s * 0.22) ** 2) / (s * 0.62)
    grad += (np.clip(1 - d, 0, 1) ** 2 * 78)[..., None]
    d2 = np.sqrt((xx - s * 0.78) ** 2 + (yy - s * 0.82) ** 2) / (s * 0.70)
    grad *= (1 - (np.clip(1 - d2, 0, 1) ** 2 * 0.30)[..., None])
    grad = np.clip(grad, 0, 255).astype(np.uint8)
    base_img = Image.fromarray(grad, "RGB").convert("RGBA")

    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(5.4 * u), fill=255)
    base_img.putalpha(mask)

    bolt = Image.new("L", (s, s), 0)
    ImageDraw.Draw(bolt).polygon([(x * u, y * u) for x, y in BOLT], fill=255)

    shadow_alpha = Image.new("L", (s, s), 0)
    shadow_alpha.paste(bolt.filter(ImageFilter.GaussianBlur(s * 0.022))
                       .point(lambda a: int(a * 0.40)), (int(u * 0.30), int(u * 0.46)))
    shadow = Image.new("RGBA", (s, s), (16, 10, 44, 0))
    shadow.putalpha(shadow_alpha)

    glow = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    glow.putalpha(bolt.filter(ImageFilter.GaussianBlur(s * 0.030))
                  .point(lambda a: int(a * 0.30)))

    vg = np.linspace(0, 1, s)[:, None]
    rows = (np.array([255.0, 255.0, 255.0])[None, :] * (1 - vg)
            + np.array([0xE8, 0xEE, 0xFF])[None, :] * vg)[:, None, :]
    mark_img = Image.fromarray(np.repeat(rows, s, axis=1).astype(np.uint8), "RGB").convert("RGBA")
    mark_img.putalpha(bolt)

    edge = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    ImageDraw.Draw(edge).rounded_rectangle(
        [u * 0.14, u * 0.14, s - 1 - u * 0.14, s - 1 - u * 0.14],
        radius=int(5.4 * u - u * 0.14), outline=(255, 255, 255, 62), width=int(u * 0.16))

    icon = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    icon.alpha_composite(base_img)
    icon.alpha_composite(glow)
    icon.alpha_composite(shadow)
    icon.alpha_composite(mark_img)
    icon.alpha_composite(edge)

    final = icon.resize((base, base), Image.LANCZOS)
    final.save(os.path.join(OUT_DIR, "logo-alt.png"))
    images = {size: final.resize((size, size), Image.LANCZOS) for size in ICO_SIZES}
    write_multi_ico(images, os.path.join(OUT_DIR, "app-alt.ico"))
    print("OK ->", os.path.join(OUT_DIR, "logo-alt.png"))
    print("OK ->", os.path.join(OUT_DIR, "app-alt.ico"))


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        print("请指定源图路径，例如：python tools/make_icon.py art/sudong.png")
        sys.exit(1)
    if args[0] == "--alt":
        alt_mode()
    else:
        import_mode(args[0])
