using System.Drawing;
using System.Runtime.InteropServices;

namespace Tugle;

/// <summary>A deliberately small dialog for the one bit of tab-group metadata users type.</summary>
internal sealed class WorkspaceNameDialog : Form
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    private readonly TextBox _nameBox;
    private readonly ThemePalette _theme;

    public string WorkspaceName => _nameBox.Text.Trim();

    public WorkspaceNameDialog(string title, string initialName, ThemePalette theme)
    {
        _theme = theme;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(390, 174);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = theme.ContentBackground;
        ForeColor = theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            Padding = new Padding(22, 18, 22, 18),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = theme.Text,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Name the tabs you want to keep together.",
            ForeColor = theme.Muted,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 1);

        _nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = initialName,
            MaxLength = 40,
            BackColor = theme.Surface,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = Padding.Empty
        };
        root.Controls.Add(_nameBox, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0)
        };
        var create = CreateButton(title.StartsWith("Rename", StringComparison.OrdinalIgnoreCase) ? "Save" : "Create", theme.Accent, theme.ProminentText);
        create.DialogResult = DialogResult.OK;
        var cancel = CreateButton("Cancel", theme.Surface, theme.Text);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);

        AcceptButton = create;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var darkMode = 1;
            if (DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));
            var captionColor = _theme.Chrome.R | (_theme.Chrome.G << 8) | (_theme.Chrome.B << 16);
            DwmSetWindowAttribute(Handle, DwmCaptionColor, ref captionColor, sizeof(int));
        }
        catch
        {
            // Native caption styling is optional; the naming dialog must still work on older Windows builds.
        }
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor) => new()
    {
        AutoSize = true,
        MinimumSize = new Size(78, 30),
        Text = text,
        BackColor = backColor,
        ForeColor = foreColor,
        FlatStyle = FlatStyle.Flat,
        UseVisualStyleBackColor = false,
        Margin = new Padding(7, 0, 0, 0),
        Padding = new Padding(9, 0, 9, 0)
    };
}
