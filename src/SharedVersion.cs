using System.Reflection;

// =============================================================================
//  版本号唯一真源（Single Source of Truth）
//
//  本文件通过 <Compile Include="..\SharedVersion.cs" Link="Properties\SharedVersion.cs" />
//  链接进全部 C# 项目（宿主 / 控件库 / Framework / Modules / UI.Controls），
//  因此改一次即可让所有程序集版本一致。
//
//  推荐用法：在仓库根目录执行
//      version.bat 1.0.0.1
//  脚本会同时更新本文件与 README 的「当前版本」。
//
//  手改也可以，但三个版本号必须保持一致（版本管理脚本会一起替换）：
//  - AssemblyVersion          程序集绑定版本（强名称 / 插件兼容判断用）
//  - AssemblyFileVersion      资源管理器里「文件版本」显示的值
//  - AssemblyInformationalVersion  产品版本（Application.ProductVersion）
// =============================================================================

[assembly: AssemblyVersion("1.0.15.1")]
[assembly: AssemblyFileVersion("1.0.15.1")]
[assembly: AssemblyInformationalVersion("1.0.15.1")]
