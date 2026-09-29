using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QproFaceTracking.Hub;

internal sealed class DarkProgressBar : Control
{
    private int _value;
    private bool _isIndeterminate;
    private int _animationOffset;

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    [DefaultValue(false)]
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set { _isIndeterminate = value; _animationOffset = 0; Invalidate(); }
    }

    public void AdvanceAnimation()
    {
        if (!_isIndeterminate) return;
        _animationOffset = (_animationOffset + 8) % Math.Max(1, Width + Math.Max(36, Width / 4));
        Invalidate();
    }

    public DarkProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        MinimumSize = new Size(120, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(HubForm.Inset);
        var inner = new Rectangle(2, 2, Math.Max(0, Width - 4), Math.Max(0, Height - 4));
        if (_isIndeterminate && inner.Width > 0)
        {
            var blockWidth = Math.Max(36, inner.Width / 4);
            var x = inner.X + _animationOffset - blockWidth;
            using var fill = new LinearGradientBrush(
                new Rectangle(x, inner.Y, blockWidth, Math.Max(1, inner.Height)),
                Color.FromArgb(120, HubForm.Accent),
                HubForm.Accent,
                LinearGradientMode.Horizontal);
            e.Graphics.SetClip(inner);
            e.Graphics.FillRectangle(fill, x, inner.Y, blockWidth, inner.Height);
            e.Graphics.ResetClip();
        }
        else if (_value > 0)
        {
            var fillWidth = (int)Math.Round(inner.Width * (_value / 100.0));
            using var fill = new SolidBrush(_value >= 100 ? HubForm.Good : HubForm.Accent);
            e.Graphics.FillRectangle(fill, inner.X, inner.Y, fillWidth, inner.Height);
        }
        using var border = new Pen(HubForm.Border, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        base.OnPaint(e);
    }
}

internal sealed class DarkButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _emphasized;
    private Color _outlineColor = Color.Empty;
    private int _outlineWidth = 1;
    [DefaultValue(false)]
    public bool Emphasized { get => _emphasized; set { _emphasized = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
    public Color OutlineColor { get => _outlineColor; set { _outlineColor = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
    public int OutlineWidth { get => _outlineWidth; set { _outlineWidth = Math.Clamp(value, 1, 4); Invalidate(); } }

    public DarkButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_pressed ? HubForm.Accent : _hovered && Enabled ? HubForm.RaisedHover : BackColor);
        var borderColor = OutlineColor.IsEmpty ? (Emphasized ? HubForm.Accent : HubForm.Border) : OutlineColor;
        var borderWidth = Emphasized ? Math.Max(2, OutlineWidth) : OutlineWidth;
        using var border = new Pen(borderColor, borderWidth);
        var inset = borderWidth > 1 ? 1 : 0;
        e.Graphics.DrawRectangle(border, inset, inset, Width - (inset * 2 + 1), Height - (inset * 2 + 1));
        var textColor = !Enabled ? HubForm.DisabledText : _pressed ? HubForm.Background : Color.White;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        // User-painted buttons must draw their own keyboard focus cue.
        if (Focused && ShowFocusCues && Width > 12 && Height > 12)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), textColor, BackColor);
    }
}

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _input;
    public string Value => _input.Text;

    public TextPromptDialog(string title, string prompt, string initial, string fontName, Color background, Color panel, Color raised, Color border, Color accent)
    {
        Text = title;
        Size = new Size(520, 235);
        MinimumSize = new Size(440, 220);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = background;
        ForeColor = Color.White;
        Font = new Font(fontName, 10F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(20), BackColor = panel };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(450, 0), ForeColor = Color.White, Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        _input = new TextBox { Text = initial, Dock = DockStyle.Top, BackColor = raised, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MaxLength = 80 };
        layout.Controls.Add(_input, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 16, 0, 0) };
        var save = new DarkButton { Text = "Save name", DialogResult = DialogResult.OK, AutoSize = true, Enabled = true, Emphasized = true, BackColor = raised, ForeColor = Color.White, Padding = new Padding(14, 7, 14, 7) };
        var cancel = new DarkButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Enabled = true, BackColor = raised, ForeColor = Color.White, Padding = new Padding(14, 7, 14, 7), Margin = new Padding(8, 0, 0, 0) };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { _input.SelectAll(); _input.Focus(); };
    }
}

internal sealed class DarkSlider : Control
{
    private int _value;
    [DefaultValue(0)]
    public int Minimum { get; set; }
    [DefaultValue(100)]
    public int Maximum { get; set; } = 100;
    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, Minimum, Maximum); Invalidate(); }
    }

    public DarkSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var left = 8;
        var right = Math.Max(left + 1, Width - 8);
        var center = Height / 2;
        var range = Math.Max(1, Maximum - Minimum);
        var ratio = (Value - Minimum) / (float)range;
        var thumbX = left + (int)Math.Round((right - left) * ratio);
        using var track = new Pen(HubForm.Border, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(HubForm.Accent, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, left, center, right, center);
        e.Graphics.DrawLine(active, left, center, thumbX, center);
        using var thumb = new SolidBrush(Enabled ? Color.White : HubForm.DisabledText);
        e.Graphics.FillEllipse(thumb, thumbX - 7, center - 7, 14, 14);
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFromX(e.X); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Down) Value--;
        if (e.KeyCode is Keys.Right or Keys.Up) Value++;
    }
    private void SetFromX(int x)
    {
        if (!Enabled) return;
        var ratio = Math.Clamp((x - 8f) / Math.Max(1, Width - 16), 0f, 1f);
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
    }
}
