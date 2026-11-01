# 目标

实现上游 issue #17：Starshot 可选（opt-in）接管 Windows `ms-screenclip` 截图协议。开启后，系统截图入口（Win+Shift+S 热键、应用发起的 `ms-screenclip:` 协议调用）唤起 Starshot 的区域截图——与 Alt+Q 完全同路径、同 HDR/SDR 处理与输出行为；关闭后完全还原系统默认行为，不影响其他处理程序。

# 范围

- 设置页（`Features/Setting/ScreenshotSetting`）新增「接管系统截图协议（ms-screenclip）」开关，默认关闭
- 开启时：写经典协议键 `HKCU\Software\Classes\ms-screenclip`（`URL Protocol` 空值 + `shell\open\command` 指向稳定入口 exe）+ Default Programs 注册（`HKCU\Software\Starshot\Capabilities\UrlAssociations` 与 `RegisteredApplications`），并自动打开系统「默认程序」页，引导用户选中 Starshot 勾选 ms-screenclip 完成一次性确认（UserChoice 由系统写入）
- 关闭时移除上述自建注册并删除 `UrlAssociations\ms-screenclip\UserChoice`（还原系统仲裁状态）；只动自建 HKCU 键，不碰 HKLM 与其他应用的注册
- 协议激活路由：`ExtendedActivationKind.Protocol`（冷启动 + 已运行实例重定向两条路径）→ `ScreenCaptureService.CaptureRegion()`
- 未知/不支持的 URI 参数优雅降级（忽略参数进入区域截图，不崩溃不弹错）
- 注册路径跨版本稳定：便携版指向根目录 C++ 启动器（`app-{version}` 目录名变化不影响），安装版指向主 exe
- README（zh-CN 与 en）补充：启用方式、还原方式、卸载后残留的手动清理说明
- 多语言资源键：zh-CN / en 齐备，其余语言按项目现有占位惯例

来源映射（issue #17 验收标准 → 本 change）：

| issue #17 验收标准 | 对应验收示例 |
| --- | --- |
| opt-in 注册、不静默替换默认程序 | A1 |
| Win+Shift/Screenshot 入口调用 Starshot 区域截图并保持 HDR/SDR 行为 | A2、A3 |
| 文档说明启用与还原 | A6 |
| 无论程序是否在托盘运行均能唤起 | A2 |
| 优雅处理不支持的 URI 参数 | A4 |
| 更新后注册仍有效 | A5 |
| 卸载清理干净且不影响其他处理程序 | A1（还原不伤其他程序）+ 非目标（卸载器外部维护，文档说明残留） |

# 非目标

- 不修改外部维护的卸载器：卸载后的注册表残留仅在 README 已知限制中说明并给出手动清理命令（与项目现有 AUMID 残留处理模式一致）
- 不接管 Print Screen 键（上游 issue #9 已由维护者关闭处理）
- 不实现 `ms-screenclip` 以外的协议（如 `ms-screensketch`）
- 不处理 README 已知限制 L1–L4（光标疑难、JXL 亮度元数据、MF 竞态、AUMID 卸载残留完整治理）——留待后续独立 change 评估
- 不改变区域截图自身的行为与输出逻辑（只新增触发入口，`ScreenCaptureService` 零改动）

# 验收示例

- Scenario: 开关与注册表一致性——首次运行开关默认关闭且 `reg query HKCU\Software\Classes\ms-screenclip` 无 Starshot 自建键；打开开关后该键存在且 `shell\open\command` 指向 Starshot 稳定入口 exe；关闭开关后该键被移除，Win+Shift+S 恢复系统截图工具。
- Scenario: 协议激活唤起区域截图——接管开启时：(a) Starshot 未运行，通过「运行」执行 `ms-screenclip:`，Starshot 冷启动并直接进入区域截图覆盖层；(b) Starshot 托盘常驻（--hide），同一调用唤起区域截图覆盖层且不弹出主窗口。
- Scenario: Win+Shift+S 接管（硬验收）——接管开启后按 Win+Shift+S 唤起 Starshot 区域截图（Win11 宿主机实测）；关闭开关后恢复系统截图工具。
- Scenario: 未知参数降级——`ms-screenclip:?unknownFlag=1` 之类带不支持参数的调用进入区域截图或安全忽略，应用不崩溃、无错误弹窗。
- Scenario: 注册跨版本稳定——便携版下注册的 command 指向根目录启动器（非 `app-{version}/Starshot.exe`）；模拟版本目录名变化后注册路径仍有效。
- Scenario: 文档与多语言——README zh-CN 与 en 含启用/还原/卸载残留清理说明；设置页新增文案在 zh-CN/en 资源中齐备。

