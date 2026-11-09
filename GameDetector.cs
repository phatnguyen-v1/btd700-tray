// Finds a running game for "Gaming while a game runs", three ways:
// - Your games: process names you added by hand.
// - Windows' game list: the exes Game Bar has recognised as games (HKCU\System\GameConfigStore\Children, value
//   MatchedExeFullPath). Undocumented, only read, reread every minute.
// - Full screen: the foreground window covers its whole monitor without a title bar (exclusive or borderless full
//   screen) for two checks in a row, and isn't a browser, video player, remote desktop, launcher or the desktop.
//   Once found, that app counts as a game until it exits, so alt-tabbing out doesn't flip the mode back and forth.
// Names on your "not a game" list are never counted; the built-in list below only stops the two automatic ways,
// so you can still add, say, a browser game by hand.
// Called from the headset thread only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

public class GameHit
{
    public readonly string Name;   // process name without .exe
    public readonly string How;    // "your games", "Windows' game list" or "full screen"
    public GameHit(string name, string how) { Name = name; How = how; }
    public bool Automatic { get { return How != GameDetector.YourGames; } }
}

public static class GameDetector
{
    public const string YourGames = "your games", WindowsList = "Windows' game list", FullScreen = "full screen";

    // Never treated as games by the automatic ways: browsers (so full-screen YouTube stays in its mode), video
    // players, slides and meetings, remote desktops and VMs, game launchers and stores, and Windows itself.
    static readonly HashSet<string> NotGamesBuiltIn = new HashSet<string>(new[] {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "arc", "zen", "librewolf", "waterfox",
        "floorp", "thorium", "chromium", "msedgewebview2",
        "vlc", "mpv", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayer64", "potplayermini",
        "potplayermini64", "kmplayer", "kmplayer64x", "gom", "smplayer", "wmplayer", "kodi", "plex", "plex htpc",
        "stremio", "netflix", "spotify", "microsoft.media.player", "video.ui", "photos", "microsoft.photos",
        "applicationframehost",
        "powerpnt", "teams", "ms-teams", "zoom", "obs64",
        "mstsc", "msrdc", "vmconnect", "vmware", "vmware-vmx", "virtualboxvm", "parsecd", "moonlight", "anydesk",
        "teamviewer", "rustdesk",
        "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "agent", "eadesktop",
        "eabackgroundservice", "upc", "ubisoftconnect", "galaxyclient", "riotclientservices", "riotclientux",
        "leagueclient", "leagueclientux", "leagueclientuxrender", "xboxpcapp", "gamebar", "playnite.desktopapp",
        "playnite.fullscreenapp",
        "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "lockapp", "textinputhost",
        "taskmgr", "dwm", "logonui",
    }, StringComparer.OrdinalIgnoreCase);

    // Folders whose exes Windows' game list sometimes picks up although they aren't games.
    static readonly string[] NotGameFolders = { @"\reWASD\" };

    static Dictionary<string, List<string>> windowsGames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    static DateTime windowsGamesRead = DateTime.MinValue;
    static int fullScreenPid;           // the full-screen app found, counted as a game while it runs
    static string fullScreenName;
    static int candidatePid;            // full screen on the last check, not yet confirmed

    public static GameHit Find()
    {
        string[] mine = Copy(Settings.Games), notGames = Copy(Settings.NotGames);
        if (Settings.DetectWindowsGames && DateTime.Now - windowsGamesRead > TimeSpan.FromMinutes(1)) ReadWindowsGames();

        GameHit hit = null;
        bool fullScreenAlive = false;
        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName;
                if (p.Id == fullScreenPid && name == fullScreenName) fullScreenAlive = true;
                if (hit != null || p.Id == self || Contains(notGames, name)) continue;
                if (Contains(mine, name)) hit = new GameHit(name, YourGames);
                else if (Settings.DetectWindowsGames && !NotGamesBuiltIn.Contains(name))
                {
                    List<string> paths;
                    if (windowsGames.TryGetValue(name, out paths) && PathMatches(p.Id, paths)) hit = new GameHit(name, WindowsList);
                }
            }
            catch { } // exited meanwhile
            finally { p.Dispose(); }
        }
        if (hit != null) return hit;

        if (!Settings.DetectFullScreen) { fullScreenPid = candidatePid = 0; return null; }
        if (fullScreenPid != 0 && fullScreenAlive && !Contains(notGames, fullScreenName)) return new GameHit(fullScreenName, FullScreen);
        fullScreenPid = 0;

        string fsName;
        int pid = FullScreenApp(notGames, out fsName);
        if (pid != 0 && pid == candidatePid)
        {
            fullScreenPid = pid;
            fullScreenName = fsName;
            candidatePid = 0;
            return new GameHit(fsName, FullScreen);
        }
        candidatePid = pid;
        return null;
    }

    static string[] Copy(List<string> list)
    {
        lock (list) return list.ToArray();
    }

    static bool Contains(string[] names, string name)
    {
        return Array.Exists(names, delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
    }

    static void ReadWindowsGames()
    {
        windowsGamesRead = DateTime.Now;
        Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (RegistryKey children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children"))
            {
                if (children != null)
                    foreach (string sub in children.GetSubKeyNames())
                        using (RegistryKey k = children.OpenSubKey(sub))
                        {
                            string path = k == null ? null : k.GetValue("MatchedExeFullPath") as string;
                            if (string.IsNullOrEmpty(path)) continue;
                            if (Array.Exists(NotGameFolders, delegate(string f) { return path.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0; })) continue;
                            string name = Path.GetFileNameWithoutExtension(path);
                            List<string> paths;
                            if (!found.TryGetValue(name, out paths)) found[name] = paths = new List<string>();
                            paths.Add(path);
                        }
            }
        }
        catch { } // keep whatever was read before
        if (found.Count > 0 || windowsGames.Count == 0) windowsGames = found;
    }

    // Same exe name as a listed game: check the full path too, unless Windows won't say (some anti-cheat games).
    static bool PathMatches(int pid, List<string> paths)
    {
        string path = ProcessPath(pid);
        return path == null || paths.Exists(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); });
    }

    static string ProcessPath(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            StringBuilder sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    // The foreground app if its window fills its monitor with no title bar, else 0.
    static int FullScreenApp(string[] notGames, out string name)
    {
        name = null;
        IntPtr w = GetForegroundWindow();
        if (w == IntPtr.Zero) return 0;
        StringBuilder cls = new StringBuilder(64);
        GetClassName(w, cls, cls.Capacity);
        string c = cls.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return 0;

        RECT r;
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetWindowRect(w, out r) || !GetMonitorInfo(MonitorFromWindow(w, 2 /* NEAREST */), ref mi)) return 0;
        RECT m = mi.rcMonitor;
        bool covers = r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        bool titleBar = (GetWindowLong(w, -16 /* GWL_STYLE */) & 0x00C00000 /* WS_CAPTION */) == 0x00C00000;
        if (!covers || titleBar) return 0;

        int pid;
        GetWindowThreadProcessId(w, out pid);
        if (pid == 0 || pid == Process.GetCurrentProcess().Id) return 0;
        try
        {
            using (Process p = Process.GetProcessById(pid)) name = p.ProcessName;
        }
        catch { return 0; }
        if (NotGamesBuiltIn.Contains(name) || Contains(notGames, name)) return 0;
        string path = ProcessPath(pid);
        if (path != null && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return 0;
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
