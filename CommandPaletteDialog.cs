namespace Tugle;

/// <summary>A lightweight keyboard-first picker for browser commands, tabs, and workspaces.</summary>
internal sealed record CommandPaletteEntry(string Category, string Title, string Detail, Action Action)
{
    public string SearchText => $"{Category} {Title} {Detail}";
    public override string ToString() => $"{Title} · {Category} · {Detail}";
}

internal sealed class CommandPaletteDialog : Form
{
    private readonly ThemePalette _theme;
    private readonly float _guiScale;
    private readonly IReadOnlyList<CommandPaletteEntry> _entries;
    private readonly TextBox _search = new();
    private readonly ListBox _results = new();
    private readonly Label _hint = new();
    private readonly Label _empty = new();
    private readonly List<CommandPaletteEntry> _filtered = [];
    private readonly Font _searchFont;
    private readonly Font _hintFont;
    private readonly Font _categoryFont;
    private readonly Font _titleFont;
    private readonly Font _detailFont;

    public CommandPaletteEntry? SelectedEntry { get; private set; }

    public CommandPaletteDialog(
        IReadOnlyList<CommandPaletteEntry> entries,
        ThemePalette theme,
        float guiScale)
    {
        _entries = entries;
        _theme = theme;
        _guiScale = guiScale;
        _searchFont = CreateFont(13F);
        _hintFont = CreateFont(10.5F);
        _categoryFont = CreateFont(9.5F);
        _titleFont = CreateFont(12.5F);
        _detailFont = CreateFont(10F);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = theme.Chrome;
        ClientSize = new Size(Ui(610), Ui(410));
        Text = "Command palette";
        KeyPreview = true;
        DoubleBuffered = true;

        var surface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.Chrome,
            Padding = new Padding(Ui(16), Ui(14), Ui(16), Ui(12))
        };

        var searchSurface = new Panel
        {
            Dock = DockStyle.Top,
            Height = Ui(46),
            BackColor = theme.Surface,
            Padding = new Padding(Ui(12), Ui(10), Ui(12), Ui(8))
        };
        _search.Dock = DockStyle.Fill;
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = theme.Surface;
        _search.ForeColor = theme.Text;
        _search.Font = _searchFont;
        _search.PlaceholderText = "Type a command, tab, or workspace";
        _search.AccessibleName = "Search commands, tabs, and workspaces";
        _search.Margin = Padding.Empty;
        _search.TextChanged += (_, _) => RefreshResults();
        _search.KeyDown += OnSearchKeyDown;
        searchSurface.Controls.Add(_search);
        searchSurface.MouseDown += (_, _) => _search.Focus();

        _hint.Dock = DockStyle.Bottom;
        _hint.Height = Ui(27);
        _hint.Text = "↑ ↓ select    Enter open    Esc close";
        _hint.Font = _hintFont;
        _hint.ForeColor = theme.Muted;
        _hint.TextAlign = ContentAlignment.BottomLeft;

