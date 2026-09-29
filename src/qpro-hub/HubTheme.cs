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

internal sealed partial class HubForm
{
    private static Label StatusLabel() => new() { AutoSize = true, Font = new Font(UiFontName, 10F, FontStyle.Bold), Margin = new Padding(8, 0, 25, 8) };
    private static Label SetupStatusLabel() => new() { Text = "○ Waiting", AutoSize = true, Font = new Font(UiFontName, 9.5F, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(3, 7, 3, 8) };
    private static void SetStatus(Label label, StatusKind status, string text)
    {
        label.Text = "● " + text;
        label.ForeColor = status switch { StatusKind.Good => Good, StatusKind.Warning => Warning, _ => Bad };
    }
    private static Label SectionTitle(string text) => new() { Text = text, AutoSize = true, Font = new Font(UiFontName, 14F, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(6, 4, 6, 10) };
    private static Label Info(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(395, 0), ForeColor = Muted, Margin = new Padding(6, 0, 6, 14), Tag = "responsive-info" };
    private static TableLayoutPanel Card() => new() { AutoSize = true, Dock = DockStyle.Top, BackColor = Panel, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
    private static DarkButton PrimaryButton(string text) => SecondaryButton(text);
    private static DarkButton SecondaryButton(string text)
    {
        var button = new DarkButton { Text = text, AutoSize = true, BackColor = Raised, ForeColor = Color.White, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 0), Enabled = false };
        return button;
    }
    private static DarkButton ActionButton(string text, EventHandler action) { var button = SecondaryButton(text); button.Enabled = true; button.Margin = new Padding(6, 4, 6, 4); button.Click += action; return button; }
    private static DarkButton SetupButton(string text) { var button = SecondaryButton(text); button.Enabled = true; button.AutoSize = false; button.Height = 42; button.Dock = DockStyle.Bottom; button.Margin = new Padding(3, 8, 3, 3); return button; }

    private static Control SetupStepCard(string number, string title, string description, Label status, params DarkButton[] buttons)
    {
        if (buttons.Length == 0) throw new ArgumentException("A setup step needs an action", nameof(buttons));
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, RowCount = 5, ColumnCount = 1, BackColor = Raised, Padding = new Padding(13), Margin = new Padding(5) };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 54 * buttons.Length));
        card.Controls.Add(new Label { Text = $"STEP {number}", AutoSize = true, ForeColor = Warning, Font = new Font(UiFontName, 8.5F, FontStyle.Bold) }, 0, 0);
        card.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.White, Font = new Font(UiFontName, 11F, FontStyle.Bold), Margin = new Padding(3, 3, 3, 4) }, 0, 1);
        card.Controls.Add(status, 0, 2);
        card.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Muted, Margin = new Padding(3, 0, 3, 5), Tag = "responsive-info" }, 0, 3);
        if (buttons.Length == 1)
            card.Controls.Add(buttons[0], 0, 4);
        else
        {
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = buttons.Length, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 0; index < buttons.Length; index++)
            {
                // Divide the outer action row evenly after its DPI height is set.
                actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / buttons.Length));
                actions.Controls.Add(buttons[index], 0, index);
            }
            card.Controls.Add(actions, 0, 4);
        }
        return card;
    }

    private static Control WorkflowCard(string title, string description, ComboBox queue, Label queueStatus, ComboBox recorded, Button capture, Button train, Button delete)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, BackColor = Raised, Padding = new Padding(10), Margin = new Padding(5) };
        card.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(UiFontName, 10.5F, FontStyle.Bold), ForeColor = Warning });
        card.Controls.Add(new Label { Text = description, AutoSize = false, Dock = DockStyle.Top, Height = 48, ForeColor = Muted, Margin = new Padding(3, 4, 3, 7) });
        card.Controls.Add(new Label { Text = "Recorded datasets waiting to train", AutoSize = true, ForeColor = Color.White, Margin = new Padding(3, 5, 3, 3) });
        card.Controls.Add(queue);
        queueStatus.Margin = new Padding(3, 3, 3, 8);
        card.Controls.Add(queueStatus);
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        capture.AutoSize = false; train.AutoSize = false; capture.Height = 38; train.Height = 38; capture.Dock = DockStyle.Fill; train.Dock = DockStyle.Fill;
        actions.Controls.Add(capture, 0, 0); actions.Controls.Add(train, 0, 1);
        card.Controls.Add(actions);
        card.Controls.Add(new Label { Text = "Recorded datasets (including trained)", AutoSize = true, ForeColor = Color.White, Margin = new Padding(3, 9, 3, 3) });
        card.Controls.Add(recorded);
        delete.AutoSize = false; delete.Height = 38; delete.Dock = DockStyle.Top;
        card.Controls.Add(delete);
        return card;
    }

    private static CheckBox FeatureToggle(string text, bool initial)
    {
        var toggle = new CheckBox
        {
            Text = "  " + text,
            Checked = initial,
            Appearance = Appearance.Button,
            AutoSize = false,
            Height = 35,
            Dock = DockStyle.Top,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            BackColor = Raised,
            Margin = new Padding(0, 2, 0, 4),
        };
        toggle.FlatAppearance.BorderColor = Border;
        toggle.FlatAppearance.CheckedBackColor = Selected;
        toggle.FlatAppearance.MouseDownBackColor = RaisedHover;
        return toggle;
    }

    private static void UpdateToggleStyle(CheckBox toggle)
    {
        toggle.Text = (toggle.Checked ? "  ◆ " : "  ◇ ") + toggle.Text.TrimStart(' ', '◆', '◇');
        toggle.BackColor = toggle.Checked ? Selected : Raised;
        toggle.ForeColor = Color.White;
        toggle.FlatAppearance.BorderColor = toggle.Checked ? Accent : Border;
        toggle.FlatAppearance.BorderSize = toggle.Checked ? 2 : 1;
        toggle.FlatAppearance.MouseOverBackColor = RaisedHover;
    }

    private static void ConfigureDropDown(ComboBox box)
    {
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = Raised;
        box.ForeColor = Color.White;
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.ItemHeight = 25;
        box.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? Selected : Raised);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var item = box.Items[e.Index]?.ToString() ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, item, box.Font, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 7, e.Bounds.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private static void ConfigureModelList(ListBox box)
    {
        box.BackColor = Inset;
        box.ForeColor = Color.White;
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.ItemHeight = 38;
        box.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? Selected : Inset);
            e.Graphics.FillRectangle(fill, e.Bounds);
            using var border = new Pen(selected ? Accent : Border, selected ? 2 : 1);
            e.Graphics.DrawRectangle(border, e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 3, e.Bounds.Height - 3);
            var item = box.Items[e.Index]?.ToString() ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, (selected ? "●  " : "○  ") + item, box.Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private static DarkButton NavigationButton(string text)
    {
        var button = new DarkButton
        {
            Text = "○  " + text,
            AutoSize = false,
            ForeColor = Color.White,
            BackColor = Panel,
            Tag = "workflow-tab",
        };
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2);
        button.Height = 36;
        button.Enabled = true;
        return button;
    }

    private static void StyleNavigationButton(DarkButton button, bool selected)
    {
        button.Text = (selected ? "●  " : "○  ") + button.Text.TrimStart(' ', '●', '○');
        button.BackColor = selected ? Selected : Panel;
        button.Emphasized = selected;
        button.Invalidate();
    }

    private static void StyleRunButton(Button button, bool nextAction)
    {
        button.BackColor = Raised;
        button.ForeColor = button.Enabled ? Color.White : DisabledText;
        button.FlatAppearance.BorderColor = nextAction ? Accent : Border;
        button.FlatAppearance.BorderSize = nextAction ? 2 : 1;
        if (button is DarkButton dark) dark.Emphasized = nextAction;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void EnableDarkTitleBar(IntPtr handle)
    {
        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch { }
    }
}
