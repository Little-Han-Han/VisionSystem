"""
把 models/ 下的 .pt 权重导出成 OpenVINO 推理要用的 ONNX。

用法（在项目根目录执行）：
    python tools/export_onnx.py

依赖：
    pip install ultralytics onnx onnxsim

导出结果直接写回同名目录：
    models/seg/yolo26n-seg.pt     -> models/seg/yolo26n-seg.onnx
    models/pose/yolo26n-pose.pt   -> models/pose/yolo26n-pose.onnx
    models/depth/yolo26n-depth.pt -> models/depth/yolo26n-depth.onnx

注意：
    · 必须固定 imgsz（默认 640），动态尺寸会让 C++ 侧每次都要 reshape
    · opset 用 12（OpenVINO 2025.x 支持良好）
    · 深度模型的输出解释与 seg/pose 不同，见 models/README.md
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 默认 Release；想导 Debug 目录就传 --dir bin/x64/Debug/models
_config = sys.argv[sys.argv.index("--dir") + 1] if "--dir" in sys.argv and len(sys.argv) > sys.argv.index("--dir") + 1 else None
MODELS = os.path.join(ROOT, _config) if _config else os.path.join(ROOT, "bin", "x64", "Release", "models")

# (子目录, 权重文件名, imgsz, 任务说明)
TARGETS = [
    ("seg",   "yolo26n-seg.pt",   640, "实例分割"),
    ("pose",  "yolo26n-pose.pt",  640, "姿态估计"),
    ("depth", "yolo26n-depth.pt", 640, "深度估计"),
]


def export_one(subdir: str, weight: str, imgsz: int, desc: str) -> bool:
    src = os.path.join(MODELS, subdir, weight)
    if not os.path.exists(src):
        print(f"[跳过] {desc}：找不到 {src}")
        return False

    try:
        from ultralytics import YOLO
    except ImportError:
        print("[错误] 未安装 ultralytics：pip install ultralytics")
        return False

    print(f"[导出] {desc}：{src}")
    model = YOLO(src)

    out = model.export(
        format="onnx",
        imgsz=imgsz,
        opset=12,
        simplify=True,
        dynamic=False,
    )

    # ultralytics 会把 onnx 放在源文件旁边（同名 .onnx）
    if isinstance(out, str) and out.lower().endswith(".onnx"):
        print(f"        -> {out}")
    else:
        print(f"        -> 返回值 {out}")
    return True


def main() -> int:
    os.makedirs(MODELS, exist_ok=True)
    ok = 0
    fail = 0

    for subdir, weight, imgsz, desc in TARGETS:
        try:
            if export_one(subdir, weight, imgsz, desc):
                ok += 1
        except Exception as ex:            # noqa: BLE001 - 单个体失败不影响其他
            fail += 1
            print(f"[失败] {desc}：{ex.__class__.__name__}: {ex}")

    print(f"\n完成：成功 {ok}，失败/跳过 {fail}")
    print(f"输出目录：{MODELS}")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
