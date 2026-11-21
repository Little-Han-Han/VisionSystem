# UI 图标资源

按 `docs/设计/界面图标清单.md` 生成，共 **96 个图标 / 10 个分组**，与 `manifest.json` 一致。

## 目录

- `src/ui-<分组>-<名称>.svg` —— 矢量源文件，24×24 网格，1.7px 圆头描边。
- `png/ui-<分组>-<名称>-{16,24,32}.png` —— 三档透明底位图。
- `manifest.json` —— 分组 → 图标名 → 文件路径，代码侧可据此批量加载。

## 配色

- SVG 主描边：`currentColor`（浅色底自动取深色，深色底由宿主反色）。
- SVG 强调色：`var(--ui-icon-accent, #0E9BD8)`。
- 状态徽标 OK / NG / WARN / ERROR 使用固定语义色（绿 / 红 / 琥珀 / 红）。
- PNG 是浅色主题用的深色描边版本；深色主题请在加载时反色，不要另存一套位图。

## 重新生成

```bat
python tools\icon-design\make-ui-icons.py
```

图形定义与配色集中在 `tools/icon-design/make-ui-icons.py`，SVG 和 PNG 同源，禁止手工改动导出文件。
