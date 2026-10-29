using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class SubStepsDialog : Window
{
    private readonly List<ActionStep> _children;
    private readonly List<StepRow> _rows = new();

    private sealed class StepRow
    {
        public ActionStep Step { get; set; } = new();
        public string DisplayName { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public SubStepsDialog(List<ActionStep> children, string title)
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        _children = children;
        Title = title;
        HintText.Text = "这些步骤在该容器条件满足时按顺序执行。";
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Clear();
        foreach (var s in _children)
        {
            var impl = StepRegistry.Default.Get(s.StepType);
            _rows.Add(new StepRow
            {
                Step = s,
                DisplayName = impl?.Metadata.DisplayName ?? s.StepType,
                Summary = string.Join("  ", s.Inputs.Select(kv => $"{kv.Key}={Truncate(kv.Value)}"))
            });
        }
        ChildrenList.ItemsSource = null;
        ChildrenList.ItemsSource = _rows;
    }

    private static string Truncate(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Length > 30 ? v[..30] + "…" : v;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new StepPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedStepType is { } type)
        {
            var step = new ActionStep { StepType = type };
            var impl = StepRegistry.Default.Get(type);
            if (impl is not null)
                foreach (var p in impl.Metadata.Params)
                {
                    if (!string.IsNullOrEmpty(p.DefaultValue))
                        step.Inputs[p.Name] = p.DefaultValue;
                    else if (p.Required)
                        step.Inputs[p.Name] = "";
                }
            _children.Add(step);
            Rebuild();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChildrenList.SelectedItem is StepRow row)
        {
            _children.Remove(row.Step);
            Rebuild();
        }
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
