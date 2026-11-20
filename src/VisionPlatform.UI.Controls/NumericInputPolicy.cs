using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 数值输入控件（<see cref="NumericUpDown"/>）承载的值语义。
    /// <para>本枚举只决定「上下按钮一次走多少」，不参与任何业务计算或序列化。</para>
    /// </summary>
    public enum NumericValueKind
    {
        /// <summary>整数值（显示 0 位小数），上下一次 ±1。</summary>
        Int,

        /// <summary>单精度小数值（显示 1 位小数），上下一次 ±0.1。</summary>
        Float,

        /// <summary>双精度小数值（显示 2 位及以上小数），上下一次 ±0.01。</summary>
        Double
    }

    /// <summary>
    /// 全软件数值框的统一行为策略：① 鼠标滚轮一律不改值；② 上下按钮步长按值语义统一。
    /// <para>
    /// 【为什么要有它】各窗体的数值框散落在 Designer / Layout / 动态参数表里，
    /// 逐个挂 <c>MouseWheel</c> 处理器必然会漏（尤其是运行时重建的
    /// <see cref="ParamListControl"/>），而且每个地方各写一个 <c>Increment</c>，
    /// 同一类值的步长会互相漂移。这里做单一出口，主题遍历和全局消息过滤双通道兜底。
    /// </para>
    /// <para>
    /// 【滚轮为什么拦两层】<c>NumericUpDown</c> 的滚轮消息实际落在它内部的
    /// <c>UpDownEdit</c>（TextBox）上，再转发给父控件；只挂控件的
    /// <c>MouseWheel</c> 事件挡不住所有路径，而消息过滤器在测试宿主 / 设计器里
    /// 又可能没安装。两层都做，任何窗体、任何时刻创建出来的数值框都改不了值。
    /// 点击上下箭头与手动输入完全不受影响 —— 它们不走 WM_MOUSEWHEEL。
    /// </para>
    /// <para>
    /// 【步长为什么按显示精度而不是存储类型】模块参数表里大量字段是
    /// <c>NodeParamKind.Double</c> 存储但整数语义（如「最小面积」decimals=0、
    /// 「曝光 µs」decimals=1）。若按存储类型一律给 0.01，面积框每次只能加 0.01、
    /// 曝光框每次只能加 0.01 µs，实际不可用。用户看到的是显示出来的值，
    /// 所以以 <c>DecimalPlaces</c> 作为默认判据：0 位 → int → 1，
    /// 1 位 → float → 0.1，≥2 位 → double → 0.01。
    /// 确实知道底层 C# 类型的调用方可以用 <see cref="SetValueType"/> 显式指定。
    /// </para>
    /// </summary>
    public static class NumericInputPolicy
    {
        /// <summary>int 语义的上下步长。</summary>
        public const decimal IntStep = 1m;

        /// <summary>float 语义的上下步长。</summary>
        public const decimal FloatStep = 0.1m;

        /// <summary>double 语义的上下步长。</summary>
        public const decimal DoubleStep = 0.01m;

        private const int WmMouseWheel = 0x020A;

        /// <summary>同一个静态委托实例，才能靠「先 -= 再 +=」做幂等订阅。</summary>
        private static readonly MouseEventHandler WheelBlocker = BlockWheel;

        /// <summary>
        /// 显式注册的值类型。用弱键表而不是 Dictionary / Tag：
        /// Tag 已被各窗体占用（NodeParamField、TextBox 引用等），而参数面板会频繁重建控件，
        /// 强引用字典会把已销毁的控件一直钉在内存里。
        /// </summary>
        private static readonly ConditionalWeakTable<NumericUpDown, KindBox> ExplicitKinds =
            new ConditionalWeakTable<NumericUpDown, KindBox>();

        private static bool _installed;

        private sealed class KindBox
        {
            public NumericValueKind Kind;
        }

        /// <summary>
        /// 安装进程级滚轮过滤器。必须在首个窗体创建之前调用一次（<c>Program.Main</c>）。
        /// 重复调用无副作用。
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            Application.AddMessageFilter(new NumericWheelFilter());
            _installed = true;
        }

        /// <summary>返回某值语义对应的上下步长。</summary>
        public static decimal StepFor(NumericValueKind kind)
        {
            switch (kind)
            {
                case NumericValueKind.Float: return FloatStep;
                case NumericValueKind.Double: return DoubleStep;
                default: return IntStep;
            }
        }

        /// <summary>返回某 C# 值类型对应的上下步长：int → 1，float → 0.1，double → 0.01。</summary>
        public static decimal StepFor(Type valueType)
        {
            return StepFor(KindFromType(valueType));
        }

        /// <summary>把 C# 类型归一到三种值语义；未识别的类型按整数处理（步长 1）。</summary>
        public static NumericValueKind KindFromType(Type t)
        {
            if (t == null) return NumericValueKind.Int;

            Type underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null) t = underlying;

            if (t == typeof(float) || t == typeof(Single)) return NumericValueKind.Float;
            if (t == typeof(double) || t == typeof(decimal)) return NumericValueKind.Double;
            return NumericValueKind.Int;
        }

        /// <summary>
        /// 显式声明某个数值框的真实值类型，覆盖按 <c>DecimalPlaces</c> 的推断。
        /// 传 null 取消显式声明、回到推断。
        /// </summary>
        public static void SetValueType(NumericUpDown num, Type valueType)
        {
            if (num == null) return;

            ExplicitKinds.Remove(num);
            if (valueType != null)
                ExplicitKinds.Add(num, new KindBox { Kind = KindFromType(valueType) });

            ApplyTo(num);
        }

        /// <summary>解析某个数值框当前应采用的值语义（显式声明 → 数据绑定 → 显示精度）。</summary>
        public static NumericValueKind ResolveKind(NumericUpDown num)
        {
            if (num == null) return NumericValueKind.Int;

            KindBox box;
            if (ExplicitKinds.TryGetValue(num, out box) && box != null) return box.Kind;

            Type bound = TryBoundValueType(num);
            if (bound != null) return KindFromType(bound);

            if (num.DecimalPlaces <= 0) return NumericValueKind.Int;
            if (num.DecimalPlaces == 1) return NumericValueKind.Float;
            return NumericValueKind.Double;
        }

        /// <summary>
        /// 递归套用策略到整棵控件树。幂等，可在主题切换时反复调用。
        /// <para>注意：主题遍历会对自绘控件整棵跳过，被跳过的子树（如
        /// <see cref="ParamListControl"/>）必须在自己的控件创建点单独调用
        /// <see cref="ApplyTo"/>；滚轮另有全局过滤器兜底。</para>
        /// </summary>
        public static void ApplyTree(Control root)
        {
            if (root == null) return;

            NumericUpDown num = root as NumericUpDown;
            if (num != null)
            {
                ApplyTo(num);       // 内部编辑框由 ApplyTo 自己处理
                return;
            }

            if (!root.HasChildren) return;
            foreach (Control c in root.Controls) ApplyTree(c);
        }

        /// <summary>
        /// 对单个数值框套用策略：挂滚轮拦截 + 按值语义设步长 + 保证小数位够显示步长。
        /// </summary>
        public static void ApplyTo(NumericUpDown num)
        {
            if (num == null) return;

            // ① 滚轮拦截：先 -= 再 +=，主题切换 / 重复遍历不会累加订阅
            num.MouseWheel -= WheelBlocker;
            num.MouseWheel += WheelBlocker;
            foreach (Control child in num.Controls)
            {
                child.MouseWheel -= WheelBlocker;
                child.MouseWheel += WheelBlocker;
            }

            // ② 步长。DecimalPlaces 不足时补齐，否则「点一下加 0.01 但只显示 1 位小数」
            //    看起来会像按钮失灵（内部值其实变了，显示被四舍五入回去）。
            NumericValueKind kind = ResolveKind(num);
            decimal step = StepFor(kind);
            if (num.Increment != step) num.Increment = step;

            int needed = kind == NumericValueKind.Int ? 0
                       : kind == NumericValueKind.Float ? 1 : 2;
            if (num.DecimalPlaces < needed) num.DecimalPlaces = needed;
        }

        private static void BlockWheel(object sender, MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        /// <summary>
        /// 判断某个窗口句柄是否落在数值框（含其内部编辑框）里。
        /// 供消息过滤器使用，也供回归测试直接断言，故公开。
        /// </summary>
        public static bool ShouldBlockWheel(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                Control c = Control.FromHandle(hwnd);
                for (int i = 0; c != null && i < 12; i++)
                {
                    if (c is NumericUpDown) return true;
                    c = c.Parent;
                }
            }
            catch
            {
                // 消息过滤路径绝不能因为控件树异常把 UI 线程带崩
            }
            return false;
        }

        /// <summary>读 Value 数据绑定指向的属性类型；没有绑定或解析不了时返回 null。</summary>
        private static Type TryBoundValueType(NumericUpDown num)
        {
            try
            {
                foreach (Binding b in num.DataBindings)
                {
                    if (b == null) continue;
                    if (!string.Equals(b.PropertyName, "Value", StringComparison.OrdinalIgnoreCase))
                        continue;

                    object cur = b.DataSource;
                    BindingSource source = cur as BindingSource;
                    if (source != null) cur = source.Current;
                    if (cur == null) continue;

                    string member = b.BindingMemberInfo.BindingMember;
                    if (string.IsNullOrEmpty(member)) continue;

                    string[] segs = member.Split('.');
                    for (int i = 0; i < segs.Length; i++)
                    {
                        if (cur == null) break;
                        PropertyDescriptor pd = TypeDescriptor.GetProperties(cur).Find(segs[i], true);
                        if (pd == null) break;
                        if (i == segs.Length - 1) return pd.PropertyType;
                        cur = pd.GetValue(cur);
                    }
                }
            }
            catch
            {
                // 绑定源可能是任意对象，解析失败就退回显示精度推断
            }
            return null;
        }

        /// <summary>
        /// 进程级兜底：无论数值框是什么时候、由谁创建的，滚轮消息一律在派发前吞掉。
        /// </summary>
        private sealed class NumericWheelFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmMouseWheel) return false;
                if (!ShouldBlockWheel(m.HWnd)) return false;

                // 懒配置：没走过主题遍历的控件，第一次滚轮经过时也把步长补齐。
                try
                {
                    Control c = Control.FromHandle(m.HWnd);
                    while (c != null && !(c is NumericUpDown)) c = c.Parent;
                    NumericUpDown num = c as NumericUpDown;
                    if (num != null) ApplyTo(num);
                }
                catch
                {
                    // 即使懒配置失败，下面照样吞消息，绝不让滚轮改值
                }
                return true;
            }
        }
    }
}
