using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Core;

namespace VisionPlatform.Recipe
{
    /// <summary>配方的加载 / 保存 / 枚举。</summary>
    public static class RecipeStore
    {
        /// <summary>当前生效的配方。</summary>
        public static Recipe Current { get; private set; }

        /// <summary>当前配方切换时触发。</summary>
        public static event EventHandler CurrentChanged;

        public static List<string> List()
        {
            var names = new List<string>();
            try
            {
                AppPaths.Ensure();

                // 方案目录（新布局）：<配方目录>\<方案名>\recipe.json
                foreach (string dir in Directory.GetDirectories(AppPaths.RecipeDir))
                {
                    if (File.Exists(Path.Combine(dir, "recipe.json")))
                        names.Add(Path.GetFileName(dir));
                }

                // 平铺 json（旧布局）：<配方目录>\<方案名>.json —— 兼容未迁移的老数据
                foreach (string f in Directory.GetFiles(AppPaths.RecipeDir, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!names.Contains(name)) names.Add(name);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("枚举配方失败：" + ex.Message);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static OpResult<Recipe> Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return OpResult<Recipe>.Fail("配方名为空");

            try
            {
                string path = BodyPath(name);
                if (!File.Exists(path)) return OpResult<Recipe>.Fail("配方不存在：" + name);

                Recipe r = Json.LoadFromFile<Recipe>(path);
                if (r == null) return OpResult<Recipe>.Fail("配方解析失败：" + name);
                if (r.Inspection == null) r.Inspection = new Vision.InspectionConfig();

                SetCurrent(r);
                Logger.Info("已加载配方：" + name);
                return OpResult<Recipe>.Ok(r);
            }
            catch (Exception ex)
            {
                return OpResult<Recipe>.Fail("加载配方失败：" + ex.Message);
            }
        }

        /// <summary>方案配置文件路径：新布局 <c>Recipes\&lt;名&gt;\recipe.json</c>，旧布局回落到 <c>Recipes\&lt;名&gt;.json</c>。</summary>
        private static string BodyPath(string name)
        {
            string inDir = Path.Combine(AppPaths.RecipePath(name), "recipe.json");
            if (File.Exists(inDir)) return inDir;

            string flat = Path.Combine(AppPaths.RecipeDir, name + ".json");
            return File.Exists(flat) ? flat : inDir;   // 都不存在时返回新布局路径（Save 会建）
        }

        /// <summary>方案目录下的资产子目录（模板 / OCR 字符库 / 深度学习模型 / 采集图）。</summary>
        public static readonly string[] RecipeSubDirs =
        {
            "Templates",
            System.IO.Path.Combine("Models", "OCR"),
            System.IO.Path.Combine("Models", "DL"),
            "Images"
        };

        private static void EnsureRecipeSubdirs(string recipeDir)
        {
            try
            {
                for (int i = 0; i < RecipeSubDirs.Length; i++)
                    Directory.CreateDirectory(Path.Combine(recipeDir, RecipeSubDirs[i]));
            }
            catch (Exception) { }
        }

        public static OpResult Save(Recipe recipe)
        {
            if (recipe == null) return OpResult.Fail("配方为空");
            if (string.IsNullOrWhiteSpace(recipe.Name)) return OpResult.Fail("配方名不能为空");

            try
            {
                AppPaths.Ensure();
                recipe.Modified = DateTime.Now;

                string dir = AppPaths.RecipePath(recipe.Name);
                Directory.CreateDirectory(dir);
                EnsureRecipeSubdirs(dir);

                string path = Path.Combine(dir, "recipe.json");
                Json.SaveToFile(path, recipe);

                // 保存配方 = 保存整个工程：通讯 / 流程 / 标定一并写进方案目录。
                // 这样"把 A 机工程搬到 B 机"只需拷 Recipes\<方案名> 一个目录。
                //
                // 【失败必须报错，不能只打 Warn】方案目录现在是这些数据的**唯一真源** ——
                // 写失败却返回"保存成功"，现场会表现为"参数改了、重启又在、可方案包还是旧的"，
                // 拿去另一台机器部署时全部丢失，而且无从察觉。宁可让用户看到失败提示。
                OpResult bundle = ProjectBundle.WriteProjectFiles(recipe.Name);
                if (!bundle.Success)
                {
                    Logger.Error("方案数据写入失败：" + bundle.Message);
                    return OpResult.Fail("配方已保存，但方案数据（流程/通讯/标定）写入失败："
                                         + bundle.Message);
                }

                Logger.Info("已保存配方与方案数据：" + recipe.Name);
                return OpResult.Ok("已保存到 " + path);
            }
            catch (Exception ex)
            {
                return OpResult.Fail("保存配方失败：" + ex.Message);
            }
        }

        public static OpResult Delete(string name)
        {
            try
            {
                string path = BodyPath(name);
                if (!File.Exists(path)) return OpResult.Fail("配方不存在：" + name);

                // 方案目录里装的就是该方案的全部资产（配方 + 模板 + 模型），删方案即删资产
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                    && !string.Equals(Path.GetFullPath(dir).TrimEnd('\\'),
                                      Path.GetFullPath(AppPaths.RecipeDir).TrimEnd('\\'),
                                      StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(dir, true);
                }
                else
                {
                    File.Delete(path);   // 旧平铺布局：只删 json
                }
                if (Current != null && string.Equals(Current.Name, name, StringComparison.OrdinalIgnoreCase))
                    SetCurrent(null);

                return OpResult.Ok("已删除");
            }
            catch (Exception ex)
            {
                return OpResult.Fail("删除配方失败：" + ex.Message);
            }
        }

        /// <summary>切换当前配方（界面与引擎共用同一份引用）。</summary>
        public static void SetCurrent(Recipe recipe)
        {
            Current = recipe;
            EventHandler handler = CurrentChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
