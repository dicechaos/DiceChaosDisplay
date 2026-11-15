using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiceChaosDisplay.Models;

namespace DiceChaosDisplay.Services;

public sealed class AppUserSettings
{
    public TvDisplayProfile DefaultTvProfile { get; set; } = new();
    public List<string> RecentCampaignFiles { get; set; } = new();
}

public static class CampaignStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiceChaosDisplay",
            "settings.json");

    public static async Task SaveCampaignAsync(CampaignProject project, string filePath, CancellationToken ct = default)
    {
        string? campaignDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var clone = CloneWithNormalizedPaths(project, campaignDir, makeRelative: true);

        Directory.CreateDirectory(campaignDir ?? ".");
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fs, clone, JsonOptions, ct);
    }

    public static async Task<CampaignProject> LoadCampaignAsync(string filePath, CancellationToken ct = default)
    {
        string fullPath = Path.GetFullPath(filePath);
        string? campaignDir = Path.GetDirectoryName(fullPath);

        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var project = await JsonSerializer.DeserializeAsync<CampaignProject>(fs, JsonOptions, ct)
            ?? throw new InvalidDataException("Failed to deserialize .dicechaos campaign file.");

        return CloneWithNormalizedPaths(project, campaignDir, makeRelative: false);
    }

    public static AppUserSettings LoadUserSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppUserSettings>(json, JsonOptions) ?? new AppUserSettings();
            }
        }
        catch
        {
            // Ignore malformed settings
        }
        return new AppUserSettings();
    }

    public static void SaveUserSettings(AppUserSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Non-critical persistence
        }
    }

    public static void RecordRecentCampaign(string filePath)
    {
        string full = Path.GetFullPath(filePath);
        var settings = LoadUserSettings();
        settings.RecentCampaignFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        settings.RecentCampaignFiles.Insert(0, full);
        if (settings.RecentCampaignFiles.Count > 10)
            settings.RecentCampaignFiles.RemoveRange(10, settings.RecentCampaignFiles.Count - 10);
        SaveUserSettings(settings);
    }

    private static CampaignProject CloneWithNormalizedPaths(CampaignProject src, string? baseDir, bool makeRelative)
    {
        // Round-trip clone through JSON so we don't mutate live paths unexpectedly
        string raw = JsonSerializer.Serialize(src, JsonOptions);
        var copy = JsonSerializer.Deserialize<CampaignProject>(raw, JsonOptions)!;

        if (string.IsNullOrWhiteSpace(baseDir)) return copy;

        foreach (var scene in copy.Scenes)
        {
            scene.MediaFilePath = NormalizePath(scene.MediaFilePath, baseDir, makeRelative);
        }

        foreach (var handout in copy.Handouts)
        {
            handout.ImageFilePath = NormalizePath(handout.ImageFilePath, baseDir, makeRelative);
        }

        if (!string.IsNullOrWhiteSpace(copy.TvProfile.StandbyImagePath))
        {
            copy.TvProfile.StandbyImagePath = NormalizePath(copy.TvProfile.StandbyImagePath, baseDir, makeRelative);
        }

        return copy;
    }

    private static string NormalizePath(string path, string baseDir, bool makeRelative)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            if (makeRelative)
            {
                if (Path.IsPathRooted(path))
                {
                    string rel = Path.GetRelativePath(baseDir, path);
                    // Only store relative if on the same volume/root and doesn't climb excessively
                    if (!Path.IsPathRooted(rel) && !rel.StartsWith("..\\..\\..", StringComparison.Ordinal))
                        return rel;
                }
                return path;
            }
            else
            {
                if (!Path.IsPathRooted(path))
                {
                    return Path.GetFullPath(Path.Combine(baseDir, path));
                }
                return path;
            }
        }
        catch
        {
            return path;
        }
    }
}
