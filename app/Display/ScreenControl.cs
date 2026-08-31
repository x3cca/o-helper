using OHelper.Helpers;
using System.Diagnostics;

namespace OHelper.Display
{
    public enum RefreshRateMode
    {
        Auto = 0,
        Hz60 = 1,
        Hz120 = 2,
        Dynamic = 3
    }

    public static class ScreenControl
    {

        public const int MAX_REFRESH = 1000;
        public static int MIN_RATE = AppConfig.Get("min_rate", 60);
        public static int MAX_RATE = AppConfig.Get("max_rate");
        public const string REFRESH_MODE_KEY = "refresh_rate_mode";

        public static int GetMaxRate(string? laptopScreen)
        {
            if (MAX_RATE > 0) return MAX_RATE;
            else return ScreenNative.GetMaxRefreshRate(laptopScreen);
        }

        // ============================================
        // REFRESH RATE MODE API (Auto/60Hz/120Hz/Dynamic)
        // ============================================

        public static RefreshRateMode GetRefreshRateMode()
        {
            int stored = AppConfig.Get(REFRESH_MODE_KEY, -1);
            if (stored >= 0 && stored <= 3) return (RefreshRateMode)stored;

            // Derive from legacy screen_auto flag for backward compatibility
            return AppConfig.Is("screen_auto") ? RefreshRateMode.Auto : RefreshRateMode.Hz120;
        }

        public static void SetRefreshRateMode(RefreshRateMode mode)
        {
            if (mode == RefreshRateMode.Dynamic && !ScreenNative.IsDynamicRefreshAvailable())
            {
                Program.toast?.RunToast(Properties.Strings.DynamicRefreshUnsupported);
                return;
            }

            AppConfig.Set(REFRESH_MODE_KEY, (int)mode);
            AppConfig.Set("screen_auto", mode == RefreshRateMode.Auto ? 1 : 0);
            ApplyRefreshRateMode(mode);
        }

        public static void ApplyRefreshRateMode(RefreshRateMode? modeOverride = null)
        {
            if (!AppConfig.HasDisplayModes()) return;

            var mode = modeOverride ?? GetRefreshRateMode();
            var laptopScreen = ScreenNative.FindLaptopScreen(true);

            switch (mode)
            {
                case RefreshRateMode.Auto:
                    ScreenNative.EnableDynamicRefresh(false);
                    if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
                        ScreenNative.SetRefreshRateExact(laptopScreen, 120);
                    else
                        ScreenNative.SetRefreshRateExact(laptopScreen, 60);
                    break;
                case RefreshRateMode.Hz60:
                    ScreenNative.EnableDynamicRefresh(false);
                    ScreenNative.SetRefreshRateExact(laptopScreen, 60);
                    break;
                case RefreshRateMode.Hz120:
                    ScreenNative.EnableDynamicRefresh(false);
                    ScreenNative.SetRefreshRateExact(laptopScreen, 120);
                    break;
                case RefreshRateMode.Dynamic:
                    if (!ScreenNative.EnableDynamicRefresh(true))
                        Program.toast?.RunToast(Properties.Strings.DynamicRefreshUnsupported);
                    break;
            }

            InitScreen();
        }

        // Called on power source change (AC<->battery) to honor Auto mode
        public static void OnPowerChangedRefreshMode()
        {
            ApplyRefreshRateMode(GetRefreshRateMode());
        }
        public static void AutoScreen(bool force = false)
        {
            if (AppConfig.HasDisplayModes())
            {
                if (force || GetRefreshRateMode() == RefreshRateMode.Auto)
                    ApplyRefreshRateMode(RefreshRateMode.Auto);
                else
                    ApplyRefreshRateMode(GetRefreshRateMode());
                return;
            }

            if (force || AppConfig.Is("screen_auto"))
            {
                if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
                    SetScreen(MAX_REFRESH, 1);
                else
                    SetScreen(MIN_RATE, 0);
            }
            else
            {
                SetScreen(overdrive: AppConfig.Get("overdrive"));
            }
        }

