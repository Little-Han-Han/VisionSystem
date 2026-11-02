using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VisionPlatform.Calibration
{
    /// <summary>
    /// 标定类型。
    /// <para>
    /// 【本轮实现范围】只做 <see cref="NPoint"/>（N 点标定：像素 → 机械 的二维相似变换，
    /// 纯 C# 最小二乘，不依赖 C++ 算法库）。
    /// <see cref="Distortion"/>（相机畸变矫正，需要棋盘格 / 圆点 + 原生 calibrateCamera）
    /// 与 <see cref="RotationCenter"/>（旋转中心：需要旋转平台多点拟圆）<b>本轮不做</b>，
    /// 枚举值与字段先留好，界面只放「下一轮实现」占位，绝不放假 UI。
    /// </para>
    /// </summary>
    public enum CalibKind
    {
        /// <summary>N 点标定（像素坐标 → 机械坐标，含旋转 / 尺度 / 平移）。</summary>
        NPoint = 0,

        /// <summary>相机畸变矫正（棋盘格 / 圆点标定板）。下一轮实现。</summary>
        Distortion = 1,

        /// <summary>旋转中心标定。下一轮实现。</summary>
        RotationCenter = 2
    }

    /// <summary>
    /// 一条标定记录。同一台相机支持多条记录（不同镜头 / 不同工位 / 历史版本），
    /// 由 <see cref="CalibSettings.CurrentByCamera"/> 指定「当前生效」的那一条。
    /// <para>
    /// 【持久化】整条记录以 <c>[DataMember]</c> 落到 <c>AppConfig.Calibrations</c>，
    /// 与项目其它配置同走 <c>Core/Json.cs</c>（DataContractJsonSerializer，零第三方依赖）。
    /// </para>
    /// <para>
    /// 【Affine 的排列】<c>double[6]</c> = { A11, A12, A21, A22, Tx, Ty }，含义是
    /// <code>
    /// 机械X = A11 * 像素X + A12 * 像素Y + Tx
    /// 机械Y = A21 * 像素X + A22 * 像素Y + Ty
    /// </code>
    /// 对二维相似变换有 A21 = -A12、A22 = A11（尺度 + 旋转 + 平移，不含错切 / 透视）。
    /// 留出完整 6 参数是为了下一轮万一要升级成仿射时不必改配置结构。
    /// </para>
    /// </summary>
    [DataContract]
    public sealed class CalibRecord
    {
        /// <summary>Affine 数组的固定长度。</summary>
        public const int AffineLength = 6;

        /// <summary>记录主键（GUID 字符串，永不为空）。</summary>
        [DataMember]
        public string Id { get; set; }

        /// <summary>所属相机 Id（与 <c>CameraInfo.Id</c> 一致；空串表示「未绑定相机」）。</summary>
        [DataMember]
        public string CameraId { get; set; }

        /// <summary>用户可改的显示名（同一相机下允许重名，不做唯一约束）。</summary>
        [DataMember]
        public string Name { get; set; }

        /// <summary>标定类型。</summary>
        [DataMember]
        public CalibKind Kind { get; set; }

        /// <summary>创建时间（界面「时间」列）；缺失时反序列化得 <see cref="DateTime.MinValue"/>。</summary>
        [DataMember]
        public DateTime CreatedAt { get; set; }

        /// <summary>RMS 拟合残差（mm）。</summary>
        [DataMember]
        public double RmsError { get; set; }

        /// <summary>平均偏移（mm）：各点模型预测值与实测值的欧氏距离均值。</summary>
        [DataMember]
        public double MeanOffset { get; set; }

        /// <summary>最大偏移（mm）。</summary>
        [DataMember]
        public double MaxOffset { get; set; }

        /// <summary>精度（1σ，mm）：残差的样本标准差。</summary>
        [DataMember]
        public double Precision { get; set; }

        /// <summary>参与解算的点数。</summary>
        [DataMember]
        public int PointCount { get; set; }

        /// <summary>
        /// 变换参数 6 元组，排列见类型注释。N 点标定成功后写入；
        /// 畸变 / 旋转中心本轮不填，保持全 0。
        /// </summary>
        [DataMember]
        public double[] Affine { get; set; }

        /// <summary>
        /// 预留扩展位（JSON 文本）：畸变系数、旋转中心坐标等下一轮的附加参数。
        /// 本轮始终为空串，反序列化缺失即为 null → <see cref="Normalize"/> 归一化为 ""。
        /// </summary>
        [DataMember]
        public string ExtraJson { get; set; }

        /// <summary>创建一条空记录（结果字段全 0，等待「计算」填入）。</summary>
        /// <param name="cameraId">所属相机 Id，可为空串。</param>
        /// <param name="name">显示名。</param>
        /// <param name="kind">标定类型。</param>
        public static CalibRecord Create(string cameraId, string name, CalibKind kind)
        {
            CalibRecord rec = new CalibRecord();
            rec.Id = Guid.NewGuid().ToString("N");
            rec.CameraId = cameraId ?? "";
            rec.Name = string.IsNullOrEmpty(name) ? "标定" : name;
            rec.Kind = kind;
            rec.CreatedAt = DateTime.Now;
            rec.Affine = new double[AffineLength];
            rec.ExtraJson = "";
            return rec;
        }

        /// <summary>
        /// 归一化：把反序列化可能得到的 null / 短数组补成合法状态。
        /// 【为什么必须有】旧版 appconfig.json 里没有 Calibrations 段，
        /// 手改过的配置也可能缺 Affine —— 不归一化的话界面一读就 NRE。
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (CameraId == null) CameraId = "";
            if (Name == null) Name = "";
            if (ExtraJson == null) ExtraJson = "";

            if (Affine == null || Affine.Length != AffineLength)
            {
                double[] fixedAffine = new double[AffineLength];
                if (Affine != null)
                {
                    int copy = Math.Min(Affine.Length, AffineLength);
                    for (int i = 0; i < copy; i++) fixedAffine[i] = Affine[i];
                }
                Affine = fixedAffine;
            }
        }

        /// <summary>类型的中文显示名（界面「类型」列）。</summary>
        public string KindDisplay
        {
            get { return Display(Kind); }
        }

        /// <summary>是否有可用解算结果（用于决定 RMS 列显示数值还是占位符）。</summary>
        public bool HasResult
        {
            get { return PointCount > 0 && Affine != null && Affine.Length == AffineLength; }
        }

        /// <summary>标定类型 → 中文名。</summary>
        public static string Display(CalibKind kind)
        {
            switch (kind)
            {
                case CalibKind.NPoint: return "N 点";
                case CalibKind.Distortion: return "畸变";
                case CalibKind.RotationCenter: return "旋转中心";
                default: return "未知";
            }
        }

        /// <summary>浅拷贝（<see cref="Affine"/> 深拷贝，避免两条记录共用同一数组）。</summary>
        public CalibRecord Clone()
        {
            CalibRecord copy = new CalibRecord();
            copy.Id = Id;
            copy.CameraId = CameraId;
            copy.Name = Name;
            copy.Kind = Kind;
            copy.CreatedAt = CreatedAt;
            copy.RmsError = RmsError;
            copy.MeanOffset = MeanOffset;
            copy.MaxOffset = MaxOffset;
            copy.Precision = Precision;
            copy.PointCount = PointCount;
            copy.ExtraJson = ExtraJson;
            copy.Affine = new double[AffineLength];
            if (Affine != null)
            {
                int n = Math.Min(Affine.Length, AffineLength);
                for (int i = 0; i < n; i++) copy.Affine[i] = Affine[i];
            }
            return copy;
        }
    }

    /// <summary>
    /// 标定配置段（<c>AppConfig.Calibrations</c>）：全部记录 + 每台相机「当前生效」的记录 Id。
    /// <para>
    /// 【反序列化必须归一化】旧配置文件没有本段 → <c>Calibrations</c> 为 null；
    /// 段存在但缺字段 → <c>Records</c> / <c>CurrentByCamera</c> 为 null。
    /// 一律由 <see cref="Normalize"/> 补齐，绝不让界面拿到 null。
    /// </para>
    /// </summary>
    /// <summary>「某台相机当前生效的标定记录」一条映射（用 List 存，见 CalibSettings 的说明）。</summary>
    [DataContract]
    public sealed class CalibCurrent
    {
        [DataMember]
        public string CameraId { get; set; }

        [DataMember]
        public string RecordId { get; set; }

        public CalibCurrent() { CameraId = ""; RecordId = ""; }
    }

    [DataContract]
    public sealed class CalibSettings
    {
        /// <summary>全部标定记录（跨相机，按 CameraId 过滤使用）。</summary>
        [DataMember]
        public List<CalibRecord> Records { get; set; }

        /// <summary>相机 Id → 当前生效记录 Id（键区分大小写，取自 <c>CameraInfo.Id</c>）。</summary>
        /// <summary>
        /// 「每台相机当前生效的标定记录」。
        /// <para>
        /// 【为什么不是 Dictionary】<c>DataContractJsonSerializer</c> 对 <c>Dictionary&lt;K,V&gt;</c>
        /// 要求集合契约，直接用会在序列化时抛异常（表现为"点新建就崩"）。
        /// 换成 List + 小结构体，序列化稳定，查找量也极小（相机数量个位数）。
        /// </para>
        /// </summary>
        [DataMember]
        public List<CalibCurrent> CurrentByCamera { get; set; }

        /// <summary>默认空配置。</summary>
        public static CalibSettings CreateDefault()
        {
            return new CalibSettings
            {
                Records = new List<CalibRecord>(),
                CurrentByCamera = new List<CalibCurrent>()
            };
        }

        /// <summary>
        /// 归一化：null 容器补空、逐条记录补字段、指向已删除记录的「当前」映射就地清理。
        /// 幂等，可重复调用。
        /// </summary>
        public static void Normalize(CalibSettings settings)
        {
            if (settings == null) return;

            if (settings.Records == null) settings.Records = new List<CalibRecord>();
            if (settings.CurrentByCamera == null)
                settings.CurrentByCamera = new List<CalibCurrent>();

            // 逐条记录补字段；顺带剔掉反序列化出来的 null 元素（手改配置可能出现）
            for (int i = settings.Records.Count - 1; i >= 0; i--)
            {
                CalibRecord rec = settings.Records[i];
                if (rec == null) { settings.Records.RemoveAt(i); continue; }
                rec.Normalize();
            }

            // 「当前」映射里指向不存在的记录 → 直接删除，避免界面每帧都要判空
            for (int i = settings.CurrentByCamera.Count - 1; i >= 0; i--)
            {
                CalibCurrent cur = settings.CurrentByCamera[i];
                // 内联判存在，别再走 Find（Find 里若调 Normalize 就是死循环）
                if (cur == null || string.IsNullOrEmpty(cur.CameraId) || string.IsNullOrEmpty(cur.RecordId)
                    || !ContainsRecord(settings, cur.RecordId))
                {
                    settings.CurrentByCamera.RemoveAt(i);
                }
            }
        }

        /// <summary>某台相机的全部记录（按创建时间倒序，最新的在最上面）。</summary>
        public List<CalibRecord> RecordsFor(string cameraId)
        {
            Normalize(this);

            string id = cameraId ?? "";
            List<CalibRecord> list = new List<CalibRecord>();
            for (int i = 0; i < Records.Count; i++)
            {
                CalibRecord rec = Records[i];
                if (rec != null && string.Equals(rec.CameraId ?? "", id, StringComparison.Ordinal))
                    list.Add(rec);
            }

            list.Sort(delegate(CalibRecord a, CalibRecord b)
            {
                return b.CreatedAt.CompareTo(a.CreatedAt);
            });
            return list;
        }

        /// <summary>按 Id 取记录；未找到返回 null。</summary>
        public CalibRecord Find(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return null;

            // 【不要在这里调 Normalize】Normalize 清理「当前映射」时会回调本方法，
            // 形成 Normalize → Find → Normalize 的死循环（实测直接 StackOverflow 崩进程）。
            // Normalize 自己保证容器非 null，这里只做查找。
            if (Records == null) return null;

            for (int i = 0; i < Records.Count; i++)
            {
                CalibRecord rec = Records[i];
                if (rec != null && string.Equals(rec.Id, recordId, StringComparison.Ordinal)) return rec;
            }
            return null;
        }

        /// <summary>某台相机「当前生效」的记录；未设置或已删除返回 null。</summary>
        public CalibRecord Current(string cameraId)
        {
            Normalize(this);

            string recordId;
            recordId = LookupCurrent(cameraId);
            if (string.IsNullOrEmpty(recordId)) return null;
            return Find(recordId);
        }

        /// <summary>某台相机「当前生效」的记录 Id；未设置返回空串。</summary>
        public string CurrentIdFor(string cameraId)
        {
            Normalize(this);

            string recordId;
            return LookupCurrent(cameraId);
        }

        /// <summary>把某条记录设为它所属相机的当前记录；传 null 表示清除该相机的当前标记。</summary>
        /// <summary>是否存在指定 Id 的记录（Normalize 内部用，刻意不回调 Find）。</summary>
        private static bool ContainsRecord(CalibSettings settings, string recordId)
        {
            if (settings == null || settings.Records == null || string.IsNullOrEmpty(recordId)) return false;

            for (int i = 0; i < settings.Records.Count; i++)
            {
                CalibRecord rec = settings.Records[i];
                if (rec != null && string.Equals(rec.Id, recordId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>取某相机"当前记录 Id"（找不到返回空串）。</summary>
        private string LookupCurrent(string cameraId)
        {
            string key = cameraId ?? "";
            for (int i = 0; i < CurrentByCamera.Count; i++)
            {
                CalibCurrent cur = CurrentByCamera[i];
                if (cur != null && string.Equals(cur.CameraId, key, StringComparison.Ordinal))
                    return cur.RecordId ?? "";
            }
            return "";
        }

        /// <summary>写某相机的"当前记录 Id"（存在则覆盖，不存在则追加）。</summary>
        private void SetCurrentId(string cameraId, string recordId)
        {
            string key = cameraId ?? "";
            for (int i = 0; i < CurrentByCamera.Count; i++)
            {
                CalibCurrent cur = CurrentByCamera[i];
                if (cur != null && string.Equals(cur.CameraId, key, StringComparison.Ordinal))
                {
                    cur.RecordId = recordId ?? "";
                    return;
                }
            }
            CurrentByCamera.Add(new CalibCurrent { CameraId = key, RecordId = recordId ?? "" });
        }

        public void SetCurrent(CalibRecord record)
        {
            Normalize(this);

            if (record == null) return;
            SetCurrentId(record.CameraId, record.Id);
        }

        /// <summary>删除一条记录，并清理指向它的「当前」映射。</summary>
        public void RemoveRecord(string recordId)
        {
            Normalize(this);
            if (string.IsNullOrEmpty(recordId)) return;

            for (int i = Records.Count - 1; i >= 0; i--)
            {
                CalibRecord rec = Records[i];
                if (rec != null && string.Equals(rec.Id, recordId, StringComparison.Ordinal))
                    Records.RemoveAt(i);
            }

            for (int i = CurrentByCamera.Count - 1; i >= 0; i--)
            {
                CalibCurrent cur = CurrentByCamera[i];
                if (cur != null && string.Equals(cur.RecordId, recordId, StringComparison.Ordinal))
                    CurrentByCamera.RemoveAt(i);
            }
        }

        /// <summary>为一台相机生成下一个不重名的默认名：「标定 1」「标定 2」…。</summary>
        public string NextName(string cameraId)
        {
            List<CalibRecord> exist = RecordsFor(cameraId);

            for (int index = 1; index < 10000; index++)
            {
                string candidate = "标定 " + index.ToString();
                bool used = false;
                for (int i = 0; i < exist.Count; i++)
                {
                    if (string.Equals(exist[i].Name, candidate, StringComparison.Ordinal)) { used = true; break; }
                }
                if (!used) return candidate;
            }
            return "标定 " + DateTime.Now.ToString("HHmmss");
        }
    }
}
