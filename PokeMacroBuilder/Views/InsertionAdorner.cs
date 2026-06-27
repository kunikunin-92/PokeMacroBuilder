using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PokeMacroBuilder.Views;

/// <summary>
/// ドラッグ中、ブロックが挿入される位置を示す水平ラインを描画する。
/// </summary>
public sealed class InsertionAdorner : Adorner
{
    private static readonly Brush LineBrush = MakeBrush();
    private static readonly Pen LinePen = MakePen();

    private double _x1, _x2, _y;
    private bool _visible;

    public InsertionAdorner(UIElement adorned) : base(adorned)
    {
        IsHitTestVisible = false;
    }

    private static Brush MakeBrush()
    {
        var b = new SolidColorBrush(Color.FromRgb(0x1C, 0x97, 0xEA));
        b.Freeze();
        return b;
    }

    private static Pen MakePen()
    {
        var p = new Pen(MakeBrush(), 3);
        p.Freeze();
        return p;
    }

    public void Update(double x1, double x2, double y)
    {
        _x1 = x1; _x2 = x2; _y = y; _visible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_visible) return;
        dc.DrawLine(LinePen, new Point(_x1, _y), new Point(_x2, _y));
        dc.DrawEllipse(LineBrush, null, new Point(_x1, _y), 4, 4);
        dc.DrawEllipse(LineBrush, null, new Point(_x2, _y), 4, 4);
    }
}
