using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using QuickLauncher.App.Hooks;
using QuickLauncher.App.Interaction;
using QuickLauncher.App.Scenes;
using QuickLauncher.App.Views;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Storage;

namespace QuickLauncher.App.Services;

/// <summary>
/// 面板服务实现：管理面板窗口的显示、场景切换、动作执行。
/// </summary>
public sealed class PanelService : IPanelService
{
    private readonly ActionStore _store;
    private SceneMatcher _matcher;
    private readonly AppSettings _settings;

    private PanelWindow? _panelWindow;
    private bool _suspended;

    public PanelService(
        ActionStore store,
        SceneMatcher matcher,
        AppSettings settings)
    {
        _store = store;
        _matcher = matcher;
        _settings = settings;
    }

    /// <summary>交互控制器（启动后注入，避免循环依赖）</summary>
    public InteractionController? Interaction { get; set; }

    /// <summary>全局钩子（启动后注入）。模态对话框显示期间临时挂起，防止钩子误触发。</summary>
    public Hooks.GlobalHooks? Hooks { get; set; }

    public event Action? PanelShown;

    public bool IsSuspended
    {
        get => _suspended;
        set
        {
            _suspended = value;
            if (value)
            {
                HidePanel();
            }
        }
    }

    #region 面板

    public bool IsPanelOpen => _panelWindow is { IsVisible: true };

    /// <summary>
    /// 是否正在进行磁贴拖放等交互。拖放期间（OLE 模态循环）全局钩子事件仍在派发，
    /// 此标记让交互控制器暂停"面板外点击关闭"与触发键响应，防止拖放途中面板被误关。
    /// </summary>
    public bool IsInteracting { get; set; }

