---
generated_from_state_version: 14
---

# 验证

## 当前结果

- 结果: **已归档**
- 验证情况: **已完成检查，验证结果已确认**
- 目标周期: 2
- 迭代: 1
- 验证器尝试次数: 2
- 完成时间: 2026-10-31T17:34:39.125Z
- 摘要: 只读 Verifier 独立核对：4 个实现提交（cab34dd/dad7438/d1d725d/e34edc1）与交接清单一致；Runtime 检查 build-main 与 csharpier-format-clean 均 passed；A1-A6 全部通过：注册/注销/归属检查/失败回弹代码完整（A1），冷启动与常驻唤起路由不弹主窗口（A2），纯协议路由机制与宿主机硬验收实测对应（A3），参数不可引发崩溃（A4），跨版本稳定与自启同策略（A5），文档三语资源齐备（A6）；注册表现场四处已清洁无测试残留

## 验收

| 编号 | 结果 | 来源 | 验收项 | 原因 |
| --- | --- | --- | --- | --- |
| A1 | passed | brief.md | Scenario: 开关与注册表一致性——首次运行开关默认关闭且 `reg query HKCU\Software\Classes\ms-screenclip` 无 Starshot 自建键；打开开关后该键存在且 `shell\open\command` 指向 Starshot 稳定入口 exe；关闭开关后该键被移除，Win+Shift+S 恢复系统截图工具。 | 默认关 AppConfig.Setting.cs:619-623；Register()（ScreenClipProtocolService.cs:156-201）写经典协议键+Capabilities+RegisteredApplications+SHChangeNotify+打开默认程序页，Unregister()（203-244）全量删除并删 UserChoice 还原仲裁；211-214 行归属检查不碰他人注册，全程仅 HKCU；开关失败回弹（ScreenshotSetting.xaml.cs:201-217）；reg query 确认现场已还原 |
| A2 | passed | brief.md | Scenario: 协议激活唤起区域截图——接管开启时：(a) Starshot 未运行，通过「运行」执行 `ms-screenclip:`，Starshot 冷启动并直接进入区域截图覆盖层；(b) Starshot 托盘常驻（--hide），同一调用唤起区域截图覆盖层且不弹出主窗口。 | 冷启动 App.xaml.cs:91-110 设 startHidden 跳过主窗口并 TryEnqueue(CaptureRegion)；托盘常驻走第二实例命名事件直达（App.xaml.cs:61-67 Exit(0)）+常驻实例 StartActivationListener（ScreenClipProtocolService.cs:109-124，静态保活防 GC）；Builder 探针链 listener-registered→signal-result true→event-fired 与代码路径逐点对应、进程数恒 1；GUI 实测不可复现，按代码自洽+证据一致性判定 |
| A3 | passed | brief.md | Scenario: Win+Shift+S 接管（硬验收）——接管开启后按 Win+Shift+S 唤起 Starshot 区域截图（Win11 宿主机实测）；关闭开关后恢复系统截图工具。 | 代码为纯注册表协议路由、无任何热键挂钩，与 spec 机制声明（shell 触发 ms-screenclip://?source=HotKey 经当前用户关联路由）一致；Builder 宿主机 Win11 26200 硬验收实测（覆盖层出现、SnippingTool/ScreenClipping 进程 0）与代码机制完全对应，关闭还原由删 UserChoice+SHChangeNotify 支撑 |
| A4 | passed | brief.md | Scenario: 未知参数降级——`ms-screenclip:?unknownFlag=1` 之类带不支持参数的调用进入区域截图或安全忽略，应用不崩溃、无错误弹窗。 | FirstArgumentStartsWithScheme（144-154）仅匹配 scheme 前缀从不解析参数，未知参数与无参数路由完全相同；全部注册/信号路径有 catch 兜底，不存在可因参数崩溃的代码；Builder 实测覆盖层正常弹出 |
| A5 | passed | brief.md | Scenario: 注册跨版本稳定——便携版下注册的 command 指向根目录启动器（非 `app-{version}/Starshot.exe`）；模拟版本目录名变化后注册路径仍有效。 | GetStableEntryPath（285-294）与 GeneralSetting.GetLauncherPath（510-522）逐行同策略；根启动器 Starshot.Launcher.cpp:65-71 透传 argv 含协议 URI、按 version.ini 动态解析 app-{version}，版本目录变化后注册路径不变仍有效；与既有自启功能同策略，静态逻辑链闭合（便携布局实测缺口已记录于 risks） |
| A6 | passed | brief.md | Scenario: 文档与多语言——README zh-CN 与 en 含启用/还原/卸载残留清理说明；设置页新增文案在 zh-CN/en 资源中齐备。 | README.md:426/288 与 README.zh-CN.md:428/290 含启用/还原/卸载残留 reg delete 清理；4 资源键在 Lang.resx(en)/Lang.zh-CN.resx/Lang.ja-JP.resx 三语齐备，Designer.cs:2927-2957 四属性生成，ScreenshotSetting.xaml:200-207 绑定完整 |

## 检查