# 约束与不变量

- 仅写 HKCU（当前用户）注册表；不写 HKLM、不修改其他应用（含系统截图工具）的任何注册
- 区域截图链路复用 `ScreenCaptureService`，不复制逻辑；HDR/SDR 行为不变
- 单实例与激活重定向复用 `App.xaml.cs` 现有 `AppInstance` main-key 机制
- 不静默替换：开关默认关闭，状态变化有明确 UI 反馈
- 遵循项目代码风格与提交规范；PR 前执行 `csharpier format .`

# 决策

- D1 工作区隔离采用 branch（`comet/ms-screenclip-protocol`，目标分支本地 main）——接管开发保持主线干净，便于日后对上游发 PR
- D2 卸载边界：应用侧在关闭开关时清理自建键；卸载残留遵循项目现行模式（README 已知限制 + 手动清理文档）。理由：卸载器为外部维护，仓库内无其他可行选项，故直接采用
- D3 开关放在 ScreenshotSetting（截图设置）页：与截图行为强相关，GeneralSetting 无系统接管类先例
- D4 注册入口路径：安装版指向主 exe；便携版（进程位于 `app-{version}` 子目录时）指向根目录 C++ 启动器，保证跨版本稳定
- D5 Win+Shift+S 机制依据（查证）：Win+Shift+S 触发 `ms-screenclip://?source=HotKey` 经 shell 协议路由，HKCU Classes 覆盖可接管；Win11 新版本存在被系统重置的报道，须本机实测（Win11 26200）
- D6 Q1 决议：Win+Shift+S 接管为**硬验收**（宿主机 Win11 实测必须通过；不过则修复或走 revise-requirements 重议）。理由：issue #17 核心诉求；影响仅当前用户单键、秒级可回滚，风险可控
- D7 Q1 决议：实测环境为**宿主机**（Win11 26200）。理由：当前用户级单键覆盖回滚成本≈0；虚拟机存在已知 ICC 崩溃坑且需重搭构建链
- D8 Q2 决议：接管走**官方 Default Programs 路线**——经典协议键（ProgId 执行目标）+ `HKCU\Software\Starshot\Capabilities\UrlAssociations` + `RegisteredApplications` 注册为默认程序候选，开关开启时自动打开系统「默认程序」页，由用户选中 Starshot 并勾选 ms-screenclip 完成一次性确认（合法 UserChoice 由系统写入哈希，不做逆向伪造）。理由：宿主机实测证明 Win11 以 UserChoice 仲裁 ms-screenclip，仅经典注册不进候选也不生效；逆向 hash（SetUserFTA 路线）非官方、Windows 更新可能失效且上游不可收；官方路线稳定可合并，代价仅为首次一次系统确认。注销时一并删除 Capabilities、RegisteredApplications 项与 UrlAssociations\ms-screenclip\UserChoice（还原系统仲裁状态，删除 UserChoice 不需要哈希）

# 待解决问题

（无）

# 验证预期

- `dotnet build` 构建 `src/Starshot/Starshot.csproj`（x64 Debug）成功
- 注册表断言：`reg query` 验证键的写入/删除/指向路径
- 激活行为本机实测：执行 `ms-screenclip:`、`ms-screenclip:?unknownFlag=1`、按 Win+Shift+S，观察覆盖层、进程与主窗口行为
- README 与多语言资源人工核对
- 只读 Verifier 按验收项独立核对全部结论；最终结果由用户明确接受