    public void ShowPanel(int mouseX, int mouseY)
    {
        if (_suspended) return;
        ReleasePinIfProcessChanged(); // 同程序保持锁定；换程序恢复场景匹配

        // 诊断：记录弹面板时的真实前台窗口，用于定位“下区串到别的程序”类问题
        var fgInfo = SceneMatcher.GetStableForegroundInfo();
        Dbg.Log($"ShowPanel: fg proc={fgInfo.process} class={fgInfo.className} title={fgInfo.title}");

        // 上区：全局场景页面（任何程序都显示）
        var globalMatch = _matcher.MatchGlobal(s => s.PanelPageId);
        var globalPage = globalMatch.Scene?.PanelPageId is { } gpid ? _store.LoadPage(gpid) : null;

        // 下区：当前前台窗口的上下文场景页面（仅对应应用显示，匹配不到则隐藏下区）
        var contextMatch = _matcher.MatchContext(SceneMatcher.ResolveContextProcess(fgInfo.process), fgInfo.title, fgInfo.className, s => s.PanelPageId);
        var contextPage = contextMatch.Scene?.PanelPageId is { } cpid ? _store.LoadPage(cpid) : null;

        if (globalPage is null && contextPage is null) return;
        // 全局页缺失时用上下文页顶上区（保证有内容可显示）
        if (globalPage is null && contextMatch.Scene is { } cs)
        {
            globalMatch = new QuickLauncher.App.Scenes.SceneMatch(cs, false);
            globalPage = contextPage;
            contextPage = null;
        }

        var actions = _store.LoadActions();
        _globalScene = globalMatch.Scene ?? new Models.Scene { Name = "全局" };
        _globalPage = globalPage;
        _contextScene = contextMatch.Scene;
        _contextPage = contextPage;

        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _panelWindow ??= CreatePanelWindow();
                _panelWindow.LoadAndShow(_globalScene, globalPage!, _contextScene, _contextPage,
                    actions, mouseX, mouseY);
                PanelShown?.Invoke();
            });
        }
        catch (Exception ex)
        {
            Dbg.Log($"PanelService EX: {ex}");
        }
    }

    private Scene _globalScene = new();
    private ActionPage? _globalPage;
    private Scene? _contextScene;
    private ActionPage? _contextPage;

    /// <summary>取指定区的页面（isContext=true 下区，false 上区全局）</summary>
    private ActionPage? ZonePage(bool isContext) => isContext ? _contextPage : _globalPage;

    /// <summary>保存场景并热更新匹配快照（否则新建场景不被识别，专用磁贴会漏到其他程序）</summary>
    private void SaveSceneAndRefresh(Models.Scene scene)
    {
        _store.SaveScene(scene);
        _matcher = new SceneMatcher(_store.LoadScenes());
    }

    /// <summary>替换匹配器（App 层场景数据变更后调用）</summary>
    public void ReplaceMatcher(SceneMatcher matcher) => _matcher = matcher;

    /// <summary>
    /// 确保下区有专用动作页：没有则为当前前台进程静默创建场景+页。
    /// （点击下区"＋"即创建，用户无需打开 App、无感知页面概念）
    /// </summary>
    private bool EnsureContextPage()
    {
        if (_contextPage is not null) return true;
        if (Application.Current is null) return false;

        var fg = SceneMatcher.GetForegroundInfo();
        // 前台进程无法识别时，不能把占位符写进 ProcessMatch——否则会造出一个
        // ProcessMatch="当前程序" 的“死场景”，永远匹配不到任何真实进程。
        if (string.IsNullOrEmpty(fg.process))
        {
            Dbg.Log("EnsureContextPage: foreground process unknown, skip creating scene");
            return false;
        }
        var proc = fg.process;

        // 已有该进程的场景则复用（补建页面）
        var existing = _store.LoadScenes().FirstOrDefault(s2 =>
            s2.Type == Models.SceneType.Application
            && string.Equals(s2.ProcessMatch, proc, StringComparison.OrdinalIgnoreCase));
        if (existing?.PanelPageId is { } epid && _store.LoadPage(epid) is { } epage)
        {
            _contextScene = existing;
            _contextPage = epage;
            return true;
        }

        var page = new Models.ActionPage { Name = $"{proc} 页", Columns = 4 };
        _store.SavePage(page);
        if (existing is not null)
        {
            existing.PanelPageId = page.Id;
            existing.Enabled = true;
            SaveSceneAndRefresh(existing);
            _contextScene = existing;
        }
        else
        {
            var scene = new Models.Scene
            {
                Name = proc,
                Type = Models.SceneType.Application,
                ProcessMatch = proc,
                PanelPageId = page.Id,
                Enabled = true
            };
            SaveSceneAndRefresh(scene);
            _contextScene = scene;
        }
        _contextPage = page;
        Dbg.Log($"EnsureContextPage: created page for {proc}");
        return true;
    }

    private PanelWindow CreatePanelWindow()
    {
        var w = new PanelWindow();
        w.ActionInvoked -= OnPanelActionInvoked;
        w.ActionInvoked += OnPanelActionInvoked;
        w.ActionEditRequested -= OnPanelActionEdit;
        w.ActionEditRequested += OnPanelActionEdit;
        w.ActionDeleteRequested -= OnPanelActionDelete;
        w.ActionDeleteRequested += OnPanelActionDelete;
        w.CellAddRequested -= OnPanelCellAdd;
        w.CellAddRequested += OnPanelCellAdd;
        w.DragStateChanged -= OnPanelDragStateChanged;
        w.DragStateChanged += OnPanelDragStateChanged;
        w.GlobalPageEditRequested -= OnGlobalPageEdit;
        w.GlobalPageEditRequested += OnGlobalPageEdit;
        w.ContextPageEditRequested -= OnContextPageEdit;
        w.ContextPageEditRequested += OnContextPageEdit;
        w.RefreshRequested -= OnPanelRefreshRequested;
        w.RefreshRequested += OnPanelRefreshRequested;
        w.ActionMoveRequested -= OnPanelActionMove;
        w.ActionMoveRequested += OnPanelActionMove;
        return w;
    }

    /// <summary>
    /// 显示模态对话框。期间挂起全局钩子，防止模态消息循环中的鼠标/键盘事件
    /// 被钩子捕获，误触发"面板外点击关闭面板"、重复呼出面板，或误触发了
    /// 文本指令/扩展热键。对话框关闭后恢复此前的挂起状态。
    /// （另有一道 App.IsModalDialogOpen 全局守卫兜底）
    /// </summary>
    private bool ShowModalDialog(Window dialog)
    {
        using (SuspendHooksForModal())
        {
            return dialog.ShowDialog() == true;
        }
    }

    /// <summary>
    /// 模态交互期间挂起全局钩子（using 模式）。
    /// 用于 Win32 模态对话框（OpenFileDialog 等不产生 WPF Modal 窗口的场景）。
    /// </summary>
    private IDisposable? SuspendHooksForModal()
    {
        var hooks = Hooks;
        if (hooks is null) return null;
        var previous = hooks.IsSuspended;
        hooks.IsSuspended = true;
        return new ModalHookSuspender(hooks, previous);
    }

    private sealed class ModalHookSuspender : IDisposable
    {
        private readonly Hooks.GlobalHooks _hooks;
        private readonly bool _previous;
        private bool _disposed;

        public ModalHookSuspender(Hooks.GlobalHooks hooks, bool previous)
        {
            _hooks = hooks;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 仅恢复由本次造成的挂起；模态期间用户若手动挂起（托盘）则保持挂起
            if (_hooks.IsSuspended) _hooks.IsSuspended = _previous;
        }
    }

    private void OnPanelActionEdit(ActionItem action)
    {
        // 先关闭面板：无焦点面板窗口（WS_EX_NOACTIVATE）会停留在原地遮挡模态编辑器
        HidePanel();
        var editor = new Views.ActionEditorWindow(action)
        {
            Owner = Application.Current.MainWindow is { IsVisible: true } mw ? mw : null,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        if (ShowModalDialog(editor))
            RefreshPanelContent();
    }

    private void OnPanelActionDelete(ActionItem action)
    {
        HidePanel(); // 关闭面板，避免遮挡确认框
        var confirm = MessageBox.Show($"确定删除动作「{action.Name}」？\n\n（将从所有动作页移除）",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _store.DeleteAction(action.Id);
        // 从所有页面移除该动作的格子
        foreach (var page in _store.LoadPages())
        {
            var changed = page.Cells.RemoveAll(c => c.ActionId == action.Id) > 0
                || page.OuterCells.RemoveAll(c => c.ActionId == action.Id) > 0;
            if (changed) _store.SavePage(page);
        }
        RefreshPanelContent();
    }

    /// <summary>磁贴拖放状态同步到 IsInteracting（拖放期间交互控制器暂停自动关闭/触发）</summary>
    private void OnPanelDragStateChanged(bool dragging) => IsInteracting = dragging;

    private void OnPanelRefreshRequested() => RefreshPanelContent();

    /// <summary>设置菜单：编辑全局动作页（弹窗内完成，无需打开 App）</summary>
    private void OnGlobalPageEdit()
    {
        if (_globalPage is null) return;
        HidePanel(); // 避免无焦点面板遮挡模态编辑器
        var editor = new Views.PageEditorWindow(_globalPage) { Owner = Application.Current.MainWindow };
        if (ShowModalDialog(editor) == true)
            ReopenPanel();
    }

    /// <summary>设置菜单：编辑当前程序专用动作页</summary>
    private void OnContextPageEdit()
    {
        if (_contextPage is null) return;
        HidePanel();
        var editor = new Views.PageEditorWindow(_contextPage) { Owner = Application.Current.MainWindow };
        if (ShowModalDialog(editor) == true)
            ReopenPanel();
    }

    /// <summary>在当前鼠标位置重新打开面板（弹窗内操作后刷新可见状态）</summary>
    private void ReopenPanel()
    {
        var (x, y) = GetCursorPosition();
        ShowPanel(x, y);
    }

    private void OnPanelCellAdd(int cellIndex, bool isContext)
    {
        Dbg.Log($"OnPanelCellAdd: index={cellIndex} context={isContext}");
        if (isContext && _contextPage is null && !EnsureContextPage()) return;
        if (ZonePage(isContext) is null) return;

        // 先关闭面板（原版行为：点"＋"后面板关闭）；无焦点面板会遮挡后续模态窗口
        HidePanel();

        // 菜单选择（对标 Quicker 的"添加动作"菜单）
        // 注意：不能用无焦点的面板窗口作 Owner（会导致模态框异常）
        try
        {
            var dialog = new Views.AddActionDialog
            {
                // 显式 Owner：WPF 对 ShowDialog 必定禁用 owner，模态检测（App.IsModalDialogOpen）才可靠
                Owner = Application.Current.MainWindow
            };
            Dbg.Log("OnPanelCellAdd: showing dialog");
            var result = ShowModalDialog(dialog);
            Dbg.Log($"OnPanelCellAdd: dialog result={result} choice={dialog.Choice}");
            if (result != true) return;
            switch (dialog.Choice)
            {
                case AddActionChoice.NewBasic:
                    CreateBasicActionInCell(cellIndex, "OpenUrl", "https://", "打开网址", isContext: isContext);
                    break;
                case AddActionChoice.NewComposite:
                    CreateCompositeActionInCell(cellIndex, "组合动作", presetStep: null, isContext: isContext);
                    break;
                case AddActionChoice.NewRunProgram:
                    CreateBasicActionInCell(cellIndex, "RunProgram", "", "启动软件", isContext: isContext);
                    break;
                case AddActionChoice.NewOpenFile:
                    CreateBasicActionInCell(cellIndex, "OpenFile", "", "打开文件", isContext: isContext);
                    break;
                case AddActionChoice.NewOpenFolder:
                    CreateBasicActionInCell(cellIndex, "OpenFolder", "", "打开文件夹", isContext: isContext);
                    break;
                case AddActionChoice.NewRunCommand:
                    CreateBasicActionInCell(cellIndex, "RunCommand", "", "运行命令", isContext: isContext);
                    break;
                case AddActionChoice.NewOpenUrl:
                    CreateBasicActionInCell(cellIndex, "OpenUrl", "https://", "打开网址", isContext: isContext);
                    break;
                case AddActionChoice.NewSendKeys:
                    CreateBasicActionInCell(cellIndex, "SendKeys", "", "模拟按键", isContext: isContext);
                    break;
                case AddActionChoice.NewSendText:
                    CreateBasicActionInCell(cellIndex, "SendText", "", "发送文本", isContext: isContext);
                    break;
                case AddActionChoice.NewScript:
                    // 执行脚本：简单动作（RunScript 统一入口），默认 C#，代码/语言在编辑器表单中编辑
                    CreateBasicActionInCell(cellIndex, "RunScript", "", "执行脚本",
                        new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["language"] = "csharp",
                            ["code"] = "ctx.Log(\"hello\"); return 1 + 1;"
                        }, isContext: isContext);
                    break;
                case AddActionChoice.NewSwitchPage:
                    // 加载动作页：简单动作（SwitchPage），目标页在编辑器表单中下拉选择
                    CreateBasicActionInCell(cellIndex, "SwitchPage", "", "加载动作页", isContext: isContext);
                    break;
                case AddActionChoice.ImportAction:
                    ImportActionInCell(cellIndex, isContext);
                    break;
                case AddActionChoice.PickExisting:
                    PickExistingActionInCell(cellIndex, isContext);
                    break;
            }
        }
        catch (Exception ex)
        {
            Dbg.Log($"OnPanelCellAdd EX: {ex}");
            MessageBox.Show($"添加动作失败: {ex.Message}", "速动",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>在格子中新建指定类型的基础动作（打开编辑器，保存后自动放入格子）</summary>
    private void CreateBasicActionInCell(int cellIndex, string simpleType, string simpleParam, string defaultName,
        System.Collections.Generic.Dictionary<string, string>? simpleInputs = null, bool isContext = false)
    {
        if (ZonePage(isContext) is null) return;
        var action = new Models.ActionItem
        {
            Name = defaultName,
            Kind = Models.ActionKind.Simple,
            SimpleType = simpleType,
            SimpleParam = simpleParam
        };
        if (simpleInputs is not null)
            foreach (var kv in simpleInputs)
                action.SimpleInputs[kv.Key] = kv.Value;
        _store.SaveAction(action);
        var editor = new Views.ActionEditorWindow(action) { Owner = Application.Current.MainWindow };
        if (ShowModalDialog(editor))
        {
            BindActionToCell(action.Id, cellIndex, isContext);
        }
        else
        {
            // 取消编辑：移除刚创建的空动作，避免垃圾数据
            _store.DeleteAction(action.Id);
        }
    }

    /// <summary>在格子中新建组合动作（可带预置步骤）</summary>
    private void CreateCompositeActionInCell(int cellIndex, string defaultName, Models.ActionStep? presetStep, bool isContext = false)
    {
        if (ZonePage(isContext) is null) return;
        var action = new Models.ActionItem
        {
            Name = defaultName,
            Kind = Models.ActionKind.Composite
        };
        if (presetStep is not null) action.Steps.Add(presetStep);
        _store.SaveAction(action);
        var editor = new Views.ActionEditorWindow(action) { Owner = Application.Current.MainWindow };
        if (ShowModalDialog(editor))
        {
            BindActionToCell(action.Id, cellIndex, isContext);
        }
        else
        {
            _store.DeleteAction(action.Id);
        }
    }

    /// <summary>从 JSON 文件导入动作到格子（重新分配 Id 避免冲突）</summary>
    private void ImportActionInCell(int cellIndex, bool isContext)
    {
        if (ZonePage(isContext) is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入动作",
            Filter = "动作 JSON|*.json|所有文件|*.*"
        };
        using (SuspendHooksForModal())
        {
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var imported = Models.Json.Deserialize<Models.ActionItem>(json);
                if (imported is null || string.IsNullOrEmpty(imported.Name))
                {
                    MessageBox.Show("无法解析该文件（不是有效的动作 JSON）。", "导入失败",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // 重新分配 Id，避免与现有动作冲突
                imported.Id = Guid.NewGuid();
                _store.SaveAction(imported);
                BindActionToCell(imported.Id, cellIndex, isContext);
            }
            catch (Exception ex)
            {
                Dbg.Log($"ImportAction EX: {ex}");
                MessageBox.Show($"导入失败: {ex.Message}", "导入失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>从已有动作选择并放入格子</summary>
    private void PickExistingActionInCell(int cellIndex, bool isContext)
    {
        if (ZonePage(isContext) is null) return;
        var picker = new Views.ActionPickerDialog { Owner = Application.Current.MainWindow };
        if (ShowModalDialog(picker) && picker.SelectedAction is { } action)
            BindActionToCell(action.Id, cellIndex, isContext);
    }

    private void BindActionToCell(Guid actionId, int cellIndex, bool isContext)
    {
        var page = ZonePage(isContext);
        if (page is null) return;
        var cell = page.Cells.FirstOrDefault(c => c.Index == cellIndex);
        if (cell is null)
        {
            cell = new Models.ActionCell { Index = cellIndex };
            page.Cells.Add(cell);
        }
        cell.ActionId = actionId;
        _store.SavePage(page);
        RefreshPanelContent();
    }

    private void OnPanelActionMove(Guid actionId, int toIndex, bool isContext, bool srcIsContext)
    {
        Dbg.Log($"OnPanelActionMove: action={actionId} to={toIndex} dst={isContext} src={srcIsContext}");
        if (isContext && _contextPage is null && !EnsureContextPage()) return;
        var page = ZonePage(isContext);
        var srcPage = ZonePage(srcIsContext);
        if (page is null) return;

        // 跨区拖放（上区 ↔ 下区）
        if (!ReferenceEquals(page, srcPage))
        {
            OnCrossZoneMove(actionId, toIndex, page, srcPage);
            return;
        }

        var from = page.Cells.FirstOrDefault(c => c.ActionId == actionId);
        if (from is null) return;

        // 窗口级兜底哨兵（-1）= 放到最后一个占用位之后；其余目标索引（来自格子）直接信任，
        // 允许放到当前范围外的空格（稀疏布局：前排留空、直接放下一排）
        if (toIndex < 0)
        {
            var maxIndex = page.Cells
                .Where(c => c.ActionId != Guid.Empty)
                .Select(c => c.Index)
                .DefaultIfEmpty(-1)
                .Max();
            toIndex = maxIndex + 1;
        }

        if (from.Index == toIndex)
        {
            RefreshPanelContent();
            return;
        }

        var to = page.Cells.FirstOrDefault(c => c.Index == toIndex);
        if (to is null || to.ActionId == Guid.Empty)
        {
            // 自由放置到空格：直接占用该格（允许稀疏布局——前排可留空，其余磁贴不动）
            from.Index = toIndex;
        }
        else if (to.ActionId != actionId)
        {
            // 拖到已有磁贴上：交换两个位置（不破坏其他磁贴的布局）
            var tmp = to.ActionId;
            to.ActionId = from.ActionId;
            from.ActionId = tmp;
        }

        _store.SavePage(page);
        Dbg.Log("OnPanelActionMove: saved");
        RefreshPanelContent();
    }

    /// <summary>磁贴增删/移动后刷新面板内容（保持打开状态）</summary>
    /// <summary>跨区移动：拖到空格=移动到目标区；拖到磁贴=两磁贴互换区</summary>
    private void OnCrossZoneMove(Guid actionId, int toIndex, ActionPage dstPage, ActionPage? srcPage)
    {
        if (srcPage is null) return;
        var srcCell = srcPage.Cells.FirstOrDefault(c => c.ActionId == actionId);
        if (srcCell is null) return;

        var to = dstPage.Cells.FirstOrDefault(c => c.Index == toIndex);
        if (to is null || to.ActionId == Guid.Empty)
        {
            // 拖到目标区空格：整体移动到目标区
            srcPage.Cells.Remove(srcCell);
            srcCell.Index = toIndex;
            dstPage.Cells.Add(srcCell);
        }
        else if (to.ActionId != actionId)
        {
            // 拖到目标区磁贴：两个磁贴互换区（交换各自 cell 的 ActionId）
            var tmp = to.ActionId;
            to.ActionId = srcCell.ActionId;
            srcCell.ActionId = tmp;
        }

        _store.SavePage(srcPage);
        _store.SavePage(dstPage);
        Dbg.Log("OnCrossZoneMove: saved");
        RefreshPanelContent();
    }

    private void RefreshPanelContent()
    {
        if (Application.Current is null) return; // 无 UI 环境（测试/命令行模式）
        // 重新解析两区（页面数据可能已变），保持面板打开状态
        var globalMatch = _matcher.MatchGlobal(s => s.PanelPageId);
        var globalPage = globalMatch.Scene?.PanelPageId is { } gpid ? _store.LoadPage(gpid) : _globalPage;

        ReleasePinIfProcessChanged();

        Scene? ctxScene;
        ActionPage? ctxPage;
        if (_pinnedContextPageId is { } pinned && _store.LoadPage(pinned) is { } pinnedPage)
        {
            // 锁定态（加载动作页）：下区显示指定页
            ctxScene = new Models.Scene { Name = pinnedPage.Name };
            ctxPage = pinnedPage;
        }
        else
        {
            var contextMatch = _matcher.MatchContextForeground(s => s.PanelPageId);
            ctxScene = contextMatch.Scene;
            ctxPage = contextMatch.Scene?.PanelPageId is { } cpid ? _store.LoadPage(cpid) : null;
        }
        if (globalPage is null && ctxPage is null) return;

        if (globalPage is not null)
        {
            _globalScene = globalMatch.Scene ?? _globalScene;
            _globalPage = globalPage;
        }
        _contextScene = ctxScene;
        _contextPage = ctxPage;

        var actions = _store.LoadActions();
        var (x, y) = GetCursorPosition();
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_panelWindow is { IsVisible: true })
            {
                _panelWindow.LoadAndShow(_globalScene, _globalPage!, _contextScene, _contextPage,
                    actions, x, y);
                PanelShown?.Invoke();
            }
        });
    }

    private void OnPanelActionInvoked(ActionItem action)
    {
        // 加载动作页：保持面板打开（执行后原地切换到目标动作页）
        if (action.SimpleType == "SwitchPage")
        {
            Interaction?.RunAction(action);
            return;
        }

        // 其他动作：点击后面板立即关闭（原版行为），避免无焦点面板遮挡后续界面
        HidePanel();
        Interaction?.RunAction(action);
    }

    public void HidePanel()
    {
        Dbg.Log("HidePanel");
        Application.Current.Dispatcher.Invoke(() => _panelWindow?.Hide());
    }

    /// <summary>锁定的下区页面 Id（加载动作页动作触发；磁贴刷新时保持）</summary>
    private Guid? _pinnedContextPageId;

    /// <summary>锁定时的前台进程（换程序后自动解除锁定，保证专用页隔离性）</summary>
    private string? _pinnedProcess;

    /// <summary>进程变化则解除页面锁定（别的程序打开时恢复其自身场景匹配）</summary>
    private void ReleasePinIfProcessChanged()
    {
        if (_pinnedContextPageId is null) return;
        var proc = SceneMatcher.GetForegroundInfo().process;
        if (!string.Equals(proc, _pinnedProcess, StringComparison.OrdinalIgnoreCase))
        {
            _pinnedContextPageId = null;
            _pinnedProcess = null;
        }
    }

    /// <summary>
    /// 切换面板下区（上下文区）到指定动作页（SwitchPage 步骤/动作调用）。
    /// 锁定状态下磁贴增删后的刷新保持该页；重新打开面板时恢复场景匹配。
    /// </summary>
    public void SwitchToPage(Guid pageId)
    {
        if (Application.Current is null) return; // 无 UI 环境（测试/命令行模式）
        Application.Current.Dispatcher.Invoke(() =>
        {
            var page = _store.LoadPage(pageId);
            if (page is null) return;
            _pinnedContextPageId = pageId;
            _pinnedProcess = SceneMatcher.GetForegroundInfo().process;
            if (IsPanelOpen) RefreshPanelContent();
        });
    }

    /// <summary>获取面板窗口矩形（屏幕坐标），未打开返回 null</summary>
    public (int x, int y, int w, int h)? GetPanelRect()
    {
        if (!IsPanelOpen || _panelWindow is null) return null;
        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (_panelWindow is null || !_panelWindow.IsVisible) return ((int x, int y, int w, int h)?)null;
            // 注意：PointToScreen 输出物理像素，Width/ActualHeight 是 DIP——必须用两个角点换算，
            // 直接相加在高 DPI 下矩形偏小，会把面板边缘误判为"面板外"导致误关闭
            var tl = _panelWindow.PointToScreen(new System.Windows.Point(0, 0));
            var br = _panelWindow.PointToScreen(
                new System.Windows.Point(_panelWindow.Width, _panelWindow.ActualHeight));
            // 向外扩 10px 容差：布局刚更新时 ActualHeight 可能略小，避免误判"面板外点击"
            const int pad = 10;
            return ((int)tl.X - pad, (int)tl.Y - pad, (int)(br.X - tl.X) + pad * 2, (int)(br.Y - tl.Y) + pad * 2);
        });
    }

    #endregion


    private static (int x, int y) GetCursorPosition()
    {
        return WpfCursor.Position;
    }
}
