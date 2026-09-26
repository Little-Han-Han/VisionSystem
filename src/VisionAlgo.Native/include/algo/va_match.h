/* 模板训练与匹配（归一化相关系数法）。
   注意：当前实现只支持平移匹配，不具备旋转 / 尺度不变性。 */
#ifndef VA_MATCH_H
#define VA_MATCH_H

#include "va_export.h"
#include "va_types.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 用 ROI 区域训练模板。roi 为 [x, y, w, h]，传 NULL 表示整图。
   模板保存在 handle 对应的上下文中。 */
VA_API int VA_CALL va_train_template(void* handle,
                                     const VA_Image* gray,
                                     const int* roi);

/* 在整幅图中搜索模板，返回最佳位置与得分。
   未训练模板时返回 VA_ERR_NO_TEMPLATE。 */
VA_API int VA_CALL va_match_template(void* handle,
                                     const VA_Image* gray,
                                     VA_MatchResult* out);

#ifdef __cplusplus
}
#endif

#endif /* VA_MATCH_H */
