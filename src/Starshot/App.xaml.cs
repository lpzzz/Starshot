using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Starshot.Features.Notifications;
using Starshot.Features.Screenshot;
using Starshot.Features.Setting;
using Starshot.Features.Telemetry;
using Starshot.Features.Update;
using Starshot.Features.ViewHost;
using Windows.ApplicationModel.Activation;
using Windows.UI;

namespace Starshot;

public partial class App : Application
{
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly Timer _gcTimer = new(TimeSpan.FromSeconds(60));

    public static new App Current => (App)Application.Current;

    public App()
    {
        this.InitializeComponent();
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += App_UnhandledException;
        // 后台定时 GC：截图（尤其区域截图覆盖层）残留的 RCW/未引用资源靠 GC 回收，
        // GC 看托管堆不看显存，不主动 Collect 会累积占显存。每 60s 回收一次（参考 Starward，且补上它漏掉的 Start）。
        _gcTimer.Elapsed += (_, _) => GC.Collect();
        _gcTimer.Start();
    }

    private void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e
    )
    {
        Program.WriteCrashLog("App Crash", e.Exception);
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs _)
    {
        await AppConfig.CheckEnviromentAsync();

        instance = AppInstance.GetCurrent();
        instance.Activated += AppInstance_Activated;

        var main = AppInstance.FindOrRegisterForKey("main");
        if (!main.IsCurrent)
        {
            // 协议唤起撞上常驻实例：重定向转发的激活参数读不出 URI，改用命名事件
            // 直接送达「开始区域截图」；送达即退出，不走会弹主窗口的重定向
            if (
                ScreenClipProtocolService.IsScreenClipCommandLine()
                && ScreenClipProtocolService.SignalRunningInstanceToCapture()
            )
            {
                Environment.Exit(0);
            }
            await main.RedirectActivationToAsync(instance.GetActivatedEventArgs());
            Environment.Exit(0);
        }

        // 常驻实例：监听第二实例的协议唤起信号
        ScreenClipProtocolService.StartActivationListener(_uiDispatcherQueue);

        // 主实例：检测自启项指向的 exe 是否存在，不存在则清除
        AppConfig.CheckAutoStartValidity();
        AppConfig.CheckTaskValidity();

        // 应用通知：先订阅点击并注册身份，再读冷启动激活参数（顺序不能反）
        AppNotificationService.Initialize(_uiDispatcherQueue, StartupUpdateCheck.ShowPendingUpdate);

        bool startHidden = Environment
            .GetCommandLineArgs()
            .Contains("--hide", StringComparer.OrdinalIgnoreCase);
        // 点通知冷启动：只起托盘和后台服务，不弹主窗口
        if (AppNotificationService.HandleActivation(instance.GetActivatedEventArgs()))
        {
            startHidden = true;
        }

        // ms-screenclip 协议冷启动：直接进区域截图，不弹主窗口（与通知点击同一条静默启动路径）
        bool screenClipLaunch =
            ScreenClipProtocolService.IsScreenClipCommandLine()
            || ScreenClipProtocolService.IsScreenClipActivation(instance.GetActivatedEventArgs());
        if (screenClipLaunch)
        {
            startHidden = true;
        }

        if (!startHidden)
        {
            m_MainWindow = new MainWindow();
            m_MainWindow.Activate();
        }
        EnsureSystemTray();
        if (screenClipLaunch)
        {
            // 托盘常驻宿主就绪后立即唤起区域截图（与热键同一条 UI 线程队列）
            _uiDispatcherQueue.TryEnqueue(ScreenCaptureService.CaptureRegion);
        }
        // 匿名心跳与托盘/全局热键同一条常驻生命周期：--hide 自启、从不打开主窗口的用户同样被统计
        TelemetryService.Start(_uiDispatcherQueue);
        // 更新检查同理：不依附主窗口，进程内每 5 小时查一次，呈现等主窗口可见或应用通知
        StartupUpdateCheck.Start(_uiDispatcherQueue);
    }

    private AppInstance instance;

    private MainWindow m_MainWindow;

    /// <summary>
    /// 主窗口引用（供设置页等调用 ApplyTheme）
    /// </summary>
    public MainWindow? MainWindow => m_MainWindow;

    private SystemTrayWindow? m_SystemTrayWindow;

    /// <summary>
    /// 托盘窗口（常驻宿主，同时承担后台通知：更新提醒等）
    /// </summary>
    public SystemTrayWindow? SystemTray => m_SystemTrayWindow;

    public void EnsureSystemTray()
    {
        if (m_SystemTrayWindow is null)
        {
            m_SystemTrayWindow = new SystemTrayWindow();
        }
    }

    public void EnsureMainWindow()
    {
        m_MainWindow ??= new MainWindow();
        m_MainWindow.Activate();
        m_MainWindow.Show();
    }

    private void AppInstance_Activated(object? sender, AppActivationArguments e)
    {
        _uiDispatcherQueue.TryEnqueue(() =>
        {
            // ms-screenclip 协议重定向激活：唤起区域截图，不弹主窗口
            if (ScreenClipProtocolService.IsScreenClipActivation(e))
            {
                ScreenCaptureService.CaptureRegion();
                return;
            }
            // --hide 的重定向激活（自启项与已运行实例撞车等）：保持现状，不拉起主窗口
            if (!IsHideActivation(e))
            {
                EnsureMainWindow();
            }
        });
    }

    /// <summary>
    /// 重定向激活参数是否带 --hide：第二实例的意图只能经激活参数传入主实例，本进程命令行不含它。
    /// </summary>
    private static bool IsHideActivation(AppActivationArguments e)
    {
        return e.Kind is ExtendedActivationKind.CommandLineLaunch
            && e.Data is ICommandLineActivatedEventArgs { Operation.Arguments: string args }
            && args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("--hide", StringComparer.OrdinalIgnoreCase);
    }

    public new void Exit()
    {
        if (m_MainWindow is not null)
        {
            m_MainWindow.ForceExit = true;
        }
        m_SystemTrayWindow?.Close();
        m_MainWindow?.Close();
        Application.Current.Exit();
    }
}
