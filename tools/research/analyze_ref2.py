"""第二遍：精确量出各面板的像素边界矩形（只输出测量结果）。"""
import os, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
im = Image.open(os.path.join(ROOT, "流程图参考图.png")).convert("RGB")
W, H = im.size
px = im.load()

DARK = 90          # 深色阈值（画布/图像区）
BLUE_MIN = 25      # B 通道比 R/G 高多少算“蓝色高亮”

# ---- 1. 深色大块的精确 bbox ----
col_dark = [0]*W
row_dark = [0]*H
for x in range(0, W, 2):
    for y in range(0, H, 2):
        r,g,b = px[x,y]
        if (r+g+b)/3 < DARK:
            col_dark[x]+=1; row_dark[y]+=1

def runs(arr, thr):
    out=[]; s=None
    for i,v in enumerate(arr):
        if v>=thr:
            if s is None: s=i
        else:
            if s is not None: out.append((s,i-1)); s=None
    if s is not None: out.append((s,len(arr)-1))
    return out

ncols = H//2; nrows = W//2
print("=== 深色区（画布/图像）===")
cx = [r for r in runs(col_dark, ncols*0.5)]
cy = [r for r in runs(row_dark, nrows*0.5)]
if cx: print("  横向候选 x 段:", [(a*1,b*1) for a,b in cx if b-a>50])
if cy: print("  纵向候选 y 段:", [(a*1,b*1) for a,b in cy if b-a>50])

# ---- 2. 蓝色高亮像素分布（左侧树/列表的选中项、链接）----
print()
print("=== 蓝色高亮像素聚集区（按 40px 网格统计，top 20）===")
grid = {}
for y in range(0, H, 4):
    for x in range(0, W, 4):
        r,g,b = px[x,y]
        if b-r > BLUE_MIN and b-g > BLUE_MIN:
            k = (x//40, y//40)
            grid[k] = grid.get(k,0)+1
top = sorted(grid.items(), key=lambda t:-t[1])[:20]
for (gx,gy),c in top:
    print("   区块 x=%4d..%4d  y=%4d..%4d  count=%d" % (gx*40, gx*40+39, gy*40, gy*40+39, c))

# ---- 3. 底部区域：最后 250px 里的内容行 ----
print()
print("=== 底部 250px 内容行（y 从 %d 起，非背景像素占比）===" % (H-250))
BG = (0xF0,0xF2,0xF5)
for y in range(H-250, H, 10):
    n=0; tot=0
    for x in range(0, W, 3):
        r,g,b = px[x,y]
        tot+=1
        if abs(r-BG[0])>28 or abs(g-BG[1])>28 or abs(b-BG[2])>28:
            n+=1
    bar = "#"*int(40*n/max(1,tot))
    print("   y=%4d %5.1f%% %s" % (y, 100.0*n/max(1,tot), bar))
