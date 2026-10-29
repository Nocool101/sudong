using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using QuickLauncher.App.Hooks;
using QuickLauncher.App.Interaction;
using QuickLauncher.Steps;
using QuickLauncher.App.Scenes;
using QuickLauncher.App.Services;
using QuickLauncher.Engine;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Storage;

namespace QuickLauncher.App;

/// <summary>
/// 应用入口：装配各组件（钩子、状态机、存储、执行器、UI 服务）。
/// </summary>
public partial class App : Application
{
    private const string MutexName = "QuickLauncher_SingleInstance_Mutex";

    private Database? _db;
    private ActionStore? _store;
    private GlobalHooks? _hooks;
    private InteractionController? _interaction;
    private SceneMatcher? _matcher;
    private PanelService? _panelService;
    private TextCommandWatcher? _textWatcher;
    private HotkeyLeaderWatcher? _hotkeyWatcher;
    private HotkeyManager? _hotkeyManager;
    private Views.FloatingButtonWindow? _floatingButton;
    private AppSettings _settings = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public static ActionStore? Store { get; private set; }
    public static StepExecutor? Executor { get; private set; }
    public static AppSettings Settings => ((App)Current)._settings;
    public static GlobalHooks? Hooks => ((App)Current)._hooks;
    public static string DataDir { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常防护：未处理异常记录日志，避免直接闪退
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var log = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickLauncher", "logs", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
                System.IO.File.AppendAllText(log,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled: {args.Exception}" + Environment.NewLine);
            }
            catch { }
            args.Handled = true; // 阻止应用终止
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var log = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickLauncher", "logs", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
                System.IO.File.AppendAllText(log,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal: {args.ExceptionObject}" + Environment.NewLine);
            }
            catch { }
        };

        // 命令行: --run <actionId> 直接执行动作（供外部调用/测试）
        if (e.Args.Length >= 2 && e.Args[0] == "--run" && Guid.TryParse(e.Args[1], out var runId))
        {
            RunFromCommandLine(runId);
            return;
        }

        // 单实例：先尝试唤醒已有实例（命名管道 IPC）
        if (TryActivateRunningInstance())
        {
            Console.WriteLine("[QL] activated running instance, exiting");
            Shutdown();
            return;
        }

