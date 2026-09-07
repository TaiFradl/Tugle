using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Tugle;

internal sealed class ColorPickerDialog : Form
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    private readonly ThemePalette _theme;
    private readonly SaturationValueControl _colorField;
    private readonly HueStripControl _hueStrip;
    private readonly TextBox _hexBox;
    private readonly NumericUpDown _redBox;
    private readonly NumericUpDown _greenBox;
    private readonly NumericUpDown _blueBox;
    private readonly Panel _preview;
    private bool _updating;
    private float _hue;
    private float _saturation;
    private float _value;

    public Color SelectedColor { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var darkMode = 1;
        if (DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PlaceBesideOwner();
    }

    private void PlaceBesideOwner()
    {
        if (Owner is null) return;

        var workArea = Screen.FromControl(Owner).WorkingArea;
        var ownerBounds = Owner.Bounds;
        var isFullscreenOrMaximized = Owner.WindowState == FormWindowState.Maximized ||
                                      (ownerBounds.Width >= workArea.Width - 2 && ownerBounds.Height >= workArea.Height - 2);

        if (isFullscreenOrMaximized)
        {
            Location = new Point(
                workArea.Left + Math.Max(0, (workArea.Width - Width) / 2),
                workArea.Top + Math.Max(0, (workArea.Height - Height) / 2));
            return;
        }

        var rightOfOwner = new Rectangle(ownerBounds.Right + 12, ownerBounds.Top + 28, Width, Height);
        var leftOfOwner = new Rectangle(ownerBounds.Left - Width - 12, ownerBounds.Top + 28, Width, Height);
        var target = workArea.Contains(rightOfOwner) ? rightOfOwner :
            workArea.Contains(leftOfOwner) ? leftOfOwner :
            new Rectangle(
                Math.Clamp(ownerBounds.Right - Width - 18, workArea.Left, workArea.Right - Width),
                Math.Clamp(ownerBounds.Bottom - Height - 18, workArea.Top, workArea.Bottom - Height),
                Width,
                Height);

        Location = target.Location;
    }

    public ColorPickerDialog(Color initial, string title, ThemePalette theme)
    {
        _theme = theme;
        SelectedColor = initial;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        // This dialog uses a number of compact controls.  Let WinForms scale both
        // the chrome and its layout together so a larger display scale cannot put
        // labels on top of their fields.
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(600, 500);
        ClientSize = new Size(620, 500);
        BackColor = _theme.ContentBackground;
        ForeColor = _theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 18),
            ColumnCount = 2,
            RowCount = 3,
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            ColumnCount = 1,
            RowCount = 2
        };
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var headingTitle = new Label
        {
            AutoSize = false,
            Text = title,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = _theme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };
        var headingHint = new Label
        {
            AutoSize = false,
            Text = "Pick a color or enter an exact value",
            ForeColor = _theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft
        };
        heading.Controls.Add(headingTitle, 0, 0);
        heading.Controls.Add(headingHint, 0, 1);
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        var pickerColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(0, 6, 16, 0)
        };
        pickerColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pickerColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        pickerColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _colorField = new SaturationValueControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        _hueStrip = new HueStripControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) };
        var hueLabel = new Label
        {
            AutoSize = false,
            Text = "Hue",
            ForeColor = _theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pickerColumn.Controls.Add(_colorField, 0, 0);
        pickerColumn.Controls.Add(_hueStrip, 0, 1);
        pickerColumn.Controls.Add(hueLabel, 0, 2);
        root.Controls.Add(pickerColumn, 0, 1);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0, 4, 0, 0)
        };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _preview = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = initial,
            Margin = new Padding(0, 0, 0, 10)
        };
        _preview.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(120, 255, 255, 255));
            e.Graphics.DrawRectangle(pen, 0, 0, _preview.Width - 1, _preview.Height - 1);
        };
        details.Controls.Add(_preview, 0, 0);

        details.Controls.Add(FieldLabel("HEX"), 0, 1);
        _hexBox = CreateTextBox();
        _hexBox.Text = ToHex(initial);
        details.Controls.Add(_hexBox, 0, 2);

        details.Controls.Add(FieldLabel("RGB"), 0, 3);
        var rgb = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 3) };
        rgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        rgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        rgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        _redBox = CreateNumericBox();
        _greenBox = CreateNumericBox();
        _blueBox = CreateNumericBox();
        _redBox.Margin = new Padding(0, 0, 4, 0);
        _greenBox.Margin = new Padding(0, 0, 4, 0);
        _blueBox.Margin = Padding.Empty;
        rgb.Controls.Add(_redBox, 0, 0);
        rgb.Controls.Add(_greenBox, 1, 0);
        rgb.Controls.Add(_blueBox, 2, 0);
        details.Controls.Add(rgb, 0, 4);

        details.Controls.Add(new Label { Dock = DockStyle.Fill }, 0, 5);
        root.Controls.Add(details, 1, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var apply = MakeButton("Apply", _theme.Accent, _theme.ProminentText);
        apply.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", _theme.Surface, _theme.Text);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = apply;
        CancelButton = cancel;

        _colorField.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _saturation = _colorField.Saturation;
            _value = _colorField.Value;
            UpdateColorFromHsv();
        };
        _hueStrip.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _hue = _hueStrip.Hue;
            UpdateColorFromHsv();
        };
        _hexBox.TextChanged += (_, _) =>
        {
            if (_updating || !TryParseHex(_hexBox.Text, out var parsed)) return;
            SetColor(parsed);
        };
        _redBox.ValueChanged += (_, _) => UpdateColorFromRgb();
        _greenBox.ValueChanged += (_, _) => UpdateColorFromRgb();
        _blueBox.ValueChanged += (_, _) => UpdateColorFromRgb();
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            if (TryParseHex(_hexBox.Text, out var parsed)) SetColor(parsed);
            e.SuppressKeyPress = true;
        };

        SetColor(initial);
    }

    private Label FieldLabel(string text) => new()
    {
        AutoSize = false,
        Text = text,
        ForeColor = _theme.Muted,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 2)
    };

    private TextBox CreateTextBox() => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = _theme.Surface,
        ForeColor = _theme.Text,
        Font = new Font("Consolas", 10.5f),
        MinimumSize = new Size(0, 30),
        Margin = new Padding(0, 0, 0, 2)
    };

    private NumericUpDown CreateNumericBox() => new()
    {
        Minimum = 0,
        Maximum = 255,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = _theme.Surface,
        ForeColor = _theme.Text,
        TextAlign = HorizontalAlignment.Center,
        Font = new Font("Segoe UI", 9.5f),
        ThousandsSeparator = false,
        AutoSize = false,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(62, 30),
        Margin = Padding.Empty
    };

    private Button MakeButton(string text, Color background, Color foreground) => new()
    {
        AutoSize = true,
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = background,
        ForeColor = foreground,
        FlatAppearance = { BorderColor = _theme.BorderStrong, BorderSize = 1 },
        Padding = new Padding(16, 6, 16, 6),
        Margin = new Padding(8, 0, 0, 0),
        Cursor = Cursors.Hand
    };

    private void SetColor(Color color)
    {
        RgbToHsv(color, out _hue, out _saturation, out _value);
        SelectedColor = Color.FromArgb(color.R, color.G, color.B);
        _updating = true;
        try
        {
            _colorField.Hue = _hue;
            _colorField.Saturation = _saturation;
            _colorField.Value = _value;
            _hueStrip.Hue = _hue;
            _hexBox.Text = ToHex(SelectedColor);
            _redBox.Value = SelectedColor.R;
            _greenBox.Value = SelectedColor.G;
            _blueBox.Value = SelectedColor.B;
            _preview.BackColor = SelectedColor;
        }
        finally
        {
            _updating = false;
        }
        _preview.Invalidate();
        _colorField.Invalidate();
        _hueStrip.Invalidate();
    }

    private void UpdateColorFromHsv() => SetColor(ColorFromHsv(_hue, _saturation, _value));

    private void UpdateColorFromRgb()
    {
        if (_updating) return;
        SetColor(Color.FromArgb((int)_redBox.Value, (int)_greenBox.Value, (int)_blueBox.Value));
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseHex(string text, out Color color)
    {
        color = Color.Empty;
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            return false;
        color = Color.FromArgb((number >> 16) & 255, (number >> 8) & 255, number & 255);
        return true;
    }

    internal static Color ColorFromHsv(float hue, float saturation, float value)
    {
        hue = (hue % 360 + 360) % 360;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0f),
            < 120 => (x, chroma, 0f),
            < 180 => (0f, chroma, x),
            < 240 => (0f, x, chroma),
            < 300 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return Color.FromArgb(
            Math.Clamp((int)Math.Round((r + m) * 255), 0, 255),
            Math.Clamp((int)Math.Round((g + m) * 255), 0, 255),
            Math.Clamp((int)Math.Round((b + m) * 255), 0, 255));
    }

    private static void RgbToHsv(Color color, out float hue, out float saturation, out float value)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        hue = 0;
        if (delta > 0.0001f)
        {
            if (Math.Abs(max - r) < 0.0001f) hue = 60 * ((g - b) / delta % 6);
            else if (Math.Abs(max - g) < 0.0001f) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;
        saturation = max <= 0 ? 0 : delta / max;
        value = max;
    }
}

