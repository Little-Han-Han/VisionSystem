# 图标设计脚本

`assets/icons/` 里应用图标的生成脚本。改图标只改这里，跑完覆盖输出。

## 产出

| 脚本 | 输出 |
|---|---|
| `generate-icons.ps1` | `assets/icons/app.ico`（9 档）、`png/app-{16,24,32,48,64,128,256}.png`、`png/splash-mark.png` |
| `make-svg.py` | `assets/icons/app-icon.svg`（磁贴版）、`app-mark.svg`（透明标记） |

## 运行

```bat
powershell -ExecutionPolicy Bypass -File tools\icon-design\generate-icons.ps1
python tools\icon-design\make-svg.py
```

两者都按自身位置回推仓库根，不需要传参。

## 注意

- **两个脚本的几何参数必须一致**（`ROut / RIn / BracketInset ...`），否则 SVG 与位图会错位。
- `.ps1` 保持**纯 ASCII**：PowerShell 5.1 按 ANSI 读脚本，中文会让引号解析失败。
- 位图走 .NET `System.Drawing`（零依赖）：2048 超采样 → 预乘 alpha 面积平均降采样；
  ico 内 ≤48 用 BMP 载荷、≥64 用 PNG 载荷。
- `< 32px` 会自动切换为简化图形（粗环、去掉角标与十字），这是刻意的光学尺寸处理，别当成 bug。
- 叶片**不留角度间隙**，缝由 7 单位深色描边形成 —— 留间隙会在透明底上漏出背景色白线。
