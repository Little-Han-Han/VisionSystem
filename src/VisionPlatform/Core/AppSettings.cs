using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Vision;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 全局配置根对象，按三类参数清晰分区，统一持久化到 <see cref="AppPaths.ConfigFile"/>。
    ///   - System    : 系统参数（图片保存、通讯校验等全局开关）
    ///   - Algorithm : 算法参数（当前配方名 + 检测参数快照，保证二次打开软件即恢复）
    ///   - Ui        : 界面设置参数（布局、各视图的相机绑定）
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class AppConfig
    {
        /// <summary>系统参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public SystemSettings System { get; set; }

        /// <summary>算法参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public AlgorithmSettings Algorithm { get; set; }

        /// <summary>界面设置参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public UiSettings Ui { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                System = SystemSettings.CreateDefault(),
                Algorithm = new AlgorithmSettings(),
                Ui = new UiSettings()
            };
        }
    }

    /// <summary>系统参数：图片保存相关与运行前置条件的全局开关。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class SystemSettings
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

        public static SystemSettings CreateDefault()
        {
            return new SystemSettings
            {
                ImageSavePath = Path.Combine(AppPaths.Root, "Images"),
                ImageFormat = "PNG",
                RetentionDays = 30,
                AutoSaveEnabled = false,
                SaveNgOnly = false,
                RequireCommBeforeRun = false
            };
        }

        public SystemSettings Clone()
        {
            return new SystemSettings
            {
                ImageSavePath = ImageSavePath,
                ImageFormat = ImageFormat,
                RetentionDays = RetentionDays,
                AutoSaveEnabled = AutoSaveEnabled,
                SaveNgOnly = SaveNgOnly,
                RequireCommBeforeRun = RequireCommBeforeRun
            };
        }
    }

    /// <summary>算法参数：记录上次使用的配方名，并缓存一份检测参数快照，确保二次打开即恢复。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class AlgorithmSettings
    {
        /// <summary>上次生效的配方名（对应 Recipes 目录下的文件名，空表示无命名配方）。</summary>
        [System.Runtime.Serialization.DataMember]
        public string CurrentRecipeName { get; set; }

        /// <summary>检测参数快照（来自当前配方的 <see cref="InspectionConfig"/>）。</summary>
        [System.Runtime.Serialization.DataMember]
        public InspectionConfig Inspection { get; set; }

        public AlgorithmSettings()
        {
            CurrentRecipeName = "";
            Inspection = null;
        }

        public AlgorithmSettings Clone()
        {
            return new AlgorithmSettings
            {
                CurrentRecipeName = CurrentRecipeName,
                Inspection = Inspection != null ? Inspection.Clone() : null
            };
        }
    }

    /// <summary>界面设置参数：主界面布局与各显示视图的相机绑定。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class UiSettings
    {
        /// <summary>布局预设索引（对应 MainForm 的 LayoutPresets）。</summary>
        [System.Runtime.Serialization.DataMember]
        public int LayoutIndex { get; set; }

        /// <summary>每个视图绑定的相机 Id（空字符串 = 自动分配），下标即视图序号。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<string> ViewBindings { get; set; }

        public UiSettings()
        {
            LayoutIndex = 0;
            ViewBindings = new List<string>();
        }

        public UiSettings Clone()
        {
            var clone = new UiSettings();
            clone.LayoutIndex = LayoutIndex;
            if (ViewBindings != null) clone.ViewBindings = new List<string>(ViewBindings);
            return clone;
        }
    }

    /// <summary>
    /// 全局配置的加载 / 保存与全局访问。启动时 <see cref="Load"/>，界面保存时 <see cref="Save"/>。
    /// 配置按系统 / 算法 / 界面三类分区存放于同一个 JSON 文件（<see cref="AppPaths.ConfigFile"/>）。
    /// </summary>
    public static class ConfigStore
    {
        private static AppConfig _current;

        /// <summary>当前配置（首次访问时自动从磁盘加载）。</summary>
        public static AppConfig Current
        {
            get { return _current ?? (_current = Load()); }
        }

        /// <summary>配置被保存后触发（界面据此刷新状态）。</summary>
        public static event EventHandler Changed;

        /// <summary>从配置文件加载；文件不存在或字段缺失时按默认值补齐。</summary>
        public static AppConfig Load()
        {
            AppConfig cfg = null;

            try
            {
                cfg = Json.LoadFromFile<AppConfig>(AppPaths.ConfigFile);
            }
            catch (Exception ex)
            {
                Logger.Warn("读取配置失败，使用默认值：" + ex.Message);
            }

            if (cfg == null) cfg = AppConfig.CreateDefault();

            // 兼容旧配置 / 字段缺失：逐段补齐
            if (cfg.System == null) cfg.System = SystemSettings.CreateDefault();
            if (cfg.Algorithm == null) cfg.Algorithm = new AlgorithmSettings();
            if (cfg.Ui == null) cfg.Ui = new UiSettings();
            if (cfg.Ui.ViewBindings == null) cfg.Ui.ViewBindings = new List<string>();

            if (string.IsNullOrWhiteSpace(cfg.System.ImageSavePath))
                cfg.System.ImageSavePath = Path.Combine(AppPaths.Root, "Images");
            if (string.IsNullOrWhiteSpace(cfg.System.ImageFormat)) cfg.System.ImageFormat = "PNG";
            if (cfg.System.RetentionDays < 0) cfg.System.RetentionDays = 30;
            if (cfg.Ui.LayoutIndex < 0) cfg.Ui.LayoutIndex = 0;

            _current = cfg;
            return cfg;
        }

        /// <summary>保存配置并广播变更。</summary>
        public static void Save(AppConfig config)
        {
            if (config == null) return;

            _current = config;

            try
            {
                Json.SaveToFile(AppPaths.ConfigFile, config);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败：" + ex.Message);
            }

            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>保存当前配置（便捷重载）。</summary>
        public static void Save()
        {
            Save(Current);
        }
    }
}
