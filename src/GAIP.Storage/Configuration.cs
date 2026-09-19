using System.Text.Json;

namespace GAIP.Storage;

public enum StorageMode { Local, Shared }
public enum AppTheme { System, Light, Dark }
public sealed class AppConfig
{
    public StorageMode Mode { get; set; }
    public string SharedPath { get; set; } = "";
    public int SyncSeconds { get; set; } = 60;
    public int BackupCount { get; set; } = 30;
    public string CsvSeparator { get; set; } = ";";
    public AppTheme Theme { get; set; }
    public int MaxHomeColumns { get; set; } = 3;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Theme)) throw new InvalidDataException("Mode ou thème inconnu.");
        if (Mode == StorageMode.Shared && (string.IsNullOrWhiteSpace(SharedPath) || !Path.IsPathFullyQualified(SharedPath)))
            throw new InvalidDataException("Indiquez un chemin partagé absolu.");
        if (SyncSeconds is < 5 or > 86400) throw new InvalidDataException("Synchronisation : entre 5 et 86400 secondes.");
        if (BackupCount is < 1 or > 10000) throw new InvalidDataException("Sauvegardes : entre 1 et 10000.");
        if (MaxHomeColumns is < 1 or > 6) throw new InvalidDataException("Colonnes de l’accueil : entre 1 et 6.");
        if (CsvSeparator.Length != 1 || CsvSeparator[0] is '"' or '\r' or '\n' or '\0')
            throw new InvalidDataException("Le séparateur CSV doit être un caractère autre que guillemet ou retour à la ligne.");
    }
}

public static class UserPaths
{
    public static string Root => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GAIP")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "GAIP");
    public static string ConfigRoot => OperatingSystem.IsWindows() ? Root
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "GAIP");
    public static string User => OperatingSystem.IsWindows() ? $"{Environment.UserDomainName}\\{Environment.UserName}" : Environment.UserName;
    public static string Machine => Environment.MachineName;
    public static AppConfig LoadConfig(string configRoot)
    {
        var path = Path.Combine(configRoot, "config.json");
        if (!File.Exists(path)) return new();
        var config = JsonSerializer.Deserialize(File.ReadAllBytes(path), StorageJsonContext.Default.AppConfig) ?? throw new InvalidDataException("Configuration vide.");
        config.Validate();
        return config;
    }
    public static void SaveConfig(string configRoot, AppConfig config)
    {
        config.Validate();
        Directory.CreateDirectory(configRoot);
        JsonData.AtomicWrite(Path.Combine(configRoot, "config.json"), JsonSerializer.SerializeToUtf8Bytes(config, StorageJsonContext.Default.AppConfig));
    }
}
