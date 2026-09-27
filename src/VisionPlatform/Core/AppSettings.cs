using System;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 系统设置。成员标记 [DataMember]，由 <see cref="Json"/>（DataContractJsonSerializer）持久化到
    /// <see cref="AppPaths.ConfigFile"/>。新增设置项时记得同时补默认值与设置界面。
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class AppSettings
    {
        /// <summary>图片保存根目录。</summary>
        [System.Runtime.Serialization.DataMember]
        public string ImageSavePath { get; set; }

        /// <summary>保存格式：PNG / JPEG / BMP / TIFF。</summary>
        [System.Runtime.Serialization.DataMember]
        public string ImageFormat { get; set; }

        /// <summary>图片保留天数；超过该天数的历史图片会被清理，0 表示不清理。</summary>
        [System.Runtime.Serialization.DataMember]
        public int RetentionDays { get; set; }

        /// <summary>每帧检测后自动保存图像。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool AutoSaveEnabled { get; set; }

        /// <summary>自动保存时仅保存 NG 图像。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool SaveNgOnly { get; set; }

        /// <summary>循环运行前是否要求通讯已连接（TCP 客户端 / Modbus / TCP 服务端）。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool RequireCommBeforeRun { get; set; }

        /// <summary>默认布局索引（对应 MainForm 的布局预设）。</summary>
        [System.Runtime.Serialization.DataMember]
        public int LayoutIndex { get; set; }

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                ImageSavePath = Path.Combine(AppPaths.Root, "Images"),
                ImageFormat = "PNG",
                RetentionDays = 30,
                AutoSaveEnabled = false,
                SaveNgOnly = false,
                RequireCommBeforeRun = false,
                LayoutIndex = 0
            };
        }

        /// <summary>复制一份，界面编辑时先改副本，点保存才写回全局。</summary>
        public AppSettings Clone()
        {
            return new AppSettings
            {
                ImageSavePath = ImageSavePath,
                ImageFormat = ImageFormat,
                RetentionDays = RetentionDays,
                AutoSaveEnabled = AutoSaveEnabled,
                SaveNgOnly = SaveNgOnly,
                RequireCommBeforeRun = RequireCommBeforeRun,
                LayoutIndex = LayoutIndex
            };
        }
    }

    /// <summary>
    /// 系统设置的全局访问与持久化。启动时 <see cref="Load"/>，界面保存时 <see cref="Save"/>。
    /// </summary>
    public static class SettingsStore
    {
        private static AppSettings _current;

        /// <summary>当前设置（首次访问时自动从磁盘加载）。</summary>
        public static AppSettings Current
        {
            get { return _current ?? (_current = Load()); }
        }

        /// <summary>设置被保存后触发。</summary>
        public static event EventHandler Changed;

        /// <summary>从配置文件加载；文件不存在或字段缺失时用默认值补齐。</summary>
        public static AppSettings Load()
        {
            AppSettings s = null;

            try
            {
                s = Json.LoadFromFile<AppSettings>(AppPaths.ConfigFile);
            }
            catch (Exception ex)
            {
                Logger.Warn("读取系统设置失败，使用默认值：" + ex.Message);
            }

            AppSettings def = AppSettings.CreateDefault();
            if (s == null) s = def;

            // 兼容旧配置：缺失字段补默认值
            if (string.IsNullOrWhiteSpace(s.ImageSavePath)) s.ImageSavePath = def.ImageSavePath;
            if (string.IsNullOrWhiteSpace(s.ImageFormat)) s.ImageFormat = def.ImageFormat;
            if (s.RetentionDays < 0) s.RetentionDays = def.RetentionDays;
            if (s.LayoutIndex < 0) s.LayoutIndex = 0;

            _current = s;
            return s;
        }

        /// <summary>保存设置并广播变更。</summary>
        public static void Save(AppSettings settings)
        {
            if (settings == null) return;

            _current = settings;

            try
            {
                Json.SaveToFile(AppPaths.ConfigFile, settings);
                Logger.Info("系统设置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("保存系统设置失败：" + ex.Message);
            }

            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
