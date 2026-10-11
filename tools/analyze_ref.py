"""分析「流程图参考图.png」的真实版面结构（不猜测文字，只输出可测量的事实）。

用法:
    C:/Users/admin/.workbuddy/binaries/python/envs/default/Scripts/python.exe tools/analyze_ref.py
"""
import os
import sys
from collections import Counter

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, "流程图参考图.png")


def main():
    if not os.path.exists(PATH):
        print("MISSING:", PATH)
        return 1

    im = Image.open(PATH)
    print("=== 基本信息 ===")
    print("size        :", im.size, "(w x h)")
    print("mode        :", im.mode)
    print("aspect      : %.3f" % (im.size[0] / im.size[1]))

    rgb = im.convert("RGB")
    w, h = rgb.size

    print()
    print("=== 主色板 (top 12, 含占比) ===")
    small = rgb.resize((min(w, 400), min(h, 400)))
    cnt = Counter(small.getdata())
    total = sum(cnt.values())
    for c, n in cnt.most_common(12):
        print("  #%02X%02X%02X  %5.2f%%" % (c[0], c[1], c[2], 100.0 * n / total))

    # ---- 行 / 列 明暗剖面：找面板分界 ----
    print()
    print("=== 列向亮度剖面（每 1/40 宽采样，暗=0 亮=9）===")
    cols = 40
    step = max(1, w // cols)
    colvals = []
    for i in range(cols):
        box = (i * step, 0, min(w, (i + 1) * step), h)
        avg = avg_lum(rgb.crop(box))
        colvals.append(avg)
    print("  " + "".join(str(bucket(v)) for v in colvals))
    print("  x:", " ".join("%d" % (i * step) for i in range(0, cols, 5)))

    print()
    print("=== 行向亮度剖面（每 1/30 高采样，暗=0 亮=9）===")
    rows = 30
    step_r = max(1, h // rows)
    rowvals = []
    for i in range(rows):
        box = (0, i * step_r, w, min(h, (i + 1) * step_r))
        avg = avg_lum(rgb.crop(box))
        rowvals.append(avg)
    print("  " + "".join(str(bucket(v)) for v in rowvals))
    print("  y:", " ".join("%d" % (i * step_r) for i in range(0, rows, 5)))

    # ---- 纵向分割线：列间亮度突变 ----
    print()
    print("=== 纵向分隔带（列亮度突变 > 12 的位置，按 x 升序，合并邻近）===")
    prof = col_profile(rgb, samples=200)
    jumps = []
    for i in range(1, len(prof)):
        if abs(prof[i][1] - prof[i - 1][1]) > 12:
            jumps.append(prof[i][0])
    merged = merge(jumps, w * 0.02)
    print("  ", merged if merged else "(无显著突变)")

    print()
    print("=== 横向分隔带（行亮度突变 > 12 的位置）===")
    prof_r = row_profile(rgb, samples=200)
    jumps_r = []
    for i in range(1, len(prof_r)):
        if abs(prof_r[i][1] - prof_r[i - 1][1]) > 12:
            jumps_r.append(prof_r[i][0])
    merged_r = merge(jumps_r, h * 0.02)
    print("  ", merged_r if merged_r else "(无显著突变)")

    # ---- 文字/高对比像素密度：推断文本行位置 ----
    print()
    print("=== 文本行密度（每行的高对比像素数，取 top 15 行区段）===")
    g = rgb.convert("L")
    px = g.load()
    band = max(1, h // 120)
    dens = []
    for i in range(120):
        y0, y1 = i * band, min(h, (i + 1) * band)
        c = 0
        for y in range(y0, y1, 2):
            prev = px[0, y]
            for x in range(1, w, 3):
                v = px[x, y]
                if abs(v - prev) > 45:
                    c += 1
                prev = v
        dens.append((y0, c))
    dens.sort(key=lambda t: -t[1])
    for y0, c in dens[:15]:
        print("   y=%4d  density=%d" % (y0, c))

    # ---- 粗粒度色块地图（便于人工判读分区）----
    print()
    print("=== 粗粒度分区图 (48x26)，字符=主色类 ===")
    gw, gh = 48, 26
    bw, bh = w // gw, h // gh
    for gy in range(gh):
        line = []
        for gx in range(gw):
            box = (gx * bw, gy * bh, min(w, (gx + 1) * bw), min(h, (gy + 1) * bh))
            r, gg, b = avg_rgb(rgb.crop(box))
            line.append(classify(r, gg, b))
        print("  %2d|%s" % (gy * bh, "".join(line)))
    print("     " + "".join(str((i // 10) % 10) if i % 10 == 0 else " " for i in range(gw)))
    return 0


def avg_lum(img):
    g = img.convert("L")
    d = list(g.getdata())
    return sum(d) / float(len(d))


def avg_rgb(img):
    d = list(img.getdata())
    n = len(d)
    return (sum(p[0] for p in d) / n, sum(p[1] for p in d) / n, sum(p[2] for p in d) / n)


def bucket(lum):
    return min(9, max(0, int(lum / 25.6)))


def col_profile(img, samples=200):
    w, h = img.size
    step = max(1, w // samples)
    out = []
    for i in range(samples):
        x0 = i * step
        x1 = min(w, x0 + step)
        if x1 <= x0:
            break
        out.append((x0, avg_lum(img.crop((x0, 0, x1, h)))))
    return out


def row_profile(img, samples=200):
    w, h = img.size
    step = max(1, h // samples)
    out = []
    for i in range(samples):
        y0 = i * step
        y1 = min(h, y0 + step)
        if y1 <= y0:
            break
        out.append((y0, avg_lum(img.crop((0, y0, w, y1)))))
    return out


def merge(vals, tol):
    if not vals:
        return []
    vals = sorted(vals)
    out = [[vals[0]]]
    for v in vals[1:]:
        if v - out[-1][-1] <= tol:
            out[-1].append(v)
        else:
            out.append([v])
    return [int(sum(g) / len(g)) for g in out]


def classify(r, g, b):
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    mx, mn = max(r, g, b), min(r, g, b)
    sat = mx - mn
    if lum < 45:
        return "#"          # 极深（深色底/标题栏）
    if lum < 90:
        return "+"          # 深灰
    if sat < 18:
        return "." if lum > 200 else (":" if lum > 140 else "-")
    if b > r and b > g:
        return "B"          # 偏蓝（选中/链接）
    if r > g and r > b:
        return "R"          # 偏红
    if g > r and g > b:
        return "G"          # 偏绿
    return "o"


if __name__ == "__main__":
    sys.exit(main())
