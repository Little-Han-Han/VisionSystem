import os, sys, ctypes
from ctypes import WinDLL, c_void_p, c_int, c_float, c_char_p, POINTER, byref, pointer, Structure, create_string_buffer

REL = r"C:\Users\admin\Desktop\VisionSystem\bin\x64\Release"
os.add_dll_directory(REL)
os.environ["PATH"] = REL + os.pathsep + os.environ["PATH"]

dll = WinDLL("VisionAlgo.Native.dll")

class VA_Image(Structure):
    _fields_ = [("data", c_void_p), ("width", c_int), ("height", c_int),
                ("channels", c_int), ("stride", c_int)]

class VA_DetectResult(Structure):
    _fields_ = [("x", c_float), ("y", c_float), ("w", c_float), ("h", c_float),
                ("class_id", c_int), ("score", c_float)]

dll.va_create.restype = c_void_p
dll.va_create.argtypes = []
dll.va_destroy.restype = None
dll.va_destroy.argtypes = [c_void_p]
dll.va_get_last_error.restype = c_int
dll.va_get_last_error.argtypes = [c_void_p, c_char_p, c_int]
dll.va_error_string.restype = c_char_p
dll.va_error_string.argtypes = [c_int]

dll.va_det_create.restype = c_int
dll.va_det_create.argtypes = [c_void_p, c_char_p, c_char_p]
dll.va_det_run.restype = c_int
dll.va_det_run.argtypes = [c_void_p, POINTER(VA_Image), c_float, c_float,
                           POINTER(VA_DetectResult), c_int, POINTER(c_int)]
dll.va_det_class_count.restype = c_int
dll.va_det_class_count.argtypes = [c_void_p, POINTER(c_int)]
dll.va_det_class_name.restype = c_int
dll.va_det_class_name.argtypes = [c_void_p, c_int, c_char_p, c_int]

def last_err(h):
    buf = create_string_buffer(1024)
    dll.va_get_last_error(h, buf, 1024)
    return buf.value.decode("utf-8", "replace")

import shutil
if len(sys.argv) > 1:
    model = sys.argv[1]
else:
    model = os.path.join(REL, "models", "yolo11n.onnx")
txt_next = os.path.splitext(model)[0] + ".txt"
print("model exists:", os.path.exists(model), model)
print("sidecar .txt exists:", os.path.exists(txt_next))

h = dll.va_create()
print("handle:", h)

rc = dll.va_det_create(h, model.encode("utf-8"), b"CPU")
print("va_det_create rc:", rc, "->", dll.va_error_string(rc).decode() if rc else "OK", "| err:", last_err(h))

if rc != 0:
    print("CREATE FAILED -> cannot run. BAIL.")
    sys.exit(rc)

# synthetic 640x480 BGR image (a few bright rectangles)
W, H, C = 640, 480, 3
buf = bytearray(W * H * C)
def set_px(x, y, b, g, r):
    i = (y * W + x) * C
    buf[i] = b; buf[i+1] = g; buf[i+2] = r
# draw 3 rectangles
for (x0, y0, ww, hh) in [(80, 80, 120, 200), (300, 120, 90, 90), (450, 200, 140, 110)]:
    for yy in range(y0, y0 + hh):
        for xx in range(x0, x0 + ww):
            set_px(xx, yy, 230, 230, 230)
img = VA_Image()
img.data = ctypes.cast((ctypes.c_ubyte * len(buf)).from_buffer(buf), c_void_p)
img.width = W; img.height = H; img.channels = C; img.stride = W * C

cnt = c_int(0)
res = (VA_DetectResult * 100)()
rc2 = dll.va_det_run(h, byref(img), c_float(0.25), c_float(0.45), res, 100, byref(cnt))
print("va_det_run rc:", rc2, "| count:", cnt.value, "| err:", last_err(h))
for i in range(min(cnt.value, 5)):
    r = res[i]
    print(f"  det[{i}] cls={r.class_id} score={r.score:.3f} x={r.x:.1f} y={r.y:.1f} w={r.w:.1f} h={r.h:.1f}")

cc = c_int(0)
dll.va_det_class_count(h, byref(cc))
print("class_count:", cc.value)
if cc.value > 0:
    nm = create_string_buffer(256)
    n = dll.va_det_class_name(h, 0, nm, 256)
    print("class[0]:", nm.value[:n])

dll.va_destroy(h)
print("DONE")
