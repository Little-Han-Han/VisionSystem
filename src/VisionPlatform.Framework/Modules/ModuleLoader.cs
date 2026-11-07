using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>
    /// 模块包加载器：扫描目录下的托管 DLL，反射创建 <see cref="IFlowModulePackage"/> 并调用。
    /// <para>
    /// 【隔离原则】单个包抛异常只记录、不中断启动：现场少一个可选模块，
    /// 不应该让整套软件起不来。加载失败信息经 <c>log</c> 输出（宿主接到日志库）。
    /// </para>
    /// </summary>
    public static class ModuleLoader
    {
        private static readonly HashSet<string> _loadedFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>清空"已加载文件"缓存（Reset 用）。</summary>
        public static void ResetCache()
        {
            lock (_loadedFiles) _loadedFiles.Clear();
        }

        /// <summary>
        /// 加载目录下全部模块包；返回注册到的模块数。
        /// </summary>
        /// <param name="directory">模块目录（不存在时返回 0 并写日志）。</param>
        /// <param name="log">错误日志出口（每行一条）。</param>
        public static int LoadDirectory(string directory, Action<string> log)
        {
            int total = 0;

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                if (log != null) log("[modules] 目录不存在：" + directory);
                return 0;
            }

            string[] files;
            try { files = Directory.GetFiles(directory, "*.dll"); }
            catch (Exception ex)
            {
                if (log != null) log("[modules] 枚举目录失败：" + directory + " — " + ex.Message);
                return 0;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                lock (_loadedFiles)
                {
                    if (!_loadedFiles.Add(file)) continue;
                }

                total += LoadFile(file, log);
            }

            return total;
        }

        /// <summary>加载单个 DLL；返回注册到的模块数。</summary>
        public static int LoadFile(string file, Action<string> log)
        {
            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                // 非托管 DLL（opencv_world、sqlite3 等）会走到这里：正常情况，不记为错误
                return 0;
            }
            catch (Exception ex)
            {
                if (log != null) log("[modules] 加载失败：" + Path.GetFileName(file) + " — " + ex.Message);
                return 0;
            }

            int count = 0;
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var parts = new List<string>();
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception e in ex.LoaderExceptions)
                    {
                        if (e != null) parts.Add(e.Message);
                    }
                }
                if (log != null)
                    log("[modules] 类型加载不完整：" + Path.GetFileName(file) + " — " + string.Join(" | ", parts.ToArray()));
                types = ex.Types;
            }
            catch (Exception ex)
            {
                if (log != null) log("[modules] 读取类型失败：" + Path.GetFileName(file) + " — " + ex.Message);
                return 0;
            }

            foreach (Type t in types)
            {
                if (t == null || !t.IsClass || t.IsAbstract) continue;
                if (!typeof(IFlowModulePackage).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null)
                {
                    if (log != null) log("[modules] " + t.FullName + " 缺少公共无参构造函数，已跳过");
                    continue;
                }

                try
                {
                    var pkg = (IFlowModulePackage)Activator.CreateInstance(t);
                    int before = FlowModuleRegistry.Count;
                    pkg.Register();
                    count += Math.Max(0, FlowModuleRegistry.Count - before);
                    if (log != null) log("[modules] 已加载包：" + pkg.Name + "（" + t.FullName + "）");
                }
                catch (Exception ex)
                {
                    Exception root = ex is TargetInvocationException && ex.InnerException != null
                        ? ex.InnerException : ex;
                    if (log != null) log("[modules] 包注册失败：" + t.FullName + " — " + root.Message);
                }
            }

            return count;
        }
    }
}
