// 条码 / 二维码识别与定位（zxing-cpp 2.3.0，只读）。
//
// 【为什么用 zxing-cpp 而不是自己写】码制有 20 种，一维码要逐行扫描+校验、二维码要
// 定位图案+网格采样+Reed-Solomon 纠错。自研成本远高于引入一个成熟库；zxing-cpp 是
// ZXing 的官方 C++ 移植，纯头/源、无第三方依赖（写端可关），且支持一次识别多个码。
//
// 【坐标口径】zxing 返回的是四角点（Position），与其它节点一致：
//   · px/py 顺时针：左上、右上、右下、左下；
//   · angle 取「左上→右上」这条边，图像坐标下顺时针为正（与形状匹配同口径）；
//   · 另算一份轴对齐外接框 x/y/w/h，给只认矩形的老代码用。
#include "va_internal.h"
#include "va_error.h"

#include "ReadBarcode.h"
#include "Barcode.h"
#include "BarcodeFormat.h"
#include "ImageView.h"
#include "ReaderOptions.h"
#include "Version.h"          // ZXING_VERSION_STR（由 ThirdParty/ZXing/core/src/Version.h 提供）

#include <cmath>
#include <cstdio>
#include <cstring>

namespace va {

namespace {

// 码类型（C 端 VA_BC_*，单选）→ zxing 码制。
// 【为什么是单选表而不是"名字列表"】现场一个节点只认一种码：只跑这一类解码器更快，
// 也不会把 EAN-13 误认成 Code128 之类的近亲。SSCC-18 / EAN-128 / GS1-128 在 zxing 里
// 都属于 Code128 家族（带 FNC1），没有独立码制，所以统一映射到 Code128。
ZXing::BarcodeFormat FormatOfType(int codeType)
{
    switch (codeType)
    {
        case VA_BC_EAN13:            return ZXing::BarcodeFormat::EAN13;
        case VA_BC_UPCA:             return ZXing::BarcodeFormat::UPCA;
        case VA_BC_CODABAR:          return ZXing::BarcodeFormat::Codabar;
        case VA_BC_CODE39:           return ZXing::BarcodeFormat::Code39;
        case VA_BC_CODE128:          return ZXing::BarcodeFormat::Code128;
        case VA_BC_ITF:              return ZXing::BarcodeFormat::ITF;
        case VA_BC_QRCODE:           return ZXing::BarcodeFormat::QRCode;
        case VA_BC_DATAMATRIX:       return ZXing::BarcodeFormat::DataMatrix;
        case VA_BC_AZTEC:            return ZXing::BarcodeFormat::Aztec;
        case VA_BC_PDF417:           return ZXing::BarcodeFormat::PDF417;
        case VA_BC_MAXICODE:         return ZXing::BarcodeFormat::MaxiCode;
        case VA_BC_EAN8:             return ZXing::BarcodeFormat::EAN8;
        case VA_BC_UPCE:             return ZXing::BarcodeFormat::UPCE;
        case VA_BC_CODE93:           return ZXing::BarcodeFormat::Code93;
        case VA_BC_MICROQRCODE:      return ZXing::BarcodeFormat::MicroQRCode;
        case VA_BC_RMQRCODE:         return ZXing::BarcodeFormat::RMQRCode;
        case VA_BC_DATABAR:          return ZXing::BarcodeFormat::DataBar;
        case VA_BC_DATABAR_EXPANDED: return ZXing::BarcodeFormat::DataBarExpanded;
        case VA_BC_DATABAR_LIMITED:  return ZXing::BarcodeFormat::DataBarLimited;
        case VA_BC_DXFILMEDGE:       return ZXing::BarcodeFormat::DXFilmEdge;
        default:                     return ZXing::BarcodeFormat::Any;   // VA_BC_AUTO
    }
}

// UTF-8 安全拷贝：不越界，且不切断多字节字符（否则 C# 解码出乱码）
void CopyUtf8(const std::string& s, char* dst, int cap, int* truncated)
{
    if (dst == 0 || cap <= 0) { if (truncated) *truncated = 1; return; }

    size_t n = s.size();
    bool cut = false;
    if (n >= (size_t)cap)
    {
        n = (size_t)cap - 1;
        cut = true;
        while (n > 0 && (((unsigned char)s[n]) & 0xC0) == 0x80) --n;
    }
    if (n > 0) std::memcpy(dst, s.data(), n);
    dst[n] = '\0';
    if (truncated) *truncated = (cut ? 1 : 0);
}

const char* FormatName(ZXing::BarcodeFormat f)
{
    // ToString 返回 std::string，生命周期不跨语句 —— 这里转成静态存储的短串
    static thread_local std::string s;
    s = ZXing::ToString(f);
    return s.c_str();
}

} // namespace

int BarcodeRead(Context* ctx, const VA_Image* gray, const VA_BarcodeParam* param,
                VA_Barcode* out, int maxCount, int* outCount)
{
    if (outCount != 0) *outCount = 0;
    if (gray == 0 || out == 0 || outCount == 0 || maxCount <= 0)
    {
        return SetErrorEx(ctx, VA_ERR_ARG, "条码", "image / out / max_count",
                          "传入空指针或 max_count<=0", "全部必填且 max_count>=1",
                          "条码识别：入参为空（图像或输出缓冲区未提供）");
    }

    try
    {
        cv::Mat g;
        if (!ToMat(gray, g, ctx->lastError)) return VA_ERR_ARG;

        cv::Mat lum;
        if (g.channels() == 1)      lum = g;                                  // 只读，取引用
        else if (g.channels() == 3) cv::cvtColor(g, lum, cv::COLOR_BGR2GRAY);
        else if (g.channels() == 4) cv::cvtColor(g, lum, cv::COLOR_BGRA2GRAY);
        else
        {
            char buf[64];
            std::snprintf(buf, sizeof(buf), "%d 通道", g.channels());
            return SetErrorEx(ctx, VA_ERR_CHANNEL, "条码", "图像通道数", buf,
                              "1=灰度 / 3=BGR / 4=BGRA",
                              "条码识别：不支持的通道数（需要 1=灰度 / 3=BGR / 4=BGRA）");
        }

        // 小于 16×16 的图像连一维码的一行都放不下，直接给出可定位的错误
        if (lum.cols < 16 || lum.rows < 16)
        {
            char val[64], msg[192];
            std::snprintf(val, sizeof(val), "%dx%d", lum.cols, lum.rows);
            std::snprintf(msg, sizeof(msg),
                          "条码识别：图像太小 %dx%d（小于 16x16，无法容纳任何码）；"
                          "请检查搜索 ROI 是否框到了码上", lum.cols, lum.rows);
            return SetErrorEx(ctx, VA_ERR_IMAGE_TOO_SMALL, "条码", "搜索区尺寸", val, ">= 16x16", msg);
        }

        VA_BarcodeParam p;
        std::memset(&p, 0, sizeof(p));
        if (param != 0) p = *param;

        int maxN = (p.max_count > 0) ? p.max_count : 1;
        if (maxN > 255) maxN = 255;          // zxing 的 maxNumberOfSymbols 是 uint8
        if (maxN > maxCount) maxN = maxCount;

        // 码类型是单选：越界值直接拒绝并报出实际值（配置损坏时能一眼看出来）
        if (p.code_type < VA_BC_AUTO || p.code_type > VA_BC_MAX_TYPE)
        {
            char val[32], exp[32], msg[224];
            std::snprintf(val, sizeof(val), "%d", p.code_type);
            std::snprintf(exp, sizeof(exp), "%d~%d", VA_BC_AUTO, VA_BC_MAX_TYPE);
            std::snprintf(msg, sizeof(msg),
                          "条码识别：码类型取值非法（当前 %d，合法范围 %d~%d）；"
                          "请重新选择节点参数里的「码类型」", p.code_type, VA_BC_AUTO, VA_BC_MAX_TYPE);
            return SetErrorEx(ctx, VA_ERR_BARCODE_TYPE, "条码", "CodeType", val, exp, msg);
        }

        ZXing::ReaderOptions opts;
        opts.setFormats(FormatOfType(p.code_type));

        opts.setTryHarder(p.try_harder != 0);
        opts.setTryRotate(p.try_rotate != 0);
        opts.setTryDownscale(p.try_downscale != 0);
        opts.setTryInvert(p.try_invert != 0);
        opts.setIsPure(p.is_pure != 0);
        opts.setMaxNumberOfSymbols((unsigned char)maxN);
        opts.setTextMode(ZXing::TextMode::Plain);   // HRI 会给部分码制加人读前缀，干扰下游比对

        ZXing::ImageView view(lum.data, lum.cols, lum.rows, ZXing::ImageFormat::Lum, (int)lum.step);
        ZXing::Barcodes results = ZXing::ReadBarcodes(view, opts);

        // 软超时兜底：zxing 内部是一次不可中断的整幅扫描（尤其"更努力 + 多方向 + 全码制"），
        // 只有等它返回才能判断。这里到点就把结果丢掉、明确报超时 ——
        // 半截结果比没有更糟：现场会拿一个"漏了一个码"的判 NG 去怀疑工艺。
        if (DeadlineHit())
            return SetErrorEx(ctx, VA_ERR_ALGO_TIMEOUT, "条码", "TimeoutMs",
                              "超时", "在超时前完成扫描",
                              "条码扫描在超时时间内未完成；请缩小搜索 ROI、关闭「更努力/多方向」或只选具体码制，或调大「超时(ms)」");

        int n = 0;
        for (size_t i = 0; i < results.size() && n < maxN; ++i)
        {
            const ZXing::Barcode& bc = results[i];
            if (!bc.isValid()) continue;

            VA_Barcode r;
            std::memset(&r, 0, sizeof(r));

            int truncated = 0;
            CopyUtf8(bc.text(), r.text, VA_BARCODE_TEXT_BYTES, &truncated);
            r.text_truncated = truncated;

            CopyUtf8(FormatName(bc.format()), r.format, (int)sizeof(r.format), 0);
            CopyUtf8(bc.ecLevel(), r.ec_level, (int)sizeof(r.ec_level), 0);

            const ZXing::Position& pos = bc.position();
            const ZXing::PointI& p0 = pos.topLeft();
            const ZXing::PointI& p1 = pos.topRight();
            const ZXing::PointI& p2 = pos.bottomRight();
            const ZXing::PointI& p3 = pos.bottomLeft();

            // 纯码模式（isPure）下 zxing 可能不给位置 —— 退化到整幅图，避免输出 (0,0) 的假框
            const bool hasPos = (p0.x != 0 || p0.y != 0 || p1.x != 0 || p1.y != 0
                                 || p2.x != 0 || p2.y != 0 || p3.x != 0 || p3.y != 0);
            if (hasPos)
            {
                r.px[0] = (double)p0.x; r.py[0] = (double)p0.y;
                r.px[1] = (double)p1.x; r.py[1] = (double)p1.y;
                r.px[2] = (double)p2.x; r.py[2] = (double)p2.y;
                r.px[3] = (double)p3.x; r.py[3] = (double)p3.y;
            }
            else
            {
                r.px[0] = 0.0;          r.py[0] = 0.0;
                r.px[1] = lum.cols - 1.0; r.py[1] = 0.0;
                r.px[2] = lum.cols - 1.0; r.py[2] = lum.rows - 1.0;
                r.px[3] = 0.0;          r.py[3] = lum.rows - 1.0;
            }

            double minX = r.px[0], maxX = r.px[0], minY = r.py[0], maxY = r.py[0];
            double sumX = 0.0, sumY = 0.0;
            for (int k = 0; k < 4; ++k)
            {
                if (r.px[k] < minX) minX = r.px[k];
                if (r.px[k] > maxX) maxX = r.px[k];
                if (r.py[k] < minY) minY = r.py[k];
                if (r.py[k] > maxY) maxY = r.py[k];
                sumX += r.px[k];
                sumY += r.py[k];
            }
            r.x = minX;
            r.y = minY;
            r.w = maxX - minX;
            r.h = maxY - minY;
            r.cx = sumX * 0.25;
            r.cy = sumY * 0.25;

            // 角度：左上→右上这条边。图像坐标 y 向下，atan2(dy,dx) 本身就是「顺时针为正」，
            // 与形状匹配 / RotatedRectShape 的口径一致，不用再取反。
            const double dx = (double)p1.x - (double)p0.x;
            const double dy = (double)p1.y - (double)p0.y;
            if (std::fabs(dx) > 1e-9 || std::fabs(dy) > 1e-9)
            {
                double ang = std::atan2(dy, dx) * 180.0 / 3.14159265358979323846;
                while (ang <= -90.0) ang += 180.0;
                while (ang > 90.0) ang -= 180.0;
                r.angle = ang;
            }

            out[n++] = r;
        }

        *outCount = n;
        return VA_OK;
    }
    catch (const cv::Exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (const std::exception& e) { SetError(ctx, e); return VA_ERR_INTERNAL; }
    catch (...) { SetError(ctx, "条码识别：未知异常（zxing 内部错误）"); return VA_ERR_INTERNAL; }
}

const char* BarcodeVersion()
{
    return ZXING_VERSION_STR;
}

} // namespace va
