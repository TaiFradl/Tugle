using System.Runtime.InteropServices;

namespace Tugle;

// Keep the native resizable-window contract while drawing Tugle's own title bar.
public class SnapWindowForm : Form
{
    private Control? _snapButton;
    private readonly List<CaptionHitTestBridge> _bridges = new();
    protected virtual bool IsImmersiveFullscreen => false;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00CF0000; // Caption, sizing frame, system menu, min/max boxes.
            return cp;
        }
    }

    protected void RegisterSnapButton(Control button)
    {
        _snapButton = button;
        // Child HWNDs must let the top-level window own caption hit testing.
        for (Control? control = button; control is not null && control != this; control = control.Parent)
            _bridges.Add(new CaptionHitTestBridge(control, () => !IsImmersiveFullscreen && button.Visible &&
                button.RectangleToScreen(button.ClientRectangle).Contains(Cursor.Position)));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00A1 && m.WParam == (IntPtr)9 && !IsImmersiveFullscreen)
        {
            // WinForms still considers the managed border style to be None.
            // Let Windows track the native caption press and issue SC_MAXIMIZE/RESTORE.
            DefWndProc(ref m);
            return;
        }
        if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) // WM_NCCALCSIZE
        {
            if (WindowState == FormWindowState.Maximized && !IsImmersiveFullscreen)
            {
                var area = Screen.FromHandle(Handle).WorkingArea;
                Marshal.StructureToPtr(new NativeRect(area), m.LParam, false);
            }
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == 0x0084 && _snapButton is { IsHandleCreated: true, Visible: true } && !IsImmersiveFullscreen)
        {
            long packed = m.LParam.ToInt64();
            var point = new Point((short)packed, (short)(packed >> 16));
            if (_snapButton.RectangleToScreen(_snapButton.ClientRectangle).Contains(point))
            {
                m.Result = (IntPtr)9; // HTMAXBUTTON: native Windows 11 Snap Layouts.
                return;
            }
        }
        base.WndProc(ref m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public NativeRect(Rectangle r) { Left=r.Left; Top=r.Top; Right=r.Right; Bottom=r.Bottom; }
    }

    private sealed class CaptionHitTestBridge : NativeWindow
    {
        private readonly Func<bool> _overButton;
        public CaptionHitTestBridge(Control control, Func<bool> overButton)
        {
            _overButton = overButton;
            control.HandleCreated += (_, _) => AssignHandle(control.Handle);
            control.HandleDestroyed += (_, _) => ReleaseHandle();
            if (control.IsHandleCreated) AssignHandle(control.Handle);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084 && _overButton()) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }
    }
}
