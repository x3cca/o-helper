using OHelper.Ally;
using OHelper.Battery;
using OHelper.Display;
using OHelper.Gpu;
using OHelper.Helpers;
using OHelper.Input;
using OHelper.Mode;
using OHelper.Overlay;
using OHelper.Peripherals;
using OHelper.USB;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using static NativeMethods;

namespace OHelper
{

    static class Program
    {
        public static NotifyIcon trayIcon;
        public static HpACPI acpi;

        public static SettingsForm settingsForm;

        public static ModeControl modeControl;
        public static GPUModeControl gpuControl;
        public static AllyControl allyControl;
        public static ClamshellModeControl clamshellControl;

        public static ToastForm toast;

        public static HardwareOverlay? hardwareOverlay;

        public static IntPtr unRegPowerNotify, unRegPowerNotifyLid, unRegSuspendResume;
        public static int WM_TASKBARCREATED = 0;

        private static long lastAuto;
        public static InputDispatcher? inputDispatcher;
        private static int _isExiting;
        internal static bool IsExiting => Volatile.Read(ref _isExiting) != 0;

        // The main entry point for the application
        public static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.ApplicationExit += OnExit;

            string action = "";
            if (args.Length > 0) action = args[0];

            if (action == "charge")
            {
                BatteryLimit();
                try
                {
                    InputDispatcher.StartupBacklight();
                } catch (Exception ex) { 
                    Logger.WriteLine($"Startup Backlight: {ex.Message}");
                }
                Application.Exit();
                return;
            }

            string language = AppConfig.GetString("language");
            try
            {
                if (language != null && language.Length > 0)
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                else
                {
                    var culture = CultureInfo.CurrentUICulture;
                    if (culture.ToString() == "kr") culture = CultureInfo.GetCultureInfo("ko");
                    Thread.CurrentThread.CurrentUICulture = culture;
                }
            } catch
            {
                Logger.WriteLine("Unknown Language: " + language);
            }

            Logger.WriteLine("----------------------");
            Logger.WriteLine("App launched: " + AppConfig.GetModel() + " :" + Assembly.GetExecutingAssembly().GetName().Version.ToString() + CultureInfo.CurrentUICulture + (ProcessHelper.IsUserAdministrator() ? "." : ""));

            settingsForm = new SettingsForm();
            modeControl = new ModeControl();
            gpuControl = new GPUModeControl(settingsForm);
            allyControl = new AllyControl(settingsForm);
            clamshellControl = new ClamshellModeControl();
            toast = new ToastForm();

            hardwareOverlay = new HardwareOverlay();

            ProcessHelper.CheckAlreadyRunning();
            ProcessHelper.SetPriority();

            CleanupLegacyFiles();

            var startCount = AppConfig.Get("start_count") + 1;
            AppConfig.Set("start_count", startCount);
            Logger.WriteLine("Start Count: " + startCount);

            acpi = new HpACPI();
            HardwareMonitor.Start();

            // ACPI hardware is optional on HP Omen (graceful WMI no-op fallback),
            // but required on legacy ASUS models where the app can't function without it
            if (!acpi.IsConnected() && AppConfig.IsASUS())
            {
                DialogResult dialogResult = MessageBox.Show(Properties.Strings.ACPIError, Properties.Strings.StartupError, MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("https://support.hp.com/products/laptops") { UseShellExecute = true });
                }

                Application.Exit();
                return;
            }

            if (AppConfig.IsASUS()) ProcessHelper.KillSmartDisplayControl();

            Application.EnableVisualStyles();

            HardwareControl.RecreateGpuControl();

            trayIcon = new NotifyIcon
            {
                Text = "O-Helper",
                Icon = Properties.Resources.standard,
                Visible = true
            };

            var trayRetry = new System.Windows.Forms.Timer { Interval = 5000 };
            trayRetry.Tick += (_, _) => { trayRetry.Dispose(); trayIcon.Visible = false; trayIcon.Visible = true; };
            trayRetry.Start();

            WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
            Logger.WriteLine($"Tray Icon: {trayIcon.Visible} | {WM_TASKBARCREATED}");

            settingsForm.SetContextMenu();
            trayIcon.MouseClick += TrayIcon_MouseClick;
            trayIcon.MouseMove += TrayIcon_MouseMove;


