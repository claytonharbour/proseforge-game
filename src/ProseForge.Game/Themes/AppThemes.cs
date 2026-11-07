namespace ProseForge.Game.Themes;

// The phone's side of theme packs: files come from the app package, and pictures are streamed
// from it on demand, so a pack's art never has to be compiled into the app's image resources.
public static class AppThemes
{
    public static ThemeLoad Load(string id) => ThemePacks.Load(id, Open);

    public static ImageSource? Image(string? path) =>
        path is null ? null : ImageSource.FromStream(_ => FileSystem.OpenAppPackageFileAsync(path));

    public static Stream? Open(string path)
    {
        try
        {
            // Package files open synchronously underneath on Android and iOS; there's no I/O wait.
            return FileSystem.OpenAppPackageFileAsync(path).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;   // not in the package: the pack loader reports it
        }
    }
}