        // 成为主实例（处理上一个实例被强杀后的 abandoned mutex）
        try
        {
            _instanceMutex = new System.Threading.Mutex(true, MutexName, out var isNew);
            if (!isNew)
            {
                // 能等到说明上一个实例已退出（mutex 被放弃），接管它
                if (!_instanceMutex.WaitOne(1000))
                {
                    MessageBox.Show("速动 已在运行（请在托盘图标双击打开主窗口）。",
                        "速动", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // 上一个实例异常退出：接管 mutex
        }
        _pipeServer = StartPipeServer();

        // 数据目录
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickLauncher");
        Directory.CreateDirectory(dataDir);
        DataDir = dataDir;

        // 存储
        _db = new Database(dataDir);
        _store = new ActionStore(_db, dataDir);
        _store.EnsureSeedData();
        _store.SanitizeScenes(); // 清理历史坏场景(纯* / "当前程序"占位符)，保证下区专用磁贴的隔离性
        Store = _store;

        // 设置
        var settingsJson = _store.GetCommon("settings");
        if (settingsJson is not null)
            _settings = Json.Deserialize<AppSettings>(settingsJson) ?? new AppSettings();

        // 开机自启迁移：旧版用注册表 Run 项，现改为“计划任务·登录时·普通权限(LIMITED)”。
        // 权限模型为 asInvoker + UIAccess（见 app.manifest），无需最高权限即可操作管理员窗口 UI。
        try { if (_settings.General.RunAtStartup) Services.StartupService.Apply(true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Startup migrate failed: {ex.Message}"); }

        // 执行器（注册内置步骤 + 脚本步骤）
        var registry = new StepRegistry();
        foreach (var step in Steps.BuiltinSteps.CreateAll())
            registry.Register(step);
        // 脚本引擎步骤（Scripting 层）：C#/JS/Python/脚本文件
        registry.Register(new QuickLauncher.Scripting.ScriptSteps.RunCSharp());
        registry.Register(new QuickLauncher.Scripting.ScriptSteps.RunJavaScript());
        registry.Register(new QuickLauncher.Scripting.ScriptSteps.RunPython());
        registry.Register(new QuickLauncher.Scripting.ScriptSteps.RunScriptFile());
        registry.Register(new QuickLauncher.Scripting.ScriptSteps.RunScript());
        var services = new AppServiceProvider(this);
        Executor = new StepExecutor(registry, _store, services);

        // 场景匹配
        var scenes = _store.LoadScenes();
        _matcher = new SceneMatcher(scenes);

        // 面板服务与交互状态机
        _panelService = new PanelService(_store, _matcher, _settings);

        // 全局钩子
        _hooks = new GlobalHooks();
        _hooks.IsSuspended = false;
        _interaction = new InteractionController(_hooks, _matcher, _store, Executor, _panelService, _settings);
        _panelService.Interaction = _interaction;
        _panelService.Hooks = _hooks;

        // 文本指令监听
        _textWatcher = new TextCommandWatcher(_hooks, _store, Executor, _matcher, _settings,
            action => { _interaction.RunAction(action); return true; });

        // 扩展热键（引导键）监听
        _hotkeyWatcher = new HotkeyLeaderWatcher(_hooks, _store,
            action => { _interaction.RunAction(action); return true; });

        try
        {
            _hooks.Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"全局钩子安装失败: {ex.Message}\n\n请尝试以管理员身份运行。",
                "速动", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // 悬浮按钮
        SetupFloatingButton();

        // 托盘
        SetupTrayIcon();

        // 主窗口（设置/编辑器）
        var mainWindow = new Views.MainWindow();
        MainWindow = mainWindow;
        mainWindow.SourceInitialized += (_, _) =>
        {
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.Attach(mainWindow);
            RefreshActionHotkeys();
        };
        mainWindow.Show();
    }

    private void RunFromCommandLine(Guid actionId)
    {
        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickLauncher");
        _db = new Database(dataDir);
        _store = new ActionStore(_db, dataDir);
        Store = _store;
        var action = _store.LoadAction(actionId);
        if (action is null)
        {
            Console.WriteLine($"[QL] action not found: {actionId}");
            Shutdown(2);
            return;
        }
        var registry = new StepRegistry();
        foreach (var step in Steps.BuiltinSteps.CreateAll())
            registry.Register(step);
        Executor = new StepExecutor(registry, _store, new ConsoleServiceProvider());
        Console.WriteLine($"[QL] running: {action.Name}");
        var result = Executor.Execute(action);
        Console.WriteLine($"[QL] result: success={result.Success} duration={result.Duration.TotalMilliseconds:F0}ms");
        foreach (var line in result.Log) Console.WriteLine($"  {line}");
        if (!result.Success) Console.WriteLine($"  error: {result.Error}");
        Shutdown(result.Success ? 0 : 1);
    }

    #region 单实例 IPC

    private const string PipeName = "QuickLauncher_SingleInstance_Pipe";
    private System.IO.Pipes.NamedPipeServerStream? _pipeServer;
    private System.Threading.Mutex? _instanceMutex;

    /// <summary>
    /// 尝试连接已在运行的实例并发送"显示主窗口"命令。
    /// 返回 true 表示已有实例在运行（本实例应退出）。
    /// </summary>
    private static bool TryActivateRunningInstance()
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName,
                System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.None);
            client.Connect(300);
            if (client.IsConnected)
            {
                var msg = System.Text.Encoding.UTF8.GetBytes("SHOW");
                client.Write(msg, 0, msg.Length);
                client.Flush();
                return true;
            }
        }
        catch
        {
            // 没有运行中的实例或连接失败
        }
        return false;
    }

    /// <summary>启动管道服务，接收其他实例的唤醒命令</summary>
    private System.IO.Pipes.NamedPipeServerStream StartPipeServer()
    {
        var server = new System.IO.Pipes.NamedPipeServerStream(PipeName,
            System.IO.Pipes.PipeDirection.In, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous);
        server.BeginWaitForConnection(OnPipeConnection, server);
        return server;
    }

    private void OnPipeConnection(IAsyncResult ar)
    {
        var server = (System.IO.Pipes.NamedPipeServerStream)ar.AsyncState!;
        try
        {
            server.EndWaitForConnection(ar);
            var buf = new byte[16];
            var read = server.Read(buf, 0, buf.Length);
            var cmd = System.Text.Encoding.UTF8.GetString(buf, 0, read);
            if (cmd.StartsWith("SHOW"))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MainWindow?.Show();
                    if (MainWindow is { } w)
                    {
                        w.WindowState = WindowState.Normal;
                        w.Activate();
                    }
                }));
            }
        }
        catch
        {
            // 管道中断：重建
        }
        finally
        {
            try { server.Disconnect(); } catch { }
            try { server.BeginWaitForConnection(OnPipeConnection, server); } catch { }
        }
    }

    #endregion

    private void ShowMainWindow()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainWindow is { } w)
            {
                w.Show();
                w.WindowState = WindowState.Normal;
                w.Activate();
            }
        }));
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                Environment.ProcessPath ?? "shell32.dll"),
            Text = "速动",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add("挂起/恢复 (Ctrl+Alt+Q)", null, (_, _) => ToggleSuspend());
        menu.Items.Add("-");
        menu.Items.Add("退出", null, (_, _) =>
        {
            if (MainWindow is Views.MainWindow mw) mw.AllowCloseAndExit();
            else Shutdown();
        });
        _trayIcon.ContextMenuStrip = menu;
    }

    private void SetupFloatingButton()
    {
        if (!_settings.General.ShowFloatingButton) return;
        _floatingButton = new Views.FloatingButtonWindow();
        _floatingButton.ShowPanelRequested += () =>
        {
            var (fx, fy) = _floatingButton is null ? (0, 0) : ((int)_floatingButton.Left + 26, (int)_floatingButton.Top + 52);
            _panelService?.ShowPanel(fx, fy);
        };
        _floatingButton.CloseRequested += () => HideFloatingButton();

        // 恢复位置
        var pos = _store?.GetCommon("floating_button_pos");
        if (pos is not null)
        {
            var parts = pos.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y))
            {
                _floatingButton.Left = x;
                _floatingButton.Top = y;
            }
        }
        else
        {
            _floatingButton.Left = SystemParameters.WorkArea.Width - 80;
            _floatingButton.Top = SystemParameters.WorkArea.Height / 2;
        }
        _floatingButton.LocationChanged += (_, _) => SaveFloatingButtonPos();
        _floatingButton.Show();
    }

    private void SaveFloatingButtonPos()
    {
        if (_floatingButton is null) return;
        _store?.SetCommon("floating_button_pos", $"{_floatingButton.Left},{_floatingButton.Top}");
    }

    private void HideFloatingButton()
    {
        if (_floatingButton is null) return;
        SaveFloatingButtonPos();
        _floatingButton.Close();
        _floatingButton = null;
        _settings.General.ShowFloatingButton = false;
        SaveSettings();
    }

    public void ApplyFloatingButtonSetting()
    {
        if (_settings.General.ShowFloatingButton && _floatingButton is null)
        {
            SetupFloatingButton();
        }
        else if (!_settings.General.ShowFloatingButton && _floatingButton is not null)
        {
            HideFloatingButton();
        }
    }

    /// <summary>
    /// 场景数据变更后刷新匹配快照。主窗口保存/删除场景后面板立即识别，
    /// 保证"专用磁贴只在该程序出现"的隔离性（否则新场景不被匹配）。
    /// </summary>
    public void RefreshSceneMatcher()
    {
        if (_store is null) return;
        var fresh = new Scenes.SceneMatcher(_store.LoadScenes());
        _matcher = fresh;
        if (_panelService is not null) _panelService.ReplaceMatcher(fresh);
        if (_interaction is not null) _interaction.ReplaceMatcher(fresh);
        if (_textWatcher is not null) _textWatcher.ReplaceMatcher(fresh);
    }

    private void ToggleSuspend()
    {
        if (_panelService is null || _hooks is null) return;
        _panelService.IsSuspended = !_panelService.IsSuspended;
        _hooks.IsSuspended = _panelService.IsSuspended;
        _trayIcon!.Text = _panelService.IsSuspended ? "速动（已挂起）" : "速动";
    }

    /// <summary>
    /// 是否有模态对话框（ShowDialog）正在显示。模态期间全局钩子监听器应静默，
    /// 防止模态消息循环中的鼠标/键盘事件误触发"面板外点击关闭"、重复呼出面板
    /// 或误触发了文本指令/扩展热键。
    /// 判定依据：WPF 的 ShowDialog 会禁用其 owner 窗口（无 Owner 时自动用主窗口），
    /// 因此"某可见窗口的 owner 被禁用"即表示模态显示中。
    /// 注意：低层钩子回调运行在安装钩子的 UI 线程，读取窗口属性无跨线程问题。
    /// </summary>
    public static bool IsModalDialogOpen
    {
        get
        {
            var app = Application.Current;
            if (app is null) return false;
            foreach (Window w in app.Windows)
            {
                if (!w.IsVisible) continue;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) continue;
                var owner = GetWindow(hwnd, GW_OWNER);
                if (owner != IntPtr.Zero && !IsWindowEnabled(owner))
                    return true;
            }
            return false;
        }
    }

    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    public void SaveSettings()
    {
        _store?.SetCommon("settings", Json.Serialize(_settings));
    }

    /// <summary>一键导出配置：把数据目录（db + states + icons）打包为 zip。</summary>
    public void ExportConfig(string zipPath)
        => Services.ConfigBackupService.Export(DataDir, zipPath, _db);

    /// <summary>独立恢复器脚本：在应用退出后完成 备份→清空→解压→(失败回滚)→重启。全 ASCII，路径经 info.json(UTF8) 传入以避免中文乱码。</summary>
    private const string RestoreScript = """
        param([string]$Json)
        $ErrorActionPreference = "Stop"
        $log = Join-Path $env:TEMP "ql_restore.log"
        function L($m){ Add-Content -LiteralPath $log -Value ((Get-Date -Format 'HH:mm:ss').ToString() + " " + $m) }
        $bak = $null
        try {
          L "start"
          $info = Get-Content -LiteralPath $Json -Raw -Encoding UTF8 | ConvertFrom-Json
          $exe = $info.exe; $data = $info.dataDir; $zip = $info.zip; $pid = [int]$info.pid
          L ("waiting pid " + $pid)
          $w = 0
          while ((Get-Process -Id $pid -ErrorAction SilentlyContinue) -and ($w -lt 20)) { Start-Sleep -Milliseconds 500; $w += 0.5 }
          try { Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue } catch {}
          Start-Sleep -Milliseconds 500
          $bak = $data + ".bak_" + (Get-Date -Format 'yyyyMMdd_HHmmss')
          L ("backup -> " + $bak)
          Copy-Item -LiteralPath $data -Destination $bak -Recurse -Force
          L "clearing"
          Get-ChildItem -LiteralPath $data -Force | Remove-Item -Recurse -Force
          L "extract"
          Expand-Archive -LiteralPath $zip -DestinationPath $data -Force
          L "done"
        } catch {
          L ("error: " + $_.Exception.Message)
          try {
            if ($bak -and (Test-Path -LiteralPath $bak)) {
              Get-ChildItem -LiteralPath $data -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
              Copy-Item -Path ($bak + "\*") -Destination $data -Recurse -Force -ErrorAction SilentlyContinue
              L "rolled back"
            }
          } catch { L ("rollback failed: " + $_.Exception.Message) }
        } finally {
          try { if ($exe -and (Test-Path -LiteralPath $exe)) { Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe); L "relaunched" } } catch { L ("relaunch failed: " + $_.Exception.Message) }
          try { Remove-Item -LiteralPath (Split-Path $Json) -Recurse -Force -ErrorAction SilentlyContinue } catch {}
          L "end"
        }
        """;

    /// <summary>一键导入配置：准备临时恢复脚本，交给独立进程在应用退出后完成"备份→覆盖→重启"，避免运行中操作 SQLite 导致卡顿/闪退。</summary>
    public void ImportConfig(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("备份文件不存在", zipPath);

        // 备份 zip 复制到临时 ASCII 路径（避免把可能含中文的原始路径传给命令行）
        var tmpDir = Path.Combine(Path.GetTempPath(), "ql_restore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var zipTemp = Path.Combine(tmpDir, "config.zip");
        File.Copy(zipPath, zipTemp, overwrite: true);

        var exe = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

        var jsonPath = Path.Combine(tmpDir, "info.json");
        var ps1Path = Path.Combine(tmpDir, "restore.ps1");
        File.WriteAllText(jsonPath,
            $"{{\"exe\":\"{Esc(exe)}\",\"dataDir\":\"{Esc(DataDir)}\",\"zip\":\"{Esc(zipTemp)}\",\"pid\":{Environment.ProcessId}}}");
        File.WriteAllText(ps1Path, RestoreScript, new System.Text.UTF8Encoding(false));

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1Path}\" -Json \"{jsonPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        });

        // 恢复器已独立启动；干净退出主应用（OnExit 正常释放钩子/存储/托盘），
        // 由恢复器在应用退出后独占完成文件替换并重启，避免运行中操作数据库与单实例竞争。
        Shutdown();
    }

    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>指令数据变更后刷新监听器</summary>
    public void RefreshTextCommands() => _textWatcher?.ReloadCommands();

    /// <summary>扩展热键数据变更后刷新监听器</summary>
    public void RefreshHotkeyCommands() => _hotkeyWatcher?.ReloadCommands();

    /// <summary>重新注册所有动作的全局快捷键</summary>
    public void RefreshActionHotkeys()
    {
        if (_hotkeyManager is null || _store is null || _interaction is null) return;
        _hotkeyManager.UnregisterAll();
        foreach (var action in _store.LoadActions())
        {
            if (string.IsNullOrWhiteSpace(action.Hotkey) || !action.Enabled) continue;
            var target = action;
            if (!_hotkeyManager.Register(action.Hotkey, () => _interaction.RunAction(target)))
            {
                // 冲突：忽略（可扩展为提示）
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _textWatcher?.Dispose();
        _hotkeyWatcher?.Dispose();
        _hotkeyManager?.Dispose();
        _interaction?.Dispose();
        _hooks?.Dispose();
        _db?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private sealed class AppServiceProvider : IServiceProvider
    {
        private readonly App _app;
        public AppServiceProvider(App app) => _app = app;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(INotificationService))
                return new NotificationService();
            if (serviceType == typeof(IDialogService))
                return new DialogService();
            if (serviceType == typeof(ActionStore))
                return Store;
            if (serviceType == typeof(IPanelService))
                return _app._panelService;
            return null;
        }
    }
}

