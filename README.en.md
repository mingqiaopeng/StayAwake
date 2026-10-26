# StayAwake

防止 Windows 在你忙碌时自动锁屏或休眠�?
单个便携 exe，无需安装、无运行时依赖、不需要管理员权限�?
**Windows 10/11 · .NET Framework 4.x · �?95 KB · MIT**

[English](README.md)

<!-- 仓库发布后替换路径即可启用：
[![Build](https://github.com/OWNER/REPO/actions/workflows/build.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/build.yml)
-->

---

## 问题在哪

Windows 会自行决定何时锁屏、何时休眠。在长时间渲染、下载、远程会话或开会的时候，
这往往正好是最不该被打断的时刻�?
这里需�?*两套不同的机�?*，因为它们由 Windows 的不同部分管�?
| 想阻止的 | 由谁控制 | 手段 |
|---|---|---|
| 睡眠、息�?| 电源管理�?| `SetThreadExecutionState` |
| 锁屏 | 独立的组策略 | 真正产生输入事件 |

`SetThreadExecutionState` **挡不住锁�?*。要保持屏幕不锁，必须真的产生输入�?
---

## 它做什�?
- 挡住睡眠与息�?- 可选禁用屏保，**退出时恢复你原本的开关状�?*
- 可选注入鼠标移动与按键来对付锁�?- 只在**你真的空闲时**才注入，绝不跟你抢操�?- 出错一定上报，绝不静默失效

---

## 快速开�?
1. 下载 `StayAwake.exe`
2. 双击

安装到此为止。它会落进通知区并立即开始工作。没有窗口、没有配置向导、没有提权弹窗�?
---

## 托盘菜单

```
状态：守护中（已模�?12 �?· 下次 27s�?──────────────────────────────
停止守护                      �?命令项，粗体，无勾号
设置�?                       �?双击图标也开这里
──────────────────────────────
�?阻止系统睡眠   �?阻止显示器关�?  �?禁用屏幕保护程序
──────────────────────────────
�?模拟鼠标移动   �?模拟键盘 (F24)   �?仅空闲时模拟
──────────────────────────────
�?开机自动启�?──────────────────────────────
退�?```

命令项与勾选开�?*形态有意区�?*。「停止守护」是**动作**不是**状�?* —�?状态已经写�?文字里了，再加勾号属于信息重复，而且勾号在动词上会让人误以为点一下是�?设置某个开�?�?
托盘图标只在状�?*真的变化�?*重建，并且显式释放旧图标 —�?`NotifyIcon` 不接�?你赋给它的图标所有权�?
---

## 命令�?
| 命令 | 行为 |
|---|---|
| `StayAwake.exe` | **默认**：托盘常�?+ 立即守护，无窗口 |
| `StayAwake.exe --gui` | 打开图形界面 |
| `StayAwake.exe --no-autostart` | 托盘驻留�?*先不守护** |
| `StayAwake.exe --silent` | 无窗口无托盘（计划任务用�?|
| `StayAwake.exe --selftest` | 自检，打印诊断报告后退�?|
| `StayAwake.exe --verbose` | 打开 Debug 级日�?|

`--silent` 额外支持�?
| 参数 | 行为 |
|---|---|
| `--interval N` | 覆盖间隔�?~3600 �?|
| `--no-mouse` | 不模拟鼠�?|
| `--no-key` | 不模拟键�?|
| `--always` | 即使你正在使用电脑也注入 |

---

## 锁屏是怎么被挡住的

`SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` 挡住睡眠与息屏�?锁屏由独立的组策略控制，不受它影响，所�?StayAwake 还会注入输入事件�?
- **鼠标** —�?相对移动 1 像素再移回，视觉上无位移
- **键盘** —�?�?**F24**，几乎没有任何程序绑定它，因此无副作�?
默认只在**你真的空闲时**才注入，�?`GetLastInputInfo` 检测�?你正在敲键盘时，**一个事件都不会注入**�?
鼠标用的�?*相对**移动，所以指针不会往屏幕边缘漂移�?
---

## 配置

`%APPDATA%\StayAwake\settings.ini`

```ini
Sleep=1
Display=1
Saver=1
SimMouse=1
SimKey=1
OnlyIdle=1
Interval=30
```

写入是原子的（先写临时文件再替换），写到一半崩溃也不会留下半截配置�?
---

## 开机自�?
写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`�?*不需要管理员权限**�?
注册表里的命令行�?*与当前这�?exe 比对校验**。程序一旦被移动或改名，
托盘会明确告诉你"开机自启指向其它路�?，而不是到点悄悄拉起一个旧版本�?
---

## 日志

`%LOCALAPPDATA%\StayAwake\stayawake.log`

```
[INF] 配置载入 | 睡眠=True 息屏=True 屏保禁用=False 鼠标=True 键盘=True 仅空�?True 间隔=30s
[WRN] SendInput 仅提�?0/2 个事�? 拒绝访问 (0x5)（若前台是管理员权限程序，此�?UIPI 拦截；亦可能被安全软件拦截）
```

日志存在的理由：过去所有故障都只体现在"已模�?N 次不�?这种间接现象上，
没有日志等于瞎猜。电源请求失败、`SendInput` �?UIPI 拦下、注册表写不进去�?现在都有明确记录�?
---

## 编译

只需�?Windows 自带�?.NET Framework 4.x 编译器�?*不需�?Visual Studio、不需�?NuGet�?不需要下�?SDK�?*

```cmd
build.cmd
```

产物 `StayAwake.exe` 就在脚本旁边。`build.cmd /clean` 先清产物�?
<details>
<summary>实际的编译器调用</summary>

```cmd
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" ^
  /nologo /target:winexe /platform:anycpu /optimize+ ^
  /warnaserror+ ^
  /win32manifest:app.manifest ^
  /out:StayAwake.exe ^
  /reference:System.dll /reference:System.Core.dll ^
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /resource:SA_ICON_GREY.png,StayAwake.SA_ICON_GREY.png ^
  /resource:SA_ICON_LIGHT.png,StayAwake.SA_ICON_LIGHT.png ^
  /resource:SA_ICON_RED.png,StayAwake.SA_ICON_RED.png ^
  /resource:SA_ICON_NAVY.png,StayAwake.SA_ICON_NAVY.png ^
  StayAwake.cs Log.cs UiKit.cs AutoStart.cs TrayIconFactory.cs ^
  TrayController.cs PanelForm.cs MainForm.cs Program.cs
```

</details>

几个必须知道的点�?
- `/warnaserror+` —�?构建预期�?*零警�?*
- `/win32manifest:app.manifest` **不能�?* —�?它声�?PerMonitorV2 DPI 感知�?  少了它托盘图标在缩放显示器上是糊�?- `build.cmd` **必须�?ASCII** —�?cmd.exe �?OEM 代码页读批处理文件，
  中文注释会变成乱码命�?- `SA_ICON_*.png` �?*源文�?*不是产物，它们通过 `/resource` 嵌进 exe

---

## 文件结构

| 文件 | 内容 |
|---|---|
| `StayAwake.cs` | Win32 互操�?+ 配置对象 + 核心引擎 |
| `Log.cs` | 滚动文件日志 |
| `UiKit.cs` | 配色 / 字体缓存 / 控件工厂 |
| `AutoStart.cs` | 开机自启注册表 + 配置原子持久�?|
| `TrayIconFactory.cs` | 嵌入资源加载 + 裁边超采�?+ 多尺�?ICO |
| `TrayController.cs` | 托盘常驻控制�?+ 菜单 + 设置提交中枢 |
| `PanelForm.cs` | 托盘模式设置面板 |
| `MainForm.cs` | 图形界面（`--gui`�?|
| `Program.cs` | 入口 + 模式分发 + `--selftest` |
| `app.manifest` | PerMonitorV2 DPI 感知 |
| `SA_ICON_*.png` | 4 个图标源文件�?00×200 RGBA�?|

---

## 设计说明

这一节记录的是那�?*不明显、但必须写对**的细节。写下来的原因是它们全都�?**静默失效** —�?代码能编译、看起来正确、然后就是不工作�?
**WinForms 勾选框必须 `CheckOnClick = true`�?* 若是 `false`，点击不会改�?`Checked`，`CheckedChanged` 永不触发，整个菜单项形同虚设 —�?而且代码上完全看不出问题�?
**�?`CheckedChanged` 里写 `Checked` 会无限递归�?* setter 会再次触发事件，
于是「写失败 �?回滚 �?再失�?�?再回滚」直到栈溢出把进程带走�?回滚必须用独立的重入闸门包住�?*绝不�?*再调一次写入函数�?
**`ShowImageMargin` / `ShowCheckMargin` 必须�?`true`**，否则勾号那一列根本不绘制�?`Checked = true` 也看不见任何标记�?
**`NotifyIcon.Text` 是一次系统调用�?* 每写一次就是一�?`Shell_NotifyIcon(NIM_MODIFY)`。无条件每轮重写，屏幕上什么变化都没有，CPU 却实打实地烧�?菜单项的 `Text` 同理，每次赋值都会触发重绘�?
**`NotifyIcon.Icon` 不接管所有权�?* 你赋给它的图标绝不会被控件释放，
换图标时不显式释放旧的，就每次漏一�?GDI 句柄�?
**`Control.Paint` �?`Label` / `Panel` 不触发�?* 它们默认�?`ControlStyles.Opaque`，不�?`UserPaint`，挂上去�?`Paint` 处理函数**一次都不会�?*�?自定义绘制必须自�?`SetStyle(ControlStyles.UserPaint, true)`�?
**`Label` 忘了传坐标会精确叠在一起�?* 三个分组标题算好�?`y` 却没传，
`Label` 默认落在 `(0,0)`，三个标题精确重叠，只剩最后添加的那个可见�?所�?`UiKit.MakeLabel` �?`x` `y` 设成**必填参数**且没有默认�?—�?这类"编译能过、运行时静默错位"的错误就写不出来了�?
**�?Win32 错误码不能用�?`SetLastError` 的导入�?* CLR 会在这种导入前后
保存并还原错误码槽位，于是自己声明的、带 `SetLastError = true` �?`GetLastError`
读到的正�?*刚被覆盖�?*的值。正确做法是 `Marshal.GetLastWin32Error()`�?
**"这次调用失败"不等�?API 不可�?�?* `GetLastInputInfo` 返回 `false` 说明
这次调用失败，不代表 API 缺失。在 `false` 上清�?是否可用"标志�?降级路径就永久失效了�?
**空闲检测失败必须当"未知"，不能当"�?�?* 0 毫秒空闲等于"用户刚刚动过鼠标"�?所以一条返�?0 的失败路径会�?仅空闲时模拟"**每一轮都跳过** —�?模拟永久停摆且毫无提示。失败应返回 `-1`，由调用方决定放行�?
**电源请求按间隔续期，不要每秒调�?* 请求会过期，一秒一调纯属浪费�?
**`SetThreadExecutionState` 挡不住锁屏�?* 它只管睡眠和息屏�?假装它能挡，就会做出一�?看着在跑、照样锁�?的程序�?
---

## 自检

`StayAwake.exe --selftest` 覆盖：嵌入资源、各状态图标渲染、屏保读写往返�?电源请求、空闲检测、输入注入、开机自启注册项、配置文件�?
拿不到控制台时会自动把报告落盘并弹窗告知路径�?
```
[屏幕保护程序]
  [ OK ] 当前状�?          启用
  [FAIL] 写回原�?          SPI_SETSCREENSAVEACTIVE(0x0011) 返回 false
```

屏保这项失败往往**是环境状态而不是代码缺�?* —�?MSDN 明确写着
「若机器已进入省电模式或锁定状态，会抛�?ERROR_OPERATION_IN_PROGRESS」�?自检的价值正体现在此：它�?环境导致"�?代码坏了"区分开了�?
---

## 资源占用实测

托盘模式连续运行 9 分钟（每 20 秒采样）�?
| 指标 | �?| �?|
|---|---|---|
| GDI 对象 | 14 | 14 |
| USER 对象 | 12 | 13 |
| 工作�?| 30.7 MB | 31.6 MB |
| **CPU 累计** | 0.2 s | **0.4 s / 9 分钟**（≈0.04% 单核�?|

另单独压了两条最可疑的路径，�?*无增�?*：托盘图标反复重�?1000 次，GDI 恒定 7�?设置面板开�?150 次，�?25 次到 150 次纹丝不动（那是一次性预热，不是线性泄漏）�?
---

## 已知限制

- 界面与日志目�?*只有中文**，尚未做英文本地化，欢迎贡献�?- 锁屏靠产生输入事件来挡。这对普通锁屏有效，但挡不住第三方客户端的强制锁屏，
  也挡不住 BitLocker �?PIN 输入界面�?- 任务管理器强杀进程时，屏保状态无法恢�?—�?进程连收尾代码都跑不到�?  注销、关机、正常退出都有兜底�?- **不会**阻止你主动锁屏（Win+L）或手动休眠，只防自动触发�?
---

## 许可�?
MIT，见 [LICENSE](LICENSE)�?
图标来源：[Icons8](https://icons8.com)，verified_account 系列�?
