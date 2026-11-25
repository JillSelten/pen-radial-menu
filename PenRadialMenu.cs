// =====================================================================
//  Pen Radial Menu  -  een rond snelmenu bij je pen
//  Open het met een pen-knop (vasthouden, één klik of dubbelklik) of met
//  een sneltoets. 8 vakken met acties naar keuze.
//  Gedeelde onderdelen staan in Common.cs, het ronde menu in Radial.cs.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Pen Radial Menu")]
[assembly: AssemblyProduct("Pen Radial Menu")]
[assembly: AssemblyDescription("Pen Radial Menu")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PenMapper
{
    static class AppInfo
    {
        public const string Name = "Pen Radial Menu";
        public const string Id = "PenRadialMenu";
        public const string Version = "1.0";
        public static readonly string Glyph = "" + (char)0xE8A9;
        public static string Tagline { get { return L.T("snelmenu bij je pen", "quick menu at your pen"); } }
    }

    class MainForm : Form
    {
        static readonly int[] DblValues = { 250, 350, 500 };
        static readonly int[] HoldValues = { 400, 600, 900 };
        static readonly PenSignal[] SignalChoices = { PenSignal.None, PenSignal.Eraser, PenSignal.Barrel, PenSignal.Barrel2 };
        const int W = 920, H = 660, SB = 220, PX = 28;
        const int CW = W - SB - 2 * PX;

        readonly PenInput pen = new PenInput();
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, NavItem> navs = new Dictionary<string, NavItem>();
        readonly OsdForm osd = new OsdForm();
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer singleTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer conflictTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trayToggle;
        Label penDot, penLabel, mapDot, mapLabel, liveLabel, learnLabel, conflictLabel, hotkeyStatus;
        ToggleSwitch tglActive, tglAutostart, tglOsd;
        DarkCombo cmbSignal, cmbGesture, cmbDbl, cmbHold;
        FieldBox hotkeyBox;
        FlatButton learnButton;
        bool active, osdOn, penSeen, exiting, toldTray, learning, down, held, pending, openedByHold;
        string lastLive = "";
        RadialMenu radial;
        RadialConfig radialCfg;

        public MainForm(string startPage, EventWaitHandle showEvent, EventWaitHandle exitEvent)
        {
            active = Settings.GetBool("actief", true);
            osdOn = Settings.GetBool("melding", true);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = AppInfo.Name;
            ClientSize = new Size(W, H);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Regular(10);
            DoubleBuffered = true;
            var icon = LoadAppIcon(32);
            if (icon != null) Icon = icon;

            radialCfg = new RadialConfig();
            radialCfg.Load();
            radial = new RadialMenu();
            radial.Picked += it => RunAction(it.Action, it.Param);
            holdTimer.Tick += (s, e) => OnHold();
            singleTimer.Tick += (s, e) => { singleTimer.Stop(); pending = false; };   // geen tweede klik: niets doen
            conflictTimer.Tick += (s, e) => UpdateConflict();

            BuildSidebar();
            BuildMenuPage();
            BuildOpenPage();
            BuildSettingsPage();
            BuildHelpPage();
            BuildTray();

            ResumeLayout(false);
            PerformAutoScale();

            SetActive(active, false);
            UpdateConflict();
            conflictTimer.Start();
            ShowPage(startPage != null && pages.ContainsKey(startPage) ? startPage : "menu");
            pen.Report += OnPenReport;
            StartWaiter(showEvent, ShowMain);
            StartWaiter(exitEvent, ExitApp);
        }

        void StartWaiter(EventWaitHandle ev, Action action)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    ev.WaitOne();
                    try { BeginInvoke(action); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ----------------------------------------------------- venster
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            if (!pen.Register(Handle) && penLabel != null)
            {
                penLabel.Text = L.T("Pen kon niet worden aangemeld", "Could not connect to the pen");
                penDot.ForeColor = Theme.Bad;
            }
            RegisterHotkey();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00FF) pen.Handle(m.LParam);                 // WM_INPUT
            if (m.Msg == 0x0312 && active) OpenMenu(false);             // WM_HOTKEY
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!toldTray)
                {
                    toldTray = true;
                    tray.ShowBalloonTip(3000, AppInfo.Name,
                        L.T("Pen Radial Menu blijft actief. Dubbelklik het icoon rechtsonder om het venster te openen.",
                            "Pen Radial Menu keeps running. Double-click the icon in the system tray to open it."), ToolTipIcon.None);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            UnregisterHotKey(Handle, 1);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Exit();
        }

        public static Icon LoadAppIcon(int size)
        {
            try
            {
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenMapper.app.ico");
                if (s == null) return null;
                return new Icon(s, size, size);
            }
            catch { return null; }
        }

        // ----------------------------------------------------- hulpjes
        static Label AddLabel(Control parent, string text, Font font, Color fg, int x, int y, int w, int h,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            var rp = parent as RoundPanel;
            var l = new Label
            {
                Text = text, Font = font, ForeColor = fg,
                BackColor = rp != null ? rp.Fill : parent.BackColor,
                Location = new Point(x, y), Size = new Size(w, h),
                TextAlign = align, AutoSize = false, UseMnemonic = false
            };
            parent.Controls.Add(l);
            return l;
        }

        static RoundPanel AddCard(Control parent, int x, int y, int w, int h)
        {
            var c = new RoundPanel { Fill = Theme.Card, BackColor = parent.BackColor, Location = new Point(x, y), Size = new Size(w, h) };
            parent.Controls.Add(c);
            return c;
        }

        Panel NewPage(string name, string title, string sub)
        {
            var p = new Panel { Location = new Point(SB, 0), Size = new Size(W - SB, H), BackColor = Theme.Bg, Visible = false };
            Controls.Add(p);
            pages[name] = p;
            AddLabel(p, title, Theme.Bold(19), Theme.Text, PX, 22, CW, 42);
            AddLabel(p, sub, Theme.Regular(10), Theme.Muted, PX, 64, CW, 22);
            return p;
        }

        FlatButton AddButton(Control parent, string text, int x, int y, int w, int h, bool accent, EventHandler click)
        {
            var b = new FlatButton
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Fill = accent ? Theme.Accent : Theme.Field, HoverFill = accent ? Theme.AccentHover : Theme.FieldHover,
                ForeColor = accent ? Color.White : Theme.Text
            };
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        void Row(RoundPanel card, int i, string title, string desc)
        {
            int y = 8 + i * 64;
            if (i > 0) card.Controls.Add(new Panel { BackColor = Theme.Line, Location = new Point(22, y), Size = new Size(CW - 44, 1) });
            AddLabel(card, title, Theme.Semi(10), Theme.Text, 22, y + 10, 360, 22);
            AddLabel(card, desc, Theme.Regular(9), Theme.Muted, 22, y + 32, 380, 20);
        }

        ToggleSwitch RowToggle(RoundPanel card, int i, string title, string desc, bool state)
        {
            Row(card, i, title, desc);
            var t = new ToggleSwitch { Checked = state, Location = new Point(CW - 22 - 48, 8 + i * 64 + 19), Size = new Size(48, 26), BackColor = Theme.Card };
            card.Controls.Add(t);
            return t;
        }

        DarkCombo RowCombo(RoundPanel card, int i, string title, string desc, string[] items, string key, int def, int width = 230)
        {
            Row(card, i, title, desc);
            var c = new DarkCombo { Location = new Point(CW - 22 - width, 8 + i * 64 + 17), Width = width };
            c.Items.AddRange(items);
            if (key != null)
            {
                int v = Settings.GetInt(key, def);
                c.SelectedIndex = (v >= 0 && v < items.Length) ? v : def;
                c.SelectedIndexChanged += (s, e) => Settings.Set(key, c.SelectedIndex.ToString());
            }
            card.Controls.Add(c);
            return c;
        }

        // ----------------------------------------------------- zijbalk
        void BuildSidebar()
        {
            var side = new SidebarPanel { Location = new Point(0, 0), Size = new Size(SB, H), BackColor = Theme.Side };
            Controls.Add(side);
            AddNav(side, "menu", "" + (char)0xE8A9, L.T("Menu", "Menu"), 104);
            AddNav(side, "openen", "" + (char)0xEDC6, L.T("Openen met", "Open with"), 150);
            AddNav(side, "instellingen", "" + (char)0xE713, L.T("Instellingen", "Settings"), 196);
            AddNav(side, "hulp", "" + (char)0xE946, L.T("Hulp en test", "Help and test"), 242);
            penDot = AddLabel(side, "●", Theme.Regular(9), Theme.Off, 22, H - 74, 18, 22);
            penLabel = AddLabel(side, L.T("Pen nog niet gezien", "Pen not seen yet"), Theme.Regular(9), Theme.Muted, 42, H - 74, 170, 22);
            mapDot = AddLabel(side, "●", Theme.Regular(9), Theme.Good, 22, H - 48, 18, 22);
            mapLabel = AddLabel(side, "-", Theme.Regular(9), Theme.Muted, 42, H - 48, 170, 22);
        }

        void AddNav(Control parent, string page, string glyph, string text, int y)
        {
            var n = new NavItem { Glyph = glyph, Text = text, Location = new Point(12, y), Size = new Size(SB - 24, 40) };
            n.Click += (s, e) => ShowPage(page);
            parent.Controls.Add(n);
            navs[page] = n;
        }

        void ShowPage(string name)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navs) kv.Value.Selected = kv.Key == name;
        }

        // ------------------------------------------------- pagina: menu
        void BuildMenuPage()
        {
            var p = NewPage("menu", L.T("Menu", "Menu"),
                L.T("Kies wat er in de 8 vakken van het ronde menu staat.", "Choose what goes in the 8 slices of the round menu."));
            radialCfg.BuildEditor(p, PX, 100, CW);
            AddButton(p, L.T("Menu testen", "Test menu"), PX, 100 + 16 + RadialConfig.Count * 46 + 16, 180, 38, true,
                (s, e) => radial.ShowAt(Cursor.Position, radialCfg.Items, false));
        }

        // ------------------------------------------------- pagina: openen
        void BuildOpenPage()
        {
            var p = NewPage("openen", L.T("Openen met", "Open with"),
                L.T("Kies met welke pen-knop of sneltoets het menu opent.", "Choose which pen button or shortcut opens the menu."));
            var card = AddCard(p, PX, 100, CW, 3 * 64 + 16);

            string[] sigNames = { L.T("Geen pen-knop", "No pen button"), L.T("Gum-signaal (HP: onderste knop)", "Eraser signal (HP: lower button)"),
                                  L.T("Knop-signaal (HP: bovenste knop)", "Button signal (HP: upper button)"), L.T("2e knop-signaal", "2nd button signal") };
            cmbSignal = RowCombo(card, 0, L.T("Pen-knop", "Pen button"), L.T("Of klik op 'Leren' en druk de knop in.", "Or click 'Learn' and press the button."), sigNames, null, 0, 270);
            int sig = Array.IndexOf(SignalChoices, (PenSignal)Settings.GetInt("signaal", (int)PenSignal.Barrel));
            cmbSignal.SelectedIndex = sig < 0 ? 2 : sig;
            cmbSignal.SelectedIndexChanged += (s, e) => Settings.Set("signaal", ((int)SignalChoices[cmbSignal.SelectedIndex]).ToString());
            cmbSignal.Left = CW - 22 - 270 - 102;
            learnButton = AddButton(card, L.T("Leren", "Learn"), CW - 22 - 92, 8 + 17, 92, 32, false, (s, e) => ToggleLearning());

            cmbGesture = RowCombo(card, 1, L.T("Gebaar", "Gesture"), L.T("Bij vasthouden: beweeg naar een vak en laat los.", "With hold: move to a slice and let go."),
                new[] { L.T("Vasthouden", "Hold"), L.T("Eén klik", "Single click"), L.T("Dubbelklik", "Double click") }, "gebaar", 0);

            Row(card, 2, L.T("Sneltoets", "Shortcut"), L.T("Werkt ook zonder pen. Klik in het vak en druk je sneltoets.", "Works without a pen too. Click the box and press your shortcut."));
            hotkeyBox = new FieldBox { Location = new Point(CW - 22 - 230, 8 + 2 * 64 + 16), Size = new Size(230, 32) };
            hotkeyBox.Box.ShortcutMode = true;
            hotkeyBox.Box.Cue = L.T("geen sneltoets", "no shortcut");
            hotkeyBox.Box.SetCombo(Settings.Get("sneltoets", "Ctrl+Alt+M"));
            hotkeyBox.Box.TextChanged += (s, e) => { Settings.Set("sneltoets", hotkeyBox.Box.Combo); RegisterHotkey(); };
            card.Controls.Add(hotkeyBox);

            int y = 100 + 3 * 64 + 16 + 14;
            learnLabel = AddLabel(p, "", Theme.Semi(10), Theme.Accent, PX, y, CW, 22);
            hotkeyStatus = AddLabel(p, "", Theme.Regular(9), Theme.Muted, PX, y + 24, CW, 22);
            conflictLabel = AddLabel(p, "", Theme.Regular(9), Color.FromArgb(0xFF, 0xB5, 0x47), PX, y + 48, CW, 40, ContentAlignment.TopLeft);
        }

        void ToggleLearning()
        {
            learning = !learning;
            learnButton.Text = learning ? L.T("Stop", "Stop") : L.T("Leren", "Learn");
            learnLabel.Text = learning ? L.T("Druk nu de pen-knop in terwijl de pen boven het scherm zweeft...", "Now press the pen button while the pen hovers over the screen...") : "";
        }

        void UpdateConflict()
        {
            bool penMapper = Process.GetProcessesByName("Pen Mapper").Length > 0;
            conflictLabel.Text = penMapper && SelectedSignal() != PenSignal.None
                ? L.T("Let op: Pen Mapper draait ook. Geef deze pen-knop in Pen Mapper geen actie (of kies daar 'Radial menu openen' en gebruik deze app alleen met de sneltoets).",
                      "Note: Pen Mapper is running too. Don't give this pen button an action in Pen Mapper (or use 'Open radial menu' there and use this app with the shortcut only).")
                : "";
        }

        PenSignal SelectedSignal() { return SignalChoices[Math.Max(0, cmbSignal.SelectedIndex)]; }

        // ---------------------------------------------------- sneltoets
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        void RegisterHotkey()
        {
            if (!IsHandleCreated || hotkeyBox == null) return;
            UnregisterHotKey(Handle, 1);
            string combo = Settings.Get("sneltoets", "Ctrl+Alt+M");
            List<Keys> mods;
            Keys key;
            if (combo == "" || !Sender.TryParse(combo, out mods, out key) || key == Keys.None)
            {
                hotkeyStatus.Text = combo == "" ? L.T("Geen sneltoets ingesteld.", "No shortcut set.") : L.T("Kies een sneltoets met een gewone toets erbij.", "Choose a shortcut that includes a normal key.");
                return;
            }
            uint m = 0x4000;   // MOD_NOREPEAT
            foreach (var k in mods)
                m |= k == Keys.Menu ? 1u : k == Keys.ControlKey ? 2u : k == Keys.ShiftKey ? 4u : 8u;
            bool ok = RegisterHotKey(Handle, 1, m, (uint)key);
            hotkeyStatus.Text = ok ? L.T("Sneltoets actief: ", "Shortcut active: ") + Sender.Pretty(combo)
                                   : L.T("Deze sneltoets wordt al door een ander programma gebruikt.", "This shortcut is already used by another program.");
        }

        // ------------------------------------------------ pagina: instellingen
        void BuildSettingsPage()
        {
            var p = NewPage("instellingen", L.T("Instellingen", "Settings"), L.T("Hoe Pen Radial Menu werkt en reageert.", "How Pen Radial Menu behaves."));
            const int rows = 6;
            var card = AddCard(p, PX, 100, CW, rows * 64 + 16);
            tglActive = RowToggle(card, 0, L.T("Actief", "Active"), L.T("Zet het openen met pen-knop en sneltoets aan of uit.", "Turn opening with pen button and shortcut on or off."), active);
            tglActive.CheckedChanged += (s, e) => SetActive(tglActive.Checked, true);
            tglAutostart = RowToggle(card, 1, L.T("Start met Windows", "Start with Windows"),
                L.T("Pen Radial Menu start vanzelf op de achtergrond.", "Pen Radial Menu starts automatically in the background."), Autostart.IsOn);
            tglAutostart.CheckedChanged += (s, e) => Autostart.Set(tglAutostart.Checked);
            tglOsd = RowToggle(card, 2, L.T("Melding bij actie", "Show action label"),
                L.T("Toon een klein label bij je pen als een actie wordt uitgevoerd.", "Show a small label near your pen when an action runs."), osdOn);
            tglOsd.CheckedChanged += (s, e) => { osdOn = tglOsd.Checked; Settings.SetBool("melding", osdOn); };
            cmbDbl = RowCombo(card, 3, L.T("Dubbelklik-snelheid", "Double-click speed"), L.T("Hoe snel je twee keer moet drukken.", "How fast you need to press twice."),
                new[] { L.T("Snel (250 ms)", "Fast (250 ms)"), L.T("Normaal (350 ms)", "Normal (350 ms)"), L.T("Langzaam (500 ms)", "Slow (500 ms)") }, "dubbelklik", 1, 210);
            cmbHold = RowCombo(card, 4, L.T("Vasthouden na", "Hold after"), L.T("Hoe lang je de knop ingedrukt houdt.", "How long you keep the button pressed."),
                new[] { L.T("Kort (300 ms)", "Short (300 ms)"), L.T("Normaal (400 ms)", "Normal (400 ms)"), L.T("Lang (600 ms)", "Long (600 ms)") }, "vasthouden", 1, 210);
            string[] langIds = { "auto", "nl", "en" };
            var cmbLang = RowCombo(card, 5, L.T("Taal", "Language"), L.T("Pen Radial Menu start opnieuw als je dit wijzigt.", "Pen Radial Menu restarts when you change this."),
                new[] { L.T("Automatisch (Windows)", "Automatic (Windows)"), "Nederlands", "English" }, null, 0, 210);
            int li = Array.IndexOf(langIds, Settings.Get("taal", "en"));
            cmbLang.SelectedIndex = li < 0 ? 0 : li;
            cmbLang.SelectedIndexChanged += (s, e) =>
            {
                Settings.Set("taal", langIds[cmbLang.SelectedIndex]);
                try { Process.Start(Application.ExecutablePath, "/restart /page:instellingen"); } catch { }
                ExitApp();
            };
        }

        // ---------------------------------------------------- pagina: hulp
        void BuildHelpPage()
        {
            var p = NewPage("hulp", L.T("Hulp en test", "Help and test"),
                L.T("Controleer of je pen gezien wordt en lees hoe alles werkt.", "Check that your pen is detected and learn how everything works."));
            var live = AddCard(p, PX, 100, CW, 120);
            AddLabel(live, L.T("Live pen-signaal", "Live pen signal"), Theme.Semi(11), Theme.Text, 22, 14, 300, 26);
            liveLabel = AddLabel(live, L.T("Beweeg de pen vlak boven het scherm...", "Move the pen just above the screen..."), Theme.Semi(11), Theme.Accent, 22, 46, CW - 44, 28);
            AddLabel(live, L.T("Druk een knop in: je ziet hier welk signaal de pen doorgeeft.", "Press a button: you will see which signal the pen sends."), Theme.Regular(9), Theme.Muted, 22, 80, CW - 44, 22);
            var how = AddCard(p, PX, 236, CW, 260);
            AddLabel(how, L.T("Zo gebruik je het", "How to use it"), Theme.Semi(11), Theme.Text, 22, 14, 300, 26);
            AddLabel(how, L.T(
                "•  Houd de pen-knop vast: het menu opent bij je pen. Beweeg naar een vak en laat los.\n\n" +
                "•  Of tik met de pen op een vak. Esc, of tikken buiten het menu, sluit het.\n\n" +
                "•  Kies op 'Openen met' welke pen-knop of sneltoets het menu opent.\n\n" +
                "•  Gebruik je ook Pen Mapper? Geef daar deze knop geen actie, of gebruik daar 'Radial menu openen'.",
                "•  Hold the pen button: the menu opens at your pen. Move to a slice and let go.\n\n" +
                "•  Or tap a slice with the pen. Esc, or tapping outside the menu, closes it.\n\n" +
                "•  Choose on 'Open with' which pen button or shortcut opens the menu.\n\n" +
                "•  Also using Pen Mapper? Don't give this button an action there, or use 'Open radial menu' there."),
                Theme.Regular(10), Theme.Muted, 22, 48, CW - 44, 200, ContentAlignment.TopLeft);
            AddLabel(p, AppInfo.Name + " " + AppInfo.Version + "   ·   " + L.T("werkt met Windows Ink-pennen (MPP, USI, Wacom AES)", "works with Windows Ink pens (MPP, USI, Wacom AES)"),
                Theme.Regular(9), Theme.Muted, PX, 520, CW, 22);
        }

        // ------------------------------------------------------- systeemvak
        void BuildTray()
        {
            trayMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Regular(10), ShowImageMargin = false, ShowCheckMargin = true };
            trayMenu.Items.Add(L.T("Pen Radial Menu openen", "Open Pen Radial Menu"), null, (s, e) => ShowMain());
            trayMenu.Items.Add(L.T("Menu tonen", "Show menu"), null, (s, e) => OpenMenu(false));
            trayToggle = new ToolStripMenuItem(L.T("Actief", "Active"), null, (s, e) => SetActive(!active, true));
            trayMenu.Items.Add(trayToggle);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(L.T("Afsluiten", "Exit"), null, (s, e) => ExitApp());
            foreach (ToolStripItem it in trayMenu.Items) it.ForeColor = Theme.Text;
            tray = new NotifyIcon { Icon = LoadAppIcon(16) ?? SystemIcons.Application, Text = AppInfo.Name, ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += (s, e) => ShowMain();
        }

        void SetActive(bool on, bool save)
        {
            active = on;
            if (tglActive != null && tglActive.Checked != on) tglActive.Checked = on;
            mapDot.ForeColor = on ? Theme.Good : Theme.Off;
            mapLabel.Text = on ? L.T("Actief", "Active") : L.T("Gepauzeerd", "Paused");
            if (trayToggle != null) trayToggle.Checked = on;
            if (save) Settings.SetBool("actief", on);
        }

        // ------------------------------------------------ pen-signalen
        void OnPenReport(HashSet<int> set)
        {
            if (!penSeen)
            {
                penSeen = true;
                penDot.ForeColor = Theme.Good;
                penLabel.Text = L.T("Pen verbonden", "Pen connected");
            }
            UpdateLive(set);

            if (learning)
            {
                var sig = PenInput.First(set);
                if (sig == PenSignal.None) return;
                cmbSignal.SelectedIndex = Array.IndexOf(SignalChoices, sig);
                ToggleLearning();
                down = true;   // de knop is nu ingedrukt; niet meteen het menu openen
                osd.ShowMessage(L.T("Pen-knop herkend", "Pen button recognized"));
                return;
            }

            var target = SelectedSignal();
            if (target == PenSignal.None) return;
            bool pressed = PenInput.Has(set, target);
            if (pressed == down) return;
            down = pressed;
            if (!active) return;
            if (pressed) OnPress(); else OnRelease();
        }

        void UpdateLive(HashSet<int> set)
        {
            var parts = new List<string>();
            if (set.Contains(0x32)) parts.Add(L.T("in bereik", "in range"));
            if (set.Contains(0x42)) parts.Add(L.T("punt raakt scherm", "tip touches screen"));
            if (set.Contains(0x44)) parts.Add(PenInput.SignalName(PenSignal.Barrel));
            if (set.Contains(0x5A)) parts.Add(PenInput.SignalName(PenSignal.Barrel2));
            if (set.Contains(0x45) || set.Contains(0x3C)) parts.Add(PenInput.SignalName(PenSignal.Eraser));
            string s = parts.Count == 0 ? L.T("pen buiten bereik", "pen out of range") : string.Join("   •   ", parts.ToArray());
            if (s != lastLive) { lastLive = s; liveLabel.Text = s; }
        }

        // ----------------------------------------------------- gebaren
        int Gesture { get { return Math.Max(0, cmbGesture.SelectedIndex); } }   // 0 vasthouden, 1 één klik, 2 dubbelklik

        void OnPress()
        {
            held = false;
            if (Gesture == 0)
            {
                holdTimer.Interval = new[] { 300, 400, 600 }[Math.Max(0, cmbHold.SelectedIndex)];
                holdTimer.Start();
            }
        }

        void OnHold()
        {
            holdTimer.Stop();
            held = true;
            OpenMenu(true);
        }

        void OnRelease()
        {
            holdTimer.Stop();
            if (Gesture == 0)
            {
                if (held && openedByHold && radial.IsOpen) { openedByHold = false; radial.CommitHover(); }
                return;
            }
            if (Gesture == 1) { OpenMenu(false); return; }
            if (pending) { pending = false; singleTimer.Stop(); OpenMenu(false); }
            else
            {
                pending = true;
                singleTimer.Interval = DblValues[Math.Max(0, cmbDbl.SelectedIndex)];
                singleTimer.Start();
            }
        }

        void OpenMenu(bool byHold)
        {
            openedByHold = byHold;
            radial.ShowAt(Cursor.Position, radialCfg.Items, byHold);
        }

        // ------------------------------------------------- acties uitvoeren
        void RunAction(ActionDef a, string param)
        {
            try
            {
                switch (a.Kind)
                {
                    case ActionKind.Keys: Sender.Combo(a.Keys); break;
                    case ActionKind.Shortcut: if (param == "") return; Sender.Combo(param); break;
                    case ActionKind.Text: if (param == "") return; Sender.Text(param); break;
                    case ActionKind.Run:
                        if (param == "") return;
                        Process.Start(new ProcessStartInfo(param) { UseShellExecute = true });
                        break;
                    case ActionKind.Special:
                        switch (a.Id)
                        {
                            case "rclick": Sender.MouseClick(0x0008, 0x0010, 1); break;
                            case "mclick": Sender.MouseClick(0x0020, 0x0040, 1); break;
                            case "dblclick": Sender.MouseClick(0x0002, 0x0004, 2); break;
                            case "wheelup": Sender.Wheel(360); break;
                            case "wheeldown": Sender.Wheel(-360); break;
                            case "touchkb": TouchKeyboard.Toggle(); break;
                            case "lock": Native.LockWorkStation(); break;
                            case "menu": case "radial": OpenMenu(false); return;
                            case "pause":
                                SetActive(!active, true);
                                if (osdOn) osd.ShowMessage(active ? L.T("Hervat", "Resumed") : L.T("Gepauzeerd", "Paused"));
                                return;
                        }
                        break;
                }
                if (osdOn) osd.ShowMessage(a.Name);
            }
            catch (Exception ex)
            {
                if (osdOn) osd.ShowMessage(L.T("Mislukt: ", "Failed: ") + ex.Message);
            }
        }
    }
}
