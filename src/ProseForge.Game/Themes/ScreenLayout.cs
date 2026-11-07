namespace ProseForge.Game.Themes;

// Where everything sits on the game screen, in points, for any phone. MainPage.xaml uses these same
// numbers, a test checks them against a measured phone, and `make theme-guide` draws them as the
// guide artists generate theme art against (themes/README.md).
public static class ScreenLayout
{
    public const double HeaderHeight = 92;    // title row, goal and message: fixed, so no theme can move it
    public const double RowsTop = 4;          // gap under the header
    public const double DockReserve = 112;    // bottom space kept for the dock (delivered, hire, pause; power-ups)
    public const double RowSpacing = 5;
    public const double SidePadding = 10;
    public const double BarHeight = 60;       // every station's bar, in every theme
    public const double ActorSize = 172;      // a station's cut-out character, fitted in this square
    public const double ActorRightMargin = 24;

    // The phones the guide is drawn for, as page shapes (width ÷ height, below the status bar):
    // from a 9:16 screen, which shows the whole picture, to a tall 9:20 one, which crops its sides.
    public const double WidestPage = 9.0 / 16, TallestPage = 9.0 / 20;

    public readonly record struct Box(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    public sealed record Station(Box Band, Box Open, Box Bar, Box Actor);

    public sealed record Screen(double Width, double Height, Box Header, IReadOnlyList<Station> Stations, Box Dock);

    public static Screen For(double width, double height, int stations)
    {
        var rowsTop = HeaderHeight + RowsTop;
        var rowsBottom = height - DockReserve;
        var band = (rowsBottom - rowsTop - RowSpacing * (stations - 1)) / stations;
        var inner = width - 2 * SidePadding;
        var rows = new List<Station>();
        for (var i = 0; i < stations; i++)
        {
            var top = rowsTop + i * (band + RowSpacing);
            var bar = new Box(SidePadding, top + band - BarHeight, inner, BarHeight);
            // 172 wide always; on a short phone the band caps its height, and the picture fits inside.
            var actorHeight = Math.Min(ActorSize, band);
            var actor = new Box(SidePadding + inner - ActorRightMargin - ActorSize, top + (band - actorHeight) / 2, ActorSize, actorHeight);
            rows.Add(new Station(new Box(SidePadding, top, inner, band), new Box(SidePadding, top, inner, band - BarHeight), bar, actor));
        }
        return new Screen(width, height, new Box(0, 0, width, HeaderHeight), rows, new Box(0, rowsBottom, width, DockReserve));
    }

    // A desktop window for the game: phone-shaped, so the art and the measured layout hold.
    public sealed record Window(double Width, double Height, double MinWidth, double MinHeight, double X, double Y);

    // The window's page shape: a 9:19.5 phone, between the guide's 9:16 and 9:20.
    public const double DesktopPage = 9.0 / 19.5;
    public const double DesktopHeight = 1040;   // chosen by eye: a tall phone on a desktop
    public const double MinBand = BarHeight + 40;  // every station keeps its bar and a strip of art above it
    public const double MinWidth = 360;         // the narrowest phone we measure: the header's buttons fit

    // The default size and the smallest allowed for a screen of this size in points, centred on it.
    // A short screen gets 90% of its height, never more, and never smaller than
    // the minimum, which keeps every bar on screen with art above it.
    public static Window DesktopWindow(double screenWidth, double screenHeight, int stations = 3)
    {
        var minHeight = HeaderHeight + RowsTop + DockReserve + stations * MinBand + (stations - 1) * RowSpacing;
        var height = Math.Max(minHeight, Math.Min(DesktopHeight, Math.Round(screenHeight * 0.9)));
        var width = Math.Max(MinWidth, Math.Round(height * DesktopPage));
        return new Window(width, height, MinWidth, minHeight,
            Math.Max(0, (screenWidth - width) / 2), Math.Max(0, (screenHeight - height) / 2));
    }

    // The background is scaled to fill the page and centred (AspectFill), so a page narrower than the
    // picture crops its sides, and a wider one crops top and bottom. Returns a page box in picture pixels.
    public static Box ToPicture(Box page, double pageWidth, double pageHeight, int pictureWidth, int pictureHeight)
    {
        var scale = Math.Max(pageWidth / pictureWidth, pageHeight / pictureHeight);
        var offsetX = (pictureWidth * scale - pageWidth) / 2;
        var offsetY = (pictureHeight * scale - pageHeight) / 2;
        return new Box((page.Left + offsetX) / scale, (page.Top + offsetY) / scale, page.Width / scale, page.Height / scale);
    }
}