internal sealed class SaturationValueControl : Control
{
    private float _hue;
    private float _saturation;
    private float _value;

    public event EventHandler? ValueChanged;

    public float Hue { get => _hue; set { _hue = value; Invalidate(); } }
    public float Saturation { get => _saturation; set { _saturation = Math.Clamp(value, 0, 1); Invalidate(); } }
    public float Value { get => _value; set { _value = Math.Clamp(value, 0, 1); Invalidate(); } }

    public SaturationValueControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        MinimumSize = new Size(220, 150);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = ClientRectangle;
        if (rect.Width < 2 || rect.Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var hueBrush = new SolidBrush(ColorPickerDialog.ColorFromHsv(_hue, 1, 1))) e.Graphics.FillRectangle(hueBrush, rect);
        using (var white = new LinearGradientBrush(rect, Color.White, Color.FromArgb(0, 255, 255, 255), 0f)) e.Graphics.FillRectangle(white, rect);
        using (var black = new LinearGradientBrush(rect, Color.FromArgb(0, 0, 0, 0), Color.Black, 90f)) e.Graphics.FillRectangle(black, rect);
        var x = Math.Clamp(_saturation * (rect.Width - 1), 0, rect.Width - 1);
        var y = Math.Clamp((1 - _value) * (rect.Height - 1), 0, rect.Height - 1);
        using var outer = new Pen(Color.FromArgb(210, 0, 0, 0), 3);
        using var inner = new Pen(Color.White, 1);
        e.Graphics.DrawEllipse(outer, x - 7, y - 7, 14, 14);
        e.Graphics.DrawEllipse(inner, x - 6, y - 6, 12, 12);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { Capture = true; UpdateFromPoint(e.Location); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture) UpdateFromPoint(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    private void UpdateFromPoint(Point point)
    {
        Saturation = Math.Clamp(point.X / (float)Math.Max(1, Width - 1), 0, 1);
        Value = Math.Clamp(1 - point.Y / (float)Math.Max(1, Height - 1), 0, 1);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class HueStripControl : Control
{
    private float _hue;

    public event EventHandler? ValueChanged;

    public float Hue { get => _hue; set { _hue = (value % 360 + 360) % 360; Invalidate(); } }

    public HueStripControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        MinimumSize = new Size(220, 18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = ClientRectangle;
        if (rect.Width < 2 || rect.Height < 2) return;
        var colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
        var positions = Enumerable.Range(0, colors.Length).Select(i => i / (float)(colors.Length - 1)).ToArray();
        using var brush = new LinearGradientBrush(rect, Color.Red, Color.Red, 0f)
        {
            InterpolationColors = new ColorBlend { Colors = colors, Positions = positions }
        };
        e.Graphics.FillRectangle(brush, rect);
        var x = Math.Clamp(_hue / 360f * (rect.Width - 1), 0, rect.Width - 1);
        using var outer = new Pen(Color.FromArgb(220, 0, 0, 0), 3);
        using var inner = new Pen(Color.White, 1);
        e.Graphics.DrawLine(outer, x, 0, x, rect.Height - 1);
        e.Graphics.DrawLine(inner, x, 0, x, rect.Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { Capture = true; UpdateFromPoint(e.Location); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture) UpdateFromPoint(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    private void UpdateFromPoint(Point point)
    {
        Hue = Math.Clamp(point.X / (float)Math.Max(1, Width - 1), 0, 1) * 360;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