        _results.Dock = DockStyle.Fill;
        _results.DrawMode = DrawMode.OwnerDrawFixed;
        _results.ItemHeight = Ui(55);
        _results.BorderStyle = BorderStyle.None;
        _results.BackColor = theme.Chrome;
        _results.ForeColor = theme.Text;
        _results.IntegralHeight = false;
        _results.AccessibleName = "Search results";
        _results.DrawItem += DrawResult;
        _results.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _results.IndexFromPoint(e.Location) != ListBox.NoMatches)
                SelectCurrentEntry();
        };
        _results.KeyDown += OnResultsKeyDown;

        var resultsSurface = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, Ui(10), 0, Ui(6))
        };
        _empty.Dock = DockStyle.Fill;
        _empty.Font = _titleFont;
        _empty.ForeColor = theme.Muted;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.Text = "No matches\nTry a tab title, website, or action";
        _empty.Visible = false;
        resultsSurface.Controls.Add(_results);
        resultsSurface.Controls.Add(_empty);
        surface.Controls.Add(resultsSurface);
        surface.Controls.Add(_hint);
        surface.Controls.Add(searchSurface);
        Controls.Add(surface);
        ClientSize = new Size(Ui(610), surface.Padding.Vertical + searchSurface.Height +
            _hint.Height + resultsSurface.Padding.Vertical + _results.ItemHeight * 5);
        Shown += (_, _) =>
        {
            RefreshResults();
            _search.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    private void RefreshResults()
    {
        var query = _search.Text.Trim();
        _filtered.Clear();
        _filtered.AddRange(_entries
            .Select(entry => (Entry: entry, Rank: MatchRank(entry, query)))
            .Where(match => match.Rank < int.MaxValue)
            .OrderBy(match => match.Rank)
            .Select(match => match.Entry));
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            _results.Items.AddRange(_filtered.Cast<object>().ToArray());
            if (_results.Items.Count > 0) _results.SelectedIndex = 0;
            _results.Visible = _filtered.Count > 0;
            _empty.Visible = _filtered.Count == 0;
            _hint.Text = _filtered.Count == 0
                ? "Esc close"
                : $"↑ ↓ select    Enter open    Esc close     ·     {_filtered.Count} results";
        }
        finally
        {
            _results.EndUpdate();
        }
    }

    private static int MatchRank(CommandPaletteEntry entry, string query)
    {
        if (query.Length == 0) return 0;
        if (entry.Title.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (entry.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        return Matches(entry.SearchText, query) ? 4 : int.MaxValue;
    }

    private static bool Matches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var cursor = 0;
        foreach (var character in query)
        {
            cursor = text.IndexOf(character.ToString(), cursor, StringComparison.OrdinalIgnoreCase);
            if (cursor < 0) return false;
            cursor++;
        }
        return true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up)
        {
            MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            SelectCurrentEntry();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.SuppressKeyPress = true;
        }
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SelectCurrentEntry();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.SuppressKeyPress = true;
        }
    }

    private void MoveSelection(int offset)
    {
        if (_results.Items.Count == 0) return;
        var current = _results.SelectedIndex < 0 ? 0 : _results.SelectedIndex;
        _results.SelectedIndex = Math.Clamp(current + offset, 0, _results.Items.Count - 1);
    }

    private void SelectCurrentEntry()
    {
        if (_results.SelectedItem is not CommandPaletteEntry entry) return;
        SelectedEntry = entry;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DrawResult(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _results.Items.Count || _results.Items[e.Index] is not CommandPaletteEntry entry)
            return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? _theme.Selection : _theme.Chrome);
        e.Graphics.FillRectangle(fill, e.Bounds);

        var categoryBounds = new Rectangle(e.Bounds.Left + Ui(9), e.Bounds.Top + Ui(8), Ui(86), Ui(17));
        TextRenderer.DrawText(
            e.Graphics,
            entry.Category.ToUpperInvariant(),
            _categoryFont,
            categoryBounds,
            selected ? _theme.ProminentText : _theme.Section,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        var titleBounds = new Rectangle(e.Bounds.Left + Ui(98), e.Bounds.Top + Ui(5), e.Bounds.Width - Ui(108), Ui(25));
        TextRenderer.DrawText(
            e.Graphics,
            entry.Title,
            _titleFont,
            titleBounds,
            _theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        var detailBounds = new Rectangle(e.Bounds.Left + Ui(98), e.Bounds.Top + Ui(29), e.Bounds.Width - Ui(108), Ui(20));
        TextRenderer.DrawText(
            e.Graphics,
            entry.Detail,
            _detailFont,
            detailBounds,
            selected ? _theme.ProminentText : _theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private Font CreateFont(float size) => new("Segoe UI", size * _guiScale, FontStyle.Regular);

    private int Ui(float value) => Math.Max(1, (int)Math.Round(value * _guiScale * DeviceDpi / 96f));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchFont.Dispose();
            _hintFont.Dispose();
            _categoryFont.Dispose();
            _titleFont.Dispose();
            _detailFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