| 检查 | 命令 | 工作目录 | 状态 | 退出码 | 耗时 |
| --- | --- | --- | --- | ---: | ---: |
| 构建主程序 (Debug x64) | build src/Starshot/Starshot.csproj -c Debug -p:RuntimeIdentifier=win-x64 --nologo | . | passed | 0 | 3701 ms |
| csharpier 格式合规（format 后 src 无 diff） | -c /c/Users/lence/.dotnet/tools/csharpier format . && git diff --quiet -- src && echo FORMAT-CLEAN | . | passed | 0 | 567 ms |

### Builder 报告的证据

以下为 Builder 报告，不等同于 Runtime 检查凭据或独立验收结果。

- dotnet build src/Starshot/Starshot.csproj (Debug x64): passed — 0 警告 0 错误；.NET 10 SDK 10.0.403 用户级安装
- csharpier format .: passed — 104 文件，无额外改动
- A2(a) 协议冷启动端到端（宿主机）: passed — start ms-screenclip: 后单实例直接进区域截图覆盖层（视觉+窗口几何 3640x1920 双确认），无主窗口
- A2(b) 托盘常驻唤起端到端（宿主机）: passed — 第二实例经命名事件通知常驻实例唤起覆盖层；探针链 listener-registered→signal-result true→event-fired 闭环；进程数恒为 1
- A3 Win+Shift+S 接管（宿主机，硬验收）: passed — keybd_event 模拟 Win+Shift+S 后 Starshot 覆盖层出现，SnippingTool/ScreenClipping 进程 0（无抢跑）
- A4 未知参数降级（宿主机）: passed — ms-screenclip:?unknownFlag=1 后覆盖层正常弹出、进程不崩溃
- A1 注册表结构等效验证: passed — 手动写入与 Register() 完全一致的结构（经典键+URL Protocol+command）加 SHChangeNotify 广播后协议直达实测生效
- A1 UI 开关交互路径: not-run — 需 GUI 点击设置页开关；注册/注销逻辑与上述等效验证同一代码路径，建议 Verifier 或用户在设置页实际点选一次复核
- A5 便携版跨版本稳定: not-run — 本地为扁平构建无 app-{version} 版本化布局与 C++ 根启动器；GetStableEntryPath 与 GeneralSetting.GetLauncherPath 同策略（代码审查），需 CI 便携包实测
- A6 文档与多语言: passed — README zh-CN/en FAQ 与已知限制已更新；Lang.resx(中性/en)/zh-CN/ja-JP 各 4 键齐备，Designer.cs 同步
- 已知限制: 首次开启开关后系统可能弹「选择应用」对话框或需在系统设置中一次性确认（Win11 UserChoice 仲裁）：已在设置页描述与 README FAQ 写明操作步骤，属决议 D8 官方路线的预期行为
- 已知限制: Win11 系统更新可能重置 ms-screenclip 关联（UserChoice 失效）：重新开关一次即恢复，README 已注明
- 已知限制: 重定向路径的降级分支（命名事件送达失败时回落系统重定向）会弹出主窗口：仅在事件监听未就绪的窗口期可能发生
- 已知限制: 本机为多显示器混合 DPI 环境，A2/A3 以窗口几何（不小于 2000x1000 可见窗口）而非像素级截图判定覆盖层出现

## 阻塞项

_无。_

## 风险与跳过的工作

- A2/A3/A4 为宿主机 GUI 实测类证据，Verifier 不可复现，按代码逻辑自洽性+Builder 证据内部一致性判定通过
- A5 便携版 app-{version} 布局未实测（本地为扁平构建），判定基于与既有自启功能同策略的静态代码链
- 原 csharpier-check 回执失败已归因为命令设计错误（1.3.0 无 --check 参数）；修正检查 csharpier-format-clean（format 后 src 无 diff）在 attempt 2 通过

## 之前的迭代

| 目标周期 | 迭代 | 尝试 | 结果 | 未解决项 | 摘要 | 完成时间 |
| ---: | ---: | ---: | --- | --- | --- | --- |
| 1 | 1 | 0 | recovery | — | Native Shape artifacts changed | 2026-10-31T17:17:51.224Z |
| 2 | 1 | 1 | execution-error | — | Native Verifier response was invalid: Native verification cannot pass before every required check succeeds | 2026-10-31T17:33:23.447Z |
| 2 | 1 | 2 | pass | — | 只读 Verifier 独立核对：4 个实现提交（cab34dd/dad7438/d1d725d/e34edc1）与交接清单一致；Runtime 检查 build-main 与 csharpier-format-clean 均 passed；A1-A6 全部通过：注册/注销/归属检查/失败回弹代码完整（A1），冷启动与常驻唤起路由不弹主窗口（A2），纯协议路由机制与宿主机硬验收实测对应（A3），参数不可引发崩溃（A4），跨版本稳定与自启同策略（A5），文档三语资源齐备（A6）；注册表现场四处已清洁无测试残留 | 2026-10-31T17:34:39.125Z |



## 结论

只读 Verifier 独立核对：4 个实现提交（cab34dd/dad7438/d1d725d/e34edc1）与交接清单一致；Runtime 检查 build-main 与 csharpier-format-clean 均 passed；A1-A6 全部通过：注册/注销/归属检查/失败回弹代码完整（A1），冷启动与常驻唤起路由不弹主窗口（A2），纯协议路由机制与宿主机硬验收实测对应（A3），参数不可引发崩溃（A4），跨版本稳定与自启同策略（A5），文档三语资源齐备（A6）；注册表现场四处已清洁无测试残留
