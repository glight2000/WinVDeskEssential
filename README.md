# WinVDeskEssential - Win11 Virtual Desktop Enhancer

Windows 11 虚拟桌面增强工具。解决 Windows 原生虚拟桌面切换时所有显示器同步切换的问题，让主屏独立切换桌面，副屏窗口保持不动。

## 功能

- **多屏独立切换**：主屏切换虚拟桌面时，副屏窗口自动保持在当前桌面
- **桌面切换面板**：常驻主屏的桌面列表面板，支持拖拽移动、单击切换
- **桌面名称水印**：在屏幕角落显示当前桌面名称，切换时自动更新
- **系统托盘驻留**：后台运行，右键托盘图标可配置水印样式

## 原理

Windows 虚拟桌面切换是全局的（所有屏幕一起切）。WinVDeskEssential 通过检测桌面切换事件，在切换完成后立即将副屏窗口移动到新桌面，使其保持可见。

- 使用 `IVirtualDesktopManager` COM 接口检测当前桌面
- 使用 `Slions.VirtualDesktop` 库进行桌面切换和窗口移动
- 100ms 轮询检测桌面变化（兼容 Win+Tab、快捷键、任务栏点击等所有切换方式）

## 面板

- 启动后常驻主屏左侧，100x100 方格显示每个桌面名称
- 可拖拽到任意位置；拖到屏幕顶部自动切换为横向布局
- 单击即可切换桌面，当前桌面高亮显示
- 已钉到所有虚拟桌面（切换不会消失）

## 水印

- 默认在屏幕右下角显示当前桌面名称
- 右键托盘图标 → Watermark 子菜单可配置：
  - 显示/隐藏
  - 位置（四个角）
  - 透明度、字号、边距

## 配置持久化

所有用户配置保存在 exe 同目录的 `WinVDeskEssential.ini` 文件中，重启后自动恢复。

## 技术栈

- .NET 8 / WPF
- Windows 11 (10.0.22621+)
- [Slions.VirtualDesktop](https://github.com/Slions/VirtualDesktop) - 虚拟桌面 COM 接口封装
- SQLite (EF Core) - 桌面配置存储
- Hardcodet.NotifyIcon.Wpf - 系统托盘

## 构建

```bash
dotnet build MultiDesk/WinVDeskEssential.csproj -c Release
```

发布单文件独立 exe：

```bash
dotnet publish MultiDesk/WinVDeskEssential.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 使用

1. 运行 `WinVDeskEssential.exe`
2. 系统托盘出现蓝色 "M" 图标
3. 在主屏使用 `Ctrl+Win+Arrow` 切换桌面，副屏窗口自动保持
4. 拖拽面板到合适位置
5. 右键托盘图标可配置水印和面板

## 限制

- 仅支持 Windows 11
- 副屏切桌面时不干预（使用 Windows 默认行为）
- 依赖未公开的 COM 接口，Windows 大版本更新后可能需要适配
