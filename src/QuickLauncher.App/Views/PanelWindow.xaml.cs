using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using QuickLauncher.App.Interaction;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class PanelWindow : Window
{
    private ActionPage? _globalPage;
    private ActionPage? _contextPage;
    private Scene? _globalScene;
    private Scene? _contextScene;
    private List<ActionItem> _actions = new();
    /// <summary>搜索关键词（空 = 不过滤）</summary>
    private string _filter = "";
    private int _globalPageIndex;
    private int _contextPageIndex;

    /// <summary>全局区每页格数：横 4 x 竖 3</summary>
    private const int GlobalPageSize = 12;

    /// <summary>专属区每页格数：横 4 x 竖 4</summary>
    private const int ContextPageSize = 16;

    /// <summary>翻页方向（+1 前进 / -1 后退），用于横向翻页滑入动画</summary>
    private int _lastPageTurn = 1;

    /// <summary>面板活动区（滚轮/翻页按钮作用对象，鼠标悬停切换）</summary>
    private enum Zone { Global, Context }

    private Zone _activeZone = Zone.Context;

    /// <summary>动作被点击时触发</summary>
    public event Action<ActionItem>? ActionInvoked;

    /// <summary>请求编辑动作</summary>
    public event Action<ActionItem>? ActionEditRequested;

    /// <summary>请求删除动作</summary>
    public event Action<ActionItem>? ActionDeleteRequested;

    /// <summary>请求在空格子添加动作（参数：格子索引）</summary>
    public event Action<int, bool>? CellAddRequested;

    /// <summary>磁贴拖放开始/结束通知（true=开始，false=结束）。拖放期间服务层暂停面板自动关闭。</summary>
    public event Action<bool>? DragStateChanged;

    /// <summary>请求编辑全局动作页（设置菜单）</summary>
    public event Action? GlobalPageEditRequested;

    /// <summary>请求编辑当前程序专用动作页（设置菜单）</summary>
    public event Action? ContextPageEditRequested;

    /// <summary>请求刷新面板（设置菜单）</summary>
    public event Action? RefreshRequested;



    /// <summary>请求把动作从一个格子拖到另一个格子（from, to）</summary>
    public event Action<Guid, int, bool, bool>? ActionMoveRequested;

    public PanelWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        };
        Loaded += (_, _) =>
        {
            ClampToScreenAfterLayout();
            ForceTopmost();
        };
    }

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>公共置顶刷新（磁贴操作后由 PanelService 调用）</summary>
    public void ForceTopmostPublic() => ForceTopmost();

    /// <summary>强制置顶（无焦点窗口 z-order 低于有焦点窗口，需显式提到 TOPMOST 层）</summary>
    private void ForceTopmost()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }



    /// <summary>
    /// 加载场景与页面，显示面板。mouseX/mouseY 为触发时的鼠标位置。
    /// </summary>
    /// <summary>
    /// 物理像素 → 设备无关单位（DIP）。钩子/显示器 API 返回物理像素，WPF 布局用 DIP。
    /// </summary>
    private System.Windows.Point PhysicalToDip(int px, int py)
    {
        var source = PresentationSource.FromVisual(this)
            ?? PresentationSource.FromVisual(Application.Current.MainWindow);
        if (source?.CompositionTarget is { } ct)
            return ct.TransformFromDevice.Transform(new System.Windows.Point(px, py));
        return new System.Windows.Point(px, py);
    }

    /// <summary>DIP → 物理像素</summary>
    private System.Windows.Point PhysicalToDipInverse(int dipX, int dipY)
    {
        var source = PresentationSource.FromVisual(this)
            ?? PresentationSource.FromVisual(Application.Current.MainWindow);
        if (source?.CompositionTarget is { } ct)
            return ct.TransformToDevice.Transform(new System.Windows.Point(dipX, dipY));
        return new System.Windows.Point(dipX, dipY);
    }

    public void LoadAndShow(Scene globalScene, ActionPage globalPage,
        Scene? contextScene, ActionPage? contextPage,
        List<ActionItem> actions, int mouseX, int mouseY)
    {
        // 钩子坐标是物理像素，转成 DIP 再用于布局
        var dip = PhysicalToDip(mouseX, mouseY);
        _triggerX = (int)dip.X;
        _triggerY = (int)dip.Y;
        mouseX = _triggerX;
        mouseY = _triggerY;
        _globalPage = globalPage;
        _contextPage = contextPage;
        _globalScene = globalScene;
        _contextScene = contextScene;
        _actions = actions;
        _globalPageIndex = 0;
        _contextPageIndex = 0;
        // 默认活动区：有上下文页时放下区，否则上区
        _activeZone = contextPage is not null ? Zone.Context : Zone.Global;
        GlobalTitleText.Text = "速动 · " + globalScene.Name;
        SearchBox.Text = "";

        _filter = "";
        RenderZones();

        // 跟随鼠标定位（避开屏幕边缘）：高度已在 RenderPage 设为固定值，一次夹取到位
        Left = mouseX - Width / 2;
        Top = mouseY - 70;
        ClampToScreen();

        Show();
        ForceTopmost();
        _shownTick = System.Diagnostics.Stopwatch.GetTimestamp();

        // 兜底：布局稳定后再夹一次（防止首次布局微调）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ClampToScreen();
            ForceTopmost();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private int _triggerX, _triggerY;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public int rcMonitorLeft, rcMonitorTop, rcMonitorRight, rcMonitorBottom;
        public int rcWorkLeft, rcWorkTop, rcWorkRight, rcWorkBottom;
        public uint dwFlags;
    }

    /// <summary>把面板夹在触发点所在的显示器工作区内（支持多显示器）</summary>
    private void ClampToScreen()
    {
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        // 注意：_triggerX/_triggerY 此时已是 DIP，MonitorFromPoint 需要物理像素，转回去
        var phys = PhysicalToDipInverse(_triggerX, _triggerY);
        var monitor = MonitorFromPoint(new System.Drawing.Point((int)phys.X, (int)phys.Y), 2); // MONITOR_DEFAULTTONEAREST
        int waL, waT, waR, waB;
        if (monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref mi))
        {
            // MONITORINFO 返回物理像素 → 转 DIP 与 Left/Top 比较
            var lt = PhysicalToDip(mi.rcWorkLeft, mi.rcWorkTop);
            var rb = PhysicalToDip(mi.rcWorkRight, mi.rcWorkBottom);
            waL = (int)lt.X; waT = (int)lt.Y; waR = (int)rb.X; waB = (int)rb.Y;
        }
        else
        {
            // 回退主屏（SystemParameters 已是 DIP）
            waL = (int)SystemParameters.WorkArea.Left;
            waT = (int)SystemParameters.WorkArea.Top;
            waR = waL + (int)SystemParameters.WorkArea.Width;
            waB = waT + (int)SystemParameters.WorkArea.Height;
        }

        var width = !double.IsNaN(Width) && Width > 0 ? Width : 520;
        var height = !double.IsNaN(Height) && Height > 0 ? Height : 480;

        // 若面板比屏幕还高，至少保证顶部对齐
        if (height > waB - waT - 16)
        {
            Top = waT + 8;
            Left = waL + 8;
            return;
        }

        if (Top + height > waB) Top = waB - height - 8;
        if (Top < waT) Top = waT + 8;
        if (Left + width > waR) Left = waR - width - 8;
        if (Left < waL) Left = waL + 8;

        // 最终校验：面板必须完整落在触发屏内；异常时回退主屏
        var virtualLeft = (int)SystemParameters.VirtualScreenLeft;
        var virtualTop = (int)SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + (int)SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + (int)SystemParameters.VirtualScreenHeight;
        if (Left < virtualLeft || Top < virtualTop || Left + width > virtualRight || Top + height > virtualBottom)
        {
            var pw = (int)SystemParameters.WorkArea.Width;
            var ph = (int)SystemParameters.WorkArea.Height;
            Left = Math.Max(8, Math.Min(Left, pw - width - 8));
            Top = Math.Max(8, Math.Min(Top, ph - height - 8));
        }
    }

    /// <summary>窗口加载后（ActualHeight 已计算）再次校正位置，避免超出屏幕底部</summary>
    private void ClampToScreenAfterLayout() => ClampToScreen();

    private bool Matches(ActionItem a) => string.IsNullOrEmpty(_filter)
        || a.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || a.Description.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>指定页面中过滤后可见的占用索引集合（含动作的单元格 Index）</summary>
    private HashSet<int> GetOccupiedIndexes(ActionPage? page)
    {
        var result = new HashSet<int>();
        foreach (var c in page?.Cells ?? new List<ActionCell>())
        {
            if (c.ActionId == Guid.Empty) continue;
            var a = _actions.FirstOrDefault(x => x.Id == c.ActionId);
            if (a is null || !Matches(a)) continue;
            result.Add(c.Index);
        }
        return result;
    }

    /// <summary>指定页面的总页数（按最后占用索引估算）</summary>
    private int GetTotalPages(ActionPage? page, int pageSize)
    {
        var occupied = GetOccupiedIndexes(page);
        var last = occupied.Count > 0 ? occupied.Max() : -1;
        return Math.Max(1, (int)Math.Ceiling((last + 1) / (double)pageSize));
    }

    /// <summary>鼠标悬停切换活动区（滚轮/翻页按钮作用对象）</summary>
    private void ZoneGrid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender == GlobalGrid) _activeZone = Zone.Global;
        else if (sender == ContextGrid) _activeZone = Zone.Context;
    }

    /// <summary>渲染双区：上区全局（任何程序显示）+ 下区上下文（仅当前应用）</summary>
    private void RenderZones(bool animate = false)
    {
        RenderZone(GlobalGrid, _globalPage, _globalPageIndex, GlobalPageSize, animate);

        // 下区：与上区完全相同的"＋"网格布局。无专用页时同样渲染空格，
        // 点击"＋"由服务层静默创建该程序的场景+专用页（弹窗内完成，无需打开 App）
        var hasContext = _contextPage is not null;
        var fg = QuickLauncher.App.Scenes.SceneMatcher.GetForegroundInfo();
        var proc = string.IsNullOrEmpty(fg.process) ? "当前程序" : fg.process;
        ContextTitleText.Text = _contextScene?.Name ?? proc;
        RenderZone(ContextGrid, _contextPage, _contextPageIndex, ContextPageSize, animate);

        // 页码与翻页可用性（作用于活动区）
        var gPages = GetTotalPages(_globalPage, GlobalPageSize);
        var cPages = GetTotalPages(_contextPage, ContextPageSize);
        PageInfoText.Text = hasContext
            ? $"全局 {_globalPageIndex + 1}/{gPages} · {_contextScene?.Name ?? "上下文"} {_contextPageIndex + 1}/{cPages}"
            : $"全局 {_globalPageIndex + 1}/{gPages}";
        var activeIndex = _activeZone == Zone.Global ? _globalPageIndex : _contextPageIndex;
        var activePages = _activeZone == Zone.Global ? gPages : cPages;
        PrevPageButton.IsEnabled = activeIndex > 0;
        NextPageButton.IsEnabled = activeIndex < activePages - 1;

        // 固定高度一屏全显（标题+搜索+底栏+边距≈106，每行 68px=按钮64+间距4，下区标题/分隔 34）
        var gRows = Math.Max(1, (int)Math.Ceiling(GlobalGrid.Children.Count / 4.0));
        var cRows = Math.Max(1, (int)Math.Ceiling(ContextGrid.Children.Count / 4.0));
        Height = 106 + (gRows + cRows) * 78 + 34;
    }

    /// <summary>
    /// 渲染单个区：固定横 4 竖 N 格（磁贴 + "＋"空格填满，布局稳定）。
    /// 按 Index 顺序渲染；animate=true 时做横向翻页滑入动画（手机屏幕式）。
    /// </summary>
    private void RenderZone(UniformGrid grid, ActionPage? page, int pageIndex, int pageSize, bool animate)
    {
        grid.Children.Clear();

        var allCells = page?.Cells ?? new List<ActionCell>();
        var occupied = GetOccupiedIndexes(page);
        var cellsByIndex = allCells
            .GroupBy(c => c.Index)
            .ToDictionary(g => g.Key, g => g.First());
        var isContext = ReferenceEquals(page, _contextPage);

        var startIndex = pageIndex * pageSize;
        for (int i = startIndex; i < startIndex + pageSize; i++)
        {
            if (occupied.Contains(i)
                && cellsByIndex.TryGetValue(i, out var cell)
                && _actions.FirstOrDefault(a => a.Id == cell.ActionId) is { } action)
            {
                grid.Children.Add(CreateActionButton(action, cell, isContext));
            }
            else
            {
                grid.Children.Add(CreateEmptyCellButton(i, isContext));
            }
        }

        if (animate)
            PlaySlideIn(grid, _lastPageTurn);
    }

    /// <summary>横向翻页滑入动画：新页从翻页方向滑入（仿手机屏幕翻页）</summary>
    private void PlaySlideIn(UniformGrid grid, int direction)
    {
        var width = grid.RenderSize.Width > 0 ? grid.RenderSize.Width : 420;
        var slide = new System.Windows.Media.TranslateTransform();
        grid.RenderTransform = slide;
        var anim = new DoubleAnimation(direction * width, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private Button CreateActionButton(ActionItem action, ActionCell cell, bool isContext)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Tag = action,
            CommandParameter = isContext, // 供 Drop 时判定所属区
            AllowDrop = true
        };
        var name = string.IsNullOrEmpty(cell.OverrideName) ? action.Name : cell.OverrideName;

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(CreateIconElement(
            string.IsNullOrEmpty(cell.OverrideIcon) ? action.Icon : cell.OverrideIcon, 22));
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xF0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 80,
            TextAlignment = TextAlignment.Center
        });
        btn.Content = panel;
        // 描述为空时回退动作名，避免悬停出现空白提示框
        btn.ToolTip = string.IsNullOrEmpty(action.Description)
            ? action.Name
            : $"{action.Name}\n{action.Description}";

        // 左键执行
        btn.Click += ActionButton_Click;

        // 右键菜单：编辑 / 删除
        btn.ContextMenu = CreateActionContextMenu(action);

        // 拖动排序
        btn.PreviewMouseMove += ActionButton_PreviewMouseMove;
        btn.PreviewMouseLeftButtonDown += (_, e) => _dragStartPoint = e.GetPosition(null);
        btn.Drop += ActionButton_Drop;
        btn.DragOver += ActionButton_DragOver;

        return btn;
    }

    private System.Windows.Controls.ContextMenu CreateActionContextMenu(ActionItem action)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var editItem = new MenuItem { Header = "编辑" };
        editItem.Click += (_, _) => ActionEditRequested?.Invoke(action);
        var deleteItem = new MenuItem { Header = "删除", Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x48, 0x56)) };
        deleteItem.Click += (_, _) => ActionDeleteRequested?.Invoke(action);
        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);
        return menu;
    }

    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;

    private void ActionButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
        if (sender is not Button { Tag: ActionItem action }) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y) < 12) return;

        var srcIsContext = (sender as Button)?.CommandParameter is bool sc && sc;
        Dbg.Log($"drag start: {action.Name} from context={srcIsContext}");
        _isDragging = true;
        DragStateChanged?.Invoke(true); // 拖放期间暂停钩子的面板关闭/触发
        ForceTopmost(); // OLE 模态可能改变 z-order，先确保置顶
        try
        {
            // DataObject 携带来源区，支持跨区拖放（上区 ↔ 下区）
            var data = new DataObject(typeof(ActionItem), action);
            data.SetData("QL_SourceIsContext", srcIsContext);
            DragDrop.DoDragDrop((Button)sender, data, DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
            DragStateChanged?.Invoke(false);
            // 记录拖拽结束时间：松开落点会触发一次 Click，需抑制（否则拖到磁贴=执行、拖到空格=弹添加菜单）
            _lastDragEndTick = System.Diagnostics.Stopwatch.GetTimestamp();
            ForceTopmost();
        }
    }

    private void ActionButton_DragOver(object sender, DragEventArgs e)
    {
        // 明确允许放置（移动光标）
        if (e.Data.GetDataPresent(typeof(ActionItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void ActionButton_Drop(object sender, DragEventArgs e)
    {
        Dbg.Log($"drop received, data={e.Data.GetData(typeof(ActionItem))}");
        e.Handled = true; // 阻止冒泡到窗口级兜底（避免重复移动）
        if (e.Data.GetData(typeof(ActionItem)) is not ActionItem dragged) return;
        if (sender is not Button { Tag: ActionItem target }) return;
        var isContext = sender is Button { CommandParameter: bool ctx } && ctx;
        var srcIsContext = e.Data.GetData("QL_SourceIsContext") is bool srcCtx && srcCtx;

        var page = isContext ? _contextPage : _globalPage;
        var fromCell = page?.Cells.FirstOrDefault(c => c.ActionId == dragged.Id);
        var toCell = page?.Cells.FirstOrDefault(c => c.ActionId == target.Id);
        if (fromCell is null || toCell is null || fromCell == toCell) return;

        ActionMoveRequested?.Invoke(dragged.Id, toCell.Index, isContext, srcIsContext);
    }

    /// <summary>空格子（"+"添加）</summary>
    private Button CreateEmptyCellButton(int cellIndex, bool isContext)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Tag = cellIndex,
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            AllowDrop = true
        };
        btn.Content = new TextBlock
        {
            Text = "＋",
            FontSize = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
        };
        btn.ToolTip = "添加动作到此位置";
        btn.Click += (_, _) =>
        {
            if (IsClickFromDragEnd()) return; // 拖放刚结束，忽略误触发的 Click
            CellAddRequested?.Invoke(cellIndex, isContext);
        };
        btn.DragOver += ActionButton_DragOver;
        btn.Drop += (sender, e) =>
        {
            e.Handled = true; // 阻止冒泡到窗口级兜底
            if (e.Data.GetData(typeof(ActionItem)) is ActionItem dragged)
            {
                var srcIsContext = e.Data.GetData("QL_SourceIsContext") is bool srcCtx && srcCtx;
                ActionMoveRequested?.Invoke(dragged.Id, cellIndex, isContext, srcIsContext);
            }
        };
        return btn;
    }

    /// <summary>
    /// 创建图标元素：图片文件（提取的应用图标缓存 PNG）显示真实图标，其余回退字符近似。
    /// </summary>
    public static UIElement CreateIconElement(string? iconDesc, double size = 28)
    {
        if (Services.IconExtractor.IsImageFile(iconDesc))
        {
            try
            {
                var bmp = new BitmapImage(new Uri(iconDesc!)) { CacheOption = BitmapCacheOption.OnLoad };
                return new Image
                {
                    Source = bmp,
                    Width = size,
                    Height = size,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                };
            }
            catch
            {
                // 加载失败回退字符
            }
        }
        return new TextBlock
        {
            Text = ExtractIconGlyph(iconDesc),
            FontSize = size + 2,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    /// <summary>从图标描述提取显示字符</summary>
    public static string ExtractIconGlyph(string? icon)
    {
        if (string.IsNullOrEmpty(icon)) return "▶";
        if (icon.StartsWith("[fa:"))
            return "★";
        if (icon.StartsWith("[url:")) return "▣";
        if (icon.Length <= 2) return icon;
        return icon[..1];
    }

    /// <summary>拖拽结束的时间戳（抑制拖放后误触发的 Click）</summary>
    private long _lastDragEndTick;

    /// <summary>拖拽结束后的 Click 抑制窗口（ms）</summary>
    private const int DragClickSuppressMs = 400;

    private bool IsClickFromDragEnd()
        => _lastDragEndTick != 0
           && (System.Diagnostics.Stopwatch.GetTimestamp() - _lastDragEndTick) * 1000.0
               / System.Diagnostics.Stopwatch.Frequency < DragClickSuppressMs;

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDragging || IsClickFromDragEnd()) return;
        if (sender is Button { Tag: ActionItem action })
        {
            // 只发事件，不在此处关闭面板——是否关闭由服务层按动作类型决定
            //（如"加载动作页"需保持面板打开以原地切换页面）
            ActionInvoked?.Invoke(action);
            Dbg.Log("PanelWindow: action invoked");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        _globalPageIndex = 0;
        _contextPageIndex = 0;
        RenderZones();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var first = GlobalGrid.Children.OfType<Button>().FirstOrDefault()
                        ?? ContextGrid.Children.OfType<Button>().FirstOrDefault();
            first?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else if (e.Key == Key.Escape)
        {
            Dbg.Log("PanelWindow.Hide: searchbox Esc");
            Hide();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dbg.Log("PanelWindow.Hide: window Esc");
            Hide();
        }
    }

    private long _shownTick;

    private void PanelWindow_DragOver(object sender, DragEventArgs e)
    {
        // 窗口级兜底：非格子区域（标题栏/间隙/搜索框旁）也声明可放置
        if (e.Data.GetDataPresent(typeof(ActionItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 窗口级放置兜底：拖到格子间隙/网格外区域时，定位最近格子放置（自由放置的关键保障）。
    /// 格子级 Drop 已设 Handled，不会重复触发。
    /// </summary>
    private void PanelWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ActionItem)) is not ActionItem dragged) return;
        e.Handled = true;

        var (toIndex, isContext) = ResolveDropTarget(e);
        var srcIsContext = e.Data.GetData("QL_SourceIsContext") is bool srcCtx && srcCtx;
        Dbg.Log($"window-level drop: {dragged.Name} -> index {toIndex} context={isContext} src={srcIsContext}");
        ActionMoveRequested?.Invoke(dragged.Id, toIndex, isContext, srcIsContext);
    }

    /// <summary>解析放置目标：命中哪个区的格子返回 (索引, 是否上下文区)；未命中返回 (哨兵, 全局)</summary>
    private (int index, bool isContext) ResolveDropTarget(DragEventArgs e)
    {
        // 先下区后上区（下区在上区下方，视觉上优先命中）
        foreach (var (grid, isCtx) in new[] { (ContextGrid, true), (GlobalGrid, false) })
        {
            var pos = e.GetPosition(grid);
            var hit = grid.InputHitTest(pos);
            var node = (System.Windows.DependencyObject?)hit;
            while (node is not null)
            {
                if (node is Button { Tag: ActionItem target })
                {
                    var page = isCtx ? _contextPage : _globalPage;
                    var cell = page?.Cells.FirstOrDefault(c => c.ActionId == target.Id);
                    return (cell?.Index ?? -1, isCtx);
                }
                if (node is Button { Tag: int idx })
                    return (idx, isCtx);
                node = VisualTreeHelper.GetParent(node);
            }
        }
        return (-1, false); // 面板空白区 → 上区末尾（哨兵）
    }

    /// <summary>失焦宽限期（ms）：刚显示时忽略失活，防止被其他窗口立刻抢回焦点导致面板消失</summary>
    public int DeactivateGraceMs { get; set; } = 400;

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 无焦点窗口（WS_EX_NOACTIVATE）：不因失活关闭。
        // 关闭途径：Esc / 钩子检测窗口外左键点击 / 再次按触发键。
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) NextPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else PrevPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeZone == Zone.Global)
        {
            if (_globalPageIndex > 0) { _globalPageIndex--; _lastPageTurn = -1; RenderZones(animate: true); }
        }
        else if (_contextPageIndex > 0)
        {
            _contextPageIndex--;
            _lastPageTurn = -1;
            RenderZones(animate: true);
        }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeZone == Zone.Global)
        {
            if (_globalPageIndex < GetTotalPages(_globalPage, GlobalPageSize) - 1)
            {
                _globalPageIndex++;
                _lastPageTurn = 1;
                RenderZones(animate: true);
            }
        }
        else if (_contextPageIndex < GetTotalPages(_contextPage, ContextPageSize) - 1)
        {
            _contextPageIndex++;
            _lastPageTurn = 1;
            RenderZones(animate: true);
        }
    }

    /// <summary>⚙ 设置菜单：弹窗内直接编辑全局页/专用页、刷新</summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var editGlobal = new System.Windows.Controls.MenuItem { Header = "编辑全局动作页…" };
        editGlobal.Click += (_, _) => GlobalPageEditRequested?.Invoke();

        var editContext = new System.Windows.Controls.MenuItem { Header = "编辑本程序专用动作页…" };
        editContext.Click += (_, _) => ContextPageEditRequested?.Invoke();
        editContext.IsEnabled = _contextPage is not null;

        var refresh = new System.Windows.Controls.MenuItem { Header = "刷新面板" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        menu.Items.Add(editGlobal);
        menu.Items.Add(editContext);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(refresh);
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Dbg.Log("PanelWindow.Hide: close button");
        Hide();
    }

    /// <summary>重新加载并刷新双区（磁贴增删/拖动后调用）</summary>
    public void Refresh(Scene globalScene, ActionPage globalPage,
        Scene? contextScene, ActionPage? contextPage,
        List<ActionItem> actions, int mouseX, int mouseY)
    {
        LoadAndShow(globalScene, globalPage, contextScene, contextPage, actions, mouseX, mouseY);
    }
}
