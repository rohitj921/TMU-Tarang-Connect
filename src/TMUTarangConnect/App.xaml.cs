using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using TMUTarangConnect.Models;
using TMUTarangConnect.Services;
using TMUTarangConnect.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TMUTarangConnect;

public partial class App : Application
{
    private static Mutex? _mutex;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _trayStatusItem;
    private ToolStripMenuItem? _trayStartupItem;
    private MainWindow? _mainWindow;
    private WifiAuthService? _authService;
    private AppConfig? _config;
    private ConnectionStats? _stats;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch any unhandled crash
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            ConfigService.Log("CRASH", args.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
        };
        DispatcherUnhandledException += (s, args) =>
        {
            ConfigService.Log("CRASH", args.Exception.ToString());
            args.Handled = true;
        };

        // 1. Single Instance Check & Bring-to-Front IPC
        const string mutexName = "Local\\TMUTarangConnect_WPF_SingleInstance";
        const string eventName = "Local\\TMUTarangConnect_ShowDashboard_Event";

        var currentProcess = Process.GetCurrentProcess();
        bool otherProcessRunning = Process.GetProcessesByName(currentProcess.ProcessName).Any(p => p.Id != currentProcess.Id);

        if (otherProcessRunning)
        {
            try
            {
                using var triggerEvent = EventWaitHandle.OpenExisting(eventName);
                triggerEvent.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        try
        {
            _mutex = new Mutex(true, mutexName, out _);
        }
        catch { }

        // First instance creates the EventWaitHandle to listen for subsequent launch triggers
        try
        {
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            var listenerThread = new Thread(() =>
            {
                while (true)
                {
                    showEvent.WaitOne();
                    OpenDashboard();
                }
            })
            {
                IsBackground = true
            };
            listenerThread.Start();
        }
        catch { }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 2. Load Config & Stats
        _config = ConfigService.LoadConfig();
        _stats = new ConnectionStats();
        BandwidthTrackerService.Initialize();
        _authService = new WifiAuthService(_config, _stats);

        _authService.NotificationRequested += OnNotificationRequested;
        _authService.StatusChanged += OnStatusChanged;

        // 3. Initialize Tray Icon
        InitTrayIcon();

        // 4. Create Main Window
        _mainWindow = new MainWindow(_config, _stats, _authService);

        // Check if launched on Windows startup (pass --startup argument)
        bool startMinimized = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) || arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                break;
            }
        }

        if (!startMinimized)
        {
            // ALWAYS SHOW WINDOW when user manually launches it!
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        // 5. Start Background Monitoring
        _authService.StartBackgroundWorker();
    }

    private void InitTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "TMU Tarang Connect",
            Visible = true,
            Icon = ResourceHelper.LoadDrawingIcon("wifi_connected.png")
        };

        var menu = new ContextMenuStrip();

        // Header Title
        var header = new ToolStripMenuItem("TMU Tarang Connect")
        {
            Enabled = false,
            Font = new Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        menu.Items.Add(header);

        // Status Item
        _trayStatusItem = new ToolStripMenuItem("Status: Initializing...")
        {
            Enabled = false
        };
        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(new ToolStripSeparator());

        // Check Connection
        menu.Items.Add("Check Connection", null, async (s, e) =>
        {
            if (_authService != null)
                await _authService.RunSingleCheckAsync(isManual: true);
        });

        // Connect Now
        menu.Items.Add("Connect / Retry Now", null, async (s, e) =>
        {
            if (_authService != null)
            {
                var (success, msg) = await _authService.LoginAsync();
                ShowToast(success ? "Connected Successfully" : "Connection Failed", msg, success ? "wifi_connected.png" : "wifi_error.png");
            }
        });

        // Logout
        menu.Items.Add("Logout", null, async (s, e) =>
        {
            if (_authService != null)
                await _authService.LogoutAsync();
        });

        menu.Items.Add(new ToolStripSeparator());

        // Open Dashboard
        menu.Items.Add("Open Dashboard & Settings...", null, (s, e) => OpenDashboard());

        // Startup Toggle
        _trayStartupItem = new ToolStripMenuItem("Run on Windows Startup")
        {
            CheckOnClick = true,
            Checked = StartupService.IsAutoStartEnabled()
        };
        _trayStartupItem.Click += (s, e) =>
        {
            StartupService.SetAutoStart(_trayStartupItem.Checked);
        };
        menu.Items.Add(_trayStartupItem);

        // View Activity Log
        menu.Items.Add("View Activity Log", null, (s, e) =>
        {
            try
            {
                if (!File.Exists(ConfigService.LogPath))
                {
                    File.WriteAllText(ConfigService.LogPath, "");
                }
                Process.Start("notepad.exe", $"\"{ConfigService.LogPath}\"");
            }
            catch { }
        });

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        menu.Items.Add("Exit", null, (s, e) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenDashboard();
            }
        };
        _trayIcon.DoubleClick += (s, e) => OpenDashboard();
    }

    public void OpenDashboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow(_config!, _stats!, _authService!);
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        });
    }

    private void OnStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon != null && _stats != null)
            {
                if (_stats.IsConnected)
                {
                    _trayIcon.Icon = ResourceHelper.LoadDrawingIcon("wifi_connected.png");
                    _trayIcon.Text = $"TMU Tarang: Online ({_stats.UptimeDisplay})";
                    if (_trayStatusItem != null) _trayStatusItem.Text = $"Status: Online ({_stats.UptimeDisplay})";
                }
                else if (_stats.IsPaused)
                {
                    _trayIcon.Icon = ResourceHelper.LoadDrawingIcon("wifi_disconnected.png");
                    _trayIcon.Text = "TMU Tarang: Logged Out (Paused)";
                    if (_trayStatusItem != null) _trayStatusItem.Text = "Status: Logged Out (Paused)";
                }
                else
                {
                    _trayIcon.Icon = ResourceHelper.LoadDrawingIcon("wifi_error.png");
                    _trayIcon.Text = "TMU Tarang: Disconnected";
                    if (_trayStatusItem != null) _trayStatusItem.Text = "Status: Disconnected / Captive Block";
                }
            }
        });
    }

    private void OnNotificationRequested(string title, string message, string iconFile)
    {
        if (_config?.ShowNotifications == true)
        {
            ShowToast(title, message, iconFile);
        }
    }

    private void ShowToast(string title, string message, string iconFile)
    {
        Dispatcher.Invoke(() =>
        {
            var toast = new ToastWindow(title, message, iconFile, () => OpenDashboard());
            toast.Show();
        });
    }


    private void ExitApplication()
    {
        _authService?.StopBackgroundWorker();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _authService?.StopBackgroundWorker();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }

        base.OnExit(e);
    }
}
