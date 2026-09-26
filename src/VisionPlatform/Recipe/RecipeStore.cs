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
                string[] files = Directory.GetFiles(AppPaths.RecipeDir, "*.json");
                foreach (string f in files)
                    names.Add(Path.GetFileNameWithoutExtension(f));
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
                string path = Path.Combine(AppPaths.RecipeDir, name + ".json");
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

        public static OpResult Save(Recipe recipe)
        {
            if (recipe == null) return OpResult.Fail("配方为空");
            if (string.IsNullOrWhiteSpace(recipe.Name)) return OpResult.Fail("配方名不能为空");

            try
            {
                AppPaths.Ensure();
                recipe.Modified = DateTime.Now;

                string path = Path.Combine(AppPaths.RecipeDir, recipe.Name + ".json");
                Json.SaveToFile(path, recipe);

                Logger.Info("已保存配方：" + recipe.Name);
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
                string path = Path.Combine(AppPaths.RecipeDir, name + ".json");
                if (!File.Exists(path)) return OpResult.Fail("配方不存在：" + name);

                File.Delete(path);
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
