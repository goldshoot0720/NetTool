using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tidegate.App.Controls;

public class DualSparkline : Control
{
    private static readonly IPen DownloadPen = new Pen(new SolidColorBrush(Color.Parse("#3DDC97")), 2);
    private static readonly IPen UploadPen = new Pen(new SolidColorBrush(Color.Parse("#F0A36B")), 2);
    private static readonly IPen DownloadCapPen = new Pen(new SolidColorBrush(Color.Parse("#3DDC97")), 1, DashStyle.Dash);
    private static readonly IPen UploadCapPen = new Pen(new SolidColorBrush(Color.Parse("#F0A36B")), 1, DashStyle.Dash);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#2A3830")), 1);

    public static readonly StyledProperty<double[]?> DownloadProperty =
        AvaloniaProperty.Register<DualSparkline, double[]?>(nameof(Download));

    public static readonly StyledProperty<double[]?> UploadProperty =
        AvaloniaProperty.Register<DualSparkline, double[]?>(nameof(Upload));

    public static readonly StyledProperty<double> DownloadCapProperty =
        AvaloniaProperty.Register<DualSparkline, double>(nameof(DownloadCap), double.NaN);

    public static readonly StyledProperty<double> UploadCapProperty =
        AvaloniaProperty.Register<DualSparkline, double>(nameof(UploadCap), double.NaN);

    static DualSparkline()
    {
        AffectsRender<DualSparkline>(DownloadProperty, UploadProperty, DownloadCapProperty, UploadCapProperty);
    }

    public DualSparkline()
    {
        ClipToBounds = true;
    }

    public double[]? Download
    {
        get => GetValue(DownloadProperty);
        set => SetValue(DownloadProperty, value);
    }

    public double[]? Upload
    {
        get => GetValue(UploadProperty);
        set => SetValue(UploadProperty, value);
    }

    public double DownloadCap
    {
        get => GetValue(DownloadCapProperty);
        set => SetValue(DownloadCapProperty, value);
    }

    public double UploadCap
    {
        get => GetValue(UploadCapProperty);
        set => SetValue(UploadCapProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 2 || bounds.Height < 2)
            return;

        context.DrawLine(GridPen, new Point(0, bounds.Height - 1), new Point(bounds.Width, bounds.Height - 1));
        var download = Download ?? [];
        var upload = Upload ?? [];
        var max = 1d;
        foreach (var value in download)
            max = Math.Max(max, value);
        foreach (var value in upload)
            max = Math.Max(max, value);
        if (DownloadCap > 0)
            max = Math.Max(max, DownloadCap);
        if (UploadCap > 0)
            max = Math.Max(max, UploadCap);

        DrawCap(context, bounds, DownloadCap, max, DownloadCapPen);
        DrawCap(context, bounds, UploadCap, max, UploadCapPen);
        DrawSeries(context, bounds, download, max, DownloadPen);
        DrawSeries(context, bounds, upload, max, UploadPen);
    }

    private static void DrawCap(DrawingContext context, Rect bounds, double cap, double max, IPen pen)
    {
        if (double.IsNaN(cap) || cap <= 0)
            return;
        var y = Y(bounds, cap, max);
        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
    }

    private static void DrawSeries(DrawingContext context, Rect bounds, double[] series, double max, IPen pen)
    {
        if (series.Length < 2)
            return;

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            for (var index = 0; index < series.Length; index++)
            {
                var x = index * (bounds.Width / (series.Length - 1));
                var point = new Point(x, Y(bounds, series[index], max));
                if (index == 0)
                    figure.BeginFigure(point, false);
                else
                    figure.LineTo(point);
            }

            figure.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static double Y(Rect bounds, double value, double max)
    {
        var usable = Math.Max(1, bounds.Height - 6);
        var y = bounds.Height - 3 - Math.Clamp(value, 0, max) / max * usable;
        return double.IsFinite(y) ? y : bounds.Height - 3;
    }
}
