// =====================================================================
//  Radial menu: een rond menu met 8 vakken dat bij de cursor/pen opent.
//  - Beweeg naar een vak en tik/klik, of
//  - houd een knop vast, beweeg naar een vak en laat los (CommitHover).
//  Esc, of klikken buiten het menu, sluit het.
//  Gedeeld door Pen Mapper, Mouse Mapper en Pen Radial Menu.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PenMapper
{
    class RadialItem
    {
        public ActionDef Action;
        public string Param = "";
        public bool IsEmpty { get { return Action == null || Action.Kind == ActionKind.None; } }
    }

    // ---------------------------------------------------------- instellingen
    class RadialConfig
    {
        public const int Count = 8;
        public readonly RadialItem[] Items = new RadialItem[Count];
        static readonly string[] Defaults = { "copy", "paste", "undo", "redo", "snip", "osk", "taskview", "save" };

        readonly DarkCombo[] combos = new DarkCombo[Count];
        readonly FieldBox[] fields = new FieldBox[Count];
        readonly FlatButton[] browses = new FlatButton[Count];
        bool loading;
        int width;

        public static string PositionName(int i)
        {
            string[] nl = { "Boven", "Rechtsboven", "Rechts", "Rechtsonder", "Onder", "Linksonder", "Links", "Linksboven" };
            string[] en = { "Top", "Top right", "Right", "Bottom right", "Bottom", "Bottom left", "Left", "Top left" };
            return L.T(nl[i], en[i]);
        }

        public void Load()
        {
            for (int i = 0; i < Count; i++)
            {
                var it = new RadialItem();
                it.Action = Actions.ById(Settings.Get("radial." + i, Defaults[i])) ?? Actions.All[0];
                it.Param = Settings.Get("radial." + i + ".waarde", "");
                Items[i] = it;
            }
        }

        // Bouwt een kaart met 8 regels: positie, actie, (eventueel) waarde
        public RoundPanel BuildEditor(Control parent, int x, int y, int w)
        {
            width = w;
            var card = new RoundPanel { Fill = Theme.Card, BackColor = parent.BackColor, Location = new Point(x, y), Size = new Size(w, 16 + Count * 46) };
            parent.Controls.Add(card);
            loading = true;
            for (int i = 0; i < Count; i++)
            {
                int ry = 12 + i * 46;
                card.Controls.Add(new Label
                {
                    Text = PositionName(i), Font = Theme.Regular(10), ForeColor = Theme.Muted, BackColor = Theme.Card,
                    Location = new Point(22, ry), Size = new Size(110, 32), TextAlign = ContentAlignment.MiddleLeft
                });

                var combo = new DarkCombo { Location = new Point(136, ry + 1), Width = 300 };
                foreach (var a in Actions.All) if (a.Id != "radial") combo.Items.Add(a);
                card.Controls.Add(combo);

                var field = new FieldBox { Location = new Point(448, ry), Size = new Size(w - 448 - 22, 32), Visible = false };
                card.Controls.Add(field);

                var browse = new FlatButton { Text = "...", Fill = Theme.Field, HoverFill = Theme.FieldHover, Location = new Point(w - 22 - 34, ry), Size = new Size(34, 32), Visible = false };
                card.Controls.Add(browse);

                combos[i] = combo; fields[i] = field; browses[i] = browse;
                int idx = i;
                combo.SelectedItem = Items[i].Action;
                UpdateParamUI(idx);
                if (Items[i].Action.Kind == ActionKind.Shortcut) field.Box.SetCombo(Items[i].Param);
                else field.Box.Text = Items[i].Param;

                combo.SelectedIndexChanged += (s, e) =>
                {
                    if (!loading) { fields[idx].Box.Combo = ""; fields[idx].Box.Text = ""; }
                    UpdateParamUI(idx);
                    Save(idx);
                };
                field.Box.TextChanged += (s, e) => Save(idx);
                browse.Click += (s, e) =>
                {
                    using (var dlg = new OpenFileDialog { Title = L.T("Kies een programma", "Choose a program"), Filter = L.T("Programma's", "Programs") + "|*.exe;*.lnk;*.bat;*.cmd|" + L.T("Alle bestanden", "All files") + "|*.*" })
                        if (dlg.ShowDialog() == DialogResult.OK) fields[idx].Box.Text = dlg.FileName;
                };
            }
            loading = false;
            return card;
        }

        void UpdateParamUI(int i)
        {
            var a = combos[i].SelectedItem as ActionDef;
            bool needs = a != null && a.NeedsParam;
            fields[i].Visible = needs;
            browses[i].Visible = a != null && a.Kind == ActionKind.Run;
            fields[i].Width = browses[i].Visible ? width - 448 - 22 - 40 : width - 448 - 22;
            if (!needs) return;
            fields[i].Box.ShortcutMode = a.Kind == ActionKind.Shortcut;
            fields[i].Box.Cue = a.Kind == ActionKind.Shortcut ? L.T("klik en druk een sneltoets", "click and press a shortcut")
                : a.Kind == ActionKind.Text ? L.T("tekst om te typen", "text to type") : L.T("programma of https://...", "program or https://...");
        }

        void Save(int i)
        {
            if (loading) return;
            var a = combos[i].SelectedItem as ActionDef ?? Actions.All[0];
            string param = a.Kind == ActionKind.Shortcut ? fields[i].Box.Combo : fields[i].Box.Text.Trim();
            if (!a.NeedsParam) param = "";
            Items[i].Action = a;
            Items[i].Param = param;
            Settings.Set("radial." + i, a.Id);
            Settings.Set("radial." + i + ".waarde", param);
        }
    }

    // ---------------------------------------------------------- het menu
    class RadialMenu : Form
    {
        public event Action<RadialItem> Picked;

        const int Slices = 8;
        RadialItem[] items = new RadialItem[0];
        int hover = -1;
        bool holdMode, mouseWasDown;
        Point center;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        readonly float s = Theme.S;
        readonly int rOuter, rInner, size;

        public RadialMenu()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            rOuter = (int)(150 * s);
            rInner = (int)(46 * s);
            size = 2 * rOuter + (int)(24 * s);
            Size = new Size(size, size);
            timer.Tick += (sender, e) => Track();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000;   // LAYERED | TOOLWINDOW | TOPMOST | NOACTIVATE
                return cp;
            }
        }

        public bool IsOpen { get { return Visible; } }

        public void ShowAt(Point p, RadialItem[] menuItems, bool hold)
        {
            items = menuItems;
            holdMode = hold;
            hover = -1;
            var area = Screen.FromPoint(p).WorkingArea;
            int half = size / 2;
            center = new Point(Math.Max(area.Left + half, Math.Min(area.Right - half, p.X)),
                               Math.Max(area.Top + half, Math.Min(area.Bottom - half, p.Y)));
            Location = new Point(center.X - half, center.Y - half);
            mouseWasDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (!Visible) Show();
            Render();
            timer.Start();
        }

        public void Close2()
        {
            timer.Stop();
            Hide();
        }

        // Bij loslaten van de knop (vasthoud-modus): kies het vak onder de cursor.
        // Geeft false terug als de cursor in het midden staat (menu blijft open om te tikken).
        public bool CommitHover()
        {
            holdMode = false;
            if (hover >= 0) { Pick(hover); return true; }
            return false;
        }

        void Pick(int i)
        {
            Close2();
            if (i < 0 || i >= items.Length || items[i] == null || items[i].IsEmpty) return;
            if (Picked != null) Picked(items[i]);
        }

        int SliceAt(Point screenPt, bool requireInside)
        {
            int dx = screenPt.X - center.X, dy = screenPt.Y - center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < rInner) return -1;
            if (requireInside && dist > rOuter) return -2;
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;          // 0 = rechts, 90 = onder
            double fromTop = (ang + 90 + 360 + 22.5) % 360;
            int idx = (int)(fromTop / 45) % Slices;
            return idx;
        }

        void Track()
        {
            if (!Visible) { timer.Stop(); return; }
            var p = Cursor.Position;
            int h = SliceAt(p, false);
            if (h >= 0 && (h >= items.Length || items[h] == null || items[h].IsEmpty)) h = -1;
            if (h != hover) { hover = h; Render(); }

            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { Close2(); return; }   // Esc

            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (down && !mouseWasDown && SliceAt(p, true) == -2) Close2();       // klik buiten het menu
            mouseWasDown = down;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var p = PointToScreen(e.Location);
            int i = SliceAt(p, true);
            if (i >= 0) Pick(i);
            else if (i == -1) Close2();   // midden = sluiten
        }

        static string ShortName(RadialItem it)
        {
            if (it == null || it.IsEmpty) return "";
            string n = it.Action.Name;
            int k = n.IndexOf(" (");
            if (k > 0) n = n.Substring(0, k);
            n = n.TrimEnd('.', ' ');
            if (it.Action.Kind == ActionKind.Shortcut && it.Param != "") n = Sender.Pretty(it.Param);
            return n;
        }

        void Render()
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    float c = size / 2f;
                    var outer = new RectangleF(c - rOuter, c - rOuter, 2 * rOuter, 2 * rOuter);
                    var inner = new RectangleF(c - rInner, c - rInner, 2 * rInner, 2 * rInner);

                    // schaduw
                    for (int k = 6; k >= 1; k--)
                        using (var b = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
                            g.FillEllipse(b, RectangleF.Inflate(outer, k * 2 * s, k * 2 * s));

                    using (var b = new SolidBrush(Color.FromArgb(246, Theme.Card)))
                        g.FillEllipse(b, outer);

                    // vakken
                    for (int i = 0; i < Slices; i++)
                    {
                        float start = -90 - 22.5f + i * 45;
                        if (i == hover)
                        {
                            using (var path = new GraphicsPath())
                            {
                                path.AddArc(outer, start, 45);
                                path.AddArc(inner, start + 45, -45);
                                path.CloseFigure();
                                using (var b = new SolidBrush(Theme.AccentDim)) g.FillPath(b, path);
                            }
                        }
                        // scheidingslijn
                        double rad = start * Math.PI / 180;
                        using (var pen = new Pen(Theme.Line, 1.2f * s))
                            g.DrawLine(pen, c + (float)Math.Cos(rad) * rInner, c + (float)Math.Sin(rad) * rInner,
                                            c + (float)Math.Cos(rad) * rOuter, c + (float)Math.Sin(rad) * rOuter);

                        // label
                        var it = i < items.Length ? items[i] : null;
                        string text = ShortName(it);
                        double mid = (start + 22.5) * Math.PI / 180;
                        float rMid = (rInner + rOuter) / 2f + 4 * s;
                        float lx = c + (float)Math.Cos(mid) * rMid, ly = c + (float)Math.Sin(mid) * rMid;
                        var box = new RectangleF(lx - 46 * s, ly - 22 * s, 92 * s, 44 * s);
                        using (var f = new Font("Segoe UI Semibold", 11.5f * s, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                        using (var b = new SolidBrush(text == "" ? Theme.Off : (i == hover ? Color.White : Theme.Text)))
                            g.DrawString(text == "" ? "—" : text, f, b, box, sf);
                    }

                    // rand en midden
                    using (var pen = new Pen(Color.FromArgb(60, Theme.Accent), 1.5f * s)) g.DrawEllipse(pen, outer);
                    using (var b = new SolidBrush(Theme.Field)) g.FillEllipse(b, inner);
                    using (var pen = new Pen(Theme.Line, 1.2f * s)) g.DrawEllipse(pen, inner);
                    using (var f = new Font(Theme.IconFont, 16f * s, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var b = new SolidBrush(Theme.Muted))
                        g.DrawString(hover < 0 ? "" : "", f, b, inner, sf);   // kruisje / vinkje
                }
                SetBitmap(bmp);
            }
        }

        void SetBitmap(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBmp);
            try
            {
                var sz = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT { x = 0, y = 0 };
                var dst = new POINT { x = Left, y = Top };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    }
}