/// <summary>
/// 命令行模式的服务提供者（无 UI，通知走控制台）
/// </summary>
public sealed class ConsoleServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(INotificationService))
            return new ConsoleNotificationService();
        if (serviceType == typeof(IDialogService))
            return new ConsoleDialogService();
        return null;
    }
}

public sealed class ConsoleNotificationService : INotificationService
{
    public void Show(string message, string level = "info")
        => Console.WriteLine($"  [{level}] {message}");
}

public sealed class ConsoleDialogService : IDialogService
{
    public string? Prompt(string prompt, string defaultValue = "", bool multiline = false)
    {
        Console.Write($"  {prompt} [{defaultValue}]: ");
        var line = Console.ReadLine();
        return string.IsNullOrEmpty(line) ? defaultValue : line;
    }

    public (string? value, int index) Select(string prompt, List<string> options)
    {
        Console.WriteLine($"  {prompt}");
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"    {i + 1}. {options[i]}");
        Console.Write("  选择: ");
        var line = Console.ReadLine();
        if (int.TryParse(line, out var n) && n >= 1 && n <= options.Count)
            return (options[n - 1], n - 1);
        return (null, -1);
    }
}

/// <summary>
/// 通知服务实现（WPF 消息框简化版）
/// </summary>
public sealed class NotificationService : INotificationService
{
    public void Show(string message, string level = "info")
    {
        var icon = level switch
        {
            "error" => MessageBoxImage.Error,
            "warning" => MessageBoxImage.Warning,
            _ => MessageBoxImage.Information
        };
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "速动", MessageBoxButton.OK, icon));
    }
}

/// <summary>
/// 对话框服务实现
/// </summary>
public sealed class DialogService : IDialogService
{
    public string? Prompt(string prompt, string defaultValue = "", bool multiline = false)
    {
        var window = new Views.InputDialog(prompt, defaultValue, multiline)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return window.ShowDialog() == true ? window.ResultText : null;
    }

    public (string? value, int index) Select(string prompt, List<string> options)
    {
        var window = new Views.SelectDialog(prompt, options)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return window.ShowDialog() == true ? (window.SelectedValue, window.SelectedIndex) : (null, -1);
    }
}
