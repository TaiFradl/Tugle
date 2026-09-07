using System.Drawing;
using System.Text.Json;

namespace Tugle;

/// <summary>Small, human-readable profile settings. It deliberately contains no passwords or tokens.</summary>
internal sealed class TugleSettings
{
    public float GuiScale { get; set; } = 0.9f;
    public string ThemeName { get; set; } = "Ocean";
    public string? CustomThemeAccent { get; set; }
    public bool UseSiteColors { get; set; }
    public string HomeBackgroundMode { get; set; } = "gradient";
    public string HomeBackground { get; set; } = "#071526";
    public string HomeBackgroundSecondary { get; set; } = "#203D5B";
    public string? HomeBackgroundMediaUrl { get; set; }
    public bool SetupCompleted { get; set; }
    public int SetupVersion { get; set; }
    public bool GoogleConnected { get; set; }
    public bool LowMemoryMode { get; set; } = true;
    public bool ReduceMotion { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal static string ProfileDirectory => Environment.GetEnvironmentVariable("TUGLE_PROFILE_DIRECTORY") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tugle");

    private static string FilePath => Path.Combine(ProfileDirectory, "settings.json");

    public static TugleSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<TugleSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.GuiScale = Math.Clamp(loaded.GuiScale, 0.7f, 1.4f);
                    return loaded;
                }
            }
        }
        catch
        {
            // A damaged settings file must never prevent the browser from starting.
        }

        return new TugleSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Preferences are a convenience. Keep the running browser usable if the profile is read-only.
        }
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color FromHex(string? value, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value)) return ColorTranslator.FromHtml(value);
        }
        catch
        {
            // Use the supplied fallback below.
        }

        return fallback;
    }
}
