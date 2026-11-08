namespace ShadowClient;

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
    public string JavaPath { get; set; } = "";
    public string OfflineUsername { get; set; } = "";
    public int MaximumRamMb { get; set; } = 1536;
    public int ScreenWidth { get; set; } = 1280;
    public int ScreenHeight { get; set; } = 720;
}

public sealed class GameProfile
{
    public string Name { get; set; } = "Default";
    public string VersionId { get; set; } = "1.21.1";
    public override string ToString() => $"{Name}   ·   {VersionId}";
}

public sealed class ModrinthProject
{
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public override string ToString() => $"{Title}\n{Description}";
}

public sealed class LocalGameFile
{
    public string FullPath { get; set; } = "";
    public string FileName => Path.GetFileName(FullPath);
    public bool Enabled => !FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => (Enabled ? "●  " : "○  DISABLED  ") + FileName;
}
