using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace QuickLauncher.App.Views;

/// <summary>
/// 图标选择对话框：字符/Emoji、FontAwesome 语法、图片文件、网络图片四种来源。
/// 返回图标描述：[fa:名称:颜色] / [url:地址] / 文件路径 / 单个字符。
/// </summary>
public partial class IconPickerDialog : Window
{
    private string _selected = "▶";

    /// <summary>应用程序页签状态</summary>
    private string _appPath = "";
    private int _appIndex;
    private int _appCount;

    /// <summary>选中的图标描述（取消时为 null）</summary>
    public string? SelectedIcon { get; private set; }

    /// <summary>常用字符/Emoji（与面板 ExtractIconGlyph 的字符渲染兼容）</summary>
    private static readonly string[] Chars =
    {
        "▶", "⚡", "🌐", "📁", "📄", "🖥", "⌨", "💬", "📜", "📑",
        "★", "✚", "⚙", "✉", "🔍", "🔒", "📌", "🎵", "🔊", "🔧",
        "🚀", "⏰", "📅", "🎯", "🖱", "💻", "🔗", "⚠", "❤", "⭐",
        "✨", "📎", "🗑", "⬇", "⬆", "⏸", "🔄", "🔨", "🧲", "🎨"
    };

    public IconPickerDialog(string? currentIcon = null)
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        BuildCharGrid();
        if (!string.IsNullOrEmpty(currentIcon))
        {
            _selected = currentIcon;
            RestoreCurrent(currentIcon);
        }
        UpdatePreview();
    }

    /// <summary>把当前图标描述回显到对应页签</summary>
    private void RestoreCurrent(string icon)
    {
        if (icon.StartsWith("[fa:") && icon.EndsWith("]"))
        {
            var inner = icon[4..^1];
            var idx = inner.LastIndexOf(':');
            if (idx > 0)
            {
                FaNameCombo.Text = inner[..idx];
                FaColorBox.Text = inner[(idx + 1)..];
            }
        }
        else if (icon.StartsWith("[url:") && icon.EndsWith("]"))
        {
            UrlBox.Text = icon[5..^1];
        }
        else if (icon.Length > 2)
        {
            ImagePathBox.Text = icon;
        }
        // 长度 ≤2 的字符无需回显输入框（网格点选即可）
    }

    private void BuildCharGrid()
    {
        foreach (var c in Chars)
        {
            var btn = new Button
            {
                Content = c,
                Width = 46,
                Height = 46,
                Margin = new Thickness(3),
                FontSize = 20,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = c
            };
            btn.Click += (_, _) =>
            {
                _selected = c;
                UpdatePreview();
            };
            CharGrid.Children.Add(btn);
        }
    }

    private void FaInput_Changed(object sender, RoutedEventArgs e) => UpdateFromFa();
    private void FaInput_Changed(object sender, SelectionChangedEventArgs e) => UpdateFromFa();

    private void UpdateFromFa()
    {
        var name = FaNameCombo.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var color = string.IsNullOrWhiteSpace(FaColorBox.Text) ? "#FF0078D7" : FaColorBox.Text.Trim();
        _selected = $"[fa:{name}:{color}]";
        UpdatePreview();
    }

    private void FaColorPick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            FaColorBox.Text = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    #region 应用程序图标提取

    private void AppBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择程序或图标文件",
            Filter = "程序和图标|*.exe;*.dll;*.icl;*.ico;*.lnk|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _appPath = dlg.FileName;
        AppPathBox.Text = _appPath;
        _appCount = Services.IconExtractor.CountIcons(_appPath);
        if (_appCount <= 0) _appCount = 1; // 个别文件计数为 0 但索引 0 仍可提取
        _appIndex = 0;
        LoadAppIcon();
    }

    private void LoadAppIcon()
    {
        if (string.IsNullOrEmpty(_appPath)) return;
        var cached = Services.IconExtractor.ExtractAndCache(_appPath, _appIndex);
        if (cached is null)
        {
            AppHint.Text = "未能从该文件提取图标（可能不含图标资源）。";
            return;
        }
        _selected = cached;
        AppIconIndexText.Text = $"{_appIndex + 1} / {_appCount}";
        AppHint.Text = "已提取并缓存为 PNG；多图标程序可用 ‹ › 切换。";
        UpdatePreview();
    }

    private void AppIconPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_appCount <= 1 || string.IsNullOrEmpty(_appPath)) return;
        _appIndex = (_appIndex - 1 + _appCount) % _appCount;
        LoadAppIcon();
    }

    private void AppIconNext_Click(object sender, RoutedEventArgs e)
    {
        if (_appCount <= 1 || string.IsNullOrEmpty(_appPath)) return;
        _appIndex = (_appIndex + 1) % _appCount;
        LoadAppIcon();
    }

    #endregion

    private void ImageBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图标图片",
            Filter = "图片|*.png;*.ico;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            ImagePathBox.Text = dlg.FileName;
            _selected = dlg.FileName;
            UpdatePreview();
        }
    }

    private void ImageInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ImagePathBox.Text))
        {
            _selected = ImagePathBox.Text.Trim();
            UpdatePreview();
        }
    }

    private void UrlInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(UrlBox.Text))
        {
            _selected = $"[url:{UrlBox.Text.Trim()}]";
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (Services.IconExtractor.IsImageFile(_selected))
        {
            try
            {
                var bmp = new BitmapImage(new Uri(_selected)) { CacheOption = BitmapCacheOption.OnLoad };
                PreviewImage.Source = bmp;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                FallbackTextPreview();
            }
        }
        else
        {
            FallbackTextPreview();
        }
        PreviewDesc.Text = _selected switch
        {
            var s when s.StartsWith("[fa:") => $"FontAwesome 语法：{s}",
            var s when s.StartsWith("[url:") => $"网络图片：{s}",
            var s when Services.IconExtractor.IsImageFile(s) => $"图片：{s}",
            var s when s.Length > 2 => $"图片文件：{s}",
            _ => $"字符：{_selected}"
        };
    }

    private void FallbackTextPreview()
    {
        PreviewText.Text = PanelWindow.ExtractIconGlyph(_selected);
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedIcon = _selected;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedIcon = null;
        DialogResult = false;
        Close();
    }
}
