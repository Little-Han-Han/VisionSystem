using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Flow;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>
    /// 模块注册表：宿主与模块包之间的<b>唯一交汇点</b>。
    /// <para>
    /// 【依赖方向】模块包 → 本注册表 → 宿主；宿主只读本表，不认识任何具体模块类型。
    /// 这样「视觉算法 / UI / 主框架」三者不再互相引用：模块包只依赖 Framework，
    /// 宿主只依赖 Framework 的抽象。
    /// </para>
    /// <para>
    /// 【按需加载】首次访问时自动扫描 <c>&lt;程序目录&gt;\Modules\*.dll</c>，
    /// 反射创建 <see cref="IFlowModulePackage"/> 并注册；缺失或加载失败都不会让宿主崩溃，
    /// 而是记录到 <see cref="Log"/> 并继续（便于现场排查模块部署问题）。
    /// </para>
    /// </summary>
    public static class FlowModuleRegistry
    {
        private static readonly List<IFlowNodeModule> _modules = new List<IFlowNodeModule>();
        private static readonly Dictionary<string, IFlowNodeModule> _byKey =
            new Dictionary<string, IFlowNodeModule>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _gate = new object();
        private static bool _autoLoadDone;
        private static string _moduleDirectory;
        private static int _loadErrors;

        /// <summary>诊断日志出口（宿主可接到 Logger）。默认丢弃。</summary>
        public static Action<string> Log = delegate { };

        /// <summary>模块目录（默认 <c>&lt;程序目录&gt;\Modules</c>）。</summary>
        public static string ModuleDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_moduleDirectory)) return _moduleDirectory;
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
            }
            set { _moduleDirectory = value; }
        }

        /// <summary>已注册模块数（会触发按需加载）。</summary>
        public static int Count { get { EnsureLoaded(); lock (_gate) return _modules.Count; } }

        /// <summary>加载失败的 DLL / 包数量（诊断用）。</summary>
        public static int LoadErrors { get { EnsureLoaded(); return _loadErrors; } }

        /// <summary>全部模块，按 <see cref="IFlowNodeModule.Order"/> 稳定排序（只读快照）。</summary>
        public static IList<IFlowNodeModule> Modules
        {
            get
            {
                EnsureLoaded();
                lock (_gate) return _modules.ToArray();
            }
        }

        /// <summary>
        /// 注册一个模块（重复 Key 覆盖旧项，便于热重载 / 测试）。
        /// 按 Order 稳定插入：相同 Order 保持注册先后。
        /// </summary>
        public static void Register(IFlowNodeModule module)
        {
            if (module == null) return;
            if (string.IsNullOrEmpty(module.Key))
            {
                Log("[modules] 忽略 Key 为空的模块：" + module.GetType().FullName);
                return;
            }

            // 执行器按 FlowNodeType 分派；Key 必须是已定义的枚举名，否则节点能画出来却跑不了。
            // 新增算法类型需要同时扩展枚举与执行器 —— 这是本期"不改算法代码"约束下的明确边界。
            if (!Enum.IsDefined(typeof(FlowNodeType), module.Key))
            {
                Log("[modules] 忽略未定义的节点类型 Key=\"" + module.Key + "\"（" + module.GetType().FullName + "）");
                return;
            }

            lock (_gate)
            {
                int existing = _modules.FindIndex(m => string.Equals(m.Key, module.Key, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _modules.RemoveAt(existing);

                int insertAt = _modules.Count;
                for (int i = 0; i < _modules.Count; i++)
                {
                    if (_modules[i].Order > module.Order) { insertAt = i; break; }
                }
                _modules.Insert(insertAt, module);
                _byKey[module.Key] = module;
            }
        }

        /// <summary>按 Key 查找；未找到返回 null。</summary>
        public static IFlowNodeModule Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            lock (_gate)
            {
                IFlowNodeModule m;
                return _byKey.TryGetValue(key, out m) ? m : null;
            }
        }

        /// <summary>按节点类型查找；未找到返回 null。</summary>
        public static IFlowNodeModule Find(FlowNodeType type)
        {
            return Find(type.ToString());
        }

        /// <summary>某分类下的模块（保持全局顺序）。</summary>
        public static List<IFlowNodeModule> ModulesInCategory(string categoryKey)
        {
            var list = new List<IFlowNodeModule>();
            foreach (IFlowNodeModule m in Modules)
            {
                if (string.Equals(m.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase))
                    list.Add(m);
            }
            return list;
        }

        /// <summary>
        /// 手动加载一个目录下的全部模块包（幂等：同一文件只加载一次）。
        /// 返回新注册的模块数。
        /// </summary>
        public static int LoadFromDirectory(string directory)
        {
            int before = Count;
            int modules = ModuleLoader.LoadDirectory(directory, delegate(string msg)
            {
                _loadErrors++;
                Log(msg);
            });
            Log("[modules] 从 " + directory + " 加载 " + modules + " 个模块（当前共 " + (Count) + " 个，新增 " + (Count - before) + "）");
            return modules;
        }

        /// <summary>清空并允许重新加载（测试 / 热重载用）。</summary>
        public static void Reset()
        {
            lock (_gate)
            {
                _modules.Clear();
                _byKey.Clear();
                _autoLoadDone = false;
                _loadErrors = 0;
            }
            ModuleLoader.ResetCache();
        }

        /// <summary>首次访问时自动扫描模块目录。</summary>
        public static void EnsureLoaded()
        {
            if (_autoLoadDone) return;
            lock (_gate)
            {
                if (_autoLoadDone) return;
                _autoLoadDone = true;
            }
            LoadFromDirectory(ModuleDirectory);

            // 若 Modules 子目录不存在，退一步尝试程序目录（单文件部署 / 冒烟测试）。
            if (Count == 0)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.Equals(Path.GetFullPath(baseDir).TrimEnd('\\'),
                                   Path.GetFullPath(ModuleDirectory).TrimEnd('\\'),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    LoadFromDirectory(baseDir);
                }
            }

            if (Count == 0)
                Log("[modules] 未发现任何模块包：请确认 Modules\\VisionPlatform.Modules.dll 已部署");
        }
    }
}
