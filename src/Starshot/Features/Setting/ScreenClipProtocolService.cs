using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Starshot.Features.Setting;

/// <summary>
/// ms-screenclip 截图协议接管（issue #17）：官方 Default Programs 注册 + 引导一次性系统确认，以及协议激活识别。
/// Win11 上 ms-screenclip 被 Snipping Tool 等 UWP 声明，系统以 UserChoice 仲裁：仅写经典协议键不进候选、
/// 也不生效。官方路径是 Capabilities\UrlAssociations + RegisteredApplications 注册为默认程序候选，
/// 由用户在系统「默认应用」里选中 Starshot 写入合法 UserChoice（哈希由系统生成，不做逆向伪造）。
/// 仅写当前用户（HKCU），不碰 HKLM 与其他处理程序。
/// </summary>
public static class ScreenClipProtocolService
{
    private const string Scheme = "ms-screenclip";

    private const string ClassesKeyPath = @"Software\Classes\" + Scheme;

    private const string CommandSubKey = @"shell\open\command";

    /// <summary>本应用作为默认程序注册的 Capabilities 键（RegisteredApplications 指向它）</summary>
    private const string CapabilitiesKeyPath = @"Software\Starshot\Capabilities";

    private const string RegisteredApplicationsKeyPath = @"Software\RegisteredApplications";

    /// <summary>开关开启后写入 UserChoice 时系统页面对应的关联键</summary>
    private const string UrlAssociationKeyPath =
        @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + Scheme + @"\UserChoice";

    /// <summary>
    /// 按配置注册 / 注销协议处理程序；注册成功后引导用户到系统「默认程序」页完成一次性确认。
    /// 返回是否成功（失败时调用方回弹开关，保持配置与系统状态一致）。
    /// </summary>
    public static bool Apply(bool enable) => enable ? Register() : Unregister();

    /// <summary>协议关联是否已由系统固定到 Starshot（UserChoice 指向本应用 ProgId）。</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var userChoice = Registry.CurrentUser.OpenSubKey(UrlAssociationKeyPath);
            if (userChoice?.GetValue("ProgId") is not string progId)
                return false;
            // 系统写 UserChoice 时 ProgId 即本应用注册的经典协议键名
            return string.Equals(progId, Scheme, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 激活参数是否为 ms-screenclip 协议调用。未打包应用的协议激活可能以 Protocol、
    /// CommandLineLaunch（URI 作为首个参数）或 Launch 三种形态到达，全部识别。
    /// </summary>
    public static bool IsScreenClipActivation(AppActivationArguments args)
    {
        if (args.Kind is ExtendedActivationKind.Protocol)
        {
            return args.Data is IProtocolActivatedEventArgs protocol
                && string.Equals(protocol.Uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase);
        }
        if (args.Kind is ExtendedActivationKind.CommandLineLaunch)
        {
            if (args.Data is not ICommandLineActivatedEventArgs cmd)
                return false;
            return FirstArgumentStartsWithScheme(cmd.Operation.Arguments);
        }
        if (args.Kind is ExtendedActivationKind.Launch)
        {
            return args.Data is ILaunchActivatedEventArgs launch
                && FirstArgumentStartsWithScheme(launch.Arguments);
        }
        return false;
    }

    private static bool FirstArgumentStartsWithScheme(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return false;
        string[] tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;
        // 协议调用可能带引号（"%1"），剥掉再比对
        string first = tokens[0].Trim('"');
        return first.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Register()
    {
        try
        {
            string entry = GetStableEntryPath();

            // 1) 经典协议键（UserChoice 的 ProgId 执行目标）
            using (var protocolKey = Registry.CurrentUser.CreateSubKey(ClassesKeyPath))
            {
                // URL Protocol 空值：声明该键是协议处理程序（缺了协议调用会失败）
                protocolKey.SetValue("URL Protocol", "");
                using var commandKey = Registry.CurrentUser.CreateSubKey(
                    ClassesKeyPath + @"\" + CommandSubKey
                );
                commandKey.SetValue(null, $"\"{entry}\" \"%1\"");
            }

            // 2) Default Programs 注册：让 Starshot 出现在系统「默认应用」的可选程序里
            using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKeyPath))
            {
                capabilities.SetValue("ApplicationName", "Starshot");
                capabilities.SetValue("ApplicationIcon", $"{entry},0");
                using var urlAssociations = Registry.CurrentUser.CreateSubKey(
                    CapabilitiesKeyPath + @"\UrlAssociations"
                );
                urlAssociations.SetValue(Scheme, Scheme);
            }
            using (
                var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKeyPath)
            )
            {
                registered.SetValue("Starshot", CapabilitiesKeyPath);
            }

            // 3) 引导用户到系统「默认程序」页，一次性选中 Starshot → 勾选 ms-screenclip
            OpenDefaultProgramsPage();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool Unregister()
    {
        try
        {
            // 只删自己的注册：协议键的 command 不指向 Starshot 时视为他人接管，保持不动
            using var commandKey = Registry.CurrentUser.OpenSubKey(
                ClassesKeyPath + @"\" + CommandSubKey
            );
            if (commandKey?.GetValue(null) is string command)
            {
                // 目录可能整体搬移过，按进程名判断归属；他人接管时不含该名
                if (command.Contains("Starshot.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        ClassesKeyPath,
                        throwOnMissingSubKey: false
                    );
                }
            }
            Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKeyPath, throwOnMissingSubKey: false);
            using (
                var registered = Registry.CurrentUser.OpenSubKey(
                    RegisteredApplicationsKeyPath,
                    writable: true
                )
            )
            {
                registered?.DeleteValue("Starshot", throwOnMissingValue: false);
            }
            // 还原系统仲裁状态：删除本协议的用户选择，回落系统默认（删除 UserChoice 不需要哈希）
            Registry.CurrentUser.DeleteSubKeyTree(
                UrlAssociationKeyPath,
                throwOnMissingSubKey: false
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>打开系统「默认程序」页（经典 CPL 逐项列表，桌面程序按字母可见）。</summary>
    private static void OpenDefaultProgramsPage()
    {
        try
        {
            Process.Start(
                new ProcessStartInfo(
                    "control.exe",
                    "/name Microsoft.DefaultPrograms /page pageDefaultProgram"
                )
                {
                    UseShellExecute = true,
                }
            );
        }
        catch
        {
            // 打开引导页失败不视为注册失败：注册表已就绪，用户可自行到设置中确认
        }
    }

    /// <summary>
    /// 协议注册指向的稳定入口：安装版为扁平目录主程序；便携版为根目录 C++ 启动器
    /// （app-{version} 目录随更新改名，指向启动器才能跨版本有效）。
    /// 与 GeneralSetting.GetLauncherPath 同一策略。
    /// </summary>
    private static string GetStableEntryPath()
    {
        string exePath = Environment.ProcessPath ?? "";
        if (AppConfig.Installer)
            return exePath;
        string appDir = Path.GetDirectoryName(exePath) ?? "";
        string rootDir = Path.GetDirectoryName(appDir) ?? "";
        string launcher = Path.Combine(rootDir, "Starshot.exe");
        return File.Exists(launcher) ? launcher : exePath;
    }
}
