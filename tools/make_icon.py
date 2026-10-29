# -*- coding: utf-8 -*-
"""生成「速动」应用图标：圆角方形 + 蓝紫对角渐变 + 白色闪电。
超采样抗锯齿，输出多尺寸 app.ico 与 256px logo.png。"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_DIR = os.path.join(ROOT, "src", "QuickLauncher.App", "assets")
os.makedirs(OUT_DIR, exist_ok=True)

SS = 4                    # 超采样倍率
BASE = 256
S = BASE * SS             # 1024

def hex2rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))

C1 = np.array(hex2rgb("#38BDF8"), dtype=np.float64)   # 左上 青蓝
C2 = np.array(hex2rgb("#4F6BFF"), dtype=np.float64)   # 中   蓝
C3 = np.array(hex2rgb("#8B5CF6"), dtype=np.float64)   # 右下 紫

# ---------- 1. 对角渐变底板（三段插值） ----------
yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
t = (xx / S * 0.5 + yy / S * 0.5)                     # 0..1 对角参数
grad = np.empty((S, S, 3), dtype=np.float64)
m1 = t < 0.5
t1 = (t / 0.5)[..., None]
t2 = ((t - 0.5) / 0.5)[..., None]
grad[m1] = C1 * (1 - t1[m1]) + C2 * t1[m1]
grad[~m1] = C2 * (1 - t2[~m1]) + C3 * t2[~m1]

# 左上径向高光，提升质感
cx, cy = S * 0.28, S * 0.20
d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (S * 0.9)
glow = np.clip(1 - d, 0, 1) ** 2 * 0.28
grad += glow[..., None] * 255.0
grad = np.clip(grad, 0, 255).astype(np.uint8)
base = Image.fromarray(grad, "RGB").convert("RGBA")

# ---------- 2. 圆角矩形蒙版 ----------
radius = int(S * 0.225)
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)

# 精致亮边（在蒙版内侧描一圈淡白边）
edge = Image.new("L", (S, S), 0)
ed = ImageDraw.Draw(edge)
ed.rounded_rectangle([SS, SS, S - 1 - SS, S - 1 - SS],
                     radius=radius - SS, outline=255, width=SS * 2)

base.putalpha(mask)

# ---------- 3. 闪电 ----------
def bolt_points(cx, cy, scale):
    rel = [(62, -268), (-124, 44), (-22, 44), (-96, 268),
           (132, -34), (30, -34)]
    return [(cx + x * scale, cy + y * scale) for x, y in rel]

bolt = Image.new("L", (S, S), 0)
bd = ImageDraw.Draw(bolt)
bd.polygon(bolt_points(S * 0.5, S * 0.505, SS * 1.02), fill=255)
bolt = bolt.filter(ImageFilter.GaussianBlur(SS * 0.4))  # 轻微柔化边缘

# 闪电外发光
glow_layer = bolt.filter(ImageFilter.GaussianBlur(S * 0.03))
glow_rgba = Image.new("RGBA", (S, S), (255, 255, 255, 0))
glow_rgba.putalpha(glow_layer.point(lambda a: int(a * 0.45)))

# 闪电柔和投影（向右下偏移，增强深度）
shadow = Image.new("RGBA", (S, S), (20, 16, 48, 0))
sh_mask = bolt.point(lambda a: int(a * 0.35)).transform(
    (S, S), Image.AFFINE, (1, 0, -SS * 2.5, 0, 1, SS * 3.0))
shadow.putalpha(sh_mask.filter(ImageFilter.GaussianBlur(SS * 2.0)))

# ---------- 4. 合成 ----------
icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
icon.alpha_composite(base)
icon.alpha_composite(glow_rgba)
icon.alpha_composite(shadow)
white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
white.putalpha(bolt)
icon.alpha_composite(white)

# 亮边叠到最上（细白线，仅在边缘内侧）
edge_rgba = Image.new("RGBA", (S, S), (255, 255, 255, 60))
edge_rgba.putalpha(edge)
icon.alpha_composite(edge_rgba)

# ---------- 5. 输出 ----------
final = icon.resize((BASE, BASE), Image.LANCZOS)
final.save(os.path.join(OUT_DIR, "logo.png"))

sizes = [16, 24, 32, 48, 64, 128, 256]
ico = final.resize((256, 256), Image.LANCZOS)
ico.save(os.path.join(OUT_DIR, "app.ico"),
         sizes=[(s, s) for s in sizes])

print("OK ->", os.path.join(OUT_DIR, "logo.png"))
print("OK ->", os.path.join(OUT_DIR, "app.ico"))
print("sizes:", os.path.getsize(os.path.join(OUT_DIR, "app.ico")), "bytes")
