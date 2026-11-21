# tools — 开发与运维工具

> 这里的脚本**不参与构建**，按用途分目录存放。历史文档（CHANGELOG、旧文档）里出现的
> `tools/xxx.py` 旧路径已按下表迁移。

| 目录 | 用途 | 内容 |
|---|---|---|
| `version/` | 版本号工具 | `Set-Version.ps1` —— `version.bat` 的实现在这里（版本号唯一真源 `src/SharedVersion.cs`） |
| `comm/` | 通讯联调 | `modbus_slave_sim.py` 零依赖本机 Modbus 从站（FC01/03/05/06/10，现场无 PLC 时自证链路）；`merge_comm_config.py` 通讯配置合并 |
| `barcode/` | 条码测试图 | `gen_barcode_test.py` 生成 EAN-13 等测试图（SmokeAlgo `BENCH-BC` 用例使用） |
| `model/` | 模型转换 | `export_onnx.py`（YOLO seg/pose/depth → ONNX）、`export_ocr_onnx.py`（PaddleOCR det/rec → ONNX） |
| `research/` | 参考资料测量 | `analyze_ref.py` / `analyze_ref2.py`（参考图尺寸与像素统计）、`ocr_ref.ps1` → `ocr_ref.txt`（WinRT OCR 原始输出） |
| `calib_sample/` | 标定样本 | 9×6 棋盘图（8×5 内角点），SmokeAlgo 标定用例读取；缺失时用例自动跳过 |
| `icon-design/` | 图标生成 | `generate-icons.ps1` / `make-svg.py` —— 改图标只改这里，产出覆盖 `assets/icons/` |

## 常用命令

```bat
:: 版本号（根目录）
version.bat 1.0.9.1

:: 没有 PLC 时起本机 Modbus 从站（502 被占用就换端口）
python tools\comm\modbus_slave_sim.py --port 502 --seed

:: 生成条码测试图（输出到 Image/，供 SmokeAlgo BENCH-BC）
python tools\barcode\gen_barcode_test.py Image
```

## 说明

- `dump/`、`dump_measure/`、`*.log`、一次性调试脚本（`blob_check.py` / `fix_lag.py` /
  `smoke_detect*.py` 等）已清理 —— 相关能力由 `SmokeAlgo` / `SmokeUi` 回归覆盖。
- 回归测试台在 `src/SmokeUi`、`src/SmokeAlgo`（不在解决方案内，用根目录 `_build_smoke.bat` 构建）。
