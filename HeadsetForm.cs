// The window: mode buttons, headset connection, Gaming-while-a-game-runs with its game list, start with Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public class HeadsetForm : Form
{
    readonly HeadsetMode headset;
    readonly RadioButton[] modeButtons = new RadioButton[3];
    readonly Label info = Info();
    readonly Button connectButton = new Button();
    readonly CheckBox autoGamingBox = new CheckBox();
    readonly Label gamesInfo = Info();
    readonly CheckBox windowsGamesBox = SubBox("Also games Windows recognizes (Game Bar's list)");
    readonly CheckBox fullScreenBox = SubBox("Also full-screen apps (never browsers or video players)");
    readonly Label notGamesInfo = Info();
    readonly CheckBox startupBox = new CheckBox();
    readonly Timer timer = new Timer();
    bool updating;

    public HeadsetForm(HeadsetMode headset)
    {
        this.headset = headset;
        Text = Program.Name;
        Icon = AppIcon.Value;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        FlowLayoutPanel panel = new FlowLayoutPanel();
        panel.FlowDirection = FlowDirection.TopDown;
        panel.WrapContents = false;
        panel.AutoSize = true;
        panel.Padding = new Padding(12);

        FlowLayoutPanel modes = new FlowLayoutPanel();
        modes.AutoSize = true;
        string[] hints = { "High versatility", "Low latency", "Public or private" };
        for (int i = 0; i < 3; i++)
        {
            BtdMode mode = (BtdMode)i;
            RadioButton b = new RadioButton();
            b.Appearance = Appearance.Button;
            b.Text = mode + "\n" + hints[i];
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Size = new Size(130, 44);
            b.CheckedChanged += delegate { if (!updating && b.Checked) { headset.SetMode(mode); info.Text = "Switching to " + mode + "..."; } };
            modeButtons[i] = b;
            modes.Controls.Add(b);
        }

        connectButton.AutoSize = true;
        connectButton.Margin = new Padding(3, 6, 3, 3);
        connectButton.Text = "Disconnect headset from this PC";
        connectButton.Click += delegate
        {
            BtdState s = headset.State;
            if (s == null) return;
            headset.SetConnected(!s.HeadsetConnected);
            connectButton.Enabled = false;
            info.Text = s.HeadsetConnected ? "Disconnecting..." : "Connecting...";
        };

        autoGamingBox.Text = "Switch to Gaming while a game runs, then back";
        autoGamingBox.AutoSize = true;
        autoGamingBox.Margin = new Padding(3, 6, 3, 3);
        autoGamingBox.CheckedChanged += delegate { if (!updating) { Settings.AutoGaming = autoGamingBox.Checked; Settings.Save(); } UpdateView(); };

        windowsGamesBox.CheckedChanged += delegate { if (!updating) { Settings.DetectWindowsGames = windowsGamesBox.Checked; Settings.Save(); } UpdateView(); };
        fullScreenBox.CheckedChanged += delegate { if (!updating) { Settings.DetectFullScreen = fullScreenBox.Checked; Settings.Save(); } UpdateView(); };

        FlowLayoutPanel gameButtons = new FlowLayoutPanel();
        gameButtons.AutoSize = true;
        gameButtons.Margin = new Padding(17, 0, 3, 3);
        Button gamesButton = new Button();
        gamesButton.Text = "Your games...";
        gamesButton.AutoSize = true;
        gamesButton.Click += delegate
        {
            EditList(Settings.Games, Settings.NotGames, "Your games",
                "While any of these is running, the dongle switches to Gaming. Start a game, then add it from the running apps.");
            UpdateView();
        };
        Button notGamesButton = new Button();
        notGamesButton.Text = "Not games...";
        notGamesButton.AutoSize = true;
        notGamesButton.Click += delegate
        {
            EditList(Settings.NotGames, Settings.Games, "Not games",
                "These never switch the dongle to Gaming, even if Windows lists them as games or they run full screen.");
            UpdateView();
        };
        gameButtons.Controls.Add(gamesButton);
        gameButtons.Controls.Add(notGamesButton);

        startupBox.Text = "Start with Windows";
        startupBox.AutoSize = true;
        startupBox.Margin = new Padding(3, 16, 3, 3);
        startupBox.CheckedChanged += delegate
        {
            if (updating) return;
            try { Settings.StartWithWindows = startupBox.Checked; }
            catch (Exception ex) { MessageBox.Show(this, "Could not change startup:\n" + ex.Message, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            UpdateView();
        };

        Button logButton = new Button();
        logButton.Text = "Open log";
        logButton.AutoSize = true;
        logButton.Click += delegate { AppLog.Open(); };

        Label note = Info();
        note.Margin = new Padding(3, 12, 3, 3);
        note.Text = "Unofficial tool, not affiliated with Sennheiser. Firmware updates still need Sennheiser Dongle Control.";

        panel.Controls.AddRange(new Control[] { modes, info, connectButton, autoGamingBox, windowsGamesBox, fullScreenBox, gamesInfo, notGamesInfo, gameButtons, startupBox, logButton, note });
        Controls.Add(panel);

        headset.Changed += UpdateView;
        timer.Interval = 1000;
        timer.Tick += delegate { UpdateView(); };
        UpdateView();
        timer.Start();
    }

    static Label Info()
    {
        Label l = new Label();
        l.AutoSize = true;
        l.ForeColor = SystemColors.GrayText;
        l.Margin = new Padding(20, 0, 3, 3);
        return l;
    }

    static CheckBox SubBox(string text)
    {
        CheckBox b = new CheckBox();
        b.Text = text;
        b.AutoSize = true;
        b.Margin = new Padding(20, 0, 3, 0);
        return b;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        headset.Changed -= UpdateView;
        timer.Dispose();
        base.OnFormClosed(e);
    }

    void UpdateView()
    {
        headset.WantStatus();
        BtdState s = headset.State;
        updating = true;
        for (int i = 0; i < 3; i++)
        {
            modeButtons[i].Enabled = s != null;
            modeButtons[i].Checked = s != null && (int)s.Mode == i;
        }
        autoGamingBox.Checked = Settings.AutoGaming;
        windowsGamesBox.Checked = Settings.DetectWindowsGames;
        fullScreenBox.Checked = Settings.DetectFullScreen;
        windowsGamesBox.Enabled = fullScreenBox.Enabled = Settings.AutoGaming;
        try { startupBox.Checked = Settings.StartWithWindows; } catch { }
        updating = false;

        bool busy = info.Text.EndsWith("...") && info.Text != "Checking...";
        if (!busy || s != null) info.Text = headset.StatusText;
        connectButton.Text = s != null && !s.HeadsetConnected ? "Connect headset to this PC" : "Disconnect headset from this PC";
        connectButton.Enabled = s != null;
        gamesInfo.Text = "Your games: " + Names(Settings.Games);
        notGamesInfo.Text = "Not games: " + Names(Settings.NotGames);
    }

    static string Names(List<string> list)
    {
        string[] names;
        lock (list) names = list.ToArray();
        return names.Length == 0 ? "none yet" : string.Join(", ", names);
    }

    // Edits your games or not games; adding a name to one list takes it off the other.
    void EditList(List<string> target, List<string> other, string title, string hintText)
    {
        Form dlg = new Form();
        dlg.Text = title;
        dlg.Icon = AppIcon.Value;
        dlg.Font = Font;
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.Size = new Size(420, 360);
        dlg.MinimizeBox = dlg.MaximizeBox = false;

        Label hint = new Label();
        hint.Text = hintText;
        hint.Dock = DockStyle.Top;
        hint.Padding = new Padding(4);
        hint.Height = 44;

        ListBox list = new ListBox();
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        lock (target) foreach (string g in target) list.Items.Add(g);

        FlowLayoutPanel buttons = new FlowLayoutPanel();
        buttons.Dock = DockStyle.Bottom;
        buttons.AutoSize = true;
        Button add = new Button();
        add.Text = "Add from running apps...";
        add.AutoSize = true;
        add.Click += delegate
        {
            string name = PickRunningApp(dlg);
            if (name == null) return;
            lock (target) if (!target.Contains(name)) target.Add(name);
            lock (other) other.Remove(name);
            Settings.Save();
            if (!list.Items.Contains(name)) list.Items.Add(name);
        };
        Button remove = new Button();
        remove.Text = "Remove";
        remove.AutoSize = true;
        remove.Click += delegate
        {
            string name = list.SelectedItem as string;
            if (name == null) return;
            lock (target) target.Remove(name);
            Settings.Save();
            list.Items.Remove(name);
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);

        dlg.Controls.Add(list);
        dlg.Controls.Add(hint);
        dlg.Controls.Add(buttons);
        dlg.ShowDialog(this);
        dlg.Dispose();
    }

    // Lists the apps that have a window open, by process name.
    static string PickRunningApp(Form owner)
    {
        SortedDictionary<string, string> apps = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle != "" && p.Id != self
                    && p.ProcessName != "explorer" && !apps.ContainsKey(p.ProcessName))
                    apps.Add(p.ProcessName, p.MainWindowTitle);
            }
            catch { }
            p.Dispose();
        }

        Form dlg = new Form();
        dlg.Text = "Add a running app";
        dlg.Icon = AppIcon.Value;
        dlg.Font = owner.Font;
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.Size = new Size(520, 400);
        dlg.MinimizeBox = dlg.MaximizeBox = false;

        ListBox list = new ListBox();
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        List<string> names = new List<string>(apps.Keys);
        foreach (string n in names) list.Items.Add(n + "   (" + apps[n] + ")");

        Button ok = new Button();
        ok.Text = "Add";
        ok.Dock = DockStyle.Bottom;
        ok.DialogResult = DialogResult.OK;
        list.DoubleClick += delegate { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(list);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;

        string result = dlg.ShowDialog(owner) == DialogResult.OK && list.SelectedIndex >= 0 ? names[list.SelectedIndex] : null;
        dlg.Dispose();
        return result;
    }
}
