// =====================================================================
//  Pen Radial Menu Setup - installeert Pen Radial Menu voor de huidige gebruiker.
//  Geen beheerdersrechten nodig. Pen Radial Menu.exe zit ingebakken als resource.
// =====================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pen Radial Menu Setup")]
[assembly: AssemblyProduct("Pen Radial Menu")]
[assembly: AssemblyDescription("Pen Radial Menu Setup")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PenMapperSetup
{
    static class Program
    {
        public const string AppName = "Pen Radial Menu";
        public const string Version = "1.0";
        public static bool En = true;   // installatieprogramma standaard in het Engels
        public static string T(string nl, string en) { return En ? en : nl; }

        public static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Pen Radial Menu");
        public static readonly string ExePath = Path.Combine(InstallDir, "Pen Radial Menu.exe");

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool silent = false;
            foreach (var a in args) if (a == "/silent" || a == "/S") silent = true;
            if (silent)
            {
                Installer.Install(true, false, false);
                return;
            }
            Application.Run(new SetupForm());
        }
    }

    // ---------------------------------------------------------- installatie
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PenRadialMenu";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsInstalled { get { return File.Exists(Program.ExePath); } }

        public static void Install(bool desktopShortcut, bool autostart, bool launch)
        {
            StopRunning();
            Directory.CreateDirectory(Program.InstallDir);
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenMapper.payload.exe"))
            {
                if (src == null) throw new InvalidOperationException("Payload ontbreekt.");
                string tmp = Program.ExePath + ".new";
                using (var dst = File.Create(tmp)) src.CopyTo(dst);
                if (File.Exists(Program.ExePath)) File.Delete(Program.ExePath);
                File.Move(tmp, Program.ExePath);
            }

            string desc = Program.T("Een rond snelmenu bij je pen", "A round quick menu at your pen");
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pen Radial Menu.lnk"), desc);
            string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pen Radial Menu.lnk");
            if (desktopShortcut) CreateShortcut(desk, desc);
            else if (File.Exists(desk)) File.Delete(desk);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", Program.AppName);
                k.SetValue("DisplayIcon", Program.ExePath + ",0");
                k.SetValue("InstallLocation", Program.InstallDir);
                k.SetValue("UninstallString", "\"" + Program.ExePath + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(Program.ExePath).Length / 1024), RegistryValueKind.DWord);
            }

            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue("PenRadialMenu", "\"" + Program.ExePath + "\" /tray");
                else if (k.GetValue("PenRadialMenu") != null) k.DeleteValue("PenRadialMenu");
            }

            if (launch) Process.Start(new ProcessStartInfo(Program.ExePath) { UseShellExecute = true, WorkingDirectory = Program.InstallDir });
        }

        static void StopRunning()
        {
            try { EventWaitHandle.OpenExisting("PenRadialMenu_Exit_7F3A").Set(); } catch { }
            for (int i = 0; i < 30 && Process.GetProcessesByName("Pen Radial Menu").Length > 0; i++) Thread.Sleep(100);
            foreach (var p in Process.GetProcessesByName("Pen Radial Menu")) try { p.Kill(); p.WaitForExit(2000); } catch { }
        }

        static void CreateShortcut(string path, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { Program.ExePath });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Program.InstallDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { Program.ExePath + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.ReleaseComObject(sc);
            Marshal.ReleaseComObject(shell);
        }
    }

    // ---------------------------------------------------------- venster
    class SetupForm : Form
    {
        static readonly Color Bg = ColorTranslator.FromHtml("#16161B");
        static readonly Color Card = ColorTranslator.FromHtml("#202028");
        static readonly Color Text1 = ColorTranslator.FromHtml("#F2F2F5");
        static readonly Color Muted = ColorTranslator.FromHtml("#9A9AAA");
        static readonly Color Accent = ColorTranslator.FromHtml("#7C8CFF");
        static readonly Color Good = ColorTranslator.FromHtml("#3DD68C");

        readonly Label title, subtitle, status;
        readonly Check chkDesktop, chkAuto, chkLaunch;
        readonly Btn btnInstall, btnCancel;
        bool done;

        public SetupForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.T("Pen Radial Menu installeren", "Install Pen Radial Menu");
            ClientSize = new Size(560, 380);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Segoe UI", 10f);
            try
            {
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenMapper.app.ico");
                if (s != null) Icon = new Icon(s, 32, 32);
            }
            catch { }

            var logo = new Logo { Location = new Point(32, 32), Size = new Size(64, 64) };
            Controls.Add(logo);
            title = Lbl(Program.AppName, new Font("Segoe UI", 20f, FontStyle.Bold), Text1, 112, 30, 420, 40);
            subtitle = Lbl(Program.T("Versie ", "Version ") + Program.Version + "   ·   " +
                Program.T("een rond snelmenu bij je pen", "a round quick menu at your pen"),
                new Font("Segoe UI", 9.5f), Muted, 114, 70, 430, 22);

            bool update = Installer.IsInstalled;
            Lbl(update ? Program.T("Pen Radial Menu is al geïnstalleerd. Klik op Bijwerken om de nieuwste versie te installeren.",
                                   "Pen Radial Menu is already installed. Click Update to install the latest version.")
                       : Program.T("Pen Radial Menu wordt alleen voor jou geïnstalleerd. Er zijn geen beheerdersrechten nodig.",
                                   "Pen Radial Menu is installed just for you. No administrator rights are needed."),
                new Font("Segoe UI", 10f), Muted, 32, 120, 500, 40);

            chkDesktop = AddCheck(Program.T("Snelkoppeling op het bureaublad", "Desktop shortcut"), 172, true);
            chkAuto = AddCheck(Program.T("Starten met Windows", "Start with Windows"), 208, false);
            chkLaunch = AddCheck(Program.T("Pen Radial Menu openen na installatie", "Open Pen Radial Menu after installing"), 244, true);

            status = Lbl("", new Font("Segoe UI", 9.5f), Muted, 32, 322, 250, 30);

            btnInstall = new Btn { Text = update ? Program.T("Bijwerken", "Update") : Program.T("Installeren", "Install"), Fill = Accent, Location = new Point(560 - 32 - 150, 312), Size = new Size(150, 42) };
            btnInstall.Click += (s, e) => DoInstall();
            Controls.Add(btnInstall);
            btnCancel = new Btn { Text = Program.T("Annuleren", "Cancel"), Fill = Card, Location = new Point(560 - 32 - 150 - 12 - 120, 312), Size = new Size(120, 42) };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);

            ResumeLayout(false);
            PerformAutoScale();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        Label Lbl(string text, Font f, Color c, int x, int y, int w, int h)
        {
            var l = new Label { Text = text, Font = f, ForeColor = c, BackColor = Bg, Location = new Point(x, y), Size = new Size(w, h), UseMnemonic = false };
            Controls.Add(l);
            return l;
        }

        Check AddCheck(string text, int y, bool on)
        {
            var c = new Check { Text = text, Checked = on, Location = new Point(32, y), Size = new Size(496, 28), BackColor = Bg, ForeColor = Text1, Font = new Font("Segoe UI", 10f) };
            Controls.Add(c);
            return c;
        }

        void DoInstall()
        {
            if (done) { Close(); return; }
            btnInstall.Enabled = btnCancel.Enabled = false;
            status.Text = Program.T("Bezig met installeren...", "Installing...");
            status.Refresh();
            try
            {
                Installer.Install(chkDesktop.Checked, chkAuto.Checked, chkLaunch.Checked);
                done = true;
                title.Text = Program.T("Klaar!", "All done!");
                status.Text = Program.T("Pen Radial Menu is geïnstalleerd.", "Pen Radial Menu has been installed.");
                status.ForeColor = Good;
                btnInstall.Text = Program.T("Sluiten", "Close");
                btnInstall.Enabled = true;
                btnCancel.Visible = false;
            }
            catch (Exception ex)
            {
                status.Text = Program.T("Mislukt: ", "Failed: ") + ex.Message;
                status.ForeColor = ColorTranslator.FromHtml("#FF6B6B");
                btnInstall.Enabled = btnCancel.Enabled = true;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---- kleine eigen controls
        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        class Logo : Control
        {
            public Logo() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(0, 0, Width - 1, Height - 1);
                using (var br = new LinearGradientBrush(r, Color.FromArgb(0x95, 0xA3, 0xFF), Color.FromArgb(0x5B, 0x6C, 0xF0), 45f))
                using (var path = Round(r, Width * 0.22f))
                    g.FillPath(br, path);
                using (var f = new Font("Segoe Fluent Icons", 24f))
                    TextRenderer.DrawText(g, "", f, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        class Btn : Control
        {
            public Color Fill;
            bool hover;
            public Btn()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI Semibold", 10f);
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color c = Fill;
                if (hover && Enabled) c = ControlPaint.Light(Fill, 0.15f);
                if (!Enabled) c = Color.FromArgb(120, Fill);
                using (var b = new SolidBrush(c))
                using (var p = Round(new RectangleF(0, 0, Width - 1, Height - 1), 9 * e.Graphics.DpiX / 96f))
                    g.FillPath(b, p);
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        class Check : Control
        {
            bool isOn;
            public bool Checked { get { return isOn; } set { isOn = value; Invalidate(); } }
            public Check()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }
            protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                float s = g.DpiX / 96f;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sw = 40 * s, sh = 22 * s;
                var r = new RectangleF(0, (Height - sh) / 2, sw, sh);
                using (var b = new SolidBrush(isOn ? Accent : ColorTranslator.FromHtml("#4A4A55")))
                using (var p = Round(r, sh / 2))
                    g.FillPath(b, p);
                float d = sh - 6 * s;
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, isOn ? r.Right - d - 3 * s : r.X + 3 * s, r.Y + 3 * s, d, d);
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(sw + 14 * s), 0, Width, Height), ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
