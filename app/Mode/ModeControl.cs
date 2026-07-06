using OHelper.Gpu;
using OHelper.Helpers;
using PawnIO;

namespace OHelper.Mode
{
    public class ModeControl
    {

        static SettingsForm settings = Program.settingsForm;

        private static bool customFans = false;
        private static int customPower = 0;
        private static volatile bool _fanMaxActive = false;
        private static System.Timers.Timer? fanCurveTimer;
        private static readonly object fanCurveLock = new();
        private static int lastCpuFanLevel = -1;
        private static int lastGpuFanLevel = -1;
        private static float? cpuFanAnchorTemp;
        private static float? gpuFanAnchorTemp;
        private static bool softwareFanCurveAutoMode;
        private static int softwareFanCurveTickActive;
        private static int cpuHotSampleCount;
        private const double FanCurveIntervalMs = 2000;
        private const float CpuFanBoostTemp = 90f;
        private const float CpuFanMaxTemp = 97f;
        private const float CpuImmediateMaxTemp = 100f;
        private const int CpuHotFanFloorPercent = 70;
        private const int CpuHotSamplesRequired = 2;

        private int _cpuUV = 0;
        private int _igpuUV = 0;
        private int _cpuTemp = CpuInfo.DefaultTemp;
        private bool _ryzenPower = false;

        private static RyzenSmuService? _smu;
        private static readonly object _smuLock = new();

        private static RyzenSmuService? GetSmu()
        {
            lock (_smuLock)
            {
                if (_smu != null && _smu.IsInitialized) return _smu;
                _smu?.Dispose();
                _smu = new RyzenSmuService();
                if (!_smu.Initialize(System.Reflection.Assembly.GetExecutingAssembly()))
                {
                    _smu.Dispose();
                    _smu = null;
                }
                else
                {
                    Logger.WriteLine($"SMU Init: {_smu.CpuCodeName} ({_smu.Family}), SMU v{_smu.SmuVersion >> 16}.{(_smu.SmuVersion >> 8) & 0xFF}.{_smu.SmuVersion & 0xFF}");
                }
                return _smu;
            }
        }

        public static bool IsPawnAvailable()  => GetSmu() != null;
        public static bool IsPawnInstalled()   => RyzenSmuService.IsPawnInstalled();

        static System.Timers.Timer? reapplyTimer;
        static System.Timers.Timer modeToggleTimer = default!;
        static CancellationTokenSource _modeCts = new();
        static CancellationTokenSource _autoModeCts = new();
        private static int _stopped;
        static Task _modeTask = Task.CompletedTask;

        public ModeControl()
        {
            int reapplyTime = AppConfig.Get("reapply_time", IsReapplyTempRequired() ? 30 : 0);
            if (reapplyTime > 0)
            {
                reapplyTimer = new System.Timers.Timer(reapplyTime * 1000);
                reapplyTimer.Elapsed += ReapplyTimer_Elapsed;
            }
        }

        // Cezanne/Rembrandt (Renoir) + Phoenix/HawkPoint (Mobile) silently reset temp limit under load.
        private static bool IsReapplyTempRequired()
        {
            var smu = GetSmu();
            return smu != null && smu.Family is CpuFamily.Renoir or CpuFamily.Mobile;
        }

        private static bool IsReapplyRyzenRequired()
        {
            var smu = GetSmu();
            return smu != null && smu.Family is CpuFamily.Raphael;
        }

        private static void SetReapplyEnabled(bool enabled)
        {
            if (reapplyTimer != null) reapplyTimer.Enabled = enabled;
        }


        private void ReapplyTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            SetCPUTemp(AppConfig.GetMode("cpu_temp"));
            SetRyzenPower();
        }

        public void ApplyAutoModeForPowerSource(bool notify = true, bool force = false)
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            if (!AppConfig.Is("auto_mode_enabled") || AppConfig.Is("manual_mode")) return;

            PowerLineStatus powerLineStatus = SystemInformation.PowerStatus.PowerLineStatus;
            if (powerLineStatus == PowerLineStatus.Unknown)
            {
                Logger.WriteLine("Auto power-source mode skipped: power source unknown");
                return;
            }

            bool onAc = powerLineStatus == PowerLineStatus.Online;
            int mode = AppConfig.Get(onAc ? "auto_mode_ac" : "auto_mode_dc", onAc ? 0 : 2);
            if (!Modes.Exists(mode)) mode = onAc ? 0 : 2;

            if (!force && Modes.GetCurrent() == mode)
            {
                ApplyWindowsPowerMode(mode);
                return;
            }

