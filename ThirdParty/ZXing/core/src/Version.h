/*
 * 手工生成的版本/特性头（原文件由 CMake 的 configure_file 从 core/Version.h.in 生成）。
 *
 * 【为什么手写】本仓库用 MSBuild 传统 vcxproj 构建，不引入 CMake 作为构建前置依赖
 * （本机 CMake 探测不到 MSVC 工具链）。因此把生成结果固化成源文件，升级 zxing-cpp 时
 * 只需同步这里的版本号与下面的两个开关。
 *
 * 只启用**读**（ZXING_READERS）：本项目只做条码识别，不生成条码，
 * 关掉写端可省下 zint 依赖与一批编解码表。
 */
#pragma once

#define ZXING_READERS
/* #undef ZXING_WRITERS */

#define ZXING_VERSION_MAJOR 2
#define ZXING_VERSION_MINOR 3
#define ZXING_VERSION_PATCH 0

#define ZXING_VERSION_STR "2.3.0"
