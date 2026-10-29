using System.Windows;
using System.Windows.Controls;

namespace QuickLauncher.App.Views;

/// <summary>
/// 图标展示控件：图标描述为图片文件（提取的应用图标 PNG）时显示真实图标，
/// 否则回退字符近似。供列表/模板绑定使用（Icon="{Binding Icon}"）。
/// </summary>
public partial class IconPresenter : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconPresenter),
        new PropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconPresenter),
        new PropertyMetadata(24.0, OnIconChanged));

    /// <summary>图标描述：[fa:名称:颜色] / [url:地址] / 图片文件路径 / 单个字符</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>图标尺寸（像素）</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((IconPresenter)d).Update();

    public IconPresenter()
    {
        InitializeComponent();
    }

    private void Update()
    {
        Root.Children.Clear();
        Root.Children.Add(PanelWindow.CreateIconElement(Icon, Size));
    }
}
