using OHelper.Helpers;
using OHelper.Mode;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class AppConfig
{

    private static string configFile;
    private static string fallbackConfigFile;

    private static Dictionary<string, object> config = new Dictionary<string, object>();
    private static System.Timers.Timer timer = new System.Timers.Timer(2000) { AutoReset = false };
    private static readonly object configLock = new();

    private static readonly JsonSerializerOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Regex KeyValueRegex = new(
        @"""((?:\\.|[^""\\])*)""\s*:\s*(""(?:\\.|[^""\\])*""|-?\d+(?:\.\d+)?|true|false|null)");

    static AppConfig()
    {
        string configName = "config.json";
        string appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OHelper");
        string appConfig = Path.Combine(appPath, configName);
        string startupConfig = Path.Combine(Application.StartupPath.Trim('\\'), configName);
        bool runningAsSystem = ProcessHelper.IsRunningAsSystem();

        fallbackConfigFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OHelper", configName);

        configFile = runningAsSystem && File.Exists(fallbackConfigFile) ? fallbackConfigFile
        : File.Exists(appConfig) ? appConfig
        : File.Exists(startupConfig) ? startupConfig
        : runningAsSystem ? fallbackConfigFile
        : appConfig;

        Logger.WriteLine($"Config path selected: {configFile}");
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);

        if (!TryLoadConfig(configFile) && !TryRecoverConfig(configFile) && !TryLoadConfig(configFile + ".bak") && !TryLoadConfig(fallbackConfigFile)) Init();

        timer.Elapsed += Timer_Elapsed;
    }

    private static bool TryLoadConfig(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path), LenientOptions);
            if (loaded is null) return false;
            config = loaded;
            Logger.WriteLine($"Config loaded from {path}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Broken config {path}: {ex.Message}");
            return false;
        }
    }

    private static bool TryRecoverConfig(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var pairs = new Dictionary<string, string>();
            foreach (Match m in KeyValueRegex.Matches(File.ReadAllText(path)))
                pairs["\"" + m.Groups[1].Value + "\""] = m.Groups[2].Value;

            if (pairs.Count == 0) return false;

            string rebuilt = "{" + string.Join(",", pairs.Select(p => p.Key + ":" + p.Value)) + "}";
            var recovered = JsonSerializer.Deserialize<Dictionary<string, object>>(rebuilt, LenientOptions);
            if (recovered is null) return false;
            config = recovered;
            Logger.WriteLine($"Recovered {pairs.Count} values from broken config {path}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Config recovery failed {path}: {ex.Message}");
            return false;
        }
    }

    private static void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Flush();
    }

    public static void Flush()
    {
        timer.Stop();
        string jsonString;
        lock (configLock) jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            WriteAtomic(configFile, jsonString);
            SyncFallbackConfig();
        }
        catch (Exception ex) { Logger.WriteLine("Config write failed: " + ex.Message); }
    }

    public static void Shutdown()
    {
        Flush();
        timer.Dispose();
    }

    public static string GetConfigPath() => configFile;

    private static void WriteAtomic(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Write))
            fs.Flush(flushToDisk: true);
        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak");
        else
            File.Move(tmp, path);
    }

    private static void SyncFallbackConfig()
    {
        if (fallbackConfigFile is null || fallbackConfigFile == configFile) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackConfigFile)!);
            File.Copy(configFile, fallbackConfigFile, overwrite: true);
        }
        catch (Exception)
        {
            //Logger.WriteLine("Can't sync fallback config: " + ex.Message);
        }
    }

    // Model Detection Routine

    private static readonly Lazy<string> _model =
        new Lazy<string>(LoadModel, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<(string Bios, string ModelShort)> _biosData =
        new Lazy<(string, string)>(LoadBios, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> _productId =
        new Lazy<string>(LoadProductId, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string LoadModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("Select * from Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                using (obj) return obj["Model"]?.ToString() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
        return string.Empty;
    }

    private static (string Bios, string ModelShort) LoadBios()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    string raw = obj["SMBIOSBIOSVersion"]?.ToString() ?? string.Empty;
                    string[] parts = raw.Split('.');
                    return parts.Length > 1 ? (parts[1], parts[0]) : (string.Empty, raw);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
        return (string.Empty, string.Empty);
    }

    private static string LoadProductId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    string product = obj["Product"]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(product))
                        return product;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"AppConfig: ProductId detection failed: {ex.Message}");
        }
        return string.Empty;
    }

    public static string GetModel() => _model.Value;

    public static string GetProductId() => _productId.Value;

    public static ModelCapabilities GetModelCapabilities()
    {
        string? forceFamily = Exists("force_family") ? GetString("force_family") : null;
        if (!string.IsNullOrEmpty(forceFamily))
        {
            var forcedFamily = forceFamily.ToLowerInvariant() switch
            {
                "omen" => OmenModelFamily.OMEN16,
                "omen_slim" => OmenModelFamily.OMEN16,
                "omen_max" => OmenModelFamily.OMEN2024Plus,
                "transcend" => OmenModelFamily.Transcend,
                "victus" => OmenModelFamily.Victus,
                "desktop" => OmenModelFamily.Desktop,
                _ => OmenModelFamily.Unknown
            };

            if (forcedFamily != OmenModelFamily.Unknown)
                return ModelCapabilityDatabase.GetCapabilitiesByFamily(forcedFamily);
        }

        var caps = ModelCapabilityDatabase.GetPreferredCapabilities(GetProductId(), GetModel());
        return caps ?? ModelCapabilityDatabase.GetCapabilities(GetProductId());
    }

    public static BatteryChargeLimitBackendKind GetBatteryChargeLimitBackend()
    {
        return GetModelCapabilities().BatteryChargeLimitBackend;
    }

    public static bool HasLinkedFanCurves()
    {
        return IsOmenTranscend14()
            || GetModelCapabilities().SupportsIndependentFanCurves == false;
    }

    public static bool SupportsFirmwareFanCurves()
    {
        var knownCapabilities = ModelCapabilityDatabase.GetPreferredCapabilities(GetProductId(), GetModel());
        if (knownCapabilities is not null)
            return knownCapabilities.SupportsFanCurves;

        // Unknown systems must not enable firmware-stored curve operations without
        // an explicit model capability entry.
        return false;
    }

    public static bool SupportsSoftwareFanCurves()
    {
        var knownCapabilities = ModelCapabilityDatabase.GetPreferredCapabilities(GetProductId(), GetModel());
        if (knownCapabilities is not null)
            return knownCapabilities.SupportsSoftwareFanCurves;

        // A read-only fan probe does not prove that target writes are accepted.
        // Unknown systems stay hidden until a model entry explicitly opts in.
        return false;
    }

    public static bool SupportsPerformanceModes()
        => Is("force_performance_modes") || GetModelCapabilities().SupportsPerformanceModes;

    public static bool SupportsPowerLimits()
        => Is("force_power_limits") || GetModelCapabilities().SupportsPowerLimits;

    public static bool SupportsUndervolt()
        => Is("force_undervolt") || GetModelCapabilities().SupportsUndervolt;

    public static bool SupportsRpmReadback()
        => Is("force_rpm_readback") || GetModelCapabilities().SupportsRpmReadback;

    public static OmenModelFamily GetModelFamily()
    {
        return GetModelCapabilities().Family;
    }

    public static (string, string) GetBiosAndModel() => (_biosData.Value.Bios, _biosData.Value.ModelShort);

    public static string GetModelShort()
    {
        string model = GetModel();
        int trim = model.LastIndexOf('_');
        return trim > 0 ? model[..trim] : model;
    }

    public static bool ContainsModel(string contains)
        => _model.Value.Contains(contains, StringComparison.OrdinalIgnoreCase);

    private static void Init()
    {
        config = new Dictionary<string, object>();
        config["performance_mode"] = 0;
        config["ui_mode"] = "windows";
        config["theme"] = "";
        string jsonString = JsonSerializer.Serialize(config);
        File.WriteAllText(configFile, jsonString);
    }

    public static bool Exists(string name)
    {
        lock (configLock) return config.ContainsKey(name);
    }

    public static int Get(string name, int empty = -1)
    {
        lock (configLock)
            return config.TryGetValue(name, out var val) && int.TryParse(val?.ToString(), out int result)
            ? result : empty;
    }

    public static bool Is(string name)
    {
        return Get(name) == 1;
    }

    public static bool IsNotFalse(string name)
    {
        return Get(name) != 0;
    }

    public static bool IsOnBattery(string zone)
    {
        return Get(zone + "_bat", Get(zone)) != 0;
    }

    public static string? GetString(string name, string? empty = null)
    {
        lock (configLock)
            return config.TryGetValue(name, out var val) ? val?.ToString() : empty;
    }

    private static void Write()
    {
        timer.Stop();
        timer.Start();
    }

    public static void Set(string name, int value)
    {
        lock (configLock) config[name] = value;
        Write();
    }

    public static void Set(string name, string value)
    {
        lock (configLock) config[name] = value;
        Write();
    }

    public static void Remove(string name)
    {
        lock (configLock) config.Remove(name);
        Write();
    }

    public static void RemoveMode(string name)
    {
        Remove(name + "_" + Modes.GetCurrent());
    }

    public static string GgetParamName(HpFan device, string paramName = "fan_profile")
    {
        int mode = Modes.GetCurrent();
        string name;

        switch (device)
        {
            case HpFan.GPU:
                name = "gpu";
                break;
            case HpFan.Mid:
                name = "mid";
                break;
            default:
                name = "cpu";
                break;
        }

        return paramName + "_" + name + "_" + mode;
    }

    public static byte[] GetFanConfig(HpFan device)
    {
        string? curveString = GetString(GgetParamName(device));

        if (curveString is not null)
            return StringToBytes(curveString);

        // No saved curve for this mode/device - fall back to the built-in default
        // so the correct per-mode curve is actually applied on a fresh install.
        return GetDefaultCurve(device);
    }

    public static void SetFanConfig(HpFan device, byte[] curve)
    {
        string bitCurve = BitConverter.ToString(curve);
        Set(GgetParamName(device), bitCurve);
    }

    public static byte[] StringToBytes(string str)
    {
        String[] arr = str.Split('-');
        byte[] array = new byte[arr.Length];
        for (int i = 0; i < arr.Length; i++) array[i] = Convert.ToByte(arr[i], 16);
        return array;
    }

    public static byte[] GetDefaultCurve(HpFan device)
    {
        int mode = Modes.GetCurrentBase();
        // Check if this is a Transcend model that should use the specific curves from the issue
        if (IsOmenTranscend())
        {
            switch (mode)
            {
                case HpACPI.PerformanceBalanced:
                    switch (device)
                    {
                        case HpFan.GPU:
                            return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                        default:
                            return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                    }
                case HpACPI.PerformanceTurbo:
                    switch (device)
                    {
                        case HpFan.GPU:
                            return StringToBytes("1E-32-3A-41-48-4E-55-64-16-1C-23-2D-3A-46-52-5C");
                        default:
                            return StringToBytes("1E-32-3A-41-48-4E-55-64-16-1C-23-2D-3A-46-52-5C");
                    }
                case HpACPI.PerformanceSilent:
                    switch (device)
                    {
                        case HpFan.GPU:
                            return StringToBytes("1E-32-3C-46-4E-55-5C-64-00-00-00-00-14-1E-26-2D");
                        default:
                            return StringToBytes("1E-32-3C-46-4E-55-5C-64-00-00-00-00-14-1E-26-2D");
                    }
                case HpACPI.PerformanceManual:
                    switch (device)
                    {
                        case HpFan.GPU:
                            return StringToBytes("1E-32-3A-41-48-4E-55-64-1C-26-30-3A-46-52-5C-64");
                        default:
                            return StringToBytes("1E-32-3A-41-48-4E-55-64-1C-26-30-3A-46-52-5C-64");
                    }
                default:
                    // Fallback to balanced for unknown cases
                    switch (device)
                    {
                        case HpFan.GPU:
                            return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                        default:
                            return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                    }
            }
        }

        switch (mode)
        {
            case HpACPI.PerformanceManual:
                switch (device)
                {
                    case HpFan.GPU:
                        return StringToBytes("1E-32-3A-41-48-4E-55-64-1C-26-30-3A-46-52-5C-64");
                    default:
                        return StringToBytes("1E-32-3A-41-48-4E-55-64-1C-26-30-3A-46-52-5C-64");
                }
            case HpACPI.PerformanceTurbo:
                switch (device)
                {
                    case HpFan.GPU:
                        return StringToBytes("1E-32-3A-41-48-4E-55-64-16-1C-23-2D-3A-46-52-5C");
                    default:
                        return StringToBytes("1E-32-3A-41-48-4E-55-64-16-1C-23-2D-3A-46-52-5C");
                }
            case HpACPI.PerformanceSilent:
                switch (device)
                {
                    case HpFan.GPU:
                        return StringToBytes("1E-32-3C-46-4E-55-5C-64-00-00-00-00-14-1E-26-2D");
                    default:
                        return StringToBytes("1E-32-3C-46-4E-55-5C-64-00-00-00-00-14-1E-26-2D");
                }
            default:
                switch (device)
                {
                    case HpFan.GPU:
                        return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                    default:
                        return StringToBytes("1E-32-3C-44-4B-52-5A-64-00-00-14-1E-28-32-3C-44");
                }
        }
    }

    public static string? GetModeString(string name)
    {
        return GetString(name + "_" + Modes.GetCurrent());
    }

    public static int GetMode(string name, int empty = -1)
    {
        return Get(name + "_" + Modes.GetCurrent(), empty);
    }

    public static bool IsMode(string name)
    {
        return Get(name + "_" + Modes.GetCurrent()) == 1;
    }

    public static void SetMode(string name, int value)
    {
        Set(name + "_" + Modes.GetCurrent(), value);
    }

    public static void SetMode(string name, string value)
    {
        Set(name + "_" + Modes.GetCurrent(), value);
    }

    public static bool IsHardwareFnLock()
    {
        return Is("force_fn_lock");
    }

    public static bool IsOLED()
    {
        return ContainsModel("OLED") || Is("force_oled");
    }

    public static bool IsNoOverdrive()
    {
        return Is("no_overdrive");
    }

    public static bool IsHardwareHotkeys()
    {
        return Is("hardware_hotkeys");
    }

    public static bool HasTabletMode()
    {
        return Is("tablet_mode");
    }

    public static bool IsAlwaysUltimate()
    {
        return Is("always_ultimate") || IsOmenAlwaysUltimate();
    }

    public static bool IsApplyPower() => IsMode("auto_apply_power");
    public static bool IsApplyFans() => IsMode("auto_apply");
    public static bool IsApplyUV() => IsMode("auto_uv");

    public static bool IsManualModeRequired()
    {
        if (!IsApplyPower()) return false;
        return Is("manual_mode");
    }

    public static bool IsResetRequired()
    {
        return Is("mode_reset");
    }

    public static bool IsFanRequired()
    {
        return Is("fan_required");
    }

    public static bool IsPowerRequired()
    {
        return Is("power_required");
    }

    public static bool IsModeReapplyRequired()
    {
        return Is("mode_reapply");
    }

    public static bool IsStandardModeFix()
    {
        return Is("shutdown_gpu");
    }

    public static bool IsShutdownReset()
    {
        return Is("shutdown_reset");
    }

    public static bool IsNVPlatform()
    {
        return Is("nv_platform");
    }

    public static bool IsForceSetGPUMode()
    {
        return Is("gpu_mode_force_set");
    }

    public static bool NoGpu()
    {
        return Is("no_gpu");
    }

    public static bool IsHardwareTouchpadToggle()
    {
        return Is("hardware_touchpad_toggle");
    }

    // HP OMEN MODEL DETECTION
    // ============================================

    // Base HP Omen detection
    public static bool IsOmen()
    {
        return ContainsModel("OMEN") || ContainsModel("Omen");
    }

    // OMEN Transcend series (thin-and-light, reduced thermal headroom)
    public static bool IsOmenTranscend()
    {
        return ContainsModel("Transcend") || ContainsModel("14-fb") || ContainsModel("16-wf");
    }

    // OMEN Transcend 14 specifically (4-zone RGB, no numpad)
    public static bool IsOmenTranscend14()
    {
        return IsOmenTranscend() && ContainsModel("14-fb");
    }

    // Refresh rate mode support (Auto/60Hz/120Hz/Dynamic) - Transcend 14 OLED 120Hz
    public static bool HasDisplayModes()
    {
        return GetModelCapabilities().SupportsDynamicRefresh
            || IsOmenTranscend14()
            || Is("force_dynamic_refresh");
    }

    // Any HP Omen keyboard that uses the WMI 0x20009 BIOS interface.
    // The runtime WMI probe in HpACPI.GetKeyboardType() / HasBacklight()
    // confirms the keyboard is actually present and reachable.
    public static bool IsOmenKeyboardSupported()
    {
        if (!IsKeyboardLightingControlEnabled()) return false;
        if (!IsOmen()) return false;

        // Victus and Desktop entries in the DB disable 4-zone/per-key RGB.
        var caps = GetModelCapabilities();
        if (!caps.HasKeyboardBacklight) return false;
        if (!caps.HasFourZoneRgb && !caps.HasPerKeyRgb) return false;

        // Respect the existing config escape hatch used to hide RGB controls.
        if (Is("no_rgb")) return false;

        return true;
    }

    public static bool IsKeyboardLightingControlEnabled()
    {
        return Is("enable_keyboard_lighting_control");
    }

    // OMEN Slim series (slim chassis, different fan curves)
    public static bool IsOmenSlim()
    {
        return ContainsModel("Slim 16") || ContainsModel("Slim");
    }

    // OMEN MAX series (flagship tier, higher TDP)
    public static bool IsOmenMax()
    {
        return ContainsModel("MAX") || ContainsModel("16-ah") || ContainsModel("16-ak");
    }

    // OMEN 16 standard series
    public static bool IsOmen16()
    {
        return (IsOmen() && ContainsModel("16-")) && !IsOmenMax() && !IsOmenSlim();
    }

    public static bool IsOmenAlwaysUltimate()
    {
        return IsOmenMax();
    }

    public static bool IsOmenSleepReset()
    {
        return IsOmenTranscend();
    }

    public static bool IsOmenChargeLimit6080()
    {
        return IsOmenTranscend();
    }

    public static bool IsBWIcon()
    {
        return Is("bw_icon");
    }

    public static bool IsOverlay()
    {
        return Is("overlay");
    }

    public static bool IsOverlayGameOnly()
    {
        return Is("overlay_game_only");
    }

    public static bool IsChargeLimit6080()
    {
        return Is("charge_limit_6080") || IsOmenChargeLimit6080();
    }

    // 2024 Models support Dynamic Lighting
    public static bool IsDynamicLighting()
    {
        return Is("dynamic_lighting");
    }

    public static bool IsDynamicLightingInit()
    {
        return Is("lighting_init");
    }

    public static bool IsForceMiniled()
    {
        return Is("force_miniled");
    }

    public static bool IsSleepReset()
    {
        return Is("sleep_reset") || IsOmenSleepReset();
    }

    public static bool IsAutoStatusLed()
    {
        return Is("auto_status_led");
    }

    public static bool IsClampFanDots()
    {
        return IsNotFalse("fan_clamp");
    }

    public static bool IsAutoASPM()
    {
        return IsNotFalse("aspm");
    }


}