        public static void SetAutoRefresh(int auto)
        {
            AppConfig.Set("screen_auto", auto);
        }

        public static void ToggleScreenRate()
        {
            if (AppConfig.HasDisplayModes())
            {
                var current = GetRefreshRateMode();
                var next = current switch
                {
                    RefreshRateMode.Auto => RefreshRateMode.Hz60,
                    RefreshRateMode.Hz60 => RefreshRateMode.Hz120,
                    RefreshRateMode.Hz120 => ScreenNative.IsDynamicRefreshAvailable() ? RefreshRateMode.Dynamic : RefreshRateMode.Auto,
                    _ => RefreshRateMode.Auto
                };
                SetRefreshRateMode(next);
                return;
            }

            var laptopScreen = ScreenNative.FindLaptopScreen(true);
            var refreshRate = ScreenNative.GetRefreshRate(laptopScreen);
            if (refreshRate < 0) return;

            ScreenNative.SetRefreshRate(laptopScreen, refreshRate > MIN_RATE ? MIN_RATE : GetMaxRate(laptopScreen));
            InitScreen();
        }


        public static void SetScreen(int frequency = -1, int overdrive = -1, int miniled = -1)
        {
            var laptopScreen = ScreenNative.FindLaptopScreen(true);
            var refreshRate = ScreenNative.GetRefreshRate(laptopScreen);

            if (refreshRate < 0) return;

            if (frequency >= MAX_REFRESH)
            {
                frequency = GetMaxRate(laptopScreen);
            }

            if (frequency > 0 && frequency != refreshRate)
            {
                ScreenNative.SetRefreshRate(laptopScreen, frequency);
            }

            if (Program.acpi.IsOverdriveSupported() && overdrive >= 0)
            {
                if (AppConfig.IsNoOverdrive()) overdrive = 0;
                if (overdrive != Program.acpi.DeviceGet(HpACPI.ScreenOverdrive))
                {
                    Program.acpi.DeviceSet(HpACPI.ScreenOverdrive, overdrive, "ScreenOverdrive");
                }
            }

            SetMiniled(miniled);

            InitScreen();
        }

        public static void SetMiniled(int miniled = -1)
        {
            if (miniled >= 0)
            {
                if (Program.acpi.IsSupported(HpACPI.ScreenMiniled1))
                    Program.acpi.DeviceSet(HpACPI.ScreenMiniled1, miniled, "Miniled1");
                else
                {
                    Program.acpi.DeviceSet(HpACPI.ScreenMiniled2, miniled, "Miniled2");
                    Thread.Sleep(100);
                }
            }
        }

        public static void InitMiniled()
        {
            if (AppConfig.IsForceMiniled())
            {
                SetHDRControl(AppConfig.Get("hdr_control"));
                SetMiniled(AppConfig.Get("miniled"));
            }
        }

        public static void InitOptimalBrightness()
        {
            int optimalBrightness = AppConfig.Get("optimal_brightness");
            if (optimalBrightness >= 0) SetOptimalBrightness(optimalBrightness);
        }

        public static void SetOptimalBrightness(int status)
        {
            AppConfig.Set("optimal_brightness", status);
            if (status == 2) status = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? 1 : 0;
            Program.acpi.DeviceSet(HpACPI.ScreenOptimalBrightness, status, "Optimal Brightness");
        }

        public static int GetOptimalBrightness()
        {
            return Program.acpi.DeviceGet(HpACPI.ScreenOptimalBrightness);
        }

        public static void ToogleFHD()
        {
            int fhd = Program.acpi.DeviceGet(HpACPI.ScreenFHD);
            Logger.WriteLine($"FHD Toggle: {fhd}");

            DialogResult dialogResult = MessageBox.Show(Properties.Strings.DisplayModeRestart, Properties.Strings.AlertUltimateTitle, MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                Program.acpi.DeviceSet(HpACPI.ScreenFHD, (fhd == 1) ? 0 : 1, "FHD");
                Process.Start("shutdown", "/r /t 1");
            }
        }

