namespace Tugle;

internal sealed record ThemePalette(
    string Name,
    string Description,
    Color Chrome,
    Color ChromeLighter,
    Color Surface,
    Color SurfaceHover,
    Color SurfacePressed,
    Color ContentBackground,
    Color Text,
    Color Muted,
    Color Icon,
    Color Accent,
    Color Border,
    Color BorderStrong,
    Color ActiveTab,
    Color TabHover,
    Color TabDragging,
    Color Detail,
    Color Section,
    Color Selection,
    Color Danger,
    Color DangerHover,
    Color DangerText,
    Color Prominent,
    Color ProminentHover,
    Color ProminentText,
    Color IconBackground)
{
    public static ThemePalette FromAccent(string name, string description, Color accent)
    {
        var chrome = Mix(Color.FromArgb(13, 16, 23), accent, 0.18);
        var lighter = Mix(chrome, accent, 0.26);
        var surface = Mix(Color.FromArgb(17, 21, 29), accent, 0.24);
        var hover = Mix(surface, accent, 0.28);
        var pressed = Mix(surface, accent, 0.42);
        var content = Mix(Color.FromArgb(7, 10, 15), accent, 0.10);
        var text = Color.FromArgb(240, 243, 248);
        var muted = Mix(Color.FromArgb(157, 166, 181), accent, 0.18);
        var icon = Mix(Color.FromArgb(188, 201, 220), accent, 0.30);
        var border = Mix(Color.FromArgb(57, 67, 84), accent, 0.34);
        var borderStrong = Mix(Color.FromArgb(100, 119, 151), accent, 0.48);
        var activeTab = Mix(surface, accent, 0.38);
        var dragging = Mix(surface, accent, 0.52);
        var detail = Mix(Color.FromArgb(155, 180, 204), accent, 0.20);
        var section = Mix(Color.FromArgb(137, 157, 184), accent, 0.28);
        var selection = Mix(surface, accent, 0.55);
        var prominent = Mix(Color.FromArgb(24, 27, 38), accent, 0.72);
        var prominentHover = Mix(prominent, accent, 0.30);
        var iconBackground = Mix(surface, accent, 0.34);

        return new ThemePalette(
            name,
            description,
            chrome,
            lighter,
            surface,
            hover,
            pressed,
            content,
            text,
            muted,
            icon,
            accent,
            border,
            borderStrong,
            activeTab,
            hover,
            dragging,
            detail,
            section,
            selection,
            Color.FromArgb(205, 57, 68),
            Color.FromArgb(231, 78, 88),
            Color.FromArgb(255, 237, 239),
            prominent,
            prominentHover,
            Color.FromArgb(247, 244, 255),
            iconBackground);
    }

    private static Color Mix(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount));
    }
}

internal static class ThemePalettes
{
    public static readonly ThemePalette Ocean = new(
        "Ocean",
        "Cool blue with a teal accent",
        Color.FromArgb(24, 29, 40),
        Color.FromArgb(34, 41, 55),
        Color.FromArgb(31, 41, 55),
        Color.FromArgb(34, 47, 65),
        Color.FromArgb(48, 60, 78),
        Color.FromArgb(11, 16, 24),
        Color.FromArgb(235, 240, 248),
        Color.FromArgb(151, 163, 181),
        Color.FromArgb(178, 198, 222),
        Color.FromArgb(76, 205, 188),
        Color.FromArgb(58, 71, 91),
        Color.FromArgb(92, 131, 183),
        Color.FromArgb(38, 53, 75),
        Color.FromArgb(34, 47, 65),
        Color.FromArgb(48, 66, 91),
        Color.FromArgb(151, 175, 201),
        Color.FromArgb(132, 155, 185),
        Color.FromArgb(48, 65, 88),
        Color.FromArgb(205, 57, 68),
        Color.FromArgb(229, 74, 86),
        Color.FromArgb(255, 235, 237),
        Color.FromArgb(51, 70, 113),
        Color.FromArgb(67, 88, 140),
        Color.FromArgb(225, 233, 255),
        Color.FromArgb(48, 67, 91));

