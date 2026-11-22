using System;
using System.Collections.Generic;
using System.IO;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 部署目录布局迁移：把目录名统一成 <b>首字母大写</b>（Config / Recipes / Models …），
    /// 并把两类模型合并到 <c>Models</c> 下（<c>Models\OCR</c>、<c>Models\DL</c>）。
    /// <para>
    /// 【为什么要迁移】现场升级只换二进制、保留数据目录；老目录叫 <c>Config</c>（也可能是某版本
    /// 用过的 <c>CONFIG</c>）、<c>OCRModel</c>、<c>models</c>。不统一的话会同时出现两套目录，
    /// 配置与模型"看起来丢了"。
    /// </para>
    /// <para>
    /// 【必须在 <c>AppPaths.Ensure()</c> 之前调用】一旦新目录被创建，迁移就会因"目标已存在"
    /// 而放弃，旧数据留在旧目录里。
    /// </para>
    /// </summary>
    public static class PathMigration
    {
        /// <summary>顶层目录的统一名字（首字母大写；第三方与通讯同样规则）。</summary>
        private static readonly string[] TopLevel =
        {
            "Config", "Recipes", "Templates", "Images", "Logs", "Calibrations",
            "License", "ThirdParty", "Communication"
        };

        /// <summary>模型子目录（缩写词保持全大写，与业内写法一致）。</summary>
        private const string ModelsDirName = "Models";
        private const string OcrSubDir = "OCR";
        private const string DlSubDir = "DL";

        /// <summary>
        /// 执行迁移。<b>不抛异常</b>；返回做过的动作（供调用方在日志就绪后逐条记录）。
        /// </summary>
        public static List<string> EnsureLayout(string root)
        {
            var done = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return done;

            // ---- ① 先处理模型目录（它可能是老的小写 models，需要暂存才能腾出 Models 这个名字）----
            string modelsTemp = StageLegacyModels(root, done);
            string modelsDir = Path.Combine(root, ModelsDirName);
            try
            {
                if (!Directory.Exists(modelsDir)) Directory.CreateDirectory(modelsDir);
            }
            catch (Exception ex)
            {
                done.Add("[跳过] 创建 " + ModelsDirName + " 失败：" + ex.Message);
                return done;
            }

            if (modelsTemp != null)
            {
                string dlDir = Path.Combine(modelsDir, DlSubDir);
                try
                {
                    if (!Directory.Exists(dlDir)) Directory.Move(modelsTemp, dlDir);
                    else MoveContents(modelsTemp, dlDir, done);
                    done.Add("深度学习模型目录：models → " + ModelsDirName + "\\" + DlSubDir);
                }
                catch (Exception ex)
                {
                    done.Add("[跳过] 迁移 models 失败：" + ex.Message);
                }
            }

            string ocrLegacy = FindActualName(root, "OCRModel");
            if (ocrLegacy != null)
            {
                string ocrDir = Path.Combine(modelsDir, OcrSubDir);
                try
                {
                    if (!Directory.Exists(ocrDir)) Directory.Move(Path.Combine(root, ocrLegacy), ocrDir);
                    else MoveContents(Path.Combine(root, ocrLegacy), ocrDir, done);
                    done.Add("OCR 字符库目录：OCRModel → " + ModelsDirName + "\\" + OcrSubDir);
                }
                catch (Exception ex)
                {
                    done.Add("[跳过] 迁移 OCRModel 失败：" + ex.Message);
                }
            }

            // ---- ② 顶层目录统一成首字母大写（MODELS / models / CONFIG 等都能纠正回来）----
            RenameCase(root, ModelsDirName, done);
            foreach (string name in TopLevel) RenameCase(root, name, done);

            return done;
        }

        /// <summary>
        /// 老版本的小写 <c>models</c>（内容是深度学习模型）需要先改名成临时目录，
        /// 否则它与目标 <c>Models</c> 在 Windows 上就是同一个条目，直接 Move 会失败。
        /// </summary>
        /// <returns>临时目录的完整路径；不需要处理时返回 <c>null</c>。</returns>
        private static string StageLegacyModels(string root, List<string> done)
        {
            string actual = FindActualName(root, "models");
            if (actual == null) return null;

            // 只有"实际名恰好是全小写 models"才是老目录；Models / MODELS 只需改名，不走合并
            if (!string.Equals(actual, "models", StringComparison.Ordinal)) return null;

            string temp = Path.Combine(root, "DL_MODELS_TMP");
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Directory.Move(Path.Combine(root, actual), temp);
                done.Add("模型目录暂存：" + actual + " → DL_MODELS_TMP");
                return temp;
            }
            catch (Exception ex)
            {
                done.Add("[跳过] 模型目录暂存失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 把目录名改成目标名。大小写不敏感地找实际名：只差大小写时用中转名两次改名 ——
        /// Windows 会把"只改大小写"的单次 <c>Directory.Move</c> 判成"源与目标相同"。
        /// </summary>
        private static void RenameCase(string root, string target, List<string> done)
        {
            try
            {
                string actual = FindActualName(root, target);
                if (actual == null) return;
                if (string.Equals(actual, target, StringComparison.Ordinal)) return;   // 已正确

                string from = Path.Combine(root, actual);
                string temp = from + ".~case";
                Directory.Move(from, temp);
                Directory.Move(temp, Path.Combine(root, target));
                done.Add("目录改名：" + actual + " → " + target);
            }
            catch (Exception ex)
            {
                done.Add("[跳过] 目录改名 " + target + " 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 在 <paramref name="root"/> 下查找与 <paramref name="name"/> 对应的**真实目录名**
        /// （大小写不敏感）；不存在返回 <c>null</c>。
        /// <para>不能用 <c>Directory.Exists</c> 判断大小写 —— 它在 Windows 上不区分大小写。</para>
        /// </summary>
        private static string FindActualName(string root, string name)
        {
            try
            {
                foreach (DirectoryInfo d in new DirectoryInfo(root).GetDirectories())
                {
                    if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d.Name;
                }
            }
            catch (Exception)
            {
                // 目录不可读按"不存在"处理
            }
            return null;
        }

        /// <summary>把 <paramref name="src"/> 的内容并入 <paramref name="dst"/>（同名跳过），最后删空源目录。</summary>
        private static void MoveContents(string src, string dst, List<string> done)
        {
            foreach (string file in Directory.GetFiles(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                File.Move(file, target);
            }

            foreach (string dir in Directory.GetDirectories(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(dir));
                if (Directory.Exists(target)) MoveContents(dir, target, done);
                else Directory.Move(dir, target);
            }

            try { Directory.Delete(src, true); }
            catch (Exception) { /* 留着不影响使用，下次启动再试 */ }
        }
    }
}
