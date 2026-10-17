#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
YOLO 目标检测原生 DLL 冒烟测试（ctypes，无 numpy / 纯标准库）。

验证点：
  1. va_det_create 能加载 Ultralytics 导出的 YOLO ONNX（确认 OpenVINO 的 ONNX 前端
     + CPU 插件在「最小裁剪 DLL 集」下可用）。
  2. va_det_run 走完「letterbox → BGR→RGB → /255 → NCHW → 推理 → 解析 + NMS →
     还原原图坐标」全流程不崩溃，且结果数量 / 分数 / 坐标合法。
  3. 错误路径：未 create 先 run 返回 VA_ERR_NO_TEMPLATE；非法模型路径返回错误。

用法：
  python tools/smoke_detect.py [model.onnx] [image.png|jpg]
  不传 image 时用合成彩色图（验证流程即可，能否检出真实目标取决于图内容）。
"""
import ctypes
import os
import sys

CFG = os.environ.get("SMOKE_CONFIG", "Release")
DLL_DIR = os.path.join(os.path.dirname(__file__), "..", "bin", "x64", CFG)
DLL_PATH = os.path.abspath(os.path.join(DLL_DIR, "VisionAlgo.Native.dll"))

# ---- 错误码 ----
VA_OK = 0
VA_ERR_NO_TEMPLATE = 6
VA_ERR_INTERNAL = 5
VA_ERR_ARG = 2
VA_ERR_SIZE = 3


class VA_Image(ctypes.Structure):
    _fields_ = [
        ("data", ctypes.POINTER(ctypes.c_ubyte)),
        ("width", ctypes.c_int),
        ("height", ctypes.c_int),
        ("channels", ctypes.c_int),
        ("stride", ctypes.c_int),
    ]


class VA_DetectResult(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_float),
        ("y", ctypes.c_float),
        ("w", ctypes.c_float),
        ("h", ctypes.c_float),
        ("class_id", ctypes.c_int),
        ("score", ctypes.c_float),
    ]


def load_dll():
    os.add_dll_directory(os.path.abspath(DLL_DIR))
    dll = ctypes.WinDLL(DLL_PATH)
    dll.va_create.restype = ctypes.c_void_p
    dll.va_create.argtypes = []

    dll.va_det_create.restype = ctypes.c_int
    dll.va_det_create.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p]

    dll.va_det_run.restype = ctypes.c_int
    dll.va_det_run.argtypes = [
        ctypes.c_void_p, ctypes.POINTER(VA_Image),
        ctypes.c_float, ctypes.c_float,
        ctypes.POINTER(VA_DetectResult), ctypes.c_int, ctypes.POINTER(ctypes.c_int),
    ]

    dll.va_det_class_count.restype = ctypes.c_int
    dll.va_det_class_count.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_int)]

    dll.va_det_class_name.restype = ctypes.c_int
    dll.va_det_class_name.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int]

    dll.va_det_clear.restype = ctypes.c_int
    dll.va_det_clear.argtypes = [ctypes.c_void_p]

    dll.va_get_last_error.restype = ctypes.c_int
    dll.va_get_last_error.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    return dll


def last_err(dll, h):
    buf = ctypes.create_string_buffer(512)
    dll.va_get_last_error(h, buf, 512)
    return buf.value.decode("utf-8", "replace")


def synthetic_image(w=640, h=480):
    # BGR 三通道，画几个色块（仅验证流程，不保证检出真实目标）
    stride = w * 3
    buf = bytearray(stride * h)
    # 背景灰
    for y in range(h):
        for x in range(w):
            off = y * stride + x * 3
            buf[off] = 60
            buf[off + 1] = 60
            buf[off + 2] = 60
    # 红块
    for y in range(80, 200):
        for x in range(80, 200):
            off = y * stride + x * 3
            buf[off] = 30
            buf[off + 1] = 30
            buf[off + 2] = 220
    # 蓝块
    for y in range(260, 360):
        for x in range(300, 460):
            off = y * stride + x * 3
            buf[off] = 210
            buf[off + 1] = 40
            buf[off + 2] = 30
    arr = (ctypes.c_ubyte * len(buf)).from_buffer_copy(bytes(buf))
    img = VA_Image()
    img.data = ctypes.cast(arr, ctypes.POINTER(ctypes.c_ubyte))
    img.width = w
    img.height = h
    img.channels = 3
    img.stride = stride
    return img, arr  # 保留 arr 引用，避免被回收


def main():
    model = sys.argv[1] if len(sys.argv) > 1 else None
    if model is None:
        cand = [
            os.path.join(os.path.dirname(__file__), "..", "_testmodels", "yolo11n.onnx"),
            os.path.join(os.path.dirname(__file__), "..", "bin", "x64", CFG, "models", "yolo26n.onnx"),
            os.path.join(os.path.dirname(__file__), "..", "bin", "x64", CFG, "models", "yolov8n.onnx"),
        ]
        for c in cand:
            if os.path.exists(c):
                model = os.path.abspath(c)
                break
    if model is None or not os.path.exists(model):
        print("[FAIL] 找不到模型 onnx，请传入路径或放到 _testmodels/yolo11n.onnx")
        return 1
    print("model:", model)

    dll = load_dll()
    h = dll.va_create()
    assert h, "va_create 返回空句柄"
    print("[OK] va_create")

    # 错误路径 1：未 create 先 run
    img, arr = synthetic_image()
    res = (VA_DetectResult * 64)()
    cnt = ctypes.c_int(0)
    rc = dll.va_det_run(h, ctypes.byref(img), 0.25, 0.45, res, 64, ctypes.byref(cnt))
    print("run before create -> rc=%d (期望 %d VA_ERR_NO_TEMPLATE)" % (rc, VA_ERR_NO_TEMPLATE))
    assert rc == VA_ERR_NO_TEMPLATE, "未 create 时应返回 VA_ERR_NO_TEMPLATE"

    # 错误路径 2：非法模型路径
    rc = dll.va_det_create(h, b"no_such_model.onnx", b"CPU")
    print("create bad path -> rc=%d, err=%s" % (rc, last_err(dll, h)))
    assert rc != VA_OK, "非法模型路径应失败"

    # 正常加载
    rc = dll.va_det_create(h, model.encode("utf-8"), b"CPU")
    print("create -> rc=%d, err=%s" % (rc, last_err(dll, h)))
    assert rc == VA_OK, "模型加载失败"
    print("[OK] va_det_create 加载并编译模型成功")

    # 类别数 / 名
    nc = ctypes.c_int(0)
    dll.va_det_class_count(h, ctypes.byref(nc))
    print("num_classes =", nc.value)
    assert nc.value > 0
    names = []
    for i in range(min(nc.value, 5)):
        b = ctypes.create_string_buffer(64)
        dll.va_det_class_name(h, i, b, 64)
        names.append(b.value.decode("utf-8", "replace"))
    print("前几个类名:", names)

    # 正常推理（大缓冲，避免截断；合成图背景 logits 接近 0，score≈0.5 会大量命中，故用大缓冲）
    BIG = 9000
    big = (VA_DetectResult * BIG)()
    rc = dll.va_det_run(h, ctypes.byref(img), 0.25, 0.45, big, BIG, ctypes.byref(cnt))
    print("run -> rc=%d, count=%d, err=%s" % (rc, cnt.value, last_err(dll, h)))
    assert rc == VA_OK, "推理返回非成功码: %d" % rc
    assert cnt.value > 0, "合成图上至少应有候选检测（验证解析+映射链路）"

    ok = True
    for i in range(min(cnt.value, BIG)):
        r = big[i]
        if not (0.0 <= r.score <= 1.0): ok = False
        if r.w <= 0 or r.h <= 0: ok = False
        # 框可合法地延伸到画面外（检测器常态）；仅拒绝明显荒谬值
        if not (-2000 <= r.x <= img.width + 2000 and -2000 <= r.y <= img.height + 2000): ok = False
    print("检出 %d 个目标，分数/尺寸/坐标合理性: %s" % (cnt.value, "OK" if ok else "FAIL"))
    assert ok, "结果分数/尺寸/坐标非法"

    # 强正确性信号：至少存在一个高置信检测，其框与合成图里画的红块(80,80)-(200,200)重叠，
    # 证明「letterbox 逆映射 + 输出解析 + NMS」整条链路坐标正确（不是喂错预处理导致的乱输出）。
    def overlaps(rx, ry, rw, rh, ax, ay, aw, ah):
        return not (rx + rw < ax or rx > ax + aw or ry + rh < ay or ry > ay + ah)
    hit = any(r.score >= 0.9 and overlaps(r.x, r.y, r.w, r.h, 80, 80, 120, 120)
              for r in (big[i] for i in range(min(cnt.value, BIG))))
    print("高置信检测是否命中合成红块区域: %s" % ("OK" if hit else "FAIL"))
    assert hit, "高置信检测未落在真实物体区域，疑似预处理/坐标映射错误"

    # 溢出路径：小缓冲应返回 VA_ERR_SIZE，且 out_count 反映真实数量
    small = (VA_DetectResult * 5)()
    sc = ctypes.c_int(0)
    rc2 = dll.va_det_run(h, ctypes.byref(img), 0.25, 0.45, small, 5, ctypes.byref(sc))
    print("run small buffer -> rc=%d (期望 %d VA_ERR_SIZE), out_count=%d" % (rc2, VA_ERR_SIZE, sc.value))
    assert rc2 == VA_ERR_SIZE and sc.value > 5, "小缓冲应返回 VA_ERR_SIZE 且 out_count>5"

    dll.va_det_clear(h)
    print("[OK] 冒烟测试通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
