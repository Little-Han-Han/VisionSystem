"""用 ctypes 直接调用 VisionAlgo.Native 的 va_blob_analyze，验证「5 个方块到底测出几个」。
零 numpy/PIL 依赖，纯标准库。"""
import ctypes
import os
import sys

DLL_DIR = r"C:\Users\admin\Desktop\VisionSystem\src\VisionPlatform\bin\Debug"
DLL = os.path.join(DLL_DIR, "VisionAlgo.Native.dll")

os.add_dll_directory(DLL_DIR)


class VA_Image(ctypes.Structure):
    _fields_ = [
        ("data", ctypes.c_void_p),
        ("width", ctypes.c_int),
        ("height", ctypes.c_int),
        ("channels", ctypes.c_int),
        ("stride", ctypes.c_int),
    ]


class VA_Blob(ctypes.Structure):
    _fields_ = [
        ("cx", ctypes.c_double),
        ("cy", ctypes.c_double),
        ("area", ctypes.c_double),
        ("width", ctypes.c_double),
        ("height", ctypes.c_double),
        ("angle", ctypes.c_double),
    ]


def main():
    dll = ctypes.WinDLL(DLL)
    dll.va_create.restype = ctypes.c_void_p
    dll.va_destroy.argtypes = [ctypes.c_void_p]
    dll.va_blob_analyze.restype = ctypes.c_int
    dll.va_blob_analyze.argtypes = [
        ctypes.c_void_p, ctypes.POINTER(VA_Image),
        ctypes.c_double, ctypes.c_double,
        ctypes.POINTER(VA_Blob), ctypes.c_int, ctypes.POINTER(ctypes.c_int),
    ]

    print("version =", dll.va_get_version())
    handle = dll.va_create()
    print("handle  =", hex(handle) if handle else "NULL")

    W, H = 640, 480
    buf = bytearray(W * H)  # 全黑
    blocks = [
        (40, 40, 120, 120),
        (240, 40, 320, 120),
        (440, 40, 520, 120),
        (40, 300, 120, 380),
        (240, 300, 320, 380),
    ]
    for (x0, y0, x1, y1) in blocks:
        for y in range(y0, y1):
            for x in range(x0, x1):
                buf[y * W + x] = 255
    print("blocks drawn:", len(blocks))

    arr = (ctypes.c_ubyte * len(buf)).from_buffer_copy(bytes(buf))
    img = VA_Image()
    img.data = ctypes.cast(arr, ctypes.c_void_p)
    img.width = W
    img.height = H
    img.channels = 1
    img.stride = W

    MAX = 256
    blobs = (VA_Blob * MAX)()
    out_count = ctypes.c_int(0)

    rc = dll.va_blob_analyze(handle, ctypes.byref(img), 100.0, 0.0, blobs, MAX, ctypes.byref(out_count))
    print("rc =", rc, "count =", out_count.value)
    for i in range(out_count.value):
        b = blobs[i]
        print("  blob %d: cx=%.1f cy=%.1f area=%.1f w=%.0f h=%.0f" % (i, b.cx, b.cy, b.area, b.width, b.height))

    dll.va_destroy(handle)
    return 0


if __name__ == "__main__":
    sys.exit(main())
