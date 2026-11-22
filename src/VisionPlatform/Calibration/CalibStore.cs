using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Core;

namespace VisionPlatform.Calibration
{
    /// <summary>
    /// 单条标定的落盘：<c>&lt;程序目录&gt;\Calibrations\&lt;相机&gt;_&lt;名称&gt;_&lt;时间&gt;.json</c>。
    /// <para>
    /// 【为什么每条一个文件】<c>appconfig.json</c> 只当索引（名称/时间/相机/路径），
    /// 标定本体单独成文件：① 单条能整个拷给别人；② 标定多了不会把主配置撑大；
    /// ③ 现场排查时直接看文件就知道这条标定是什么时候、用哪个相机做的。
    /// </para>
    /// </summary>
    public static class CalibStore
    {
        /// <summary>标定文件目录（程序目录下的 Calibrations）。</summary>
        public static string Dir
        {
            get { return Path.Combine(AppPaths.Root, "Calibrations"); }
        }

        /// <summary>
        /// 生成该记录的文件名：<c>相机_名称_时间.json</c>。非法字符替换成下划线。
        /// </summary>
        public static string FileNameOf(CalibRecord record)
        {
            if (record == null) return "calib.json";

            string cam = AssetPaths.Safe(record.CameraId);
            string name = AssetPaths.Safe(record.Name);
            string time = record.CreatedAt > DateTime.MinValue
                ? record.CreatedAt.ToString("yyyyMMdd-HHmmss")
                : DateTime.Now.ToString("yyyyMMdd-HHmmss");

            return cam + "_" + name + "_" + time + ".json";
        }

        /// <summary>
        /// 保存一条标定并回填 <see cref="CalibRecord.FilePath"/>（相对程序目录）。
        /// 已有同名文件时先删，避免残留旧内容。
        /// </summary>
        public static OpResult Save(CalibRecord record)
        {
            if (record == null) return OpResult.Fail("记录为空");

            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                string fileName = FileNameOf(record);
                string full = Path.Combine(Dir, fileName);

                // 改名后文件名会变：旧文件跟着删掉，别留一份重名的旧记录
                if (!string.IsNullOrEmpty(record.FilePath))
                {
                    try
                    {
                        string oldFull = Path.IsPathRooted(record.FilePath)
                            ? record.FilePath
                            : Path.Combine(AppPaths.Root, record.FilePath);

                        if (!string.Equals(Path.GetFullPath(oldFull), Path.GetFullPath(full),
                                           StringComparison.OrdinalIgnoreCase)
                            && File.Exists(oldFull))
                        {
                            File.Delete(oldFull);
                        }
                    }
                    catch (Exception) { }
                }

                Json.SaveToFile(full, record);
                record.FilePath = Path.Combine("Calibrations", fileName);

                return OpResult.Ok(record.FilePath);
            }
            catch (Exception ex)
            {
                return OpResult.Fail("保存标定文件失败：" + ex.Message);
            }
        }

        /// <summary>删除该记录对应的文件（记录被删时调用）。</summary>
        public static void Delete(CalibRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.FilePath)) return;

            try
            {
                string full = Path.IsPathRooted(record.FilePath)
                    ? record.FilePath
                    : Path.Combine(AppPaths.Root, record.FilePath);

                if (File.Exists(full)) File.Delete(full);
            }
            catch (Exception) { }
        }

        /// <summary>把一条记录的绝对路径（供界面显示/打开目录用）。</summary>
        public static string FullPathOf(CalibRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.FilePath)) return "";
            return Path.IsPathRooted(record.FilePath)
                ? record.FilePath
                : Path.Combine(AppPaths.Root, record.FilePath);
        }
    }
}
