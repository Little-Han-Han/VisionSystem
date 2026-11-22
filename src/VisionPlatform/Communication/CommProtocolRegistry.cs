using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VisionPlatform.Core;
using VisionPlatform.Framework.Communication;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 协议插件注册表：扫描 <c>&lt;程序目录&gt;\Communication\*.dll</c>，
    /// 反射查找 <see cref="ICommProtocolFactory"/>。
    /// <para>单个 DLL 失败只记日志，不影响内置 TCP / Modbus。</para>
    /// </summary>
    internal static class CommProtocolRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ICommProtocolFactory> Factories =
            new Dictionary<string, ICommProtocolFactory>(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;

        public static string PluginDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Communication"); }
        }

        public static void Initialize()
        {
            if (_initialized) return;

            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    if (!Directory.Exists(PluginDirectory))
                    {
                        Logger.Info("[comm] 未发现协议插件目录：" + PluginDirectory);
                        return;
                    }

                    string[] files = Directory.GetFiles(PluginDirectory, "*.dll");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                    foreach (string file in files)
                    {
                        TryLoad(file);
                    }

                    Logger.Info("[comm] 协议插件已加载 " + Factories.Count + " 个（目录 " + PluginDirectory + "）");
                }
                catch (Exception ex)
                {
                    Logger.Warn("[comm] 扫描协议插件失败：" + ex.Message);
                }
            }
        }

        public static ICommProtocolFactory Find(string kind)
        {
            Initialize();
            if (string.IsNullOrEmpty(kind)) return null;

            lock (Sync)
            {
                ICommProtocolFactory f;
                return Factories.TryGetValue(kind, out f) ? f : null;
            }
        }

        public static List<string> Kinds()
        {
            Initialize();
            lock (Sync) { return new List<string>(Factories.Keys); }
        }

        private static void TryLoad(string file)
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, "VisionPlatform.Framework.dll", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "VisionPlatform.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "VisionSystem.Controls.dll", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                Assembly asm = Assembly.LoadFrom(file);
                Type[] types = asm.GetTypes();

                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(ICommProtocolFactory).IsAssignableFrom(t)) continue;

                    var factory = Activator.CreateInstance(t) as ICommProtocolFactory;
                    if (factory == null || string.IsNullOrWhiteSpace(factory.Kind)) continue;

                    Factories[factory.Kind.Trim()] = factory;
                    Logger.Info("[comm] 已加载协议：" + factory.Kind + "（" + factory.DisplayName + "）");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[comm] 协议插件加载失败 " + name + "：" + ex.Message);
            }
        }
    }
}
