using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickLauncher.App.Themes;

namespace QuickLauncher.App.Views;

/// <summary>
/// 矢量图标控件：按名称从 <see cref="IconGeometries"/> 取 24×24 路径并描边渲染。
///
/// 用法：&lt;views:IconView Kind="Bolt" Size="16" Foreground="{StaticResource Brush.Brand}"/&gt;
/// 图标颜色跟随 Foreground（Control 自带属性，可被样式/继承统一控制）。
/// </summary>
public partial class IconView : UserControl
{
    /// <summary>基准描边粗细（相对 24×24 网格）；缩放时按比例换算，保证视觉粗细恒定</summary>
    private const double BaseStroke = 1.75;

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(IconView),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconView),
        new PropertyMetadata(16.0, OnVisualChanged));

    /// <summary>图标名称（对应 IconGeometries 中的键）</summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>图标边长（像素）</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IconView()
    {
        InitializeComponent();
        Apply();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((IconView)d).Apply();

    private void Apply()
    {
        var size = Size <= 0 ? 16 : Size;
        Width = size;
        Height = size;

        Shape.Data = IconGeometries.Get(Kind);
        Shape.StrokeThickness = BaseStroke * 24.0 / size;
        Shape.Visibility = Shape.Data is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
