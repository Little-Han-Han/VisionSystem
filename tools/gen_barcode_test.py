"""
生成 EAN-13 条码测试图（给 SmokeAlgo 用）。

为什么用 EAN-13 而不是二维码：本机无法安装二维码生成库（PyPI 不可达），
而 EAN-13 的编码表是固定的 95 个模块，手写 20 行就能生成**确定可解**的图。
二维码解码路径由 zxing-cpp 自身的成熟实现保证，本图只用于验证"接入是否正确"。

EAN-13 结构：101 + 左 6 位(7 模块/位) + 01010 + 右 6 位(7 模块/位) + 101 = 95 模块
左半用 L / G 两种码字（由首位决定组合），右半用 R（= L 取反）。
"""

import sys
from PIL import Image, ImageDraw

L = ["0001101", "0011001", "0010011", "0111101", "0100011",
     "0110001", "0101111", "0111011", "0110111", "0001011"]

# 首位数字 -> 左半 6 位的 L/G 组合（G = 1，L = 0）
PARITY = ["000000", "001011", "001101", "001110", "010011",
          "011001", "011100", "010101", "010110", "011010"]


def r_code(d):
    return "".join("1" if c == "0" else "0" for c in L[d])


def g_code(d):
    return r_code(d)[::-1]


def check_digit(twelve):
    s = 0
    for i, ch in enumerate(twelve):
        s += int(ch) * (1 if i % 2 == 0 else 3)
    return (10 - s % 10) % 10


def encode_ean13(digits12):
    d = digits12 + str(check_digit(digits12))
    first = int(d[0])
    parity = PARITY[first]
    left = ""
    for i in range(6):
        v = int(d[1 + i])
        left += g_code(v) if parity[i] == "1" else L[v]
    right = "".join(r_code(int(d[7 + i])) for i in range(6))
    return d, "101" + left + "01010" + right + "101"


def draw_ean13(digits12, module=4, bar_h=110, quiet=12):
    d, bits = encode_ean13(digits12)
    assert len(bits) == 95, len(bits)

    w = (len(bits) + 2 * quiet) * module
    h = bar_h + 30
    img = Image.new("L", (w, h), 255)
    dr = ImageDraw.Draw(img)

    for i, b in enumerate(bits):
        if b != "1":
            continue
        x0 = (quiet + i) * module
        dr.rectangle([x0, 10, x0 + module - 1, 10 + bar_h - 1], fill=0)

    return d, img


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "Image"

    # 1) 单个 EAN-13（正放）
    text, img = draw_ean13("690123456789")
    img.save(out + "/barcode_ean13.png")
    print("barcode_ean13.png ->", text)

    # 2) 两个码**上下排**（各自占一段扫描行）
    #    【为什么不是并排】zxing 的一维码扫描是"逐行找最长的条纹段"，
    #    两个一维码并排在同一水平带上时，同一行里只取到一段，通常只出一个结果；
    #    上下排各占自己的行带就能都出来。这条限制已写进文档，不是本实现的 bug。
    t1, a = draw_ean13("690123456789")
    t2, b = draw_ean13("400638133393")
    W = max(a.width, b.width) + 8
    H = a.height + b.height + 12
    canvas = Image.new("L", (W, H), 255)
    canvas.paste(a, (4, 4))
    canvas.paste(b, (4, a.height + 8))
    canvas.save(out + "/barcode_two.png")
    print("barcode_two.png ->", t1, "+", t2)

    # 3) 单个 EAN-13 旋转 90°（验证多方向参数）
    _, r = draw_ean13("690123456789")
    r.rotate(90, expand=True).save(out + "/barcode_rot90.png")
    print("barcode_rot90.png -> rot90")


if __name__ == "__main__":
    main()
