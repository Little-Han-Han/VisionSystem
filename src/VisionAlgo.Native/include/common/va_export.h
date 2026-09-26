/* 导出宏与调用约定 —— 所有对外头文件都应包含本文件。 */
#ifndef VA_EXPORT_H
#define VA_EXPORT_H

#ifdef _WIN32
#  ifdef VISIONALGO_EXPORTS
#    define VA_API  __declspec(dllexport)
#  else
#    define VA_API  __declspec(dllimport)
#  endif
#  define VA_CALL __stdcall
#else
#  define VA_API
#  define VA_CALL
#endif

#endif /* VA_EXPORT_H */
