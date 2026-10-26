# StayAwake

防止 Windows 在你忙碌时自动锁屏或休眠。

单个便携 exe，无需安装、无运行时依赖、不需要管理员权限。

**Windows 10/11 · .NET Framework 4.x · ~95 KB · MIT**

[English](README.en.md)

## 功能特性

- 持续保持系统活动，阻止自动锁屏、休眠和息屏
- 可选择是否模拟鼠标和键盘输入来对抗锁屏
- 仅在系统空闲时才模拟输入，避免干扰你的操作
- 禁用屏幕保护程序（退出时自动恢复原设置）
- 托盘常驻，双击打开设置界面
- 支持开机自启（无需管理员权限）
- 支持计划任务静默运行模式
- 详细日志记录，便于排查问题

## 快速开始

1. 下载 [最新 Release](https://github.com/mingqiaopeng/StayAwake/releases) 中的 `StayAwake.exe`
2. 双击运行，即刻生效，图标会出现在系统托盘
3. 右键托盘图标可打开菜单进行设置，双击可打开设置面板

## 命令行参数

| 参数 | 说明 |
|---|---|
| （无参数） | 默认模式：托盘常驻，立即开始守护 |
| `--gui` | 以窗口模式启动（MainForm） |
| `--no-autostart` | 托盘常驻但不自动开始守护 |
| `--silent` | 静默模式，无窗口无托盘（适合计划任务） |
| `--selftest` | 运行自检诊断，输出报告后退出 |
| `--verbose` | 启用调试日志 |
| `--interval N` | 设置模拟间隔（5-3600 秒，仅 `--silent` 模式） |
| `--no-mouse` | 禁用鼠标模拟（仅 `--silent` 模式） |
| `--no-key` | 禁用键盘模拟（仅 `--silent` 模式） |
| `--always` | 即使系统不空闲也模拟（仅 `--silent` 模式） |

## 工作原理

- **防止休眠/息屏**：使用 `SetThreadExecutionState` 保持系统运行
- **防止锁屏**：通过 `SendInput` 模拟微量鼠标移动和按键（默认用 F24），仅在系统空闲时触发
- **空闲检测**：使用 `GetLastInputInfo` 判断用户是否真的在操作

## 配置文件

配置保存在：`%APPDATA%\StayAwake\settings.ini`

采用原子写入方式，避免写入过程中崩溃导致配置损坏。

## 日志文件

日志保存在：`%LOCALAPPDATA%\StayAwake\stayawake.log`

超过 512KB 会自动轮转。

## 从源码编译

需要 Windows 自带的 .NET Framework 4.x 编译器（无需安装 Visual Studio）。

```cmd
cd StayAwake
build.cmd
```

编译成功后会生成 `StayAwake.exe`。`build.cmd /clean` 可清理编译产物。

## 设计要点

本项目在实现过程中踩过很多 WinForms/P/Invoke 的坑，代码中都有详细注释：

- **托盘图标**：只在状态变化时重建，且显式释放旧图标，避免 GDI 句柄泄漏
- **菜单同步**：托盘菜单和设置面板双向同步，通过统一的 `Commit()` 路径保证状态一致
- **空闲检测**：失败时返回"未知"而非"零"，避免误判导致永远不模拟
- **开机自启**：校验注册表指向的路径是否与当前 exe 一致，防止启动旧版本
- **屏幕保护**：退出时严格恢复原始状态，且在崩溃等异常路径也有兜底

## 资源占用

实测托盘模式运行 9 分钟：CPU 累计约 0.4 秒（~0.04%），GDI/USER 句柄稳定无增长。

## 已知限制

- 界面和日志目前仅支持中文
- 主动锁屏（Win+L）或手动休眠不会被阻止，只防止自动触发
- 任务管理器强制结束进程时，屏幕保护程序状态可能无法恢复
- 部分安全软件可能会拦截键鼠模拟（UIPI 限制）

## 许可证

[MIT License](LICENSE)

图标来自 [Icons8](https://icons8.com) - verified_account 系列。
