// Headset mode: switches the Sennheiser BTD 700 dongle between Standard / Gaming / Broadcast (Btd700.cs),
// and optionally to Gaming while one of your games runs, then back to the mode it was in before.
// The dongle is only asked for its mode when something needs it (status window or tray menu open, a game
// starting or stopping), so it doesn't trip up Sennheiser Dongle Control if that's open too.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

public class HeadsetMode
{
    const int PollMs = 3000;

    readonly object gate = new object();
    readonly SynchronizationContext ui;
    volatile BtdState state;        // last mode read, null if unknown
    volatile string error;          // why the last read failed
    DateTime wantStatusUntil;
    bool autoSwitched;              // we put it in Gaming because a game started
    BtdMode modeBeforeGame;
    GameHit runningGame;

    public event Action Changed;    // raised on the UI thread after a new read or switch
    public event Action<string, bool> Notify; // text, is error; raised on the UI thread after each switch

    public HeadsetMode()
    {
        ui = SynchronizationContext.Current;
        Thread t = new Thread(Loop);
        t.IsBackground = true;
        t.Name = "HeadsetMode";
        t.Start();
    }

    public BtdState State { get { return state; } }

    public string StatusText
    {
        get
        {
            BtdState s = state;
            string text = s == null ? (error ?? "Checking...")
                : "Mode: " + s.Mode + ", " + (!s.HeadsetConnected ? "headset not connected to this PC"
                    : "headset connected over " + TransportName(s.ConnectedTransport) + (s.Link >= 3 ? ", streaming" : ""));
            if (Settings.AutoGaming)
            {
                GameHit game = runningGame;
                text += "\n" + (game != null ? game.Name + " is running (" + game.How + ")" + (autoSwitched ? ": switched to Gaming, goes back to " + modeBeforeGame + " when it closes" : "")
                    : GameCount() == 0 && !Settings.DetectWindowsGames && !Settings.DetectFullScreen ? "Add your games below" : "No game running");
            }
            return text;
        }
    }

    // The game running now, null if none (or Gaming-while-a-game-runs is off).
    public GameHit RunningGame { get { return runningGame; } }

    // Ask for fresh status for a few seconds (the status window calls this every second while open).
    public void WantStatus()
    {
        lock (gate) wantStatusUntil = DateTime.Now.AddSeconds(5);
    }

    // Manual switch from the status window or tray, done on a worker thread.
    public void SetMode(BtdMode mode)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            lock (gate)
            {
                autoSwitched = false; // a manual choice wins over the game switch
                Apply(mode, "switched to " + mode);
            }
        });
    }

    // Drops the headset from this PC so another device can have it, or brings it back. Waits up to 6 s for the
    // dongle to report the new link state before saying it worked.
    public void SetConnected(bool connect)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            lock (gate)
            {
                string what = connect ? "connect" : "disconnect";
                try
                {
                    Btd700.SetConnected(connect);
                    for (int i = 0; i < 12; i++)
                    {
                        Thread.Sleep(500);
                        state = Btd700.GetState();
                        if (state.HeadsetConnected == connect) break;
                    }
                    error = null;
                    if (state.HeadsetConnected == connect)
                    {
                        string text = connect ? "Headset connected to this PC" : "Headset disconnected from this PC";
                        AppLog.Write("headset", text);
                        RaiseNotify(text, false);
                    }
                    else
                    {
                        string text = connect ? "Asked the headset to connect, but it hasn't yet. Is it on and in range?"
                            : "Asked the headset to disconnect, but it's still connected.";
                        AppLog.Write("headset", text);
                        RaiseNotify(text, true);
                    }
                }
                catch (Exception ex)
                {
                    error = "Could not " + what + " the headset: " + ex.Message;
                    AppLog.Write("headset", error);
                    RaiseNotify(error, true);
                }
                RaiseChanged();
            }
        });
    }

    void Loop()
    {
        while (true)
        {
            try { lock (gate) Check(); }
            catch (Exception ex) { AppLog.Write("headset", "check failed: " + ex.Message); }
            Thread.Sleep(PollMs);
        }
    }

    void Check()
    {
        GameHit game = Settings.AutoGaming ? GameDetector.Find() : null;
        bool gameChanged = (game == null ? null : game.Name) != (runningGame == null ? null : runningGame.Name);
        runningGame = game;
        if (gameChanged) AppLog.Write("headset", game == null ? "no game running" : "game running: " + game.Name + " (" + game.How + ")");

        if (game != null && gameChanged && !autoSwitched)
        {
            Read();
            if (state != null && state.Mode != BtdMode.Gaming)
            {
                modeBeforeGame = state.Mode;
                autoSwitched = Apply(BtdMode.Gaming, game.Name + " started: switched to Gaming");
            }
        }
        else if (game == null && autoSwitched)
        {
            autoSwitched = false;
            Read();
            if (state != null && state.Mode == BtdMode.Gaming) Apply(modeBeforeGame, "game closed: back to " + modeBeforeGame);
        }
        else if (DateTime.Now < wantStatusUntil || gameChanged)
        {
            Read();
            if (autoSwitched && state != null && state.Mode != BtdMode.Gaming) autoSwitched = false; // changed by hand meanwhile
        }
    }

    bool Apply(BtdMode mode, string logText)
    {
        try
        {
            state = Btd700.SetMode(mode);
            if (state.Mode != mode) throw new Exception("the dongle stayed in " + state.Mode);
            error = null;
            AppLog.Write("headset", logText);
            RaiseNotify(logText.Substring(0, 1).ToUpper() + logText.Substring(1), false);
            return true;
        }
        catch (Exception ex)
        {
            error = "Could not switch to " + mode + ": " + ex.Message;
            AppLog.Write("headset", error);
            RaiseNotify(error, true);
            return false;
        }
        finally { RaiseChanged(); }
    }

    void Read()
    {
        try { state = Btd700.GetState(); error = null; }
        catch (Exception ex) { state = null; error = ex.Message; }
        RaiseChanged();
    }

    void RaiseNotify(string text, bool isError)
    {
        Action<string, bool> h = Notify;
        if (h != null && ui != null) ui.Post(delegate { h(text, isError); }, null);
    }

    void RaiseChanged()
    {
        Action h = Changed;
        if (h != null && ui != null) ui.Post(delegate { h(); }, null);
    }

    static int GameCount()
    {
        lock (Settings.Games) return Settings.Games.Count;
    }

    static string TransportName(int t)
    {
        return t == 1 ? "BT Classic" : t == 2 ? "LE Audio" : "transport " + t;
    }
}