        public static void SetHDRControl(int status = -1)
        {
            if (status >= 0)
            {
                AppConfig.Set("hdr_control", status);
                Program.acpi.DeviceSet(HpACPI.ScreenHDRControl, status, "HDR Control");
            }
        }

        public static void ToogleHDRControl()
        {
            int hdrControl = Program.acpi.DeviceGet(HpACPI.ScreenHDRControl);
            Logger.WriteLine($"HDR Control Toggle: {hdrControl}");
            SetHDRControl((hdrControl == 1) ? 1 : 0);
            Thread.Sleep(200);
            InitScreen();
        }

        public static string ToogleMiniled()
        {
            int miniled1 = Program.acpi.DeviceGet(HpACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(HpACPI.ScreenMiniled2);

            Logger.WriteLine($"MiniledToggle: {miniled1} {miniled2}");

            int miniled;
            string name;

            if (miniled1 >= 0)
            {
                switch (miniled1)
                {
                    case 1: 
                        miniled = 0;
                        name = Properties.Strings.OneZone;
                        break;
                    default:
                        miniled = 1;
                        name = Properties.Strings.Multizone;
                        break;
                }
            }
            else
            {
                switch (miniled2)
                {
                    case 1: 
                        miniled = 2;
                        name = Properties.Strings.OneZone;
                        break;
                    case 2: 
                        miniled = 0;
                        name = Properties.Strings.Multizone;
                        break;
                    default: 
                        miniled = 1;
                        name = Properties.Strings.MultizoneStrong;
                        break;
                }
            }

            AppConfig.Set("miniled", miniled);
            SetScreen(miniled: miniled);
            
            return name;
        }

        public static void InitScreen()
        {
            var laptopScreen = ScreenNative.FindLaptopScreen();
            int frequency = ScreenNative.GetRefreshRate(laptopScreen);
            int maxFrequency = GetMaxRate(laptopScreen);

            if (maxFrequency > 0) AppConfig.Set("max_frequency", maxFrequency);
            else maxFrequency = AppConfig.Get("max_frequency");

            bool screenAuto = AppConfig.Is("screen_auto");
            bool overdriveSetting = Program.acpi.IsOverdriveSupported() && !AppConfig.IsNoOverdrive();

            int overdrive = overdriveSetting ? Program.acpi.DeviceGet(HpACPI.ScreenOverdrive) : 0;

            int miniled1 = Program.acpi.DeviceGet(HpACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(HpACPI.ScreenMiniled2);

            int miniled = (miniled1 >= 0) ? miniled1 : miniled2;
            bool hdr = false;
            bool acm = false;

            if (miniled >= 0)
            {
                Logger.WriteLine($"Miniled: {miniled1} {miniled2}");
                AppConfig.Set("miniled", miniled);
            }

            try
            {
                hdr = ScreenCCD.GetHDRStatus(out acm);
            } catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }

            bool screenEnabled = (frequency >= 0);

            int fhd = -1;

            int hdrControl = Program.acpi.DeviceGet(HpACPI.ScreenHDRControl);
            if (hdrControl >= 0) Logger.WriteLine($"HDR Control Status: {hdrControl}");

            AppConfig.Set("frequency", frequency);
            AppConfig.Set("overdrive", overdrive);

            Program.settingsForm.Invoke(delegate
            {
                Program.settingsForm.VisualiseScreen(
                    screenEnabled: screenEnabled,
                    screenAuto: screenAuto,
                    frequency: frequency,
                    maxFrequency: maxFrequency,
                    overdrive: overdrive,
                    overdriveSetting: overdriveSetting,
                    miniled1: miniled1,
                    miniled2: miniled2,
                    hdr: hdr,
                    acm: acm,
                    fhd: fhd,
                    hdrControl: hdrControl
                );
            });

        }
    }
}
