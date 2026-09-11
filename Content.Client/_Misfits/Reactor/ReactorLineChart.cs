using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Misfits.Reactor;

public sealed class ReactorLineChart : BoxContainer
{
    private readonly Label _readoutLabel;
    private readonly GraphControl _graph;

    public float Scale
    {
        get => _graph.Scale;
        set => _graph.Scale = value;
    }

    public ReactorLineChart(string title)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        var titleLabel = new Label
        {
            Text = title,
            StyleClasses = { "PipBoyLabel" },
            FontColorOverride = Color.FromHex("#33FF66"),
            ClipText = true,
        };
        AddChild(titleLabel);

        _readoutLabel = new Label
        {
            Text = string.Empty,
            StyleClasses = { "PipBoyLabel" },
            ClipText = true,
        };
        AddChild(_readoutLabel);

        _graph = new GraphControl();
        AddChild(_graph);
    }

    public void PushSample(float target, float current)
    {
        _graph.PushSample(target, current);
        _readoutLabel.Text = $"cur {FormatPercent(current)} / tgt {FormatPercent(target)}";
        _readoutLabel.FontColorOverride = ThresholdColor(current);
    }

    private static string FormatPercent(float normalized) => $"{(int) (normalized * 100)}%";

    private static Color ThresholdColor(float normalized)
    {
        if (normalized >= 0.8f)
            return Color.Red;
        return normalized >= 0.5f ? Color.Yellow : Color.LightGreen;
    }

    private sealed class GraphControl : Control
    {
        private const int Capacity = 120;
        private const int GraphHeight = 56;

        private static readonly Color BgColor = Color.FromHex("#0C1F0E");
        private static readonly Color GridColor = Color.FromHex("#163E1E");
        private static readonly Color TargetColor = Color.FromHex("#1E9C3D");

        private readonly float[] _target = new float[Capacity];
        private readonly float[] _current = new float[Capacity];
        private int _count;

        public float Scale = 1f;

        public GraphControl()
        {
            MinHeight = GraphHeight;
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
            var height = (float) PixelSize.Y;

            handle.DrawRect(new UIBox2(0, 0, width, height), BgColor);

            for (var i = 0; i <= 2; i++)
            {
                var y = height - height * (i / 2f);
                handle.DrawLine(new Vector2(0, y), new Vector2(width, y), GridColor);
            }

            if (_count < 2)
                return;

            var spacing = width / (Capacity - 1);
            var xStart = Capacity - _count;

            DrawSeries(handle, _target, xStart, spacing, height, dashed: true, colorCoded: false);
            DrawSeries(handle, _current, xStart, spacing, height, dashed: false, colorCoded: true);
        }

        private void DrawSeries(DrawingHandleScreen handle, float[] series, int xStart, float spacing,
            float height, bool dashed, bool colorCoded)
        {
            var segment = 0;

            for (var i = 0; i + 1 < _count; i++)
            {
                var n1 = Math.Clamp(series[i] / Scale, 0f, 1f);
                var n2 = Math.Clamp(series[i + 1] / Scale, 0f, 1f);
                var v1 = new Vector2((xStart + i) * spacing, height - n1 * height);
                var v2 = new Vector2((xStart + i + 1) * spacing, height - n2 * height);

                if (dashed)
                {
                    segment++;
                    if (segment % 2 == 0)
                        continue;
                }

                var color = colorCoded ? ThresholdColor(Math.Max(n1, n2)) : TargetColor;
                handle.DrawLine(v1, v2, color);
            }
        }
    }
}
