# 能力：ms-screenclip 协议接管（screenclip-protocol）

Starshot 以当前用户（HKCU）级别、可选开启的方式注册为 Windows `ms-screenclip` 截图协议的处理程序。开启后，系统截图入口（Win+Shift+S 热键与应用发起的 `ms-screenclip:` 调用）唤起 Starshot 区域截图；关闭后移除自建注册，系统行为完全还原。

## 行为规格

### 开关与注册（opt-in，官方 Default Programs 路线）

- Scenario: 开关与注册表一致性——首次运行开关默认关闭且 `reg query HKCU\Software\Classes\ms-screenclip` 无 Starshot 自建键；打开开关后该键存在且 `shell\open\command` 指向 Starshot 稳定入口 exe，同时完成 Default Programs 注册（`Capabilities\UrlAssociations` + `RegisteredApplications`）并自动打开系统「默认程序」页引导一次性确认（选中 Starshot 勾选 ms-screenclip，UserChoice 由系统写入）；关闭开关后自建键、Capabilities、RegisteredApplications 项与 `UrlAssociations\ms-screenclip\UserChoice` 均被移除，Win+Shift+S 恢复系统截图工具。
- 开关位于设置 → 截图设置页；状态持久化于应用配置（`AppConfig` 既有机制）
- 注册结构：经典协议键 `HKCU\Software\Classes\ms-screenclip`（`URL Protocol` 空值 + `shell\open\command`，作为 UserChoice 的 ProgId 执行目标）+ Default Programs 注册（`HKCU\Software\Starshot\Capabilities\UrlAssociations` 与 `RegisteredApplications`）
- Win11 上 ms-screenclip 由系统以 UserChoice 仲裁（UWP 竞争者存在，经典注册不进候选）；不逆向伪造 UserChoice 哈希，合法 UserChoice 只能由用户经系统 UI 一次性确认写入
- 入口路径策略：安装版指向主 exe；便携版（进程位于 `app-{version}` 子目录）指向根目录 C++ 启动器，保证更新换版本目录后注册仍有效
- 仅创建/删除自建键；不修改 HKLM、不修改其他应用或系统截图工具的任何注册项；注销时删除 UserChoice 还原系统仲裁状态（删除不需要哈希）

### 协议激活路由

- Scenario: 协议激活唤起区域截图——接管开启时：(a) Starshot 未运行，通过「运行」执行 `ms-screenclip:`，Starshot 冷启动并直接进入区域截图覆盖层；(b) Starshot 托盘常驻（--hide），同一调用唤起区域截图覆盖层且不弹出主窗口。
- 激活分发：`ExtendedActivationKind.Protocol` 且 URI scheme 为 `ms-screenclip` 时进入区域截图（调用 `ScreenCaptureService.CaptureRegion()`，与 Alt+Q 完全同路径，HDR/SDR 处理与输出行为不变）
- 冷启动路径：`OnLaunched` 读取激活参数，识别协议激活后不弹主窗口、直接进入区域截图
- 已运行路径：第二实例经 `AppInstance` 重定向把激活参数交给主实例处理，不弹主窗口
- Scenario: 未知参数降级——`ms-screenclip:?unknownFlag=1` 之类带不支持参数的调用进入区域截图或安全忽略，应用不崩溃、无错误弹窗。

### Win+Shift+S

- Scenario: Win+Shift+S 接管（硬验收）——接管开启后按 Win+Shift+S 唤起 Starshot 区域截图（Win11 宿主机实测）；关闭开关后恢复系统截图工具。
- 机制：Win+Shift+S 由 shell 触发 `ms-screenclip://?source=HotKey` 协议调用，经当前用户协议关联路由到 Starshot；不做热键挂钩、不使用 DisabledHotkeys

### 版本与还原稳定性

- Scenario: 注册跨版本稳定——便携版下注册的 command 指向根目录启动器（非 `app-{version}/Starshot.exe`）；模拟版本目录名变化后注册路径仍有效。
- 更新（版本目录变化）后不重写、不需重开开关，注册持续有效

### 文档与本地化

- Scenario: 文档与多语言——README zh-CN 与 en 含启用/还原/卸载残留清理说明；设置页新增文案在 zh-CN/en 资源中齐备。
- README「已知限制」补充：卸载（外部卸载器）不清理本协议注册，给出 `reg delete` 手动清理命令——与既有 AUMID 残留条目的处理模式一致

## 非目标

见 brief「非目标」：不修改外部卸载器、不接管 Print Screen、不实现其他协议、不处理 L1–L4 已知限制、不改动区域截图自身逻辑。
