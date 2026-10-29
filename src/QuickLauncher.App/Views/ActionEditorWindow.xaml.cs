using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class ActionEditorWindow : Window
{
    private readonly ActionItem _action;
    private readonly List<StepRow> _rows = new();

    /// <summary>简单动作参数表单的步骤承载对象（参数变更实时写回其 Inputs）</summary>
    private ActionStep _simpleStep = new();

    /// <summary>简单动作主参数名（用于兼容镜像 SimpleParam）</summary>
    private static readonly string[] MainParamNames =
        { "url", "program", "command", "text", "keys", "pageId", "path", "code" };

    /// <summary>步骤列表行（含展示信息）</summary>
    private sealed class StepRow
    {
        public ActionStep Step { get; set; } = new();
        public string DisplayName { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public ActionEditorWindow(ActionItem action)
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        _action = action.Clone();
        Title = $"编辑动作 - {action.Name}";

        NameBox.Text = _action.Name;
        IconBox.Text = _action.Icon;
        DescBox.Text = _action.Description;
        HotkeyBox.Text = _action.Hotkey;

        if (_action.Kind == ActionKind.Simple)
        {
            SimpleRadio.IsChecked = true;
            MigrateLegacySimpleParam();
            SelectSimpleType(_action.SimpleType);
            LoadSimpleForm(); // 确保表单必然加载（index 未变化时 SelectionChanged 不触发）
        }
        else
        {
            CompositeRadio.IsChecked = true;
        }

        ParamEditor.Changed += () => RebuildRows();
        ParamEditor.EditChildrenRequested += OnEditChildren;
        SimpleParamEditor.Changed += SimpleParamEditor_Changed;

        RebuildRows();
    }

    #region 简单动作

    /// <summary>打开图标选择器</summary>
    private void PickIconButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.IconPickerDialog(IconBox.Text) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedIcon is { } icon)
        {
            IconBox.Text = icon;
            UpdateIconPreview();
        }
    }

    /// <summary>图标输入变化：刷新预览（图片文件显示真实图标，否则字符近似）</summary>
    private void IconBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateIconPreview();

    private void UpdateIconPreview()
    {
        var icon = IconBox.Text?.Trim();
        if (Services.IconExtractor.IsImageFile(icon))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage(new Uri(icon!))
                {
                    CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad
                };
                IconPreviewImage.Source = bmp;
                IconPreviewImage.Visibility = Visibility.Visible;
                IconPreviewText.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // 回退字符
            }
        }
        IconPreviewText.Text = PanelWindow.ExtractIconGlyph(icon);
        IconPreviewImage.Visibility = Visibility.Collapsed;
        IconPreviewText.Visibility = Visibility.Visible;
    }

    /// <summary>从当前配置的程序/文件路径提取内嵌图标（对标原版：选完程序即得应用图标）</summary>
    private void ExtractIconButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _simpleStep.Inputs.GetValueOrDefault("program")
                   ?? _simpleStep.Inputs.GetValueOrDefault("path");
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            MessageBox.Show("请先在「程序/命令」参数中填写有效的程序路径。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var cached = Services.IconExtractor.ExtractAndCache(path, 0);
        if (cached is null)
        {
            MessageBox.Show("未能从该程序提取图标。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        IconBox.Text = cached;
        UpdateIconPreview();
    }

    private void SelectSimpleType(string simpleType)
    {
        var index = simpleType switch
        {
            "OpenUrl" => 0,
            "OpenFile" => 1,
            "OpenFolder" => 2,
            "RunProgram" => 3,
            "SendText" => 4,
            "SendKeys" => 5,
            "SwitchPage" => 6,
            "RunCommand" => 7,
            "RunScript" => 8,
            _ => 0
        };
        SimpleTypeCombo.SelectedIndex = index;
    }

    /// <summary>旧数据迁移：SimpleParam 单参数 -> SimpleInputs 主参数（新编辑器保存后即以多参数为准）</summary>
    private void MigrateLegacySimpleParam()
    {
        if (_action.SimpleInputs.Count > 0 || string.IsNullOrEmpty(_action.SimpleParam)) return;
        var mainParam = _action.SimpleType switch
        {
            "OpenUrl" => "url",
            "OpenFile" or "OpenFolder" => "path",
            "RunProgram" => "program",
            "RunCommand" => "command",
            "SendText" => "text",
            "SendKeys" => "keys",
            "SwitchPage" => "pageId",
            _ => null
        };
        if (mainParam is not null) _action.SimpleInputs[mainParam] = _action.SimpleParam;
    }

    /// <summary>按当前选择的类型加载参数表单（复用步骤参数编辑器渲染专属参数）</summary>
    private void LoadSimpleForm()
    {
        var stepType = CurrentSimpleType() switch
        {
            "RunCommand" => "RunCmd",
            "RunScript" => "RunScript",
            var t => t
        };
        _simpleStep = new ActionStep { StepType = stepType };
        foreach (var kv in _action.SimpleInputs)
            _simpleStep.Inputs[kv.Key] = kv.Value;
        SimpleParamEditor.SetVariables(_action.Variables.Select(v => v.Name).ToList());
        SimpleParamEditor.LoadStep(_simpleStep);
        // 仅「运行程序/打开文件」类型可一键提取程序图标
        ExtractIconButton.IsEnabled = CurrentSimpleType() is "RunProgram" or "OpenFile";
    }

    /// <summary>创建时的默认动作名（用户未改名时才自动识别）</summary>
    private static readonly HashSet<string> DefaultActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "启动软件", "打开文件", "打开文件夹", "运行命令", "打开网址",
        "模拟按键", "发送文本", "执行脚本", "加载动作页", "组合动作", "新动作"
    };

    private void SimpleParamEditor_Changed()
    {
        // 输入程序路径后自动提取图标（对标原版：选完程序即得应用图标），仅当图标尚未设置时
        if (string.IsNullOrWhiteSpace(IconBox.Text)
            && CurrentSimpleType() is "RunProgram" or "OpenFile"
            && (_simpleStep.Inputs.GetValueOrDefault("program")
                ?? _simpleStep.Inputs.GetValueOrDefault("path")) is { } program
            && System.IO.File.Exists(program))
        {
            var cached = Services.IconExtractor.ExtractAndCache(program, 0);
            if (cached is not null) IconBox.Text = cached;
        }

        TryAutoName();
    }

    /// <summary>
    /// 自动识别应用名称：名称为空或仍是创建时的默认名时，
    /// 用程序的文件描述（FileDescription，如"记事本"）或文件名填充。
    /// </summary>
    private void TryAutoName()
    {
        if (CurrentSimpleType() is not ("RunProgram" or "OpenFile")) return;
        var current = NameBox.Text?.Trim();
        if (!string.IsNullOrEmpty(current) && !DefaultActionNames.Contains(current)) return;

        var path = _simpleStep.Inputs.GetValueOrDefault("program")
                   ?? _simpleStep.Inputs.GetValueOrDefault("path");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        string? name = null;
        try
        {
            name = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch
        {
            // 忽略版本信息读取失败
        }
        if (string.IsNullOrWhiteSpace(name))
            name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (!string.IsNullOrWhiteSpace(name))
            NameBox.Text = name;
    }

    private string CurrentSimpleType() => SimpleTypeCombo.SelectedIndex switch
    {
        0 => "OpenUrl",
        1 => "OpenFile",
        2 => "OpenFolder",
        3 => "RunProgram",
        4 => "SendText",
        5 => "SendKeys",
        6 => "SwitchPage",
        7 => "RunCommand",
        8 => "RunScript",
        _ => "OpenUrl"
    };

    private void SimpleTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadSimpleForm();

    #endregion

    #region 类型切换

    private void TypeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (SimpleEditor is null || CompositeEditor is null) return;
        var isSimple = SimpleRadio.IsChecked == true;
        SimpleEditor.Visibility = isSimple ? Visibility.Visible : Visibility.Collapsed;
        CompositeEditor.Visibility = isSimple ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region 步骤列表

    private void RebuildRows()
    {
        _rows.Clear();
        FlattenSteps(_action.Steps, 0);
        StepsList.ItemsSource = null;
        StepsList.ItemsSource = _rows;
    }

    private void FlattenSteps(List<ActionStep> steps, int depth)
    {
        foreach (var step in steps)
        {
            var impl = StepRegistry.Default.Get(step.StepType);
            _rows.Add(new StepRow
            {
                Step = step,
                DisplayName = (impl?.Metadata.DisplayName ?? step.StepType) + (step.Enabled ? "" : "（已停用）"),
                Summary = BuildSummary(step)
            });
            if (step.Children.Count > 0)
                FlattenSteps(step.Children, depth + 1);
        }
    }

    private static string BuildSummary(ActionStep step)
    {
        var parts = new List<string>();
        foreach (var kv in step.Inputs)
        {
            var value = kv.Value ?? "";
            if (value.Length > 40) value = value[..40] + "…";
            parts.Add($"{kv.Key}={value}");
        }
        return string.Join("  ", parts);
    }

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new StepPickerWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedStepType is { } type)
            InsertStep(CreateDefaultStep(type));
    }

    private void AddIfButton_Click(object sender, RoutedEventArgs e)
    {
        InsertStep(new ActionStep { StepType = "If", Inputs = { ["condition"] = "true" } });
    }

    private void AddRepeatButton_Click(object sender, RoutedEventArgs e)
    {
        InsertStep(new ActionStep { StepType = "Repeat", Inputs = { ["count"] = "3" } });
    }

    private void InsertStep(ActionStep step)
    {
        var selectedRow = StepsList.SelectedItem as StepRow;
        if (selectedRow is not null)
        {
            var index = _action.Steps.IndexOf(selectedRow.Step);
            if (index >= 0)
                _action.Steps.Insert(index + 1, step);
            else
                _action.Steps.Add(step);
        }
        else
        {
            _action.Steps.Add(step);
        }
        RebuildRows();
        // 选中新步骤
        var row = _rows.FirstOrDefault(r => r.Step == step);
        if (row is not null) StepsList.SelectedItem = row;
    }

    private static ActionStep CreateDefaultStep(string type)
    {
        var step = new ActionStep { StepType = type };
        var impl = StepRegistry.Default.Get(type);
        if (impl is not null)
        {
            foreach (var p in impl.Metadata.Params)
            {
                if (!string.IsNullOrEmpty(p.DefaultValue))
                    step.Inputs[p.Name] = p.DefaultValue;
                else if (p.Required)
                    step.Inputs[p.Name] = "";
            }
        }
        return step;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepRow row) return;
        var index = _action.Steps.IndexOf(row.Step);
        if (index > 0)
        {
            (_action.Steps[index - 1], _action.Steps[index]) = (_action.Steps[index], _action.Steps[index - 1]);
            RebuildRows();
            StepsList.SelectedIndex = index - 1;
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepRow row) return;
        var index = _action.Steps.IndexOf(row.Step);
        if (index >= 0 && index < _action.Steps.Count - 1)
        {
            (_action.Steps[index + 1], _action.Steps[index]) = (_action.Steps[index], _action.Steps[index + 1]);
            RebuildRows();
            StepsList.SelectedIndex = index + 1;
        }
    }

    private void DeleteStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepRow row) return;
        _action.Steps.Remove(row.Step);
        RebuildRows();
        ParamEditor.LoadStep(null);
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = StepsList.SelectedItem as StepRow;
        var variables = _action.Variables.Select(v => v.Name).ToList();
        // 加入常见内置变量
        variables.AddRange(new[] { "cliptext", "selection", "now", "param", "quicker_in_param" });
        ParamEditor.SetVariables(variables.Distinct().ToList());
        ParamEditor.LoadStep(row?.Step);
    }

    private void OnEditChildren(ActionStep step)
    {
        var impl = StepRegistry.Default.Get(step.StepType);
        var title = impl?.Metadata.DisplayName ?? step.StepType;
        var dialog = new SubStepsDialog(step.Children, $"编辑子步骤 - {title}") { Owner = this };
        if (dialog.ShowDialog() == true)
            RebuildRows();
    }

    #endregion

    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new KeyCaptureDialog { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.CapturedKey))
        {
            // KeyCaptureDialog 输出 Quicker 语法（^c），转换为标准格式（Ctrl+C）
            HotkeyBox.Text = ConvertToStandardHotkey(dlg.CapturedKey);
        }
    }

    /// <summary>Quicker 语法 → 标准格式：^c → Ctrl+C，^+{F4} → Ctrl+Shift+F4</summary>
    private static string ConvertToStandardHotkey(string syntax)
    {
        var parts = new List<string>();
        var body = syntax;
        while (body.Length > 0 && "^!+#".Contains(body[0]))
        {
            switch (body[0])
            {
                case '^': parts.Add("Ctrl"); break;
                case '!': parts.Add("Alt"); break;
                case '+': parts.Add("Shift"); break;
                case '#': parts.Add("Win"); break;
            }
            body = body[1..];
        }
        if (body.StartsWith("{") && body.EndsWith("}"))
            body = body[1..^1];
        parts.Add(body.Length == 1 ? body.ToUpperInvariant() : body);
        return string.Join("+", parts);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _action.Name = NameBox.Text.Trim();
        _action.Icon = IconBox.Text.Trim();
        _action.Description = DescBox.Text.Trim();
        _action.Hotkey = HotkeyBox.Text.Trim();

        if (SimpleRadio.IsChecked == true)
        {
            _action.Kind = ActionKind.Simple;
            _action.SimpleType = CurrentSimpleType();
            _action.SimpleInputs = new Dictionary<string, string>(_simpleStep.Inputs, StringComparer.OrdinalIgnoreCase);
            // SimpleParam 兼容镜像：主参数值（旧版本/外部工具可读）
            _action.SimpleParam = MainParamNames
                .Select(n => _simpleStep.Inputs.GetValueOrDefault(n))
                .FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
        }
        else
        {
            _action.Kind = ActionKind.Composite;
        }

        App.Store!.SaveAction(_action);
        ((App)Application.Current).RefreshActionHotkeys();
        DialogResult = true;
        Close();
    }
}
