#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
VisionPlatform UI icon set generator.

Reads the visual language defined in docs/设计/界面图标清单.md and writes:
    assets/icons/ui/src/ui-<group>-<name>.svg     vector source (currentColor)
    assets/icons/ui/png/ui-<group>-<name>-{16,24,32}.png
    assets/icons/ui/manifest.json                 group -> icon -> file map

Design rules (kept in one place on purpose):
    * 24 x 24 design grid, 1.7 px rounded strokes
    * one dark ink for light themes (currentColor in SVG), one cyan accent
    * semantic colours only for OK / NG / WARN / ERROR badges
    * no text glyphs (unreadable at 16 px)

Zero third-party code except Pillow for rasterisation:
    pip install pillow
    python tools/icon-design/make-ui-icons.py
"""

import json
import math
import os
from collections import OrderedDict
from dataclasses import dataclass

try:
    from PIL import Image, ImageDraw
except ImportError:
    raise SystemExit("Pillow is required:  pip install pillow")


# ---------------------------------------------------------------------------
# palette / geometry
# ---------------------------------------------------------------------------
INK = "#243243"
ACCENT = "#0E9BD8"
SVG_ACCENT = "var(--ui-icon-accent, #0E9BD8)"
GREEN = "#159A6B"
RED = "#D64550"
AMBER = "#D98A0B"

GRID = 24.0
STROKE = 1.7
SIZES = (16, 24, 32)
SS = 6  # supersampling factor for PNG


@dataclass(frozen=True)
class P:
    """One drawing primitive."""
    kind: str                 # L / C / R / A / G
    data: tuple
    accent: bool = False
    fill: bool = False
    close: bool = False
    color: str = None         # explicit semantic colour, overrides accent/ink


def line(x1, y1, x2, y2, a=False, color=None):
    return P("L", ((x1, y1), (x2, y2)), a, False, False, color)


def poly(pts, close=False, a=False, fill=False, color=None):
    return P("L", tuple(pts), a, fill, close, color)


def ring(cx, cy, r, a=False, color=None):
    return P("C", (cx, cy, r), a, False, False, color)


def dot(cx, cy, r, a=False, color=None):
    return P("C", (cx, cy, r), a, True, False, color)


def rect(x, y, w, h, r=1.6, a=False, fill=False, color=None):
    return P("R", (x, y, w, h, r), a, fill, False, color)


def arc(cx, cy, r, a0, a1, a=False, color=None):
    return P("A", (cx, cy, r, a0, a1), a, False, False, color)


def gear(cx, cy, r, teeth=8, a=False):
    pts = []
    for i in range(teeth * 4):
        ang = math.pi * 2 * i / (teeth * 4)
        rad = r if i % 4 in (0, 3) else r * 0.81
        pts.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang)))
    return poly(pts, close=True, a=a)


def tip(x, y, ang, size=3.0, a=False, color=None):
    """Filled triangle whose point is at (x,y) and faces `ang` degrees."""
    t = math.radians(ang)
    bx, by = x - size * math.cos(t), y - size * math.sin(t)
    px, py = -math.sin(t), math.cos(t)
    return poly([(x, y),
                 (bx + px * size * 0.55, by + py * size * 0.55),
                 (bx - px * size * 0.55, by - py * size * 0.55)],
                close=True, a=a, fill=True, color=color)


def arrow_head(cx, cy, r, ang, size=2.8, a=False, color=None):
    """Filled triangle at the end of a clockwise arc (tangent direction)."""
    x = cx + r * math.cos(math.radians(ang))
    y = cy + r * math.sin(math.radians(ang))
    return tip(x, y, ang + 90.0, size, a, color)


def bracket(x, y, dx, dy, arm=4.0, a=False):
    """Corner mark: (x,y) is the corner, dx/dy point inward."""
    return poly([(x + dx * arm, y), (x, y), (x, y + dy * arm)], a=a)


# ---------------------------------------------------------------------------
# icon catalogue - one list per group, exactly the names from the spec
# ---------------------------------------------------------------------------
ICONS = OrderedDict()


def I(name, group, *prims):
    ICONS[name] = (group, list(prims))


# 1. run control -------------------------------------------------------------
I("ui-run-start", "运行控制",
  poly([(8, 4.6), (19, 12), (8, 19.4)], close=True, a=True))
I("ui-run-pause", "运行控制",
  rect(6.2, 5, 3.6, 14, 1.1),
  rect(14.2, 5, 3.6, 14, 1.1, a=True))
I("ui-run-stop", "运行控制",
  rect(6, 6, 12, 12, 1.6, a=True))
I("ui-run-step", "运行控制",
  poly([(4.5, 5), (13.5, 12), (4.5, 19)], close=True, a=True),
  line(18, 5, 18, 19))
I("ui-run-sync", "运行控制",
  line(7, 8.5, 7, 6), line(17, 8.5, 17, 6),
  line(7, 5.5, 17, 5.5, a=True), dot(12, 5.5, 1.05, a=True),
  rect(3, 8.5, 8, 8.5, 1.4), ring(7, 12.7, 2.3, a=True),
  rect(13, 8.5, 8, 8.5, 1.4), ring(17, 12.7, 2.3, a=True))
I("ui-run-loop", "运行控制",
  arc(12, 12, 7.2, 20, 258),
  arrow_head(12, 12, 7.2, 258, a=True),
  arc(12, 12, 7.2, 280, 360),
  arc(12, 12, 7.2, 0, 78, a=True),
  arrow_head(12, 12, 7.2, 78, a=True))

# 2. navigation --------------------------------------------------------------
I("ui-nav-flow", "主界面导航",
  line(6.5, 8.6, 17.5, 8.6), line(7.6, 10.6, 13.3, 16),
  ring(5.5, 6.5, 2.5), ring(18.5, 6.5, 2.5, a=True), ring(12, 18, 2.5))
I("ui-nav-camera", "主界面导航",
  rect(3.2, 7, 17.6, 12.4, 2),
  poly([(8, 7), (9.6, 4.4), (14.4, 4.4), (16, 7)], close=True),
  ring(12, 13.2, 3.6, a=True), dot(18, 9.8, 0.9, a=True))
I("ui-nav-calib", "主界面导航",
  ring(12, 12, 7), line(12, 2.6, 12, 6), line(12, 18, 12, 21.4),
  line(2.6, 12, 6, 12), line(18, 12, 21.4, 12),
  ring(12, 12, 2.5, a=True), dot(12, 12, 0.85, a=True))
I("ui-nav-comm", "主界面导航",
  rect(2.8, 4.6, 11, 8.2, 2), line(6, 12.8, 6, 16),
  rect(10.2, 11.2, 11, 8.2, 2, a=True), line(18, 8.2, 18, 11.2),
  dot(7.2, 8.7, 1), dot(14.4, 15.3, 1, a=True))
I("ui-nav-recipe", "主界面导航",
  poly([(3, 6.2), (9, 6.2), (11, 8.8), (21, 8.8), (21, 19), (3, 19)],
       close=True),
  line(3, 12.4, 21, 12.4, a=True), dot(7, 15.8, 1.05, a=True))
I("ui-nav-log", "主界面导航",
  rect(4, 3.5, 16, 17, 2),
  line(7.5, 8.5, 16.5, 8.5), line(7.5, 12, 16.5, 12, a=True),
  line(7.5, 15.5, 14, 15.5), dot(17.5, 15.5, 0.8, a=True))
I("ui-nav-settings", "主界面导航",
  gear(12, 12, 8, 8), ring(12, 12, 2.7, a=True))
I("ui-nav-stats", "主界面导航",
  line(4, 19.5, 20, 19.5),
  rect(6, 13, 3.2, 6.5, 0.5),
  rect(10.4, 8, 3.2, 11.5, 0.5, a=True),
  rect(14.8, 11, 3.2, 8.5, 0.5))
I("ui-nav-result", "主界面导航",
  poly([(4, 5), (20, 5), (20, 16), (13, 16), (9, 20), (9, 16), (4, 16)],
       close=True),
  poly([(8.5, 10.5), (11, 13), (16, 8)], a=True))
I("ui-nav-algo", "主界面导航",
  rect(3, 4.5, 18, 15, 2),
  poly([(5.5, 15), (8.5, 11.5), (11, 14), (13.5, 9.5)], a=True),
  ring(15.5, 12.8, 3.1), line(17.8, 15.2, 20.8, 18.2))

# 3. flow editor -------------------------------------------------------------
I("ui-flow-new", "流程编辑器",
  rect(4, 3, 16, 18, 2), line(12, 7.5, 12, 16.5, a=True),
  line(7.5, 12, 16.5, 12, a=True))
I("ui-flow-delete", "流程编辑器",
  rect(4, 3, 16, 18, 2), line(8, 12, 16, 12, a=True))
I("ui-flow-up", "流程编辑器",
  poly([(5, 12.5), (12, 5.5), (19, 12.5)], a=True),
  line(8, 18.5, 16, 18.5))
I("ui-flow-down", "流程编辑器",
  poly([(5, 11.5), (12, 18.5), (19, 11.5)], a=True),
  line(8, 5.5, 16, 5.5))
I("ui-common-save", "流程编辑器",
  poly([(4, 3.5), (17, 3.5), (20.5, 7), (20.5, 20.5), (4, 20.5)],
       close=True),
  line(8, 3.5, 8, 10), rect(8, 13, 8.5, 7.5, 0.8, a=True))
I("ui-flow-undo", "流程编辑器",
  arc(12, 12, 6.8, 20, 258), arrow_head(12, 12, 6.8, 258, a=True))
I("ui-flow-redo", "流程编辑器",
  arc(12, 12, 6.8, 282, 360), arc(12, 12, 6.8, 0, 80, a=True),
  arrow_head(12, 12, 6.8, 80, a=True))
I("ui-flow-copy", "流程编辑器",
  rect(8, 4, 12.5, 12.5, 1.6, a=True), rect(3.5, 7.5, 12.5, 12.5, 1.6))
I("ui-flow-paste", "流程编辑器",
  rect(6, 5.2, 12, 15, 1.6),
  rect(9, 2.8, 6, 4.2, 1, a=True),
  line(9, 11.5, 15, 11.5), line(9, 15.5, 13, 15.5))
I("ui-flow-node-delete", "流程编辑器",
  rect(3, 6, 18, 12, 2),
  line(8.5, 10, 15.5, 14, a=True), line(15.5, 10, 8.5, 14, a=True))
I("ui-flow-link", "流程编辑器",
  ring(5, 12, 2.7), ring(19, 12, 2.7, a=True),
  line(7.7, 12, 16.3, 12, a=True), dot(12, 12, 1.05))
I("ui-flow-select", "流程编辑器",
  bracket(4, 4, 1, 1), bracket(20, 4, -1, 1),
  bracket(4, 20, 1, -1), bracket(20, 20, -1, -1, a=True),
  dot(12, 12, 1, a=True))
I("ui-flow-zoom-in", "流程编辑器",
  ring(10.4, 10.4, 6.2), line(14.9, 14.9, 20.8, 20.8),
  line(7.5, 10.4, 13.3, 10.4, a=True),
  line(10.4, 7.5, 10.4, 13.3, a=True))
I("ui-flow-zoom-out", "流程编辑器",
  ring(10.4, 10.4, 6.2), line(14.9, 14.9, 20.8, 20.8),
  line(7.5, 10.4, 13.3, 10.4, a=True))
I("ui-flow-fit", "流程编辑器",
  bracket(3.5, 6, 1, 1, 5), bracket(20.5, 6, -1, 1, 5),
  bracket(3.5, 18, 1, -1, 5), bracket(20.5, 18, -1, -1, 5),
  ring(12, 12, 2.2, a=True))
I("ui-flow-grid", "流程编辑器",
  rect(3.5, 3.5, 17, 17, 1),
  line(9.2, 3.5, 9.2, 20.5), line(14.8, 3.5, 14.8, 20.5),
  line(3.5, 9.2, 20.5, 9.2), line(3.5, 14.8, 20.5, 14.8),
  rect(9.2, 3.5, 5.6, 5.7, 0, a=True))
I("ui-flow-validate", "流程编辑器",
  poly([(12, 2.8), (20, 6), (19, 14), (12, 21), (5, 14), (4, 6)],
       close=True),
  poly([(8.5, 11.5), (11, 14), (16, 8.5)], a=True))
I("ui-flow-log", "流程编辑器",
  line(5, 6, 19, 6), line(5, 12, 19, 12, a=True), line(5, 18, 14, 18),
  dot(3.2, 6, 0.8), dot(3.2, 12, 0.8, a=True), dot(3.2, 18, 0.8))
I("ui-flow-params", "流程编辑器",
  line(4, 8, 20, 8), ring(9.5, 8, 2.1, a=True),
  line(4, 16, 20, 16), ring(15, 16, 2.1, a=True))

# 4. toolbox categories ------------------------------------------------------
I("ui-cat-acquire", "节点分类",
  rect(2.8, 7.5, 11.5, 9, 1.4), ring(8.6, 12, 2.5, a=True),
  poly([(15.5, 12), (20.5, 8.5), (20.5, 15.5)], close=True, a=True))
I("ui-cat-process", "节点分类",
  rect(3, 4, 18, 16, 2),
  poly([(5.5, 16.5), (9.5, 11.5), (12.5, 15), (15.5, 10), (18.5, 16.5)],
       close=True),
  poly([(17, 5.5), (17.7, 7.8), (20, 8.5), (17.7, 9.2), (17, 11.5),
        (16.3, 9.2), (14, 8.5), (16.3, 7.8)], close=True, a=True, fill=True))
I("ui-cat-locate", "节点分类",
  ring(12, 12, 7.5), line(12, 2, 12, 5.5), line(12, 18.5, 12, 22),
  line(2, 12, 5.5, 12), line(18.5, 12, 22, 12),
  rect(7, 7, 10, 10, 0.6, a=True), dot(12, 12, 1.1))
I("ui-cat-measure", "节点分类",
  line(6, 4, 6, 20), line(18, 4, 18, 20),
  line(6, 12, 18, 12, a=True),
  poly([(8.5, 9.5), (6, 12), (8.5, 14.5)], a=True),
  poly([(15.5, 9.5), (18, 12), (15.5, 14.5)], a=True),
  dot(6, 12, 0.9), dot(18, 12, 0.9, a=True))
I("ui-cat-blob", "节点分类",
  ring(8.5, 14, 3.6), ring(14, 11.5, 4.4, a=True), ring(17.5, 16, 2.6),
  ring(11.5, 13.5, 8.5))
I("ui-cat-ocr", "节点分类",
  rect(3, 4, 18, 16, 2),
  poly([(7, 17), (11, 7), (15, 17)], a=True), line(8.6, 13.5, 13.4, 13.5, a=True),
  line(4.5, 19.5, 19.5, 19.5))
I("ui-cat-dl", "节点分类",
  dot(5, 7, 1.5), dot(5, 12, 1.5), dot(5, 17, 1.5),
  dot(12, 6, 1.5, a=True), dot(12, 12, 1.8, a=True), dot(12, 18, 1.5, a=True),
  dot(19, 12, 1.7),
  line(6.5, 7, 10.5, 6), line(6.5, 7, 10.5, 12), line(6.5, 12, 10.5, 6),
  line(6.5, 12, 10.5, 12), line(6.5, 12, 10.5, 18), line(6.5, 17, 10.5, 12),
  line(6.5, 17, 10.5, 18),
  line(13.5, 6, 17.5, 12), line(13.5, 12, 17.5, 12),
  line(13.5, 18, 17.5, 12))
I("ui-cat-3d", "节点分类",
  poly([(7, 8), (17, 8), (17, 18), (7, 18)], close=True),
  poly([(10, 4.5), (20, 4.5), (20, 14.5), (17, 18)], close=True),
  line(7, 8, 10, 4.5), line(17, 8, 20, 4.5),
  line(17, 18, 20, 14.5),
  line(12, 13, 12, 4.5, a=True), tip(12, 4.5, 270, 2.6, a=True))
I("ui-cat-output", "节点分类",
  rect(2.8, 6.5, 10, 11, 1.6),
  line(13, 12, 20.5, 12, a=True),
  poly([(17.5, 8.5), (21, 12), (17.5, 15.5)], a=True),
  line(3, 20.5, 21, 20.5))

# 5. status ------------------------------------------------------------------
I("ui-status-ok", "状态与判定",
  ring(12, 12, 8.5, color=GREEN),
  poly([(7.8, 12.2), (10.6, 15), (16.5, 8.8)], a=True, color=GREEN))
I("ui-status-ng", "状态与判定",
  ring(12, 12, 8.5, color=RED),
  line(9, 9, 15, 15, color=RED), line(15, 9, 9, 15, color=RED))
I("ui-status-warn", "状态与判定",
  poly([(12, 3.8), (21, 19.5), (3, 19.5)], close=True, color=AMBER),
  line(12, 9, 12, 14.5, color=AMBER), dot(12, 17, 0.95, a=True, color=AMBER))
I("ui-status-running", "状态与判定",
  ring(12, 12, 8.5), poly([(10, 8), (16.5, 12), (10, 16)], close=True, a=True))
I("ui-status-waiting", "状态与判定",
  ring(12, 12, 8.5), arc(12, 12, 4.6, 270, 360, a=True),
  line(12, 12, 12, 9, a=True), dot(12, 12, 0.9, a=True))
I("ui-status-paused", "状态与判定",
  ring(12, 12, 8.5), line(9.8, 8.5, 9.8, 15.5, a=True),
  line(14.2, 8.5, 14.2, 15.5, a=True))
I("ui-status-stopped", "状态与判定",
  ring(12, 12, 8.5), rect(9, 9, 6, 6, 0.8, a=True))
I("ui-status-aborted", "状态与判定",
  ring(12, 12, 8.5, color=RED), line(6, 18, 18, 6, color=RED),
  line(8.5, 8.5, 15.5, 15.5, a=True, color=RED))
I("ui-status-error", "状态与判定",
  ring(12, 12, 8.5, color=RED), line(12, 6.5, 12, 14.5, color=RED),
  dot(12, 17.2, 1, color=RED))

# 6. devices / link ----------------------------------------------------------
I("ui-dev-camera-on", "设备与链路",
  rect(3, 7, 18, 12.5, 2), poly([(8, 7), (9.5, 4.5), (14.5, 4.5), (16, 7)], close=True),
  ring(11, 13.2, 3.3, a=True), dot(18.5, 9.8, 1.2, a=True, color=GREEN))
I("ui-dev-camera-off", "设备与链路",
  rect(3, 7, 18, 12.5, 2), poly([(8, 7), (9.5, 4.5), (14.5, 4.5), (16, 7)], close=True),
  ring(11, 13.2, 3.3), line(4, 20.5, 20.5, 4, a=True, color=RED))
I("ui-dev-camera-usb", "设备与链路",
  rect(3, 7.5, 12, 9.5, 1.6), ring(9, 12.2, 2.5),
  line(19, 6, 19, 16), poly([(16.5, 8.5), (19, 6), (21.5, 8.5)], a=True),
  rect(17.2, 16, 3.6, 3, 0.6, a=True))
I("ui-comm-connect", "设备与链路",
  ring(6.5, 12, 3.2), ring(17.5, 12, 3.2, a=True),
  line(9.7, 12, 14.3, 12, a=True), arrow_head(12, 12, 2.4, 0, a=True))
I("ui-comm-disconnect", "设备与链路",
  ring(6.5, 12, 3.2), ring(17.5, 12, 3.2, a=True),
  line(10, 10.4, 14, 13.6), line(10, 13.6, 14, 10.4, a=True))
I("ui-comm-tcp", "设备与链路",
  rect(3.5, 4, 17, 6.2, 1.3), rect(3.5, 13.8, 17, 6.2, 1.3, a=True),
  line(8, 10.2, 8, 13.8, a=True), line(16, 13.8, 16, 10.2),
  poly([(6.4, 12), (8, 10.2), (9.6, 12)], a=True),
  poly([(14.4, 12), (16, 13.8), (17.6, 12)]))
I("ui-comm-modbus", "设备与链路",
  line(3, 12, 21, 12, a=True),
  rect(4, 7.5, 4.5, 9, 1), rect(9.8, 7.5, 4.5, 9, 1),
  rect(15.5, 7.5, 4.5, 9, 1, a=True),
  dot(6.25, 12, 0.8), dot(12, 12, 0.8), dot(17.8, 12, 0.8, a=True))
I("ui-comm-heartbeat", "设备与链路",
  line(2.5, 12, 7, 12),
  poly([(7, 12), (9.5, 12), (11, 6.5), (13.5, 18), (15.5, 12), (21.5, 12)],
       a=True),
  dot(11, 6.5, 0.9, a=True))
I("ui-comm-point", "设备与链路",
  ring(12, 12, 5.6, a=True), line(12, 3.5, 12, 6.4),
  line(12, 17.6, 12, 20.5), line(3.5, 12, 6.4, 12),
  line(17.6, 12, 20.5, 12), dot(12, 12, 1.3))
I("ui-comm-message", "设备与链路",
  rect(3, 5.5, 18, 13, 2), poly([(3.5, 6), (12, 13), (20.5, 6)], a=True),
  line(7, 17, 11, 17))
I("ui-comm-trigger", "设备与链路",
  poly([(13, 2.8), (6.5, 13), (11.5, 13), (9.5, 21), (17.5, 9.8),
        (12.5, 9.8)], close=True, a=True),
  dot(5, 19, 1.4))
I("ui-comm-send", "设备与链路",
  rect(2.8, 6, 10.5, 12, 1.6), line(13, 12, 20, 12, a=True),
  poly([(17, 8.8), (20.5, 12), (17, 15.2)], a=True))
I("ui-comm-recv", "设备与链路",
  rect(10.7, 6, 10.5, 12, 1.6), line(4, 12, 11, 12, a=True),
  poly([(7, 8.8), (3.5, 12), (7, 15.2)], a=True))

# 7. 3D view -----------------------------------------------------------------
I("ui-3d-rotate", "3D 视图",
  arc(12, 12, 7.5, 30, 300), arrow_head(12, 12, 7.5, 300, a=True),
  ring(12, 12, 2.2), line(12, 12, 16.5, 8, a=True))
I("ui-3d-reset", "3D 视图",
  arc(12, 12, 7.2, 40, 330, a=True), arrow_head(12, 12, 7.2, 330, a=True),
  dot(12, 12, 1.4))
I("ui-3d-axis", "3D 视图",
  line(5, 19, 19, 19), tip(19, 19, 0, 3),
  line(5, 19, 10, 7), tip(10, 7, 293, 3),
  line(5, 19, 5, 5, a=True), tip(5, 5, 270, 3, a=True),
  dot(5, 19, 1.3))
I("ui-3d-point-size", "3D 视图",
  line(4, 20, 20, 20), dot(6.5, 9, 0.9), dot(12, 9, 1.5),
  dot(18.5, 9, 2.3, a=True), line(6.5, 13, 6.5, 16),
  line(12, 13, 12, 16), line(18.5, 13, 18.5, 16, a=True))
I("ui-3d-shading", "3D 视图",
  ring(12, 12, 8), poly([(12, 4), (12, 20)], a=True),
  arc(12, 12, 8, 270, 90, a=True))
I("ui-3d-snapshot", "3D 视图",
  rect(3, 7, 18, 12.5, 2), ring(10.5, 13.2, 3.2, a=True),
  line(17.5, 5.5, 17.5, 3.5), line(15.5, 4.5, 19.5, 4.5, a=True),
  bracket(3, 19.5, 1, -1, 3.5))

# 8. dataset / training ------------------------------------------------------
I("ui-train-capture", "数据集与训练",
  rect(3, 4.5, 18, 15, 2), rect(7.5, 8, 9, 8, 0.8, a=True),
  line(16, 8, 20, 4.5, a=True), dot(12, 12, 1))
I("ui-train-start", "数据集与训练",
  poly([(8, 5.5), (18, 12), (8, 18.5)], close=True, a=True),
  rect(3.2, 5, 3.2, 14, 0.8))
I("ui-train-library", "数据集与训练",
  rect(6, 3.5, 13, 15, 1.5, a=True), rect(3.5, 6, 13, 15, 1.5),
  line(7, 10.5, 13, 10.5), line(7, 14, 13, 14), line(7, 17.5, 11, 17.5))
I("ui-train-delete", "数据集与训练",
  rect(4.5, 5.5, 15, 14, 1.8), line(3.5, 5.5, 20.5, 5.5),
  line(9, 3, 15, 3), line(9.5, 10, 14.5, 15, a=True),
  line(14.5, 10, 9.5, 15, a=True))
I("ui-train-model", "数据集与训练",
  rect(5, 5, 14, 14, 1.8), rect(9, 9, 6, 6, 0.8, a=True),
  line(9, 2.8, 9, 5), line(15, 2.8, 15, 5),
  line(9, 19, 9, 21.2), line(15, 19, 15, 21.2),
  line(2.8, 9, 5, 9), line(2.8, 15, 5, 15),
  line(19, 9, 21.2, 9), line(19, 15, 21.2, 15))

# 9. recipe / bundle ---------------------------------------------------------
I("ui-recipe", "配方与工程包",
  poly([(3.5, 6), (9, 6), (11, 8.5), (20.5, 8.5), (20.5, 19), (3.5, 19)],
       close=True),
  rect(7, 11, 10, 6, 1, a=True), dot(9.5, 14, 0.9, a=True))
I("ui-recipe-saveas", "配方与工程包",
  poly([(3.5, 3.5), (16, 3.5), (19.5, 7), (19.5, 19.5), (3.5, 19.5)],
       close=True),
  line(7.5, 3.5, 7.5, 9.5), line(11, 13, 17, 13, a=True),
  line(14, 10, 14, 16, a=True))
I("ui-bundle-import", "配方与工程包",
  rect(3, 8, 12, 11, 1.6),
  line(15.5, 13.5, 21.5, 13.5, a=True),
  poly([(18.5, 10.5), (21.5, 13.5), (18.5, 16.5)], a=True),
  line(3, 21, 21, 21))
I("ui-bundle-export", "配方与工程包",
  rect(9, 8, 12, 11, 1.6),
  line(2.5, 13.5, 8.5, 13.5, a=True),
  poly([(5.5, 10.5), (2.5, 13.5), (5.5, 16.5)], a=True),
  line(3, 21, 21, 21))
I("ui-bundle-backup", "配方与工程包",
  arc(12, 12, 7.5, 35, 320, a=True), arrow_head(12, 12, 7.5, 320, a=True),
  rect(8.5, 8.5, 7, 7, 1), line(12, 10.5, 12, 13.5), dot(12, 15.7, 0.8))
I("ui-bundle-sample", "配方与工程包",
  rect(3.5, 3.5, 17, 17, 2),
  poly([(12, 7), (13.6, 10.6), (17.5, 11.1), (14.6, 13.9),
        (15.3, 17.8), (12, 15.9), (8.7, 17.8), (9.4, 13.9),
        (6.5, 11.1), (10.4, 10.6)], close=True, a=True))

# 10. common actions ---------------------------------------------------------
I("ui-common-add", "通用动作",
  ring(12, 12, 8.3), line(12, 7.5, 12, 16.5, a=True),
  line(7.5, 12, 16.5, 12, a=True))
I("ui-common-delete", "通用动作",
  ring(12, 12, 8.3), line(7.5, 12, 16.5, 12, a=True))
I("ui-common-edit", "通用动作",
  poly([(4, 16.5), (4, 20), (7.5, 20), (19, 8.5), (15.5, 5)], close=True),
  line(13.8, 6.7, 17.3, 10.2, a=True))
I("ui-common-refresh", "通用动作",
  arc(12, 12, 7.6, 35, 330, a=True), arrow_head(12, 12, 7.6, 330, a=True),
  dot(12, 12, 1.1))
I("ui-common-search", "通用动作",
  ring(10.5, 10.5, 6.2), line(15, 15, 20.8, 20.8, a=True))
I("ui-common-filter", "通用动作",
  poly([(3.5, 5), (20.5, 5), (14, 12.5), (14, 19), (10, 16.8), (10, 12.5)],
       close=True),
  line(8, 8.5, 16, 8.5, a=True))
I("ui-common-collapse", "通用动作",
  poly([(14, 5), (8, 12), (14, 19)], a=True),
  poly([(19, 5), (13, 12), (19, 19)], a=True))
I("ui-common-expand", "通用动作",
  poly([(10, 5), (16, 12), (10, 19)], a=True),
  poly([(5, 5), (11, 12), (5, 19)], a=True))
I("ui-common-close", "通用动作",
  ring(12, 12, 8.3), line(9, 9, 15, 15, a=True), line(15, 9, 9, 15, a=True))
I("ui-common-info", "通用动作",
  ring(12, 12, 8.3), dot(12, 7.6, 1, a=True),
  line(12, 11, 12, 16.5, a=True))
I("ui-common-exit", "通用动作",
  rect(3, 4, 7, 16, 1.2), dot(8, 12, 0.8, a=True),
  line(10.5, 12, 20, 12, a=True),
  poly([(17, 9), (20.5, 12), (17, 15)], a=True),
  line(2, 20.5, 22, 20.5))
I("ui-common-user", "通用动作",
  ring(12, 8, 4.1, a=True),
  arc(12, 20.5, 7, 185, 355),
  line(5, 20.5, 19, 20.5))
I("ui-common-lock", "通用动作",
  arc(12, 11, 5.4, 180, 360),
  rect(6.5, 11, 11, 9.5, 1.6),
  dot(12, 15.2, 1.2, a=True), line(12, 16.4, 12, 18, a=True))


# ---------------------------------------------------------------------------
# SVG rendering
# ---------------------------------------------------------------------------
def fmt(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def stroke_of(p):
    if p.color:
        return p.color
    return SVG_ACCENT if p.accent else "currentColor"


def prim_svg(p):
    col = stroke_of(p)
    sw = fmt(STROKE)
    cap = 'stroke-linecap="round" stroke-linejoin="round"'
    fill = col if p.fill else "none"

    if p.kind == "L":
        pts = p.data
        d = "M " + " L ".join("%s %s" % (fmt(x), fmt(y)) for x, y in pts)
        if p.close:
            d += " Z"
        return '<path d="%s" fill="%s" stroke="%s" stroke-width="%s" %s/>' % (
            d, fill, col, sw, cap)

    if p.kind == "C":
        cx, cy, r = p.data
        return '<circle cx="%s" cy="%s" r="%s" fill="%s" stroke="%s" stroke-width="%s" %s/>' % (
            fmt(cx), fmt(cy), fmt(r), fill, col, sw, cap)

    if p.kind == "R":
        x, y, w, h, r = p.data
        return '<rect x="%s" y="%s" width="%s" height="%s" rx="%s" fill="%s" stroke="%s" stroke-width="%s" %s/>' % (
            fmt(x), fmt(y), fmt(w), fmt(h), fmt(r), fill, col, sw, cap)

    if p.kind == "A":
        cx, cy, r, a0, a1 = p.data
        x0, y0 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
        x1, y1 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
        delta = (a1 - a0) % 360
        large = 1 if delta > 180 else 0
        return '<path d="M %s %s A %s %s 0 %d 1 %s %s" fill="none" stroke="%s" stroke-width="%s" %s/>' % (
            fmt(x0), fmt(y0), fmt(r), fmt(r), large, fmt(x1), fmt(y1), col, sw, cap)

    if p.kind == "G":
        pts = p.data
        points = " ".join("%s,%s" % (fmt(x), fmt(y)) for x, y in pts)
        return '<polygon points="%s" fill="none" stroke="%s" stroke-width="%s" %s/>' % (
            points, col, sw, cap)

    raise ValueError(p.kind)


def write_svg(path, name, prims):
    lines = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<!-- %s - generated by tools/icon-design/make-ui-icons.py -->' % name,
             '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" '
             'viewBox="0 0 24 24" fill="none" role="img" aria-label="%s">' % name]
    for p in prims:
        lines.append("  " + prim_svg(p))
    lines.append("</svg>")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


# ---------------------------------------------------------------------------
# PNG rendering (Pillow, supersampled)
# ---------------------------------------------------------------------------
def ink_of(p):
    if p.color:
        return p.color
    return ACCENT if p.accent else INK


def prim_png(draw, p, k, width):
    col = ink_of(p)

    if p.kind == "L":
        pts = [(x * k, y * k) for x, y in p.data]
        if p.close:
            pts = pts + [pts[0]]
        if p.fill:
            draw.polygon(pts, fill=col)
        else:
            draw.line(pts, fill=col, width=width, joint="curve")
            if len(pts) > 1 and not p.close:
                r = width / 2.0
                for x, y in (pts[0], pts[-1]):
                    draw.ellipse((x - r, y - r, x + r, y + r), fill=col)
        return

    if p.kind == "C":
        cx, cy, r = p.data
        box = ((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k)
        if p.fill:
            draw.ellipse(box, fill=col)
        else:
            draw.ellipse(box, outline=col, width=width)
        return

    if p.kind == "R":
        x, y, w, h, r = p.data
        box = (x * k, y * k, (x + w) * k, (y + h) * k)
        if p.fill:
            draw.rounded_rectangle(box, radius=r * k, fill=col)
        else:
            draw.rounded_rectangle(box, radius=r * k, outline=col, width=width)
        return

    if p.kind == "A":
        cx, cy, r, a0, a1 = p.data
        box = ((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k)
        draw.arc(box, start=a0, end=a1, fill=col, width=width)
        return

    if p.kind == "G":
        pts = [(x * k, y * k) for x, y in p.data]
        draw.line(pts + [pts[0]], fill=col, width=width, joint="curve")
        return

    raise ValueError(p.kind)


def write_png(path, size, prims):
    work = size * SS
    k = work / GRID
    img = Image.new("RGBA", (work, work), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    width = max(1, int(round(STROKE * k)))
    for p in prims:
        prim_png(d, p, k, width)
    img = img.resize((size, size), Image.LANCZOS)
    img.save(path, "PNG", optimize=True)


# ---------------------------------------------------------------------------
def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(os.path.dirname(here))
    ui = os.path.join(root, "assets", "icons", "ui")
    src = os.path.join(ui, "src")
    png = os.path.join(ui, "png")
    os.makedirs(src, exist_ok=True)
    os.makedirs(png, exist_ok=True)

    manifest = OrderedDict()
    for name, (group, prims) in ICONS.items():
        write_svg(os.path.join(src, name + ".svg"), name, prims)
        files = []
        for size in SIZES:
            out = "%s-%d.png" % (name, size)
            write_png(os.path.join(png, out), size, prims)
            files.append(out)
        manifest.setdefault(group, OrderedDict())[name] = {
            "svg": "src/%s.svg" % name,
            "png": {str(s): "png/%s-%d.png" % (name, s) for s in SIZES},
        }

    with open(os.path.join(ui, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print("%d icons -> %s" % (len(ICONS), ui))
    for group, entries in manifest.items():
        print("  %-8s %2d" % (group, len(entries)))


if __name__ == "__main__":
    main()
