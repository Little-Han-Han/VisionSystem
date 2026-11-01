"""
把 PaddleOCR 的 inference model 转成 ONNX（放到 models/ocr/）。

前置：
    1. 下载官方推理模型（tar 包）并解压到 models/ocr/paddle/：
         PP-OCRv5_mobile_det_infer/
         PP-OCRv5_mobile_rec_infer/
       下载地址（官方，约 4.7MB + 10MB）：
         https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_det_infer.tar
         https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_rec_infer.tar

    2. 字典：PP-OCRv5 用 ppocrv5_dict.txt（PaddleOCR 仓库 ppocr/utils/dict/ 下），
       放成 models/ocr/ppocr_keys.txt

用法：
    python tools/export_ocr_onnx.py

依赖：
    pip install paddle2onnx onnx

产物：
    models/ocr/det.onnx
    models/ocr/rec.onnx
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

_config = sys.argv[sys.argv.index("--dir") + 1] if "--dir" in sys.argv and len(sys.argv) > sys.argv.index("--dir") + 1 else None
OCR = os.path.join(ROOT, _config, "ocr") if _config else os.path.join(ROOT, "bin", "x64", "Release", "models", "ocr")

JOBS = [
    ("paddle/PP-OCRv5_mobile_det_infer", "det.onnx", "检测"),
    ("paddle/PP-OCRv5_mobile_rec_infer", "rec.onnx", "识别"),
]


def export_one(src_dir: str, dst_name: str, desc: str) -> bool:
    src = os.path.join(OCR, src_dir)
    if not os.path.isdir(src):
        print(f"[跳过] {desc}：找不到目录 {src}")
        return False

    model_file = os.path.join(src, "inference.pdmodel")
    params_file = os.path.join(src, "inference.pdiparams")
    # PaddlePaddle 3.x 导出的是 PIR 格式（inference.json + .pdiparams）
    if not os.path.exists(model_file):
        model_file = os.path.join(src, "inference.json")

    if not os.path.exists(model_file):
        print(f"[跳过] {desc}：目录里没有 inference.pdmodel / inference.json")
        return False

    dst = os.path.join(OCR, dst_name)
    print(f"[导出] {desc}：{src} -> {dst}")

    try:
        import paddle2onnx
    except ImportError:
        print("[错误] 未安装 paddle2onnx：pip install paddle2onnx")
        return False

    paddle2onnx.export(
        model_filename=model_file,
        params_filename=params_file,
        save_file=dst,
        opset_version=11,
        enable_onnx_checker=False,
    )
    print(f"        -> {dst}")
    return True


def main() -> int:
    os.makedirs(OCR, exist_ok=True)
    ok = fail = 0

    for src_dir, dst_name, desc in JOBS:
        try:
            if export_one(src_dir, dst_name, desc):
                ok += 1
        except Exception as ex:            # noqa: BLE001
            fail += 1
            print(f"[失败] {desc}：{ex.__class__.__name__}: {ex}")

    print(f"\n完成：成功 {ok}，失败/跳过 {fail}")
    print(f"输出目录：{OCR}")
    print("提醒：字典必须与识别模型配套（PP-OCRv5 用 ppocrv5_dict.txt），否则会乱码。")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
