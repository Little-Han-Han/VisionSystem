using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("VisionPlatform")]
[assembly: AssemblyDescription("工业视觉软件平台：C# WinForms 界面 + C++/OpenCV 算法")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("VisionSystem")]
[assembly: AssemblyProduct("VisionPlatform")]
[assembly: AssemblyCopyright("Copyright 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("3f2a1b4c-3001-4a7e-9d21-6e8b5c4a3001")]

// 本机冒烟探针需要验证通讯内部映射/模板（不对外公开 API）
[assembly: InternalsVisibleTo("SmokeUi")]
