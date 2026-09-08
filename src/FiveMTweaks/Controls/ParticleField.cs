using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using FiveMTweaks.Services;

namespace FiveMTweaks.Controls;

/// <summary>
/// Drifting nodes joined by lines when they come close - the constellation look. Drawn straight to
/// a DrawingContext on the composition tick.
///
/// This is the background of an app whose whole job is frame rate, so it is deliberately cheap:
/// the node count is capped, pens are pre-built into opacity buckets instead of allocated per line,
/// and the whole thing stops when it is off-screen or effects are switched off.
/// </summary>
public sealed class ParticleField : FrameworkElement
{
    private const int NodeCount = 42;
    private const double LinkDistance = 190;   // device-independent pixels
    private const int OpacityBuckets = 6;

    private readonly List<Node> _nodes = new();
    private readonly Random _rng = new();
    private Pen[] _pens = Array.Empty<Pen>();
    private Brush _nodeFill = Brushes.Transparent;
    private bool _running;
    private TimeSpan _last;

    private struct Node
    {
        public double X, Y, Vx, Vy, R;
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(ParticleField),
        new FrameworkPropertyMetadata(Color.FromRgb(0xE4, 0x54, 0x4F),
            FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ParticleField)d).BuildBrushes()));

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public ParticleField()
    {
        IsHitTestVisible = false;
        BuildBrushes();

        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Sync();
        SizeChanged += (_, _) => Seed();
        ThemeService.EffectsChanged += Sync;
    }

    private void BuildBrushes()
    {
        var fill = new SolidColorBrush(Accent) { Opacity = 0.75 };
        fill.Freeze();
        _nodeFill = fill;

        // One pen per opacity step, reused every frame instead of allocating per line.
        _pens = new Pen[OpacityBuckets];
        for (var i = 0; i < OpacityBuckets; i++)
        {
            var brush = new SolidColorBrush(Accent) { Opacity = 0.05 + 0.30 * (i / (double)(OpacityBuckets - 1)) };
            brush.Freeze();
            var pen = new Pen(brush, 1);
            pen.Freeze();
            _pens[i] = pen;
        }
    }

    private void Sync()
    {
        if (IsVisible && IsLoaded && ThemeService.EffectsAllowed) Start();
        else Stop();
    }

    private void Start()
    {
        if (_running) return;
        if (_nodes.Count == 0) Seed();
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
        InvalidateVisual();
    }

    private void Seed()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        _nodes.Clear();
        for (var i = 0; i < NodeCount; i++)
            _nodes.Add(new Node
            {
                X = _rng.NextDouble() * ActualWidth,
                Y = _rng.NextDouble() * ActualHeight,
                Vx = (_rng.NextDouble() - 0.5) * 26,   // px per second
                Vy = (_rng.NextDouble() - 0.5) * 26,
                R = 1.3 + _rng.NextDouble() * 2.0
            });
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;

        // Advance by real elapsed time, so the drift looks the same at 60 and at 144 Hz.
        var dt = _last == TimeSpan.Zero ? 1 / 60.0 : (args.RenderingTime - _last).TotalSeconds;
        _last = args.RenderingTime;
        if (dt <= 0 || dt > 0.25) dt = 1 / 60.0;

        for (var i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            n.X += n.Vx * dt;
            n.Y += n.Vy * dt;

            if (n.X < 0) n.X += ActualWidth;
            else if (n.X > ActualWidth) n.X -= ActualWidth;
            if (n.Y < 0) n.Y += ActualHeight;
            else if (n.Y > ActualHeight) n.Y -= ActualHeight;

            _nodes[i] = n;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_running || _nodes.Count == 0) return;

        for (var i = 0; i < _nodes.Count; i++)
        {
            var a = _nodes[i];

            for (var j = i + 1; j < _nodes.Count; j++)
            {
                var b = _nodes[j];
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq > LinkDistance * LinkDistance) continue;

                // Closer pairs get a brighter pen; the bucket avoids a Pen per line.
                var closeness = 1 - Math.Sqrt(distSq) / LinkDistance;
                var bucket = Math.Clamp((int)(closeness * OpacityBuckets), 0, OpacityBuckets - 1);
                dc.DrawLine(_pens[bucket], new Point(a.X, a.Y), new Point(b.X, b.Y));
            }

            dc.DrawEllipse(_nodeFill, null, new Point(a.X, a.Y), a.R, a.R);
        }
    }
}
