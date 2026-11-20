/* 统一错误码定义。库内所有导出函数返回 int：0 成功，其余见下。
 *
 * 【两层结构】失败原因由两部分组成，职责不同：
 *   1) **错误码 + 分类名**  —— 机器可判：调用方 switch(code) 就能决定怎么处理；
 *   2) **结构化详情**       —— 人可定位：哪个模块 / 哪个参数 / 实际值 / 期望口径，
 *                             由 va_get_error_info 取出（见 VA_ErrorInfo）。
 * 0~9 是历史码，取值不变（ABI 兼容）；10 以后是按"失败发生在哪一层"细分出来的码。
 */
#ifndef VA_ERROR_H
#define VA_ERROR_H

#include "va_export.h"

#ifdef __cplusplus
extern "C" {
#endif

#define VA_OK              0   /* 成功 */

/* ---- 通用（历史码，取值不要改）---- */
#define VA_ERR_HANDLE      1   /* 句柄为空（未调用 va_create 或已销毁） */
#define VA_ERR_ARG         2   /* 参数非法（未细分的兜底：空指针 / 尺寸为 0） */
#define VA_ERR_SIZE        3   /* 尺寸不匹配（未细分的兜底） */
#define VA_ERR_TYPE        4   /* 图像类型不支持（未细分的兜底） */
#define VA_ERR_INTERNAL    5   /* 算法内部异常 */
#define VA_ERR_NO_TEMPLATE 6   /* 未训练模板就调用匹配 */
#define VA_ERR_TIMEOUT     7   /* 读帧超时 / 无图像 */
#define VA_ERR_CAMERA      8   /* 相机错误（未细分的兜底） */
#define VA_ERR_NO_EDGE     9   /* 未找到足够的边缘点（未细分的兜底） */

/* ---- 输入 / 参数（10~15）---- */
#define VA_ERR_NO_INPUT        10  /* 没有输入帧（上游没出图 / 首节点未取到图） */
#define VA_ERR_ROI             11  /* ROI 非法：与图像无交集、或宽高 <= 0 */
#define VA_ERR_IMAGE_TOO_SMALL 12  /* 图像小于该算法的下限（附实际尺寸与下限） */
#define VA_ERR_CHANNEL         13  /* 通道数不支持（1/3/4 之外；或本工具只吃单通道） */
#define VA_ERR_PARAM_RANGE     14  /* 参数数值越界（角度 / 缩放 / 阈值 / 计数等） */
#define VA_ERR_BUFFER_TOO_SMALL 15 /* 输出缓冲区容量不足（附容量与需求量） */

/* ---- 模板 / 模型（16~19）---- */
#define VA_ERR_TEMPLATE_TOO_SMALL  16 /* 模板在某层缩得太小（附模板尺寸 / 层号） */
#define VA_ERR_TEMPLATE_NO_FEATURE 17 /* 模板 ROI 内提取不到特征（无边缘 / 全平） */
#define VA_ERR_MODEL_NOT_LOADED    18 /* 深度学习模型未加载 / 未编译 */
#define VA_ERR_MODEL_INFER         19 /* 推理失败（输入形状不符 / 设备异常） */

/* ---- 几何 / 测量（20~22）---- */
#define VA_ERR_SCAN_DEGENERATE 20 /* 扫描线退化（起点与终点重合） */
#define VA_ERR_FIT_DEGENERATE  21 /* 拟合退化（点重合 / 共线 / 分布不足） */
#define VA_ERR_NO_EDGE_IN_ROI  22 /* 搜索区内边缘为 0（与"边缘不足"区分：一个是 0，一个是不够） */

/* ---- 业务（24~27）---- */
#define VA_ERR_BARCODE_TYPE    24 /* 条码码类型取值非法（附实际值与合法范围） */
#define VA_ERR_CALIB_NOT_READY 25 /* 标定 / 畸变数据未就绪 */
#define VA_ERR_CAMERA_OPEN     26 /* 相机打开失败（被占用 / 无此设备） */
#define VA_ERR_CAMERA_FRAME    27 /* 相机已打开但读帧失败（掉线 / 格式不支持） */

/* ---- 运行控制（28~）---- */
/* 算法软超时：节点设定的截止时间已到，算法在检查点主动退出（见 va_set_deadline_ms）。
 * 与 VA_ERR_TIMEOUT(7) 的区别：7 是"没等到数据"（读帧超时），28 是"算不完"（算力/参数问题）。
 * 处理方式也不同：7 通常重试取帧，28 应该改参数 / 缩 ROI / 放宽超时。 */
#define VA_ERR_ALGO_TIMEOUT    28

/* 错误码转分类名（库内静态字符串，不会失败，UTF-8）。
 * 只给"类别名"不带原因 —— 具体到某次调用的原因看 VA_ErrorInfo。 */
VA_API const char* VA_CALL va_error_string(int code);

/* ---------------------------------------------------------------------------
 * 结构化错误详情：把"哪一层出的问题"拆成可读字段，避免只拿到一句拼接文本。
 * 字段均为 UTF-8、以 '\0' 结尾；不适用时为空串。
 * -------------------------------------------------------------------------*/
typedef struct VA_ErrorInfo {
    int  code;              /* VA_ERR_* */
    char module[32];        /* 出问题的模块/步骤，如 "形状匹配"、"卡尺/直线"、"条码" */
    char param[32];         /* 出问题的参数名，如 "TemplateX/Y/W/H"、"CodeType" */
    char value[64];         /* 该参数的实际值，如 "ROI=(10,10,5000,4000)"、"999" */
    char expect[64];        /* 期望口径，如 "必须与图像 640x480 有交集"、"0~20" */
    char message[512];      /* 整句（上面几项拼好的人读版本，日志直接用） */
} VA_ErrorInfo;

/* 取最近一次错误的结构化详情。handle 为空返回 VA_ERR_HANDLE。
 * 没有发生过错误时 code = VA_OK、其余字段为空。 */
VA_API int VA_CALL va_get_error_info(void* handle, VA_ErrorInfo* out);

#ifdef __cplusplus
}
#endif

#endif /* VA_ERROR_H */
