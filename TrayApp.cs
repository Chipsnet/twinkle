using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Twinkle;

enum Mode { Off, AwakeOnly, Away }

sealed class TrayApp : ApplicationContext
{
    readonly Settings _settings;
    readonly Awake _awake;
    readonly NotifyIcon _notifyIcon;
    readonly ToolStripMenuItem _awayItem;
    readonly ToolStripMenuItem _awakeItem;
    readonly ToolStripMenuItem _scopeAcItem;
    readonly ToolStripMenuItem _scopeDcItem;
    readonly ToolStripMenuItem _keepDisplayItem;
    readonly ToolStripMenuItem _autostartItem;
    readonly Icon _iconOff;
    readonly Icon _iconAwake;
    readonly Icon _iconAway;

    Mode _mode = Mode.Off;
    bool _shutdown;

    public TrayApp()
    {
        _settings = Settings.Load();
        _awake = new Awake();

        _awayItem = new ToolStripMenuItem("離席モード");
        _awayItem.Click += (_, _) => SetMode(_mode == Mode.Away ? Mode.Off : Mode.Away);

        _awakeItem = new ToolStripMenuItem("スリープ防止のみ");
        _awakeItem.Click += (_, _) => SetMode(_mode == Mode.AwakeOnly ? Mode.Off : Mode.AwakeOnly);

        _scopeAcItem = new ToolStripMenuItem("影響範囲: 電源接続時");
        _scopeAcItem.Click += (_, _) =>
        {
            _settings.ApplyAc = !_settings.ApplyAc;
            _settings.Save();
            UpdateUi();
        };

        _scopeDcItem = new ToolStripMenuItem("影響範囲: バッテリー駆動時");
        _scopeDcItem.Click += (_, _) =>
        {
            _settings.ApplyDc = !_settings.ApplyDc;
            _settings.Save();
            UpdateUi();
        };

        _keepDisplayItem = new ToolStripMenuItem("画面もオンのまま");
        _keepDisplayItem.Click += (_, _) =>
        {
            _settings.KeepDisplay = !_settings.KeepDisplay;
            _settings.Save();
            SetMode(_mode);
        };

        _autostartItem = new ToolStripMenuItem("Windows起動時に開始");
        _autostartItem.Click += (_, _) =>
        {
            try
            {
                Autostart.Set(!Autostart.IsEnabled());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "twinkle");
            }
            UpdateUi();
        };

        var settingsMenu = new ToolStripMenuItem("設定");
        settingsMenu.DropDownItems.Add(_scopeAcItem);
        settingsMenu.DropDownItems.Add(_scopeDcItem);
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        settingsMenu.DropDownItems.Add(_keepDisplayItem);
        settingsMenu.DropDownItems.Add(_autostartItem);

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) =>
        {
            Shutdown();
            ExitThread();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_awayItem);
        menu.Items.Add(_awakeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _iconOff = MakeIcon(Color.Gray);
        _iconAwake = MakeIcon(Color.DeepSkyBlue);
        _iconAway = MakeIcon(Color.Gold);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconOff,
            ContextMenuStrip = menu,
            Text = "twinkle",
            Visible = false,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                SetMode(_mode == Mode.Away ? Mode.Off : Mode.Away);
            }
        };

        // 前回、異常終了などで元に戻せていなければ復元する
        if (_settings.Saved is not null)
        {
            RestoreLid();
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(3000, "twinkle", "前回の終了時の電源設定を復元しました", ToolTipIcon.Info);
        }

        SystemEvents.SessionEnding += OnSessionEnding;

        UpdateUi();
        _notifyIcon.Visible = true;
    }

    void OnSessionEnding(object sender, SessionEndingEventArgs e) => Shutdown();

    /// <summary>何度呼んでも同じ結果になるように作る。</summary>
    void SetMode(Mode m)
    {
        try
        {
            if (m == Mode.Away && _settings.Saved is null)
            {
                ApplyLid();
            }
            else if (m != Mode.Away && _settings.Saved is not null)
            {
                RestoreLid();
            }

            _awake.Set(m != Mode.Off, m != Mode.Off && _settings.KeepDisplay);
            _mode = m;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "twinkle");
        }
        finally
        {
            UpdateUi();
        }
    }

    void ApplyLid()
    {
        var scheme = Lid.ActiveScheme();
        uint? ac = _settings.ApplyAc ? Lid.Read(scheme, ac: true) : null;
        uint? dc = _settings.ApplyDc ? Lid.Read(scheme, ac: false) : null;

        _settings.Saved = new SavedLid(scheme, ac, dc);
        _settings.Save(); // 書き換える前に保存しておく（途中で落ちても次回起動時に戻せる）

        if (ac is not null)
        {
            Lid.Write(scheme, ac: true, Lid.DoNothing);
        }
        if (dc is not null)
        {
            Lid.Write(scheme, ac: false, Lid.DoNothing);
        }
        Lid.Activate();
    }

    void RestoreLid()
    {
        var s = _settings.Saved!;
        try
        {
            if (s.Ac is not null)
            {
                Lid.Write(s.Scheme, ac: true, s.Ac.Value);
            }
            if (s.Dc is not null)
            {
                Lid.Write(s.Scheme, ac: false, s.Dc.Value);
            }
            Lid.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"電源設定を元に戻せませんでした。手動で戻してください。\n元の値: AC={s.Ac?.ToString() ?? "-"} DC={s.Dc?.ToString() ?? "-"}\n{ex.Message}",
                "twinkle");
        }
        finally
        {
            _settings.Saved = null;
            _settings.Save();
        }
    }

    void UpdateUi()
    {
        _awayItem.Checked = _mode == Mode.Away;
        _awakeItem.Checked = _mode == Mode.AwakeOnly;
        _scopeAcItem.Checked = _settings.ApplyAc;
        _scopeDcItem.Checked = _settings.ApplyDc;
        _keepDisplayItem.Checked = _settings.KeepDisplay;
        _autostartItem.Checked = Autostart.IsEnabled();

        // 離席モード中は影響範囲を変えられないようにする
        _scopeAcItem.Enabled = _settings.Saved is null;
        _scopeDcItem.Enabled = _settings.Saved is null;

        _notifyIcon.Icon = _mode switch
        {
            Mode.Away => _iconAway,
            Mode.AwakeOnly => _iconAwake,
            _ => _iconOff,
        };

        var scope = _settings.ApplyAc
            ? (_settings.ApplyDc ? " (AC+DC)" : " (AC)")
            : (_settings.ApplyDc ? " (DC)" : "");
        _notifyIcon.Text = _mode switch
        {
            Mode.Away => "twinkle - 離席モード" + scope,
            Mode.AwakeOnly => "twinkle - スリープ防止",
            _ => "twinkle - オフ",
        };
    }

    void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }
        _shutdown = true;

        SetMode(Mode.Off);
        _awake.Dispose();
        SystemEvents.SessionEnding -= OnSessionEnding;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconOff.Dispose();
        _iconAwake.Dispose();
        _iconAway.Dispose();
    }

    static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        using (var brush = new SolidBrush(color))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPolygon(brush, new PointF[]
            {
                new(16, 1), new(20, 12), new(31, 16), new(20, 20),
                new(16, 31), new(12, 20), new(1, 16), new(12, 12),
            });
        }
        var handle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
