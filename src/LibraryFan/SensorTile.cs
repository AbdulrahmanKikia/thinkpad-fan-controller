using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LibraryFan;

/// <summary>
/// One temperature register, built once and updated in place. The bar runs from 30°C to 90°C.
/// A missing or unread byte keeps the empty track.
/// </summary>
internal sealed class SensorTile
{
    private const double BarMinCelsius = 30;
    private const double BarMaxCelsius = 90;
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly Border _fill;
    private readonly ColumnDefinition _filled;
    private readonly ColumnDefinition _rest;

    private SensorTile(Border root, TextBlock label, TextBlock value, Border fill, ColumnDefinition filled, ColumnDefinition rest)
    {
        Root = root;
        _label = label;
        _value = value;
        _fill = fill;
        _filled = filled;
        _rest = rest;
    }

    public Border Root { get; }

    public static SensorTile Create(Style surface, Brush background, Brush track, Brush muted)
    {
        var filled = new ColumnDefinition { Width = new GridLength(0.001, GridUnitType.Star) };
        var rest = new ColumnDefinition { Width = new GridLength(0.999, GridUnitType.Star) };
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 8, 0, 0) };
        bar.ColumnDefinitions.Add(filled);
        bar.ColumnDefinitions.Add(rest);
        var trackBorder = new Border { Background = track, CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(trackBorder, 2);
        var fill = new Border { CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed };
        bar.Children.Add(trackBorder);
        bar.Children.Add(fill);

        var label = new TextBlock { Foreground = muted, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var value = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(label);
        stack.Children.Add(value);
        stack.Children.Add(bar);

        var root = new Border
        {
            Style = surface,
            Background = background,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(4),
            MinHeight = 56,
            Child = stack
        };
        return new SensorTile(root, label, value, fill, filled, rest);
    }

    public void Update(SensorSample sensor, SensorSample? hottest, Func<string, Brush> brush, Func<int?, string> heatKey)
    {
        _label.Text = sensor.Label;
        _value.Text = sensor.IsValid ? $"{sensor.Celsius}°" : sensor.WasRead ? "—" : "…";
        _value.Foreground = sensor.IsValid ? brush("Ink") : brush("Muted");
        Root.BorderBrush = sensor.IsValid && sensor.Celsius >= FanSession.DangerCelsius ? brush("Coral")
            : hottest is not null && sensor.Address == hottest.Address ? brush("Teal")
            : brush("Line");

        var share = sensor.Celsius is int celsius
            ? Math.Clamp((celsius - BarMinCelsius) / (BarMaxCelsius - BarMinCelsius), 0, 1)
            : 0;
        _filled.Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star);
        _rest.Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star);
        _fill.Background = brush(heatKey(sensor.Celsius));
        _fill.Visibility = share > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
