using System;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;

namespace QuickLauncher.Tests;

/// <summary>
/// WPF 测试宿主：在唯一的 STA 线程上创建并持有一个 WPF <see cref="System.Windows.Application"/> 实例。
///
/// 【为什么需要】
/// 测试里直接 new Window() 时 <c>Application.Current</c> 为 null，App.xaml 中定义的资源字典
/// （Brush.BgBase、C.BgBase 等）从未加载，StaticResourceExtension 找不到资源会抛
/// XamlParseException：无法找到名为"Brush.BgBase"的资源。
///
/// 【WPF 的两条硬约束】
/// 1) 一个 AppDomain 内只能存在一个 Application 实例；
/// 2) UI 对象必须在持有该 Application 的线程上创建（线程亲和性）。
/// 因此这里用唯一的后台 STA 线程承载 Application 与全部窗口操作，
/// 测试通过 <see cref="Run"/> 把工作投递过去并同步等待结果。
/// </summary>
public static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> _dispatcher =
        new(CreateHost, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>宿主线程的 Dispatcher。首次访问时会创建宿主线程。</summary>
    public static Dispatcher Dispatcher => _dispatcher.Value;

    /// <summary>在宿主 STA 线程上同步执行一段代码；异常原样抛回调用方。</summary>
    public static void Run(Action action)
    {
        var d = Dispatcher;
        if (d.CheckAccess())
        {
            action();
            return;
        }
        try
        {
            d.Invoke(action, DispatcherPriority.Normal);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    private static Dispatcher CreateHost()
    {
        var ready = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;

                // 必须用真实的 App 类（而不是裸 Application）：App 的 InitializeComponent()
                // 才会加载 App.xaml 的 Application.Resources —— 这正是 Brush.* 等静态资源的来源。
                // 注意 InitializeComponent 由生成的 App.g.cs 以 public 方法形式提供，
                // 不在构造函数里（Main() 里显式调用），故这里必须手动调一次。
                var app = new global::QuickLauncher.App.App();
                app.InitializeComponent();

                ready.Set();

                // 维持消息循环，供 Dispatcher.Invoke 投递
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                error = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "QuickLauncher-WpfTestHost"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("WPF 测试宿主启动超时（30s）");
        if (error is not null)
            throw new InvalidOperationException("WPF 测试宿主启动失败", error);

        return dispatcher!;
    }
}
