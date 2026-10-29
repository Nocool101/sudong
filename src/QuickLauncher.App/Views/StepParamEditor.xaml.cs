using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class StepParamEditor : UserControl
{
    private ActionStep? _step;

    /// <summary>参数变更回调</summary>
    public event Action? Changed;

    /// <summary>请求编辑子步骤</summary>
    public event Action<ActionStep>? EditChildrenRequested;

    public StepParamEditor()
    {
        InitializeComponent();
    }

    public void LoadStep(ActionStep? step)
    {
        _step = step;
        ParamsPanel.Children.Clear();
        OutputsPanel.Children.Clear();

        if (step is null)
        {
            HeaderText.Text = "";
            DescText.Text = "";
            OutputHeader.Visibility = Visibility.Collapsed;
            ChildrenHint.Visibility = Visibility.Collapsed;
            return;
        }

        var impl = StepRegistry.Default.Get(step.StepType);
        if (impl is null)
        {
            HeaderText.Text = step.StepType;
            DescText.Text = "未知步骤类型";
            return;
        }

        var meta = impl.Metadata;
        HeaderText.Text = meta.DisplayName;
        DescText.Text = meta.Description;

        foreach (var p in meta.Params)
        {
            var current = step.Inputs.TryGetValue(p.Name, out var v) ? v : p.DefaultValue;
            ParamsPanel.Children.Add(CreateParamRow(p, current));
        }

        // 输出参数（引擎返回值类步骤）
        if (meta.Type is "RunCSharp" or "RunJavaScript" or "RunPython" or "RunScriptFile"
            or "GetClipboardText" or "GetSelectedText" or "GetClipboardFiles"
            or "GetDateTime" or "GetForegroundWindowInfo")
        {
            OutputHeader.Visibility = Visibility.Visible;
            var resultName = meta.Type switch
            {
                "RunCSharp" or "RunJavaScript" or "RunPython" or "RunScriptFile" => "result",
                _ => meta.Type switch
                {
                    "GetClipboardText" => "cliptext",
                    "GetSelectedText" => "selection",
                    "GetClipboardFiles" => "clipfiles",
                    "GetDateTime" => "now",
                    _ => "window"
                }
            };
            OutputsPanel.Children.Add(CreateOutputRow(resultName, step.Outputs));
        }

        // 容器步骤
        if (meta.IsContainer)
        {
            ChildrenHint.Visibility = Visibility.Visible;
            ChildrenCountText.Text = $"{step.Children.Count} 个子步骤";
        }
        else
        {
            ChildrenHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>动作页下拉项模板：📑 图标 + 页名</summary>
    private static DataTemplate BuildPageItemTemplate()
    {
        var template = new DataTemplate(typeof(ActionPage));
        var spf = new FrameworkElementFactory(typeof(StackPanel));
        spf.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(TextBlock.TextProperty, "📑");
        icon.SetValue(TextBlock.FontSizeProperty, 12.0);
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        name.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        spf.AppendChild(icon);
        spf.AppendChild(name);
        template.VisualTree = spf;
        return template;
    }

    private UIElement CreateParamRow(StepParamDef p, string current)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = p.DisplayName + (p.Required ? " *" : ""),
            Foreground = System.Windows.Media.Brushes.LightGray,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (!string.IsNullOrEmpty(p.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = p.Description,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // 编辑控件
        UIElement editor;
        if (p.Name == "pageId")
        {
            // 动作页选择：下拉带图标列出所有页面（显示名称，值存 Id）
            var combo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 0),
                SelectedValuePath = "Id",
                ItemTemplate = BuildPageItemTemplate()
            };
            try
            {
                if (App.Store is { } store)
                {
                    foreach (var page in store.LoadPages().OrderBy(x => x.Name))
                        combo.Items.Add(page);
                }
            }
            catch { /* 页面加载失败时退化为手动输入 */ }

            if (Guid.TryParse(current ?? "", out var curPageId))
                combo.SelectedValue = curPageId;
            combo.SelectionChanged += (_, _) =>
            {
                if (_step is null) return;
                _step.Inputs[p.Name] = combo.SelectedValue?.ToString() ?? "";
                Changed?.Invoke();
            };

            if (combo.Items.Count == 0)
            {
                var emptyPanel = new StackPanel();
                emptyPanel.Children.Add(combo);
                emptyPanel.Children.Add(new TextBlock
                {
                    Text = "（暂无动作页，请先在场景/页面管理中创建）",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                editor = emptyPanel;
            }
            else
            {
                editor = combo;
            }
        }
        else if (p.Options is { Length: > 0 })
        {
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 0) };
            foreach (var opt in p.Options) combo.Items.Add(opt);
            combo.SelectedItem = combo.Items.Cast<string>().FirstOrDefault(o => o == current) ?? current ?? "";
            combo.SelectionChanged += (_, _) =>
            {
                if (_step is null) return;
                _step.Inputs[p.Name] = combo.SelectedItem?.ToString() ?? "";
                Changed?.Invoke();
            };
            editor = combo;
        }
        else if (p.Type == VariableType.Bool)
        {
            var cb = new CheckBox
            {
                IsChecked = bool.TryParse(current, out var b) && b,
                Content = "启用",
                Foreground = System.Windows.Media.Brushes.LightGray
            };
            cb.Checked += (_, _) => SetValue(p.Name, "true");
            cb.Unchecked += (_, _) => SetValue(p.Name, "false");
            editor = cb;
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBox
            {
                Text = current ?? "",
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                Padding = new Thickness(6, 5, 6, 5),
                AcceptsReturn = p.Multiline,
                TextWrapping = p.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = p.Multiline ? 80 : 26
            };
            tb.TextChanged += (_, _) => SetValue(p.Name, tb.Text);
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            // 文件浏览按钮
            if (p.Name is "program" or "path" or "file")
            {
                var btn = new Button
                {
                    Content = "浏览...",
                    Margin = new Thickness(6, 0, 0, 0),
                    Padding = new Thickness(10, 4, 10, 4),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46)),
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top
                };
                btn.Click += (_, _) =>
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = $"选择{p.DisplayName}",
                        Filter = p.Name == "program"
                            ? "程序|*.exe;*.bat;*.cmd|所有文件|*.*"
                            : "所有文件|*.*"
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        tb.Text = dialog.FileName;
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);
            }

            // 按键捕获按钮
            if (p.Name is "keys" or "key")
            {
                var btn = new Button
                {
                    Content = "捕获按键",
                    Margin = new Thickness(6, 0, 0, 0),
                    Padding = new Thickness(10, 4, 10, 4),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD7)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top
                };
                btn.Click += (_, _) =>
                {
                    var dlg = new KeyCaptureDialog { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.CapturedKey))
                    {
                        tb.Text = dlg.CapturedKey;
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);
            }

            // 变量插入按钮
            var varBtn = new Button
            {
                Content = "{ }",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46)),
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "插入变量引用 {变量名}"
            };
            varBtn.Click += (_, _) =>
            {
                var varWindow = new VariablePickerDialog(_currentVariables ?? new List<string>())
                {
                    Owner = Window.GetWindow(this)
                };
                if (varWindow.ShowDialog() == true && varWindow.SelectedVariable is { } name)
                {
                    var caret = tb.CaretIndex;
                    tb.Text = tb.Text.Insert(caret, "{" + name + "}");
                    tb.CaretIndex = caret + name.Length + 2;
                }
            };
            Grid.SetColumn(varBtn, 2);
            grid.Children.Add(varBtn);

            // 第三列（变量按钮）需要列定义
            if (grid.ColumnDefinitions.Count < 3)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            editor = grid;
        }

        panel.Children.Add(editor);
        return panel;
    }

    private List<string>? _currentVariables;

    /// <summary>设置可用变量列表（供变量插入按钮）</summary>
    public void SetVariables(List<string> variables)
    {
        _currentVariables = variables;
    }

    private UIElement CreateOutputRow(string resultName, Dictionary<string, string> outputs)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = $"结果（{resultName}）→ 变量",
            Foreground = System.Windows.Media.Brushes.LightGray,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var tb = new TextBox
        {
            Text = outputs.TryGetValue(resultName, out var v) ? v : "",
            Background = System.Windows.Media.Brushes.White,
            Foreground = System.Windows.Media.Brushes.Black,
            Padding = new Thickness(6, 5, 6, 5)
        };
        tb.TextChanged += (_, _) =>
        {
            if (_step is null) return;
            if (string.IsNullOrWhiteSpace(tb.Text))
                _step.Outputs.Remove(resultName);
            else
                _step.Outputs[resultName] = tb.Text;
            Changed?.Invoke();
        };
        panel.Children.Add(tb);
        return panel;
    }

    private void SetValue(string name, string value)
    {
        if (_step is null) return;
        _step.Inputs[name] = value;
        Changed?.Invoke();
    }

    private void EditChildrenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step is not null)
            EditChildrenRequested?.Invoke(_step);
    }
}
