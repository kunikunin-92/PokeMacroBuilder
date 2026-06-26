using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PokeMacroBuilder.Views;

/// <summary>
/// ドラッグ中のブロックの「ゴースト」をマウスに追従させて描画するアドナー。
/// </summary>
public sealed class DragGhostAdorner : Adorner
{
    private readonly ImageSource _image;
    private readonly Size _size;
    private Point _offset;

    public DragGhostAdorner(UIElement adorned, ImageSource image, Size size)
        : base(adorned)
    {
        _image = image;
        _size = size;
        IsHitTestVisible = false;
    }

    public void UpdatePosition(Point offset)
    {
        _offset = offset;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushOpacity(0.78);
        dc.DrawImage(_image, new Rect(_offset, _size));
        dc.Pop();
    }
}
