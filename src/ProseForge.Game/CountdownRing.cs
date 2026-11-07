namespace ProseForge.Game;

// The ring on each of the dock's power-up buttons: a full outline while it can be bought, and an arc
// that shrinks clockwise while it runs. Drawn in code, so it needs no art and takes the theme's colour.
public sealed class CountdownRing : IDrawable
{
    public double Fraction { get; set; }   // 0..1 of the power-up's time left
    public bool Running { get; set; }
    public Color Colour { get; set; } = Colors.White;

    public void Draw(ICanvas canvas, RectF rect)
    {
        const float stroke = 2.5f;
        var box = rect.Inflate(-stroke, -stroke);
        canvas.StrokeSize = stroke;
        canvas.StrokeColor = Colour.WithAlpha(Running ? 0.3f : 0.9f);
        canvas.DrawEllipse(box);
        if (!Running || Fraction <= 0) return;
        canvas.StrokeColor = Colour;
        canvas.StrokeLineCap = LineCap.Round;
        // From twelve o'clock, clockwise, as far as the time left.
        canvas.DrawArc(box, 90, 90 - (float)(360 * Math.Clamp(Fraction, 0, 1)), clockwise: true, closed: false);
    }
}

// A CountdownRing that a template can bind, so each power-up button draws its own.
// It redraws whenever its time left, its running state or its colour changes.
public sealed class CountdownRingView : GraphicsView
{
    private readonly CountdownRing ring = new();

    public static readonly BindableProperty FractionProperty = BindableProperty.Create(
        nameof(Fraction), typeof(double), typeof(CountdownRingView), 0.0, propertyChanged: Redraw);
    public static readonly BindableProperty RunningProperty = BindableProperty.Create(
        nameof(Running), typeof(bool), typeof(CountdownRingView), false, propertyChanged: Redraw);
    public static readonly BindableProperty ColourProperty = BindableProperty.Create(
        nameof(Colour), typeof(Color), typeof(CountdownRingView), Colors.White, propertyChanged: Redraw);

    public CountdownRingView() => Drawable = ring;

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public bool Running { get => (bool)GetValue(RunningProperty); set => SetValue(RunningProperty, value); }
    public Color Colour { get => (Color)GetValue(ColourProperty); set => SetValue(ColourProperty, value); }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (CountdownRingView)bindable;
        view.ring.Fraction = view.Fraction;
        view.ring.Running = view.Running;
        view.ring.Colour = view.Colour;
        view.Invalidate();
    }
}
