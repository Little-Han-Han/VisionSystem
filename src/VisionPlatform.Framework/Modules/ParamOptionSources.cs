using System;
using System.Collections.Generic;
using VisionPlatform.Flow;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>
    /// 参数表「动态选项来源」的注册表。
    /// <para>
    /// 【为什么需要它】<see cref="NodeParamField.OptionsSource"/> 以前只能写死
    /// <c>comm</c> / <c>calib:*</c> 两种，取值代码在宿主里 switch —— 每加一类业务下拉
    /// 都要改一次宿主，模块包就失去了"自带界面"的能力。
    /// 这里改成注册表：模块包在 <c>Register()</c> 里注册一个 Key + 一个委托，
    /// 宿主只按 Key 取，不认识任何具体业务。
    /// </para>
    /// <para>
    /// 【线程】注册发生在启动期（单线程），读取发生在界面线程；用锁保护，
    /// 允许后台流程刷新时也读。
    /// </para>
    /// </summary>
    public static class ParamOptionSources
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, Func<List<NodeParamOption>>> _providers
            = new Dictionary<string, Func<List<NodeParamOption>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册（或覆盖）一个选项来源。provider 返回 null / 空表时界面显示"未配置"占位项。</summary>
        public static void Register(string key, Func<List<NodeParamOption>> provider)
        {
            if (string.IsNullOrEmpty(key) || provider == null) return;
            lock (_sync) { _providers[key] = provider; }
        }

        /// <summary>该 Key 是否已注册。</summary>
        public static bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_sync) { return _providers.ContainsKey(key); }
        }

        /// <summary>取选项；未注册返回 null（与"已注册但为空"区分）。</summary>
        public static List<NodeParamOption> Build(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            Func<List<NodeParamOption>> fn;
            lock (_sync)
            {
                if (!_providers.TryGetValue(key, out fn)) return null;
            }

            try { return fn(); }
            catch (Exception) { return new List<NodeParamOption>(); }
        }

        /// <summary>已注册的 Key（诊断用）。</summary>
        public static List<string> Keys
        {
            get
            {
                lock (_sync) { return new List<string>(_providers.Keys); }
            }
        }
    }
}
