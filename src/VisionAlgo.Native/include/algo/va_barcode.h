/* ============================================================================
 *  条码 / 二维码识别与定位（zxing-cpp 2.3.0）
 *
 *  和模板匹配 / 形状匹配的区别：**不需要训练模板**，一次调用可识别多个码，
 *  并给出每个码的四角点（可直接画旋转框）。
 *
 *  【码类型】VA_BarcodeParam.code_type 是**单选**：一个节点认一种码（可设 AUTO 不限制）。
 *  单选的价值：① 只跑这一类解码器，更快；② 不会把 EAN-13 错认成 Code128 之类的近亲。
 *
 *  【关于 MicroPDF417】zxing-cpp 2.3.0 没有该码制的解码器（上游一直未实现），
 *  因此不在下表内；遇到 MicroPDF417 标签请换成能读它的库，或改贴 PDF417。
 * ==========================================================================*/
#ifndef VA_BARCODE_H
#define VA_BARCODE_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 文本缓冲区容量。二维码理论最大载荷 ~2953 字节，这里取 1KB 并另给截断标记，
 * 避免为了极端长文本把结果结构撑到几 MB（一次最多 255 个结果）。 */
#define VA_BARCODE_TEXT_BYTES 1024

/* ---------------------------------------------------------------------------
 * 码类型（单选）。顺序与条码类型参考图一致：先一维、再二维，最后是扩展码制。
 * 注意 SSCC-18 / EAN-128 / GS1-128 在 zxing 里都属于 Code128 家族（带 FNC1），
 * 没有独立码制，所以统一归到 VA_BC_CODE128。
 * -------------------------------------------------------------------------*/
#define VA_BC_AUTO             0   /* 不限制（逐个解码器都试，最慢但最省心） */
#define VA_BC_EAN13            1
#define VA_BC_UPCA             2
#define VA_BC_CODABAR          3
#define VA_BC_CODE39           4
#define VA_BC_CODE128          5   /* 含 GS1-128 / SSCC-18 / EAN-128 */
#define VA_BC_ITF              6   /* 含 ITF-14 */
#define VA_BC_QRCODE           7
#define VA_BC_DATAMATRIX       8
#define VA_BC_AZTEC            9
#define VA_BC_PDF417           10
#define VA_BC_MAXICODE         11
#define VA_BC_EAN8             12
#define VA_BC_UPCE             13
#define VA_BC_CODE93           14
#define VA_BC_MICROQRCODE      15
#define VA_BC_RMQRCODE         16
#define VA_BC_DATABAR          17
#define VA_BC_DATABAR_EXPANDED 18
#define VA_BC_DATABAR_LIMITED  19
#define VA_BC_DXFILMEDGE       20
#define VA_BC_MAX_TYPE         20  /* 合法取值上限，用于参数校验 */

/* ---------------------------------------------------------------------------
 * 识别参数。param 传 NULL 等价于「AUTO + 多方向 + 反色 + 不做纯码模式」。
 * -------------------------------------------------------------------------*/
typedef struct VA_BarcodeParam {
    int code_type;         /* 码类型，见 VA_BC_*；越界值会被拒绝并报出实际值 */
    int max_count;         /* 最多返回几个码；<=0 按 1，>255 按 255 */
    int try_harder;        /* 1 = 更努力（更慢，小码 / 低对比度 / 破损码才需要） */
    int try_rotate;        /* 1 = 也尝试其它方向（默认应为 1；关掉只认正向） */
    int try_downscale;     /* 1 = 额外做一轮降采样（大图更快，但**小码会漏**） */
    int try_invert;        /* 1 = 也试反色（白码黑底 / 底色偏暗时有用） */
    int is_pure;           /* 1 = 图里只有一个正对镜头的码，跳过定位直接解码（最快，
                              要求搜索区已经框到码且无倾斜） */
} VA_BarcodeParam;

/* ---------------------------------------------------------------------------
 * 单个码的结果。坐标均为输入图像坐标。
 * -------------------------------------------------------------------------*/
typedef struct VA_Barcode {
    char   text[VA_BARCODE_TEXT_BYTES]; /* UTF-8 文本（按 ECI/猜测字符集转码） */
    char   format[24];                  /* 码制名，如 "QRCode" / "Code128" */
    char   ec_level[8];                 /* 纠错等级（QR: L/M/Q/H）；不适用时为空串 */
    double cx;                          /* 中心 X（四角点中心） */
    double cy;                          /* 中心 Y */
    double x;                           /* 轴对齐外接框左上角 */
    double y;
    double w;
    double h;
    double px[4];                       /* 四角点 X：左上、右上、右下、左下（顺时针） */
    double py[4];                       /* 四角点 Y */
    double angle;                       /* 由左上→右上边拟合的旋转角（度，顺时针为正） */
    int    text_truncated;              /* 1 = 文本超过缓冲区被截断 */
} VA_Barcode;

/* ---------------------------------------------------------------------------
 * 识别图像中的全部条码 / 二维码。
 *   out      : 调用方分配的数组，容量 max_count
 *   out_count: 实际写入个数（0 个也返回 VA_OK —— "没识别到"不是错误，
 *              上层据 out_count 与参数生成可行动的诊断文案）
 * 返回 VA_OK / VA_ERR_ARG（含码类型越界）/ VA_ERR_TYPE / VA_ERR_SIZE / VA_ERR_INTERNAL。
 * -------------------------------------------------------------------------*/
VA_API int VA_CALL va_barcode_read(void* handle,
                                   const VA_Image* gray,
                                   const VA_BarcodeParam* param,
                                   VA_Barcode* out,
                                   int max_count,
                                   int* out_count);

/* zxing-cpp 版本串（如 "2.3.0"）。返回值指向库内静态存储，不需要释放。 */
VA_API const char* VA_CALL va_barcode_version(void);

#ifdef __cplusplus
}
#endif

#endif /* VA_BARCODE_H */
