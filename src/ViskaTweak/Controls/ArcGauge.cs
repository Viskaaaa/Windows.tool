using System;
using System.Windows;
using System.Windows.Media;

namespace ViskaTweak.Controls;

/// <summary>
/// A 270-degree arc gauge. Drawn directly rather than templated: it is two arcs and nothing else,
/// and OnRender keeps it cheap enough to animate every frame.
/// </summary>
public sealed class ArcGauge : FrameworkElement
{
    private const double StartAngle = 135;    // bottom-left, sweeping clockwise
    private const double SweepAngle = 270;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ArcThicknessProperty = DependencyProperty.Register(
        nameof(ArcThickness), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fraction of the arc to fill, 0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ValueBrush
    {
        get => (Brush)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public double ArcThickness
    {
        get => (double)GetValue(ArcThicknessProperty);
        set => SetValue(ArcThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var radius = Math.Min(w, h) / 2 - ArcThickness / 2;
        if (radius <= 0) return;

        var centre = new Point(w / 2, h / 2);

        dc.DrawGeometry(null, new Pen(TrackBrush, ArcThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        }, Arc(centre, radius, SweepAngle));

        var fraction = Math.Clamp(Value, 0, 1);
        if (fraction > 0.001)
            dc.DrawGeometry(null, new Pen(ValueBrush, ArcThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, Arc(centre, radius, SweepAngle * fraction));
    }

    private static Geometry Arc(Point centre, double radius, double sweep)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointOn(centre, radius, StartAngle), isFilled: false, isClosed: false);
            ctx.ArcTo(PointOn(centre, radius, StartAngle + sweep),
                      new Size(radius, radius),
                      rotationAngle: 0,
                      isLargeArc: sweep > 180,
                      SweepDirection.Clockwise,
                      isStroked: true,
                      isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOn(Point centre, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(rad), centre.Y + radius * Math.Sin(rad));
    }
}