            inputDispatcher = new InputDispatcher();

            settingsForm.InitAura();
            settingsForm.InitMatrix();

            SetAutoModes(init: true);

            powerSettleTimer.Elapsed += OnPowerSettled;

            // Subscribing for system power change events
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            clamshellControl.RegisterDisplayEvents();
            clamshellControl.ToggleLidAction();

            // Subscribing for monitor power on events
            unRegPowerNotify = NativeMethods.RegisterPowerSettingNotification(settingsForm.Handle, PowerSettingGuid.ConsoleDisplayState, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
            unRegPowerNotifyLid = NativeMethods.RegisterPowerSettingNotification(settingsForm.Handle, PowerSettingGuid.LIDSWITCH_STATE_CHANGE, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
            unRegSuspendResume = NativeMethods.RegisterSuspendResumeNotification(settingsForm.Handle, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);


            if (AppConfig.IsASUS())
            {
                Task task = Task.Run((Action)PeripheralsProvider.DetectAllAsusMice);
                PeripheralsProvider.RegisterForDeviceEvents();
            }

            if (Environment.CurrentDirectory.Trim('\\') == Application.StartupPath.Trim('\\') || action.Length > 0)
            {
                SettingsToggle(false);
            }

            switch (action)
            {
                case "cpu":
                    Startup.ReScheduleAdmin();
                    settingsForm.FansToggle();
                    modeControl.AutoPower(false);
                    break;
                case "gpu":
                    Startup.ReScheduleAdmin();
                    settingsForm.FansToggle(1);
                    modeControl.SetGPUClocks(false);
                    modeControl.SetGPUPower();
                    break;
                case "services":
                    Logger.WriteLine("Services action ignored: ASUS service management is not part of O-Helper");
                    break;
                case "uv":
                    Startup.ReScheduleAdmin();
                    settingsForm.FansToggle(2);
                    modeControl.SetRyzen();
                    break;
                case "colors":
                    Task.Run(async () =>
                    {
                        await ColorProfileHelper.InstallProfile();
                        settingsForm.Invoke(delegate
                        {
                            settingsForm.InitVisual();
                        });
                    });
                    break;
                default:
                    Task.Run(Startup.StartupCheck);
                    break;
            }

            if (AppConfig.IsOverlay())
                hardwareOverlay?.StartOverlay();

            Application.Run();
        }


        private static void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (IsExiting) return;
            gpuControl.StandardModeFix();
            modeControl.ShutdownReset();
            BatteryControl.AutoBattery();
            InputDispatcher.ShutdownStatusLed();
        }

