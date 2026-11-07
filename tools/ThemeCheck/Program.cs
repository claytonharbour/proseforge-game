using ProseForge.Game.Themes;

// Usage: ThemeCheck <repo root> [theme id ...]
// No ids: check themes/index.json, every pack it lists, and that no pack folder is missing from it.
// Prints each problem; exits 1 on any error. Warnings (missing art, say) don't fail the check.
// ThemeCheck --guide <stations> <out.svg> [overlay]: draw the art guide (ScreenLayout) for a 1080×1920
// background; "overlay" leaves out the paper so it can be laid over a picture.
if (args.Length is 3 or 4 && args[0] == "--guide")
{
    File.WriteAllText(args[2], ArtGuide.Svg(int.Parse(args[1]), overlay: args.Length == 4 && args[3] == "overlay"));
    Console.WriteLine($"wrote {args[2]}");
    return 0;
}

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var themesDir = Path.Combine(root, ThemePacks.Root);
Stream? Open(string path)
{
    var full = Path.Combine(root, path);
    return File.Exists(full) ? File.OpenRead(full) : null;
}

var failed = false;
string[] ids;
if (args.Length > 1)
    ids = args[1..];
else
{
    var problems = new List<ThemeProblem>();
    var index = ThemeSwitch.LoadIndex(Open, problems);
    var folders = Directory.GetDirectories(themesDir).Select(d => Path.GetFileName(d)!).Where(id => !id.StartsWith('_')).ToList();
    ids = index is null ? [.. folders] : [.. index.Offered(includeDrafts: true)];
    // A pack the index doesn't list never reaches a phone, which is easy to miss.
    foreach (var id in folders.Except(ids))
        problems.Add(new(ProblemLevel.Error, $"themes/{id}/ isn't listed in {ThemeSwitch.IndexPath}, so no build offers it."));
    Console.WriteLine($"{(problems.Count == 0 ? "ok  " : "FAIL")} {ThemeSwitch.IndexPath}");
    foreach (var problem in problems) Console.WriteLine($"     {problem}");
    failed |= problems.Count > 0;
}

foreach (var id in ids)
{
    var load = ThemePacks.Load(id, Open);
    Console.WriteLine($"{(load.Loaded ? "ok  " : "FAIL")} {id}");
    foreach (var problem in load.Problems) Console.WriteLine($"     {problem}");
    failed |= !load.Loaded;
}
if (ids.Length == 0) { Console.WriteLine($"No theme packs found in {themesDir}"); failed = true; }
return failed ? 1 : 0;
