using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeMacroBuilder.Views;

public partial class CropWindow : Window
{
    private readonly BitmapSource _source;
    private readonly double _scale;        // 表示倍率(displayed / pixel)
    private bool _dragging;
    private Point _start;

    /// <summary>追加された結果画像(キャンセル時は null)。</summary>
    public BitmapSource? Result { get; private set; }

    public CropWindow(string imagePath)
    {
        InitializeComponent();

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(imagePath);
        bmp.EndInit();
        bmp.Freeze();
        _source = bmp;

        int pw = bmp.PixelWidth, ph = bmp.PixelHeight;
        const double maxW = 760, maxH = 560;
        _scale = Math.Min(Math.Min(maxW / pw, maxH / ph), 1.0);
        double dw = pw * _scale, dh = ph * _scale;

        Img.Source = _source;
        Img.Width = dw; Img.Height = dh;
        CropCanvas.Width = dw; CropCanvas.Height = dh;
    }

    private void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(CropCanvas);
        Canvas.SetLeft(Sel, _start.X);
        Canvas.SetTop(Sel, _start.Y);
        Sel.Width = 0; Sel.Height = 0;
        Sel.Visibility = Visibility.Visible;
        CropCanvas.CaptureMouse();
    }

    private void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(CropCanvas);
        double x = Math.Max(0, Math.Min(_start.X, p.X));
        double y = Math.Max(0, Math.Min(_start.Y, p.Y));
        double w = Math.Min(CropCanvas.Width, Math.Max(_start.X, p.X)) - x;
        double h = Math.Min(CropCanvas.Height, Math.Max(_start.Y, p.Y)) - y;
        Canvas.SetLeft(Sel, x);
        Canvas.SetTop(Sel, y);
        Sel.Width = Math.Max(0, w);
        Sel.Height = Math.Max(0, h);
    }

    private void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        CropCanvas.ReleaseMouseCapture();
        if (Sel.Width < 4 || Sel.Height < 4) Sel.Visibility = Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => Sel.Visibility = Visibility.Collapsed;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Sel.Visibility == Visibility.Visible && Sel.Width >= 4 && Sel.Height >= 4)
        {
            int x = (int)Math.Round(Canvas.GetLeft(Sel) / _scale);
            int y = (int)Math.Round(Canvas.GetTop(Sel) / _scale);
            int w = (int)Math.Round(Sel.Width / _scale);
            int h = (int)Math.Round(Sel.Height / _scale);

            x = Math.Clamp(x, 0, _source.PixelWidth - 1);
            y = Math.Clamp(y, 0, _source.PixelHeight - 1);
            w = Math.Clamp(w, 1, _source.PixelWidth - x);
            h = Math.Clamp(h, 1, _source.PixelHeight - y);

            Result = new CroppedBitmap(_source, new Int32Rect(x, y, w, h));
        }
        else
        {
            Result = _source;
        }
        DialogResult = true;
    }
}
