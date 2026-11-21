using System;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 第三方原生 DLL 搜索路径注册。
    /// <para>
    /// 【为什么需要】外部依赖统一收在 <c>&lt;程序目录&gt;\thirdparty\{opencv,openvino,sqlite}\</c>，
    /// 而 Windows 加载器默认只搜「exe 目录 → 系统目录 → PATH」——
    /// 不把子目录加进搜索路径，<c>VisionAlgo.Native.dll</c> 就找不到 <c>opencv_world455.dll</c>，
    /// <c>sqlite3.dll</c> 也找不到（表现为「找不到指定的模块」/ DllNotFoundException）。
    /// </para>
    /// <para>
    /// 【为什么改进程 PATH，不用 AddDllDirectory】<c>SetDefaultDllDirectories</c> /
    /// <c>AddDllDirectory</c> 需要 Win8 或 Win7+KB2533623，工控机年代跨度大；
    /// 修改本进程 PATH 后，LoadLibrary 的默认搜索链（含 PATH）在所有版本都能命中，
    /// 且<b>只影响本进程</b>，不污染系统环境变量。
    /// </para>
    /// <para>
    /// 【时序】必须在任何 P/Invoke 之前调用 —— <see cref="VisionPlatform.Program"/> 的
    /// <c>Main</c> 第一行（<c>VisionAlgo.Native.dll</c> 与 <c>sqlite3.dll</c> 都是按需首次调用时加载，
    /// 早于 Logger 初始化即可覆盖）。
    /// </para>
    /// <para>
    /// 【兼容】老部署（第三方 dll 与 exe 平铺）没有 thirdparty 目录 → 直接返回，行为不变。
    /// </para>
    /// </summary>
    public static class NativeDllPath
    {
        /// <summary>把 &lt;程序目录&gt;\thirdparty\* 各子目录追加进本进程 PATH（幂等）。</summary>
        public static void Register()
        {
            try
            {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thirdparty");
                if (!Directory.Exists(root)) return;

                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception) { return; }
                if (dirs == null || dirs.Length == 0) return;

                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                string add = string.Join(";", dirs);

                string old = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (old.IndexOf(add, StringComparison.OrdinalIgnoreCase) >= 0) return;   // 幂等

                Environment.SetEnvironmentVariable("PATH", add + ";" + old);
            }
            catch (Exception)
            {
                // 路径注册失败不阻断启动：若 dll 实际平铺在 exe 目录，仍能正常加载
            }
        }
    }
}
