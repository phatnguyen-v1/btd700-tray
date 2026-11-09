// BTD 700 Tray: a small Windows tray app for the Sennheiser BTD 700 Bluetooth dongle.
// - Switch the audio link mode (Standard / Gaming / Broadcast) from the tray, without opening Sennheiser Dongle Control
// - Optional: Gaming mode while one of your games runs, back to the previous mode when it closes
// - Disconnect the headset from this PC (so your phone gets it) and connect it again
// Unofficial, not affiliated with Sennheiser. Protocol notes in README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

public static class Program
{
    public const string Name = "BTD 700 Tray";

    [STAThread]
    public static void Main()
    {
        bool created;
        using (Mutex m = new Mutex(true, "Btd700Tray.Running", out created))
        using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, "Btd700Tray.Show"))
        {
            if (!created) { show.Set(); return; } // already running: ask it to open its window
            Application.EnableVisualStyles();
            Application.Run(new TrayApp(show));
        }
    }
}

public static class AppLog
{
    public static readonly string FilePath = Path.Combine(Settings.Folder, "log.txt");
    static readonly object gate = new object();

    public static void Write(string source, string msg)
    {
        lock (gate)
        {
            try
            {
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + source + "] " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    public static void Open()
    {
        try { Process.Start("notepad.exe", "\"" + FilePath + "\""); } catch { }
    }
}

// settings.ini in %APPDATA%\Btd700Tray:
//   autogaming=on
//   game=cs2      (one line per game, process name without .exe)
public static class Settings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Btd700Tray");
    static readonly string FilePath = Path.Combine(Folder, "settings.ini");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Btd700Tray";

    public static bool AutoGaming = false;
    public static readonly List<string> Games = new List<string>(); // lock it: the headset thread reads it

    public static void Load()
    {
        try
        {
            foreach (string line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "autogaming") AutoGaming = value == "on";
                else if (key == "game" && value != "") lock (Games) if (!Games.Contains(value)) Games.Add(value);
            }
        }
        catch { } // no file yet: keep the defaults
    }

    public static void Save()
    {
        string text = "autogaming=" + (AutoGaming ? "on" : "off") + "\r\n";
        lock (Games) foreach (string g in Games) text += "game=" + g + "\r\n";
        try { Directory.CreateDirectory(Folder); File.WriteAllText(FilePath, text); }
        catch (Exception ex) { AppLog.Write("app", "could not save settings: " + ex.Message); }
    }

    public static bool StartWithWindows
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (value) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunValue, false);
            }
        }
    }
}

class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray = new NotifyIcon();
    readonly Control invoker = new Control();
    readonly HeadsetMode headset;
    readonly ToolStripMenuItem connectItem;
    readonly ToolStripMenuItem autoGamingItem;
    readonly List<ToolStripMenuItem> modeItems = new List<ToolStripMenuItem>();
    HeadsetForm window;

    public TrayApp(EventWaitHandle showSignal)
    {
        invoker.CreateControl(); // gives this thread a sync context for HeadsetMode's events
        Directory.CreateDirectory(Settings.Folder);
        Settings.Load();
        headset = new HeadsetMode();

        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem openItem = new ToolStripMenuItem("Open", null, delegate { ShowWindow(); });
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        foreach (BtdMode m in new[] { BtdMode.Standard, BtdMode.Gaming, BtdMode.Broadcast })
        {
            BtdMode mode = m;
            ToolStripMenuItem item = new ToolStripMenuItem(m.ToString(), null, delegate { headset.SetMode(mode); });
            item.Tag = m;
            modeItems.Add(item);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        connectItem = new ToolStripMenuItem("Disconnect headset from this PC", null, delegate
        {
            BtdState s = headset.State;
            if (s != null) headset.SetConnected(!s.HeadsetConnected);
        });
        menu.Items.Add(connectItem);
        autoGamingItem = new ToolStripMenuItem("Gaming while a game runs", null, delegate { Settings.AutoGaming = !Settings.AutoGaming; Settings.Save(); UpdateTray(); });
        menu.Items.Add(autoGamingItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { ExitThread(); });
        menu.Opening += delegate { headset.WantStatus(); UpdateTray(); };

        tray.Icon = AppIcon.Value;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { ShowWindow(); };
        tray.Visible = true;

        headset.Changed += UpdateTray;
        headset.Notify += delegate(string text, bool isError) { tray.ShowBalloonTip(4000, Program.Name, text, isError ? ToolTipIcon.Warning : ToolTipIcon.Info); };

        // A second launch sets this event; open the window on the UI thread.
        ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { invoker.BeginInvoke((MethodInvoker)ShowWindow); }, null, -1, false);

        AppLog.Write("app", "started");
        UpdateTray();
    }

    void UpdateTray()
    {
        BtdState s = headset.State;
        foreach (ToolStripMenuItem mi in modeItems) mi.Checked = s != null && s.Mode == (BtdMode)mi.Tag;
        connectItem.Text = s != null && !s.HeadsetConnected ? "Connect headset to this PC" : "Disconnect headset from this PC";
        connectItem.Enabled = s != null;
        autoGamingItem.Checked = Settings.AutoGaming;
        tray.Text = Program.Name + (s == null ? "" : " - " + s.Mode + (s.HeadsetConnected ? "" : ", headset not connected"));
    }

    void ShowWindow()
    {
        if (window == null || window.IsDisposed)
        {
            window = new HeadsetForm(headset);
            window.Show();
        }
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    protected override void ExitThreadCore()
    {
        AppLog.Write("app", "exit");
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}

// Headphones on a blue-to-purple rounded tile, drawn at runtime so the build needs no .ico file.
// The headphones are the "Headphone" glyph from Windows' own icon font.
public static class AppIcon
{
    public static readonly Icon Value = Make(32);

    static Icon Make(int size)
    {
        Bitmap b = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(b))
        using (GraphicsPath tile = RoundedRect(size, size * 0.22f))
        using (LinearGradientBrush fill = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(0, 168, 255), Color.FromArgb(124, 58, 237), 45f))
        using (GraphicsPath glyph = new GraphicsPath())
        using (StringFormat center = new StringFormat())
        using (Pen thicken = new Pen(Color.White, size * 0.04f))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(fill, tile);
            center.Alignment = StringAlignment.Center;
            center.LineAlignment = StringAlignment.Center;
            FontFamily font = IconFont();
            if (font != null)
            {
                glyph.AddString("", font, (int)FontStyle.Regular, size * 0.66f, new RectangleF(0, 0, size, size), center);
                g.FillPath(Brushes.White, glyph);
                g.DrawPath(thicken, glyph); // the outline glyph is too thin at tray size
            }
        }
        return Icon.FromHandle(b.GetHicon());
    }

    static FontFamily IconFont()
    {
        foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try { return new FontFamily(name); } catch (ArgumentException) { }
        }
        return null;
    }

    static GraphicsPath RoundedRect(int size, float radius)
    {
        float d = radius * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddArc(size - d, 0, d, d, 270, 90);
        p.AddArc(size - d, size - d, d, d, 0, 90);
        p.AddArc(0, size - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
