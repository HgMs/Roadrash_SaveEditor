#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把游戏安装目录里的美术素材提取、压缩、内嵌进界面模板，生成单文件 UI。

用法:
    python src/build_assets.py --game "C:\\Games\\Roadrash"
    python src/build_assets.py --no-assets          # 生成无素材的占位版（CI / 没有游戏时）

素材来自玩家自己的游戏安装，脚本不包含也不分发任何游戏资源。
"""
import argparse
import base64
import io
import json
import os
import sys

try:
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    sys.exit("需要 Pillow 与 numpy：pip install pillow numpy")

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = os.path.join(HERE, "ui", "template.html")
DEFAULT_OUT = os.path.join(HERE, "ui", "rrs_editor.html")

# 15 台车 → 游戏内的机车特写图
BIKES = [
    ("Rat", "Banzai"), ("Rat", "Corsair"), ("Rat", "Kamikaze"), ("Rat", "Killer"), ("Rat", "Perro"),
    ("Super", "Banzai"), ("Super", "Corsair"), ("Super", "Diablo"), ("Super", "Kamikaze"), ("Super", "Stiletto"),
    ("Sport", "Diablo"), ("Sport", "Dmg"), ("Sport", "Kamikaze"), ("Sport", "Perro"), ("Sport", "Stiletto"),
]
# 5 条赛道 → 游戏里的明信片（顺序必须与界面上的赛道一致）
POSTCARDS = ["City", "Sierra", "Penin", "Napa", "Pch"]


def find_game():
    """尝试从注册表和常见路径里找到游戏目录。"""
    cands = []
    try:
        import winreg
        for root in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            for sub in (r"SOFTWARE\Electronic Arts\RoadRash 95",
                        r"SOFTWARE\WOW6432Node\Electronic Arts\RoadRash 95"):
                try:
                    with winreg.OpenKey(root, sub) as k:
                        p, _ = winreg.QueryValueEx(k, "Path")
                        if p:
                            cands.append(os.path.abspath(p))
                except OSError:
                    pass
    except ImportError:
        pass

    for base in ("C:\\Games", "D:\\Games", "E:\\Games", "D:\\", "E:\\"):
        try:
            for d in os.listdir(base):
                if "road" in d.lower() or "rash" in d.lower():
                    cands.append(os.path.join(base, d))
        except OSError:
            pass

    for c in cands:
        if os.path.isdir(os.path.join(c, "Images")):
            return c
    return None


def jpeg_uri(im, q=62):
    buf = io.BytesIO()
    im.convert("RGB").save(buf, "JPEG", quality=q, optimize=True, progressive=True)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def cover(im, w, h, fx=0.5, fy=0.5, zoom=1.0):
    sw, sh = im.size
    s = max(w / sw, h / sh) * zoom
    nw, nh = max(1, int(sw * s + 0.5)), max(1, int(sh * s + 0.5))
    im2 = im.resize((nw, nh), Image.LANCZOS)
    x = int((nw - w) * fx)
    y = int((nh - h) * fy)
    return im2.crop((x, y, x + w, y + h))


def make_bg(w=1200, h=900, seed=7):
    """程序化生成的脏旧墙面（不依赖游戏素材，兜底用）。"""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    t = yy / h
    base = np.zeros((h, w, 3), dtype=np.float32)
    base[..., 0] = 34 - 16 * t
    base[..., 1] = 24 - 12 * t
    base[..., 2] = 19 - 9 * t
    for _ in range(26):
        cx, cy = rng.integers(0, w), rng.integers(0, h)
        r = rng.integers(80, 380)
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
        m = np.clip(1 - d, 0, 1) ** 2 * rng.uniform(0.10, 0.30)
        tint = rng.uniform(0.6, 1.4, 3)
        for c in range(3):
            base[..., c] -= m * 26 * tint[c]
    for scale, amp in ((1, 16), (4, 9), (12, 5)):
        n = rng.normal(0, 1, (h // scale + 2, w // scale + 2))
        n = np.array(Image.fromarray(((n - n.min()) / (np.ptp(n) + 1e-9) * 255).astype(np.uint8))
                     .resize((w, h), Image.BICUBIC), dtype=np.float32) / 255.0 - 0.5
        base += (n * amp)[..., None]
    im = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(im, "RGBA")
    for _ in range(220):
        x0, y0 = rng.integers(-40, w), rng.integers(-40, h)
        ln = rng.integers(20, 160)
        ang = rng.uniform(-0.5, 0.5)
        v = rng.integers(110, 190)
        d.line([(x0, y0), (x0 + ln * np.cos(ang), y0 + ln * np.sin(ang))],
               fill=(v, v - 20, v - 40, int(rng.integers(10, 34))), width=1)
    arr = np.array(im, dtype=np.float32)
    d2 = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2)
    arr *= np.clip(1 - 0.55 * d2 ** 1.6, 0, 1)[..., None]
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))


def build(game_dir, out_path):
    assets = {}
    img_root = os.path.join(game_dir, "Images")

    for i, (cls, name) in enumerate(BIKES):
        p = os.path.join(img_root, "Bikes", cls, name + ".rri")
        assets["bike%d" % i] = jpeg_uri(cover(Image.open(p), 240, 135, 0.5, 0.42, 1.06), 60)

    for i, name in enumerate(POSTCARDS):
        p = os.path.join(img_root, "Postcard", name + ".rri")
        assets["course%d" % i] = jpeg_uri(cover(Image.open(p), 260, 150, 0.5, 0.33, 1.0), 64)

    show = os.path.join(img_root, "Showroom", "Super", "Bikes.rri")
    if os.path.isfile(show):
        bg = cover(Image.open(show).convert("RGB"), 1200, 900, 0.5, 0.5, 1.15)
        arr = np.array(bg.filter(ImageFilter.GaussianBlur(13)), dtype=np.float32) * 1.55
        gr = np.array(make_bg(), dtype=np.float32)
        assets["bg"] = jpeg_uri(Image.fromarray(np.clip(arr * 0.72 + gr * 0.38, 0, 255).astype(np.uint8)), 50)
    else:
        assets["bg"] = jpeg_uri(make_bg(), 54)

    write(out_path, assets)
    return assets


def write(out_path, assets):
    tpl = open(TEMPLATE, encoding="utf-8").read()
    payload = json.dumps(assets, ensure_ascii=False)
    out = tpl.replace("/*__ASSETS__*/", "window.ASSETS=" + payload + ";")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    open(out_path, "w", encoding="utf-8").write(out)
    return os.path.getsize(out_path)


def main():
    ap = argparse.ArgumentParser(description="生成修改器界面（内嵌游戏美术素材）")
    ap.add_argument("--game", help="游戏安装目录（含 Images 子目录）")
    ap.add_argument("--out", default=DEFAULT_OUT, help="输出 html 路径")
    ap.add_argument("--no-assets", action="store_true", help="不读取游戏素材，生成占位版")
    a = ap.parse_args()

    if a.no_assets:
        size = write(a.out, {})
        print("built placeholder UI: %s (%.0f KB)" % (a.out, size / 1024))
        return

    game = a.game or find_game()
    if not game or not os.path.isdir(os.path.join(game, "Images")):
        sys.exit("找不到游戏目录（需要其中包含 Images\\）。请用 --game 指定，例如:\n"
                 "  python src/build_assets.py --game \"C:\\Games\\Roadrash\"\n"
                 "或者用 --no-assets 生成不含素材的占位版。")

    assets = build(game, a.out)
    print("built UI: %s  (%.0f KB, %d assets, game=%s)"
          % (a.out, os.path.getsize(a.out) / 1024, len(assets), game))


if __name__ == "__main__":
    main()
