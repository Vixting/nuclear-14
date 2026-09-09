using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Misfits.Reactor;

public sealed class ReactorLineChart : Control
{
    private const int Capacity = 120;
    private const int GraphHeight = 72;
    private const int LabelHeight = 16;

    private static readonly Color BgColor = Color.FromHex("#0C1F0E");
    private static readonly Color GridColor = Color.FromHex("#163E1E");
    private static readonly Color TargetColor = Color.FromHex("#1E9C3D");
    private static readonly Color CurrentColor = Color.FromHex("#33FF66");
    private static readonly Color TextColor = Color.FromHex("#33FF66");
    private static readonly Color DimTextColor = Color.FromHex("#1E9C3D");

    private readonly float[] _target = new float[Capacity];
    private readonly float[] _current = new float[Capacity];
    private int _count;

    private readonly VectorFont _font;
    private readonly string _title;

    public float Scale = 1f;

    public ReactorLineChart(IResourceCache cache, string title)
    {
        _title = title;
        _font = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSansMono-Regular.ttf"), 10);

        MinHeight = GraphHeight + LabelHeight;
        HorizontalExpand = true;
    }

    public void PushSample(float target, float current)
    {
        if (_count < Capacity)
        {
            _target[_count] = target;
            _current[_count] = current;
            _count++;
            return;
        }

        Array.Copy(_target, 1, _target, 0, Capacity - 1);
        Array.Copy(_current, 1, _current, 0, Capacity - 1);
        _target[Capacity - 1] = target;
        _current[Capacity - 1] = current;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var width = PixelSize.X;
        var graphTop = LabelHeight;
        var graphBottom = LabelHeight + GraphHeight;

        var box = new UIBox2(0, graphTop, width, graphBottom);
        handle.DrawRect(box, BgColor);

        handle.DrawString(_font, new Vector2(2, 2), _title, TextColor);

        var latestCurrent = _count > 0 ? _current[_count - 1] : 0f;
        var latestTarget = _count > 0 ? _target[_count - 1] : 0f;
        var readout = $"{FormatPercent(latestCurrent)} / {FormatPercent(latestTarget)}";
        var readoutWidth = handle.GetDimensions(_font, readout, 1f).X;
        handle.DrawString(_font, new Vector2(width - readoutWidth - 2, 2), readout, DimTextColor);

        for (var i = 0; i <= 2; i++)
        {
            var y = graphBottom - (graphBottom - graphTop) * (i / 2f);
            handle.DrawLine(new Vector2(0, y), new Vector2(width, y), GridColor);
        }

        if (_count < 2)
            return;

        var spacing = width / (Capacity - 1);
        var xStart = Capacity - _count;

        DrawSeries(handle, _target, xStart, spacing, graphTop, graphBottom, TargetColor, dashed: true);
        DrawSeries(handle, _current, xStart, spacing, graphTop, graphBottom, CurrentColor, dashed: false);
    }

    private void DrawSeries(DrawingHandleScreen handle, float[] series, int xStart, float spacing,
        float graphTop, float graphBottom, Color color, bool dashed)
    {
        var height = graphBottom - graphTop;
        var segment = 0;

        for (var i = 0; i + 1 < _count; i++)
        {
            var v1 = new Vector2((xStart + i) * spacing, graphBottom - Math.Clamp(series[i] / Scale, 0f, 1f) * height);
            var v2 = new Vector2((xStart + i + 1) * spacing, graphBottom - Math.Clamp(series[i + 1] / Scale, 0f, 1f) * height);

            if (dashed)
            {
                segment++;
                if (segment % 2 == 0)
                    continue;
            }

            handle.DrawLine(v1, v2, color);
        }
    }

    private static string FormatPercent(float normalized) => $"{(int) (normalized * 100)}%";
}