            Logger.WriteLine($"Auto power-source mode: {(onAc ? "AC" : "Battery")} -> {Modes.GetName(mode)}{(force ? " (forced)" : "")}");
            SetPerformanceMode(mode, notify);
        }

        private void ScheduleAutoModeForPowerSource()
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            if (!AppConfig.Is("auto_mode_enabled") || AppConfig.Is("manual_mode")) return;

            _autoModeCts.Cancel();
            _autoModeCts = new CancellationTokenSource();
            var ct = _autoModeCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("auto_mode_delay", 750)), ct);
                    ApplyAutoModeForPowerSource();
                }
                catch (OperationCanceledException)
                {
                    Logger.WriteLine("Auto power-source mode apply cancelled");
                }
            }, ct);
        }

        public void WaitForApply()
        {
            try { _modeTask.Wait(5000); } catch { }
        }

        public void AutoPerformance(bool powerChanged = false)
        {
            if (powerChanged && AppConfig.Is("auto_mode_enabled") && !AppConfig.Is("manual_mode"))
            {
                ScheduleAutoModeForPowerSource();
                return;
            }

            int mode = AppConfig.Get("performance_" + Program.PerformanceKey());
            if (mode != -1 && !Modes.Exists(mode)) mode = -1;
            Logger.WriteLine($"{Program.currentSource} Performance Mode: {Modes.GetName(mode == -1 ? Modes.GetCurrent() : mode)}");

            if (mode != -1)
                SetPerformanceMode(mode, powerChanged);
            else
                SetPerformanceMode(Modes.GetCurrent());
        }


        public void ResetPerformanceMode()
        {
            ResetRyzen();

            Program.acpi.DeviceSet(HpACPI.PerformanceMode, Modes.GetCurrentBase(), "Mode");

            // Default power mode
            AppConfig.RemoveMode("powermode");
            ApplyWindowsPowerMode(Modes.GetCurrent());
        }

        public void Toast()
        {
            Program.toast.RunToast(Modes.GetCurrentName(), SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? ToastIcon.Charger : ToastIcon.Battery);
        }

        public void SetPerformanceMode(int mode = -1, bool notify = false)
        {
            if (Volatile.Read(ref _stopped) != 0) return;

            int oldMode = Modes.GetCurrent();
            if (mode < 0) mode = oldMode;

            if (!Modes.Exists(mode)) mode = 0;

            settings.ShowMode(mode);

            Modes.SetCurrent(mode);


            _modeCts.Cancel();
            _modeCts = new CancellationTokenSource();
            var ct = _modeCts.Token;

            _modeTask = Task.Run(async () =>
            {
                try
                {
                    bool reset = AppConfig.IsResetRequired() && (Modes.GetBase(oldMode) == Modes.GetBase(mode)) && customPower > 0 && !AppConfig.IsApplyPower();

                    customFans = false;
                    customPower = 0;

                    SetModeLabel();

                    if (reset && AppConfig.SupportsPerformanceModes())
                    {
                        Program.acpi.DeviceSet(HpACPI.PerformanceMode, (Modes.GetBase(oldMode) != 1) ? HpACPI.PerformanceTurbo : HpACPI.PerformanceBalanced, "ModeReset");
                        await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
                    }

                    ct.ThrowIfCancellationRequested();

                    if (AppConfig.SupportsPerformanceModes())
                    {
                        if (AppConfig.Is("status_mode")) Program.acpi.DeviceSet(HpACPI.StatusMode, [0x00, Modes.GetBase(mode) == HpACPI.PerformanceSilent ? (byte)0x02 : (byte)0x03], "StatusMode");
                        int status = Program.acpi.DeviceSet(HpACPI.PerformanceMode, AppConfig.IsManualModeRequired() ? HpACPI.PerformanceManual : Modes.GetBase(mode), "Mode");
                        if (status != 1) Program.acpi.DeviceSet(HpACPI.PerformanceMode, AppConfig.IsManualModeRequired() ? HpACPI.PerformanceManual : Modes.GetBase(mode), "Mode retry");
                    }

                    SetGPUClocks();

                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                    ct.ThrowIfCancellationRequested();
                    AutoFans();
                    await Task.Delay(TimeSpan.FromMilliseconds(1000), ct);
                    ct.ThrowIfCancellationRequested();
                    AutoPower();
                    
                    var command = AppConfig.GetModeString("mode_command");
                    if (command is not null)
                    {   Logger.WriteLine("Running mode command: " + command);
                        RestrictedProcessHelper.RunAsRestrictedUser(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/C " + command);
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.WriteLine($"SetPerformanceMode cancelled (mode {mode})");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"SetPerformanceMode failed (mode {mode}): {ex}");
                }
            }, ct);

            if (notify) Toast();

            ApplyWindowsPowerMode(mode);

            // CPU Boost setting override
            if (AppConfig.GetMode("auto_boost") != -1)
                    PowerNative.SetCPUBoost(AppConfig.GetMode("auto_boost"));

            settings.FansInit();
        }

        private static void ApplyWindowsPowerMode(int mode)
        {
            if (AppConfig.Is("skip_powermode") || AppConfig.Is("no_windows_power_mode")) return;

            string? powerMode = AppConfig.GetString("powermode_" + mode);
            PowerNative.SetPowerMode(powerMode ?? PowerNative.GetDefaultPowerMode(mode));

            if (AppConfig.IsAutoASPM()) PowerNative.SetBalancedASPM();
        }


        private void ModeToggleTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            modeToggleTimer.Stop();
            Logger.WriteLine($"Hotkey mode: {Modes.GetCurrent()}");
            SetPerformanceMode();

        }

        public void CyclePerformanceMode(bool back = false)
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            int delay = AppConfig.Get("mode_delay", 1000);

            if (modeToggleTimer is null)
            {
                modeToggleTimer = new System.Timers.Timer(delay);
                modeToggleTimer.Elapsed += ModeToggleTimer_Elapsed;
            }

            modeToggleTimer.Stop();
            modeToggleTimer.Start();
            Modes.SetCurrent(Modes.GetNext(back));
            Toast();
        }

        public void SetFanMaxActive(bool active)
        {
            _fanMaxActive = active;
            if (active)
            {
                StopFanCurveLoop(false);
                SetReapplyEnabled(false);
                Logger.WriteLine("ModeControl: Fan curve reapply paused (Max Fans active)");
            }
            else
            {
                SetReapplyEnabled(true);
                Logger.WriteLine("ModeControl: Fan curve reapply resumed (Max Fans inactive)");
            }
        }

        public void AutoFans(bool force = false)
        {
            customFans = false;

            if (!AppConfig.SupportsSoftwareFanCurves())
            {
                if (AppConfig.IsApplyFans())
                {
                    AppConfig.SetMode("auto_apply", 0);
                    AppConfig.Flush();
                    Logger.WriteLine("ModeControl: custom fan curves are unsupported; persisted curve apply was disabled");
                }

                StopFanCurveLoop(false);
                settings.LabelFansResult("");
                return;
            }

            if (_fanMaxActive)
            {
                Logger.WriteLine("ModeControl: AutoFans skipped (Max Fans active)");
                return;
            }

            bool applyCustomCurve = AppConfig.IsApplyFans();
            bool monitorUnleashedThermals = Modes.GetCurrentBase() == HpACPI.PerformanceManual;

            if (applyCustomCurve || monitorUnleashedThermals || force)
            {

                if (AppConfig.SupportsFirmwareFanCurves())
                {
                    int cpuResult = Program.acpi.SetFanCurve(HpFan.CPU, AppConfig.GetFanConfig(HpFan.CPU));
                    int gpuResult = Program.acpi.SetFanCurve(HpFan.GPU, AppConfig.GetFanConfig(HpFan.GPU));

                    if (AppConfig.Is("mid_fan"))
                        Program.acpi.SetFanCurve(HpFan.Mid, AppConfig.GetFanConfig(HpFan.Mid));

                    // Alternative way to set fan curve
                    if (cpuResult != 1 || gpuResult != 1)
                    {
                        cpuResult = Program.acpi.SetFanRange(HpFan.CPU, AppConfig.GetFanConfig(HpFan.CPU));
                        gpuResult = Program.acpi.SetFanRange(HpFan.GPU, AppConfig.GetFanConfig(HpFan.GPU));

                        // Something went wrong, resetting to default profile
                        if (cpuResult != 1 || gpuResult != 1)
                        {
                            StartFanCurveLoop();
                            settings.LabelFansResult(applyCustomCurve ? Properties.Strings.SoftwareFanCurveActive : "");
                            customFans = applyCustomCurve;
                        }
                        else
                        {
                            StopFanCurveLoop(false);
                            settings.LabelFansResult("");
                            customFans = true;
                        }
                    }
                    else
                    {
                        StopFanCurveLoop(false);
                        settings.LabelFansResult("");
                        customFans = true;
                    }
                }
                else
                {
                    StartFanCurveLoop();
                    settings.LabelFansResult(applyCustomCurve ? Properties.Strings.SoftwareFanCurveActive : "");
                    customFans = applyCustomCurve;
                }

                int hystUp = AppConfig.GetMode("hysteresis_up");
                int hystDown = AppConfig.GetMode("hysteresis_down");
                if (hystUp > 0 && hystDown > 0)
                    Program.acpi.SetFanHysteresis(hystUp, hystDown);

                if (AppConfig.IsPowerRequired() && !AppConfig.IsApplyPower())
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        Program.acpi.DeviceSet(HpACPI.PPT_APUA0, 80, "PowerLimit Fix A0");
                        Program.acpi.DeviceSet(HpACPI.PPT_APUA3, 80, "PowerLimit Fix A3");
                    });
                }

            } else
            {
                StopFanCurveLoop(true);
            }

            SetModeLabel();

        }

        private static void StartFanCurveLoop()
        {
            if (!AppConfig.SupportsSoftwareFanCurves())
            {
                Logger.WriteLine("ModeControl: software fan curve loop blocked (unsupported model)");
                return;
            }

            lock (fanCurveLock)
            {
                fanCurveTimer ??= new System.Timers.Timer(FanCurveIntervalMs);
                fanCurveTimer.Elapsed -= FanCurveTimer_Elapsed;
                fanCurveTimer.Elapsed += FanCurveTimer_Elapsed;
                fanCurveTimer.AutoReset = true;
                fanCurveTimer.Start();
                lastCpuFanLevel = -1;
                lastGpuFanLevel = -1;
                cpuFanAnchorTemp = null;
                gpuFanAnchorTemp = null;
                // Force one auto write on the first tick to clear any stale max-fan latch.
                softwareFanCurveAutoMode = false;
                Volatile.Write(ref cpuHotSampleCount, 0);
            }

            Logger.WriteLine("ModeControl: software fan curve loop started");
            ApplySoftwareFanCurve();
        }

        private static void StopFanCurveLoop(bool restoreAuto)
        {
            lock (fanCurveLock)
            {
                if (fanCurveTimer is not null) fanCurveTimer.Stop();
                lastCpuFanLevel = -1;
                lastGpuFanLevel = -1;
                cpuFanAnchorTemp = null;
                gpuFanAnchorTemp = null;
                softwareFanCurveAutoMode = true;
                Volatile.Write(ref cpuHotSampleCount, 0);
            }

            if (restoreAuto && AppConfig.SupportsSoftwareFanCurves() && Program.acpi is not null)
            {
                Program.acpi.SetFanLevel(0, 0);
                Program.acpi.DeviceSet(HpACPI.PerformanceMode, Modes.GetCurrentBase(), "Restore Mode");
                Logger.WriteLine("ModeControl: software fan curve loop stopped, BIOS auto restored");
            }
        }

        private static void FanCurveTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            ApplySoftwareFanCurve();
        }

        private static void ApplySoftwareFanCurve()
        {
            if (Interlocked.CompareExchange(ref softwareFanCurveTickActive, 1, 0) != 0) return;

            try
            {
                // Serialize a curve tick with max-fan activation. SetFanMaxActive(true)
                // stops the timer under this same lock, so an in-flight temperature
                // update must finish before the user's max-fan command is sent.
                lock (fanCurveLock)
                {
                    ApplySoftwareFanCurveCore();
                }
            }
            finally
            {
                Volatile.Write(ref softwareFanCurveTickActive, 0);
            }
        }

        private static void ApplySoftwareFanCurveCore()
        {
            bool applyCustomCurve = AppConfig.IsApplyFans();
            bool monitorUnleashedThermals = Modes.GetCurrentBase() == HpACPI.PerformanceManual;
            if (!AppConfig.SupportsSoftwareFanCurves() || _fanMaxActive || (!applyCustomCurve && !monitorUnleashedThermals))
            {
                StopFanCurveLoop(!_fanMaxActive);
                return;
            }

            try
            {
                float? cpuTemp = HardwareControl.GetCPUTemp();
                float? gpuTemp = applyCustomCurve ? HardwareControl.GetGPUTemp() : null;
                int cpuLevel = applyCustomCurve ? EvaluateFanCurve(AppConfig.GetFanConfig(HpFan.CPU), cpuTemp) : 0;
                int gpuLevel = applyCustomCurve ? EvaluateFanCurve(AppConfig.GetFanConfig(HpFan.GPU), gpuTemp) : 0;
                if (applyCustomCurve)
                {
                    cpuLevel = ApplySoftwareHysteresis(cpuLevel, cpuTemp, ref cpuFanAnchorTemp, lastCpuFanLevel);
                    gpuLevel = ApplySoftwareHysteresis(gpuLevel, gpuTemp, ref gpuFanAnchorTemp, lastGpuFanLevel);
                }
                bool thermalSafetyActive = ApplyCpuThermalSafetyFloor(ref cpuLevel, ref gpuLevel, cpuTemp);
                bool linkedFans = AppConfig.HasLinkedFanCurves();

                if (linkedFans)
                {
                    int linked = Math.Max(cpuLevel, gpuLevel);
                    cpuLevel = linked;
                    gpuLevel = linked;
                }

                if (cpuLevel <= 0 && gpuLevel <= 0)
                {
                    if (!softwareFanCurveAutoMode)
                    {
                        Program.acpi.SetFanLevel(0, 0);
                        // HP firmware can retain the last direct RPM target even after
                        // max-fan is disabled and the automatic fan mode is requested.
                        // Reapplying the active performance mode clears that target and
                        // hands fan control back to the BIOS curve.
                        Program.acpi.DeviceSet(HpACPI.PerformanceMode, Modes.GetCurrentBase(), "Software Fan Auto Restore");
                        softwareFanCurveAutoMode = true;
                        lastCpuFanLevel = 0;
                        lastGpuFanLevel = 0;
                    }
                    return;
                }

                if (cpuLevel == lastCpuFanLevel && gpuLevel == lastGpuFanLevel)
                {
                    return;
                }

                if (softwareFanCurveAutoMode)
                {
                    Program.acpi.SetFanMode(0x31);
                    softwareFanCurveAutoMode = false;
                }

                int result = Program.acpi.SetFanLevel((byte)cpuLevel, (byte)gpuLevel);
                if (result == 1)
                {
                    lastCpuFanLevel = cpuLevel;
                    lastGpuFanLevel = gpuLevel;
                    if (thermalSafetyActive)
                        Logger.WriteLine($"Software fan thermal safety applied: cpuTemp={cpuTemp:0.#} cpuLevel={cpuLevel} gpuLevel={gpuLevel}");
                    if (linkedFans)
                        Logger.WriteLine($"Software fan curve linked target applied: cpuTemp={cpuTemp:0.#} gpuTemp={gpuTemp:0.#} level={cpuLevel}");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Software fan curve exception: " + ex.Message);
            }
        }

        private static int EvaluateFanCurve(byte[] curve, float? temperature)
        {
            if (curve.Length < 16 || temperature is null) return 0;

            float temp = temperature.Value;
            if (temp <= curve[0]) return ScaleFanLevel(curve[8]);

            for (int i = 1; i < 8; i++)
            {
                float leftTemp = curve[i - 1];
                float rightTemp = curve[i];
                int leftFan = curve[i + 7];
                int rightFan = curve[i + 8];

                if (temp <= rightTemp)
                {
                    if (rightTemp <= leftTemp) return ScaleFanLevel(rightFan);
                    float ratio = (temp - leftTemp) / (rightTemp - leftTemp);
                    return ScaleFanLevel((int)Math.Round(leftFan + ((rightFan - leftFan) * ratio)));
                }
            }

            return ScaleFanLevel(curve[15]);
        }

        private static bool ApplyCpuThermalSafetyFloor(ref int cpuLevel, ref int gpuLevel, float? cpuTemp)
        {
            if (cpuTemp is null || cpuTemp.Value < CpuFanBoostTemp)
            {
                Volatile.Write(ref cpuHotSampleCount, 0);
                return false;
            }

            int maxLevel = Program.acpi.MaxFanLevel;
            if (cpuTemp.Value >= CpuImmediateMaxTemp)
            {
                Volatile.Write(ref cpuHotSampleCount, CpuHotSamplesRequired);
                cpuLevel = maxLevel;
                gpuLevel = maxLevel;
                return true;
            }

            if (Interlocked.Increment(ref cpuHotSampleCount) < CpuHotSamplesRequired) return false;

            if (cpuTemp.Value >= CpuFanMaxTemp)
            {
                // HP's dedicated maximum-fan command is used only when both targets are maxed.
                cpuLevel = maxLevel;
                gpuLevel = maxLevel;
                return true;
            }

            float ratio = (cpuTemp.Value - CpuFanBoostTemp) / (CpuFanMaxTemp - CpuFanBoostTemp);
            int floorPercent = (int)Math.Round(CpuHotFanFloorPercent + ((100 - CpuHotFanFloorPercent) * ratio));
            cpuLevel = Math.Max(cpuLevel, ScaleFanLevel(floorPercent));
            return true;
        }

        private static int ScaleFanLevel(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (percent <= 0) return 0;
            return Math.Max(1, (int)Math.Round(percent * Program.acpi.MaxFanLevel / 100.0));
        }

        private static int ApplySoftwareHysteresis(int desiredLevel, float? temperature, ref float? anchorTemp, int currentLevel)
        {
            if (temperature is null || currentLevel < 0)
            {
                anchorTemp = temperature;
                return desiredLevel;
            }

            anchorTemp ??= temperature;
            int hysteresisUp = Math.Max(0, AppConfig.GetMode("hysteresis_up"));
            int hysteresisDown = Math.Max(0, AppConfig.GetMode("hysteresis_down"));

            if (desiredLevel > currentLevel && hysteresisUp > 0 && temperature.Value < anchorTemp.Value + hysteresisUp)
                return currentLevel;

            if (desiredLevel < currentLevel && hysteresisDown > 0 && temperature.Value > anchorTemp.Value - hysteresisDown)
                return currentLevel;

            if (desiredLevel != currentLevel)
                anchorTemp = temperature;

            return desiredLevel;
        }

        public void AutoPower(bool launchAsAdmin = false)
        {

            customPower = 0;

            if (!AppConfig.SupportsPowerLimits())
            {
                SetGPUPower();
                return;
            }

            bool applyPower = AppConfig.IsApplyPower();
            bool applyFans = AppConfig.IsApplyFans();

            if (applyPower && !applyFans && AppConfig.IsFanRequired())
            {
                AutoFans(true);
                Thread.Sleep(500);
            }

            if (applyPower) SetPower(launchAsAdmin);

            Thread.Sleep(500);
            SetGPUPower();
            AutoRyzen();

            if (IsReapplyRyzenRequired())
                Task.Delay(5000).ContinueWith(_ => { AutoRyzen(); ReadRyzenLimits(); });

        }

        public void SetModeLabel()
        {
            settings.SetModeLabel(Properties.Strings.PerformanceMode + ": " + Modes.GetCurrentName() + (customFans ? "+" : "") + ((customPower > 0) ? " " + customPower + "W" : ""));
        }

        public void SetRyzenPower(bool init = false)
        {
            if (init) _ryzenPower = true;

            if (!_ryzenPower) return;
            if (!AppConfig.IsApplyPower()) return;

            var smu = GetSmu();
            if (smu == null) return;

            int limit_total = AppConfig.GetMode("limit_total");
            int limit_slow = AppConfig.GetMode("limit_slow", limit_total);
            int limit_fast = AppConfig.GetMode("limit_fast", limit_slow);

            if (limit_total > HpACPI.MaxTotal) return;
            if (limit_total < HpACPI.MinTotal) return;

            smu.SetAllLimits(limit_total, limit_fast, limit_slow,
                out SmuStatus stapm, out SmuStatus fast, out SmuStatus slow);
            if (init) Logger.WriteLine($"STAPM: {limit_total}W {stapm} | SLOW: {limit_slow}W {slow} | FAST: {limit_fast}W {fast}");
        }

        public void SetPower(bool launchAsAdmin = false)
        {
            if (!AppConfig.SupportsPowerLimits()) return;

            bool allAMD = Program.acpi.IsAllAmdPPT();
            bool isAMD = CpuInfo.IsAMD;

            int limit_total = AppConfig.GetMode("limit_total");
            int limit_cpu = AppConfig.GetMode("limit_cpu");
            int limit_slow = AppConfig.GetMode("limit_slow");
            int limit_fast = AppConfig.GetMode("limit_fast");

            if (limit_slow < 0 || allAMD) limit_slow = limit_total;

            // SPL and SPPT
            if (Program.acpi.IsSupported(HpACPI.PPT_APUA0))
            {
                if (IsValidPowerLimit(limit_total, HpACPI.MinTotal, HpACPI.MaxTotal)
                    && IsValidPowerLimit(limit_slow, HpACPI.MinTotal, HpACPI.MaxTotal))
                {
                    Program.acpi.DeviceSet(HpACPI.PPT_APUA3, limit_total, "PowerLimit A3");
                    Program.acpi.DeviceSet(HpACPI.PPT_APUA0, limit_slow, "PowerLimit A0");
                    customPower = limit_total;
                }
            }
            else if (isAMD)
            {
                if (!IsValidPowerLimit(limit_total, HpACPI.MinTotal, HpACPI.MaxTotal)) return;

                if (ProcessHelper.IsUserAdministrator())
                {
                    SetRyzenPower(true);
                }
                else if (launchAsAdmin)
                {
                    ProcessHelper.RunAsAdmin("cpu");
                    return;
                }
            }

            if (allAMD && Program.acpi.IsSupported(HpACPI.PPT_CPUB0)) // CPU limit all amd models
            {
                if (IsValidPowerLimit(limit_cpu, HpACPI.MinCPU, HpACPI.MaxCPU))
                {
                    Program.acpi.DeviceSet(HpACPI.PPT_CPUB0, limit_cpu, "PowerLimit B0");
                    customPower = limit_cpu;
                }
            }
            else if (isAMD && Program.acpi.IsSupported(HpACPI.PPT_APUC1)) // FPPT boost for non all-amd models
            {
                if (IsValidPowerLimit(limit_fast, HpACPI.MinTotal, HpACPI.MaxTotal))
                    Program.acpi.DeviceSet(HpACPI.PPT_APUC1, limit_fast, "PowerLimit C1");
            }

            SetModeLabel();

        }

        private static bool IsValidPowerLimit(int value, int min, int max)
        {
            return value >= min && value <= max;
        }

        public void SetGPUClocks(bool launchAsAdmin = true, bool reset = false)
        {
            Task.Run(() =>
            {

                int core = AppConfig.GetMode("gpu_core", 0);
                int memory = AppConfig.GetMode("gpu_memory", 0);
                int clock_limit = AppConfig.GetMode("gpu_clock_limit", 0);

                if (reset) core = memory = clock_limit = 0;

                bool hasClockSettings = AppConfig.Exists("gpu_core_" + Modes.GetCurrent())
                    || AppConfig.Exists("gpu_memory_" + Modes.GetCurrent())
                    || AppConfig.Exists("gpu_clock_limit_" + Modes.GetCurrent());
                if (!hasClockSettings && !reset) return;
                //if ((gpu_core > -5 && gpu_core < 5) && (gpu_memory > -5 && gpu_memory < 5)) launchAsAdmin = false;

                if (Program.acpi.DeviceGet(HpACPI.GPUEco) == 1) { Logger.WriteLine("Clocks: Eco"); return; }
                if (HardwareControl.GpuControl is null) { Logger.WriteLine("Clocks: NoGPUControl"); return; }
                if (!HardwareControl.GpuControl.SupportsGpuClockControl) { Logger.WriteLine("Clocks: UnsupportedGPU"); return; }

                IGpuControl gpuControl = HardwareControl.GpuControl;
                try
                {
                    int statusClocks = gpuControl.SetGpuClockOffsets(core, memory);
                    int statusLimit = gpuControl.SetMaxGpuClock(clock_limit);
                    if ((statusLimit != 0 || statusClocks != 0) && launchAsAdmin) ProcessHelper.RunAsAdmin("gpu");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Clocks Error:" + ex.ToString());
                }

                settings.GPUInit();
            });
        }

        public void SetGPUPower()
        {

            int gpu_boost = AppConfig.GetMode("gpu_boost");
            int gpu_temp = AppConfig.GetMode("gpu_temp");
            int gpu_power = AppConfig.GetMode("gpu_power");

            int boostResult = -1;

            if (gpu_power >= HpACPI.MinGPUPower && gpu_power <= HpACPI.MaxGPUPower && Program.acpi.IsSupported(HpACPI.GPU_POWER))
                Program.acpi.DeviceSet(HpACPI.GPU_POWER, gpu_power, "PowerLimit TGP (GPU VAR)");

            if (gpu_boost >= HpACPI.MinGPUBoost && gpu_boost <= HpACPI.MaxGPUBoost && Program.acpi.IsSupported(HpACPI.PPT_GPUC0))
                boostResult = Program.acpi.DeviceSet(HpACPI.PPT_GPUC0, gpu_boost, "PowerLimit C0 (GPU BOOST)");

            if (gpu_temp >= HpACPI.MinGPUTemp && gpu_temp <= HpACPI.MaxGPUTemp && Program.acpi.IsSupported(HpACPI.PPT_GPUC2))
                Program.acpi.DeviceSet(HpACPI.PPT_GPUC2, gpu_temp, "PowerLimit C2 (GPU TEMP)");

            // Fallback
            if (boostResult == 0 && Program.acpi.IsSupported(HpACPI.PPT_GPUC0))
                Program.acpi.DeviceSet(HpACPI.PPT_GPUC0, gpu_boost, "PowerLimit C0");

        }

        public SmuStatus? SetCPUTemp(int cpuTemp, bool log = false)
        {
            if (cpuTemp < CpuInfo.MinTemp || cpuTemp > CpuInfo.DefaultTemp) return null;
            if (cpuTemp == CpuInfo.DefaultTemp && _cpuTemp == CpuInfo.DefaultTemp) return null;

            var smu = GetSmu();
            if (smu == null) return null;
            SmuStatus status = smu.SetThm(cpuTemp);
            if (log) Logger.WriteLine($"CPU Temp: {cpuTemp}°C {status}");
            if (status == SmuStatus.OK) _cpuTemp = cpuTemp;
            return status;
        }

        public void SetUV(int cpuUV)
        {
            if (!CpuInfo.IsSupportedUV()) return;

            if (cpuUV >= CpuInfo.MinCPUUV && cpuUV <= CpuInfo.MaxCPUUV)
            {
                var smu = GetSmu();
                if (smu == null) return;
                SmuStatus status = smu.SetCoAll(cpuUV);
                Logger.WriteLine($"UV: {cpuUV} {status}");
                if (status == SmuStatus.OK) _cpuUV = cpuUV;
            }
        }

        public void SetUViGPU(int igpuUV)
        {
            if (!CpuInfo.IsSupportedUViGPU()) return;

            if (igpuUV >= CpuInfo.MinIGPUUV && igpuUV <= CpuInfo.MaxIGPUUV)
            {
                var smu = GetSmu();
                if (smu == null) return;
                SmuStatus status = smu.SetCoGfx(igpuUV);
                Logger.WriteLine($"iGPU UV: {igpuUV} {status}");
                if (status == SmuStatus.OK) _igpuUV = igpuUV;
            }
        }

        public string SetRyzen(bool launchAsAdmin = false)
        {
            if (!AppConfig.SupportsUndervolt()) return string.Empty;

            if (!ProcessHelper.IsUserAdministrator())
            {
                if (launchAsAdmin) ProcessHelper.RunAsAdmin("uv");
                return string.Empty;
            }

            var smu = GetSmu();
            if (smu == null) return string.Empty;

            var lines = new System.Text.StringBuilder();
            try
            {
                int cpuUV   = AppConfig.GetMode("cpu_uv",   0);
                int igpuUV  = AppConfig.GetMode("igpu_uv",  0);
                int cpuTemp = AppConfig.GetMode("cpu_temp");

                if (CpuInfo.IsSupportedUV() && cpuUV >= CpuInfo.MinCPUUV && cpuUV <= CpuInfo.MaxCPUUV)
                {
                    SmuStatus s = smu.SetCoAll(cpuUV);
                    Logger.WriteLine($"UV: {cpuUV} {s}");
                    if (s == SmuStatus.OK) _cpuUV = cpuUV;
                    lines.AppendLine($"CPU UV {cpuUV}: {s}");
                }

                if (CpuInfo.IsSupportedUViGPU() && igpuUV >= CpuInfo.MinIGPUUV && igpuUV <= CpuInfo.MaxIGPUUV)
                {
                    SmuStatus s = smu.SetCoGfx(igpuUV);
                    Logger.WriteLine($"iGPU UV: {igpuUV} {s}");
                    if (s == SmuStatus.OK) _igpuUV = igpuUV;
                    lines.AppendLine($"iGPU UV {igpuUV}: {s}");
                }

                SmuStatus? tempStatus = SetCPUTemp(cpuTemp, true);
                if (tempStatus.HasValue) lines.AppendLine($"CPU Temp {cpuTemp}°C: {tempStatus}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("UV Error: " + ex.ToString());
            }

            SetReapplyEnabled(AppConfig.IsApplyUV());
            return lines.ToString().TrimEnd();
        }

        public string ReadRyzenLimits()
        {
            var smu = GetSmu();
            if (smu == null) return string.Empty;

            try
            {
                PowerLimits? lim = smu.GetPowerLimits();
                if (lim == null) return string.Empty;

                string line = $"SPL: {lim.Stapm:F1}W | sPPT {lim.Slow:F1}W | fPPT {lim.Fast:F1}W";
                if (lim.ApuSlow.HasValue) line += $" | APU {lim.ApuSlow.Value:F1}W";
                line += $", Temp: {lim.TctlTemp:F0}°C";
                Logger.WriteLine("Ryzen Limits: " + line);
                return line;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("ReadRyzenLimits Error: " + ex.ToString());
                return string.Empty;
            }
        }

        public void ResetRyzen()
        {
            if (_cpuUV != 0) SetUV(0);
            if (_igpuUV != 0) SetUViGPU(0);
            if (_cpuTemp != CpuInfo.DefaultTemp) SetCPUTemp(CpuInfo.DefaultTemp, true);
            SetReapplyEnabled(false);
        }

        public void AutoRyzen()
        {
            if (!CpuInfo.IsAMD || !AppConfig.SupportsUndervolt()) return;

            if (AppConfig.IsApplyUV()) SetRyzen();
            else ResetRyzen();
        }

        public void AutoCPUTemp()
        {
            if (!CpuInfo.IsAMD) return;
            if (!AppConfig.IsApplyUV()) return;
            if (!ProcessHelper.IsUserAdministrator()) return;

            try
            {
                SetCPUTemp(AppConfig.GetMode("cpu_temp"), true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("AutoCPUTemp Error: " + ex.Message);
            }
        }

        public void ShutdownReset()
        {
            if (!AppConfig.IsShutdownReset()) return;
            Program.acpi.DeviceSet(HpACPI.PerformanceMode,HpACPI.PerformanceBalanced, "Mode Reset");
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;

            _modeCts.Cancel();
            _autoModeCts.Cancel();
            reapplyTimer?.Stop();
            reapplyTimer?.Dispose();
            modeToggleTimer?.Stop();
            modeToggleTimer?.Dispose();
            lock (fanCurveLock)
            {
                fanCurveTimer?.Stop();
                fanCurveTimer?.Dispose();
                fanCurveTimer = null;
            }
            lock (_smuLock)
            {
                _smu?.Dispose();
                _smu = null;
            }
        }

        public void SleepReset()
        {
            if (!AppConfig.IsSleepReset()) return;
            Program.acpi.DeviceSet(HpACPI.PerformanceMode, Modes.GetCurrentBase(), "Sleep Reset");
        }

    }
}
