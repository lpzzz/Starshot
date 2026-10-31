using System;
using System.IO;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Starshot.Features.Setting;

/// <summary>
/// ms-screenclip 截图协议接管（issue #17）：当前用户级注册 / 还原，以及协议激活识别。
/// 只写 HKCU\Software\Classes\ms-screenclip 自建键，不碰 HKLM 与其他处理程序；
/// Win+Shift+S 由 shell 触发 ms-screenclip://?source=HotKey 协议调用，经当前用户关联路由到 Starshot。
/// </summary>
public static class ScreenClipProtocolService
{
    private const string Scheme = "ms-screenclip";

    private const string ClassesKeyPath = @"Software\Classes\" + Scheme;

    private const string CommandSubKey = @"shell\open\command";

    /// <summary>
    /// 按配置注册 / 注销协议处理程序。返回是否成功（失败时调用方回弹开关，保持配置与系统状态一致）。
    /// </summary>
    public static bool Apply(bool enable) => enable ? Register() : Unregister();

    /// <summary>当前用户的 ms-screenclip 协议是否指向 Starshot。</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var commandKey = Registry.CurrentUser.OpenSubKey(
                ClassesKeyPath + @"\" + CommandSubKey
            );
            if (commandKey?.GetValue(null) is not string command)
                return false;
            // 目录可能整体搬移过，按进程名判断归属即可；他人接管时不含该名，保持不动
            return command.Contains("Starshot.exe", StringComparison.OrdinalIgnoreCase);
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
            using var protocolKey = Registry.CurrentUser.CreateSubKey(ClassesKeyPath);
            // URL Protocol 空值：声明该键是协议处理程序（缺了协议调用会失败）
            protocolKey.SetValue("URL Protocol", "");
            using var commandKey = Registry.CurrentUser.CreateSubKey(
                ClassesKeyPath + @"\" + CommandSubKey
            );
            commandKey.SetValue(null, $"\"{GetStableEntryPath()}\" \"%1\"");
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
            // 只删自己的注册：command 不指向 Starshot 时视为他人接管，保持不动
            if (!IsRegistered())
                return true;
            Registry.CurrentUser.DeleteSubKeyTree(ClassesKeyPath);
            return true;
        }
        catch
        {
            return false;
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