    public static readonly ThemePalette Violet = new(
        "Violet",
        "Deep purple with a lilac accent",
        Color.FromArgb(29, 24, 40),
        Color.FromArgb(43, 34, 58),
        Color.FromArgb(39, 30, 54),
        Color.FromArgb(56, 43, 76),
        Color.FromArgb(71, 53, 94),
        Color.FromArgb(15, 11, 23),
        Color.FromArgb(245, 239, 252),
        Color.FromArgb(177, 163, 196),
        Color.FromArgb(215, 198, 241),
        Color.FromArgb(207, 154, 255),
        Color.FromArgb(76, 59, 101),
        Color.FromArgb(137, 103, 184),
        Color.FromArgb(51, 39, 72),
        Color.FromArgb(56, 43, 76),
        Color.FromArgb(76, 57, 98),
        Color.FromArgb(190, 168, 217),
        Color.FromArgb(166, 143, 190),
        Color.FromArgb(74, 56, 100),
        Color.FromArgb(213, 73, 112),
        Color.FromArgb(237, 88, 130),
        Color.FromArgb(255, 237, 244),
        Color.FromArgb(91, 61, 133),
        Color.FromArgb(119, 79, 168),
        Color.FromArgb(248, 237, 255),
        Color.FromArgb(69, 49, 91));

    public static readonly ThemePalette Forest = new(
        "Forest",
        "Calm green with a mint accent",
        Color.FromArgb(20, 32, 29),
        Color.FromArgb(28, 47, 41),
        Color.FromArgb(25, 43, 37),
        Color.FromArgb(33, 61, 50),
        Color.FromArgb(42, 78, 63),
        Color.FromArgb(9, 20, 16),
        Color.FromArgb(235, 247, 241),
        Color.FromArgb(149, 177, 164),
        Color.FromArgb(183, 221, 202),
        Color.FromArgb(112, 224, 176),
        Color.FromArgb(49, 79, 67),
        Color.FromArgb(78, 145, 116),
        Color.FromArgb(29, 57, 47),
        Color.FromArgb(33, 61, 50),
        Color.FromArgb(43, 85, 67),
        Color.FromArgb(147, 190, 167),
        Color.FromArgb(123, 161, 143),
        Color.FromArgb(42, 79, 63),
        Color.FromArgb(203, 67, 80),
        Color.FromArgb(229, 79, 93),
        Color.FromArgb(255, 239, 240),
        Color.FromArgb(43, 91, 69),
        Color.FromArgb(58, 119, 88),
        Color.FromArgb(225, 255, 239),
        Color.FromArgb(40, 74, 61));

    public static readonly ThemePalette Ember = new(
        "Ember",
        "Warm charcoal with an amber accent",
        Color.FromArgb(38, 28, 23),
        Color.FromArgb(55, 39, 30),
        Color.FromArgb(49, 34, 27),
        Color.FromArgb(70, 47, 35),
        Color.FromArgb(88, 58, 42),
        Color.FromArgb(22, 13, 9),
        Color.FromArgb(252, 241, 232),
        Color.FromArgb(190, 163, 144),
        Color.FromArgb(230, 199, 176),
        Color.FromArgb(255, 181, 107),
        Color.FromArgb(102, 69, 48),
        Color.FromArgb(174, 115, 72),
        Color.FromArgb(60, 41, 31),
        Color.FromArgb(70, 47, 35),
        Color.FromArgb(92, 60, 43),
        Color.FromArgb(207, 174, 150),
        Color.FromArgb(177, 143, 119),
        Color.FromArgb(89, 59, 42),
        Color.FromArgb(205, 65, 69),
        Color.FromArgb(231, 83, 87),
        Color.FromArgb(255, 239, 239),
        Color.FromArgb(112, 70, 36),
        Color.FromArgb(145, 91, 44),
        Color.FromArgb(255, 245, 225),
        Color.FromArgb(88, 58, 39));

    public static IReadOnlyList<ThemePalette> ColorOptions { get; } =
    [
        Ocean,
        ThemePalette.FromAccent("Sky", "Bright blue", Color.FromArgb(99, 179, 255)),
        ThemePalette.FromAccent("Indigo", "Electric indigo", Color.FromArgb(139, 150, 255)),
        Violet,
        ThemePalette.FromAccent("Magenta", "Vivid magenta", Color.FromArgb(236, 112, 205)),
        ThemePalette.FromAccent("Rose", "Soft rose", Color.FromArgb(247, 143, 179)),
        Ember,
        ThemePalette.FromAccent("Gold", "Golden yellow", Color.FromArgb(240, 210, 100)),
        Forest,
        ThemePalette.FromAccent("Cyan", "Clear cyan", Color.FromArgb(97, 230, 230)),
        ThemePalette.FromAccent("Coral", "Warm coral", Color.FromArgb(255, 139, 122)),
        ThemePalette.FromAccent("Slate", "Soft silver on charcoal", Color.FromArgb(174, 187, 204))
    ];

    public static IReadOnlyList<ThemePalette> All { get; } = [Ocean, Violet, Forest, Ember];
}

internal static class TugleTheme
{
    public static ThemePalette Current { get; set; } = ThemePalettes.Ocean;
}