        private static void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (IsExiting) return;
            if (e.Reason == SessionSwitchReason.SessionLogon || e.Reason == SessionSwitchReason.SessionUnlock)
            {
                Logger.WriteLine("Session:" + e.Reason.ToString());
                if (AppConfig.IsASUS()) ProcessHelper.KillSmartDisplayControl();
                bool wasLocked = Aura.sessionLock;
                Aura.sessionLock = false;
                ScreenControl.AutoScreen();
                if (wasLocked) Task.Delay(2000).ContinueWith(_ =>
                {
                    if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < 10000) return;
                    modeControl.AutoCPUTemp();
                });
            }
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                Logger.WriteLine("Session:" + e.Reason.ToString());
                Aura.sessionLock = true;
            }
        }

        static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsExiting) return;

            switch (e.Category)
            {
                case UserPreferenceCategory.General:
                    bool changed = settingsForm.InitTheme();
                    settingsForm.InitContextMenuTheme();
                    settingsForm.VisualiseIcon();
                    settingsForm.VisualiseFnLock();
                    settingsForm.VisualiseBatteryFull();

                    if (changed)
                    {
                        Debug.WriteLine("Theme Changed");
                    }

                    if (settingsForm.fansForm is not null && settingsForm.fansForm.Text != "")
                        settingsForm.fansForm.InitTheme();

                    if (settingsForm.extraForm is not null && settingsForm.extraForm.Text != "")
                        settingsForm.extraForm.InitTheme();

                    if (settingsForm.updatesForm is not null && settingsForm.updatesForm.Text != "")
                        settingsForm.updatesForm.InitTheme();

                    if (settingsForm.matrixForm is not null && settingsForm.matrixForm.Text != "")
                        settingsForm.matrixForm.InitTheme();

                    if (settingsForm.handheldForm is not null && settingsForm.handheldForm.Text != "")
                        settingsForm.handheldForm.InitTheme();

                    break;
            }
        }



        public static bool SetAutoModes(bool powerChanged = false, bool init = false, bool wakeup = false)
        {
            if (IsExiting) return false;
            int skipDelay = wakeup ? 10000 : 3000;

            if (init) gpuControl.CaptureNvBootState();

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < skipDelay) return false;
            lastAuto = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            currentSource = ReadPowerSource();
            Logger.WriteLine("AutoSetting for " + SystemInformation.PowerStatus.PowerLineStatus.ToString());

            BatteryControl.AutoBattery(init);
            if (init) InputDispatcher.InitScreenpad();
            DynamicLightingHelper.Init();
            ScreenControl.InitOptimalBrightness();

            inputDispatcher.Init();
            //HardwareControl.ReadSensors(true);

            if (init && AppConfig.Is("auto_mode_enabled") && !AppConfig.Is("manual_mode"))
                modeControl.ApplyAutoModeForPowerSource(false, force: true);
            else
                modeControl.AutoPerformance(powerChanged);

            settingsForm.matrixControl.SetDevice(true);
            InputDispatcher.InitStatusLed();
            if (AppConfig.IsAlly())
            {
                allyControl.Init();
            }
            else
            {
                InputDispatcher.AutoKeyboard();
            }

            bool switched = gpuControl.AutoGPUMode(delay: 1000);
            if (!switched)
            {
                gpuControl.InitGPUMode();
                ScreenControl.AutoScreen();
            }

            ScreenControl.OnPowerChangedRefreshMode();
            ScreenControl.InitMiniled();
            VisualControl.InitBrightness();

            return true;
        }

        public enum PowerSource { Battery, Barrel, USBC }

        public static PowerSource currentSource = PowerSource.Battery;
        private static PowerLineStatus lastLineStatus = SystemInformation.PowerStatus.PowerLineStatus;
        private static readonly System.Timers.Timer powerSettleTimer = new() { AutoReset = false };

        public static PowerSource ReadPowerSource()
        {
            if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
                return PowerSource.Battery;

            int chargerMode = acpi?.DeviceGet(HpACPI.ChargerMode) ?? 0;
            if (chargerMode > 0 && (chargerMode & HpACPI.ChargerBarrel) == 0)
                return PowerSource.USBC;

            return PowerSource.Barrel;
        }

        public static bool usbcProfile = AppConfig.Is("usbc_profile");

        public static int PerformanceKey() =>
            usbcProfile ? (int)currentSource : currentSource == PowerSource.Battery ? 0 : 1;

        public static void SchedulePowerCheck()
        {
            if (IsExiting || AppConfig.Is("disable_power_event")) return;
            powerSettleTimer.Interval = Math.Max(AppConfig.Get("charger_delay"), 2000);
            powerSettleTimer.Stop();
            powerSettleTimer.Start();
        }

        private static void OnPowerSettled(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (IsExiting) return;
            PowerSource source = ReadPowerSource();
            if (source == currentSource) return;

            Logger.WriteLine($"Power source: {currentSource} -> {source}");
            currentSource = source;
            SetAutoModes(powerChanged: true);
        }

        public static void OnChargerEvent() => SchedulePowerCheck();

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (IsExiting) return;
            if (e.Mode == PowerModes.Suspend)
            {
                Logger.WriteLine("Power Mode Changed:" + e.Mode.ToString());
                gpuControl.StandardModeFix();
                modeControl.ShutdownReset();
                InputDispatcher.ShutdownStatusLed();
                return;
            }

            PowerLineStatus status = SystemInformation.PowerStatus.PowerLineStatus;
            if (status != lastLineStatus)
            {
                lastLineStatus = status;
                Logger.WriteLine($"Power Mode {e.Mode}: {status}");
            }

            SchedulePowerCheck();
        }

        public static void SettingsToggle(bool checkForFocus = true, bool trayClick = false)
        {
            if (settingsForm.Visible)
            {
                // If helper window is not on top, this just focuses on the app again
                // Pressing the ghelper button again will hide the app
                if (checkForFocus && !settingsForm.HasAnyFocus(trayClick) && !AppConfig.Is("topmost"))
                {
                    settingsForm.ShowAll();
                }
                else
                {
                    settingsForm.HideAll();
                }
            }
            else
            {
                var screen = Screen.PrimaryScreen;
                if (screen is null) screen = Screen.FromControl(settingsForm);

                settingsForm.WindowState = FormWindowState.Normal;

                settingsForm.Location = screen.WorkingArea.Location;
                settingsForm.Left = screen.WorkingArea.Width - 10 - settingsForm.Width;
                settingsForm.Top = screen.WorkingArea.Height - 10 - settingsForm.Height;

                settingsForm.Show();
                settingsForm.ShowAll();

                settingsForm.Left = screen.WorkingArea.Width - 10 - settingsForm.Width;

                if (AppConfig.IsAlly())
                    settingsForm.Top = Math.Max(10, screen.Bounds.Height - 110 - settingsForm.Height);
                else
                    settingsForm.Top = screen.WorkingArea.Height - 10 - settingsForm.Height;

                settingsForm.VisualiseGPUMode();
            }
        }

        static void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                SettingsToggle(trayClick: true);

        }

        static void TrayIcon_MouseMove(object? sender, MouseEventArgs e)
        {
            settingsForm.RefreshSensors();
        }

        static void OnExit(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _isExiting, 1) != 0) return;

            TryExitCleanup(() => { powerSettleTimer.Stop(); powerSettleTimer.Dispose(); }, "power timer");
            TryExitCleanup(() => SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged, "power events");
            TryExitCleanup(() => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged, "preference events");
            TryExitCleanup(() => SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch, "session events");
            TryExitCleanup(() => SystemEvents.SessionEnding -= SystemEvents_SessionEnding, "session ending events");
            TryExitCleanup(() => hardwareOverlay?.StopOverlay(), "overlay");
            TryExitCleanup(() => modeControl?.Stop(), "mode control");
            TryExitCleanup(() => inputDispatcher?.Dispose(), "input dispatcher");
            TryExitCleanup(HardwareControl.DisposeGpuControl, "GPU control");
            TryExitCleanup(HardwareControl.Dispose, "hardware control");
            TryExitCleanup(HardwareMonitor.Stop, "hardware monitor");
            TryExitCleanup(() => acpi?.Close(), "ACPI");
            TryExitCleanup(PeripheralsProvider.UnregisterForDeviceEvents, "device events");
            TryExitCleanup(() => clamshellControl?.UnregisterDisplayEvents(), "display events");
            TryExitCleanup(() => NativeMethods.UnregisterPowerSettingNotification(unRegPowerNotify), "display power notification");
            TryExitCleanup(() => NativeMethods.UnregisterPowerSettingNotification(unRegPowerNotifyLid), "lid power notification");
            TryExitCleanup(() => NativeMethods.UnregisterSuspendResumeNotification(unRegSuspendResume), "suspend notification");

            if (trayIcon is not null)
            {
                TryExitCleanup(() => { trayIcon.Visible = false; trayIcon.Dispose(); }, "tray icon");
            }
            TryExitCleanup(AppConfig.Flush, "configuration flush");
        }

        private static void TryExitCleanup(Action cleanup, string name)
        {
            try { cleanup(); }
            catch (Exception ex) { Logger.WriteLine($"Exit cleanup ({name}) error: {ex.Message}"); }
        }

        static void BatteryLimit()
        {
            try
            {
                int limit = AppConfig.Get("charge_limit");
                if (limit > 0 && limit < 100)
                {
                    Logger.WriteLine($"------- Startup Battery Limit {limit} -------");
                    Logger.WriteLine($"Connecting to ACPI");
                    acpi = new HpACPI();
                    Logger.WriteLine($"Setting Limit");
                    acpi.DeviceSet(HpACPI.BatteryLimit, limit, "Limit");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Startup Battery Limit Error: " + ex.Message);
            }
        }

        static void CleanupLegacyFiles()
        {
            string appDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            string[] legacyFiles = ["WinRing0x64.sys", "WinRing0x64.dll"];

            foreach (string fileName in legacyFiles)
            {
                string filePath = Path.Combine(appDir, fileName);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        Logger.WriteLine($"Deleted legacy file: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"Failed to delete legacy file {fileName}: {ex.Message}");
                    }
                }
            }
        }

    }
}
