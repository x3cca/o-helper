using OHelper.Helpers;
using OHelper.Mode;
using Microsoft.Win32;
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
            config = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path), LenientOptions);
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
            config = JsonSerializer.Deserialize<Dictionary<string, object>>(rebuilt, LenientOptions);
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
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackConfigFile));
            File.Copy(configFile, fallbackConfigFile, overwrite: true);
        }
        catch (Exception ex)
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

    public static string GetString(string name, string empty = null)
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
            case HpFan.XGM:
                name = "xgm";
                break;
            default:
                name = "cpu";
                break;
        }

        return paramName + "_" + name + "_" + mode;
    }

    public static byte[] GetFanConfig(HpFan device)
    {
        string curveString = GetString(GgetParamName(device));

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
        byte[] curve;

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

    public static string GetModeString(string name)
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

    public static bool IsAlly()
    {
        return false;
    }

    public static bool IsAuraSync()
    {
        return Is("mouse_aura_sync");
    }

    public static bool NoMKeys()
    {
        return (ContainsModel("Z13") && !IsARCNM()) ||
        ContainsModel("FX706") ||
        ContainsModel("FA706") ||
        ContainsModel("FA506") ||
        ContainsModel("FX506") ||
        ContainsModel("Duo") ||
        ContainsModel("FX505");
    }

    public static bool IsARCNM()
    {
        return ContainsModel("GZ301VIC");
    }

    public static bool IsTUF()
    {
        return ContainsModel("TUF") || ContainsModel("TX Gaming") || ContainsModel("TX Air");
    }

    public static bool IsProArt()
    {
        return ContainsModel("ProArt");
    }

    public static bool IsVivoZenbook()
    {
        return ContainsModel("Vivobook") || ContainsModel("Zenbook") || ContainsModel("EXPERTBOOK") || ContainsModel(" V16") || ContainsModel("ASUSLaptop");
    }

    public static bool IsVivoZenPro()
    {
        return ContainsModel("Vivobook") || ContainsModel("Zenbook") || ContainsModel("ProArt") || ContainsModel("EXPERTBOOK") || ContainsModel(" V16") || ContainsModel("ASUSLaptop");
    }

    public static bool IsHardwareFnLock()
    {
        return IsVivoZenPro() || ContainsModel("GZ302EA");
    }

    // Devices with bugged bios command to change brightness
    public static bool SwappedBrightness()
    {
        return ContainsModel("FA506IEB") || ContainsModel("FA506IH") || ContainsModel("FA506IC") || ContainsModel("FA506II") || ContainsModel("FX506LU") || ContainsModel("FX506IC") || ContainsModel("FX506LH") || ContainsModel("FA506IV") || ContainsModel("FA706IC") || ContainsModel("FA706IH");
    }

    public static bool IsDUO()
    {
        return ContainsModel("Duo") || ContainsModel("GX550") || ContainsModel("GX551") || ContainsModel("GX650") || ContainsModel("UX840") || ContainsModel("UX482");
    }

    public static bool IsM4Button()
    {
        return IsDUO() || ContainsModel("GZ302EA");
    }

    // G14 2020 has no aura, but media keys instead
    public static bool NoAura()
    {
        return (ContainsModel("GA401I") && !ContainsModel("GA401IHR")) || ContainsModel("GA502IU") || ContainsModel("HN7306") || ContainsModel("M6500X");
    }

    public static bool MediaKeys()
    {
        return (ContainsModel("GA401I") && !ContainsModel("GA401IHR")) || ContainsModel("G712L") || ContainsModel("GX502L");
    }

    public static bool IsWhite()
    {
        return ContainsModel("GA401") || ContainsModel("FX517Z") || ContainsModel("FX516P") || ContainsModel("X13") || IsARCNM() || ContainsModel("FA617N") || ContainsModel("FA617X") || NoAura() || Is("no_rgb");
    }

    public static bool IsSleepBacklight()
    {
        return ContainsModel("FA617") || ContainsModel("FX507") || ContainsModel("FA507");
    }

    public static bool IsAnimeMatrix()
    {
        return ContainsModel("GA401") || ContainsModel("GA402") || ContainsModel("GU604V") || ContainsModel("GU604V") || ContainsModel("G835") || ContainsModel("G815") || ContainsModel("G635") || ContainsModel("G615");
    }

    public static bool IsSlash()
    {
        return ContainsModel("GA403") || ContainsModel("GU605") || ContainsModel("GA605") || IsSlashLong();
    }

    public static bool IsSlashLong()
    {
        return ContainsModel("GA405") || ContainsModel("GU405") || ContainsModel("GU606") || ContainsModel("GX651");
    }

    public static bool IsInvertedFNLock()
    {
        return ContainsModel("M140") || ContainsModel("S550") || ContainsModel("K650") || ContainsModel("P540") || IsTUF();
    }

    public static bool IsOLED()
    {
        return ContainsModel("OLED") || IsSlash() || ContainsModel("M7600") || ContainsModel("UX64") || ContainsModel("UX34") || ContainsModel("UX53") || ContainsModel("K360") || ContainsModel("X150") || ContainsModel("M340") || ContainsModel("M350") || ContainsModel("K650") || ContainsModel("UM53") || ContainsModel("K660") || ContainsModel("UX84") || ContainsModel("M650") || ContainsModel("M550") || ContainsModel("M540") || ContainsModel("K340") || ContainsModel("K350") || ContainsModel("M140") || ContainsModel("S540") || ContainsModel("S550") || ContainsModel("M7400") || ContainsModel("N650") || ContainsModel("HN7306") || ContainsModel("H760") || ContainsModel("UX5406") || ContainsModel("M5606") || ContainsModel("X513") || ContainsModel("N7400") || ContainsModel("UX760") || ContainsModel("Q530VJ") || _oledFromRegistry.Value;
    }

    private static readonly Lazy<bool> _oledFromRegistry = new(() =>
    {
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASUS\OLEDCare");
            return key is not null && Convert.ToInt32(key.GetValue("EnablePixelRefresh", 0)) != 0;
        }
        catch { return false; }
    });

    public static bool IsNoOverdrive()
    {
        return Is("no_overdrive");
    }

    public static bool IsStrix()
    {
        return ContainsModel("Strix") || ContainsModel("Scar") || ContainsModel("G703G");
    }

    public static bool IsEcoBootFix()
    {
        return ContainsModel("G635L") || ContainsModel("G615L") || ContainsModel("G835L") || ContainsModel("G815L") || ContainsModel("FA506");
    }

    public static bool IsBacklightZones()
    {
        return IsStrix() || IsZ13();
    }

    public static bool IsHardwareHotkeys()
    {
        return ContainsModel("FX506");
    }

    public static bool NoWMI()
    {
        return ContainsModel("GL704G") || ContainsModel("GM501G") || ContainsModel("GX501G");
    }

    public static bool IsNoDirectRGB()
    {
        return ContainsModel("GA503") || ContainsModel("G533Q") || ContainsModel("GU502") || IsSlash();
    }

    public static bool IsStrixNumpad()
    {
        return ContainsModel("G713R");
    }

    public static bool IsStrix4ZoneFlipped()
    {
        return ContainsModel("G513");
    }

    public static bool IsZ1325()
    {
        return ContainsModel("GZ302E");
    }

    public static bool IsZ13()
    {
        return ContainsModel("Z13");
    }

    public static bool HasRearLight()
    {
        return IsZ13();
    }

    public static bool IsPZ13()
    {
        return ContainsModel("PZ13");
    }

    public static bool IsS17()
    {
        return ContainsModel("S17");
    }

    public static bool HasTabletMode()
    {
        return ContainsModel("X16") || ContainsModel("X13") || ContainsModel("Z13");
    }

    public static bool IsX13()
    {
        return ContainsModel("X13");
    }

    public static bool IsG14AMD()
    {
        return ContainsModel("GA402R");
    }

    public static bool DynamicBoost5()
    {
        return ContainsModel("GZ301ZE");
    }

    public static bool DynamicBoost15()
    {
        return ContainsModel("FX507ZC4") || ContainsModel("GA403UM") || ContainsModel("GU605CP") || ContainsModel("FX608J") || ContainsModel("FX608L") || ContainsModel("FA608U") || ContainsModel("FA608P") ||
        ContainsModel("FA401K") || ContainsModel("FA401UM") || ContainsModel("FA401UH");
    }

    public static bool DynamicBoost20()
    {
        return ContainsModel("GU605") || ContainsModel("GA605");
    }

    public static bool IsAdvantageEdition()
    {
        return ContainsModel("13QY");
    }

    public static bool IsAlwaysUltimate()
    {
        return ContainsModel("FA507NUR") || ContainsModel("FA506NCR") || ContainsModel("FA507NVR");
    }

    public static bool IsApplyPower() => IsMode("auto_apply_power");
    public static bool IsApplyFans() => IsMode("auto_apply");
    public static bool IsApplyUV() => IsMode("auto_uv");

    public static bool IsManualModeRequired()
    {
        if (!IsApplyPower()) return false;
        return Is("manual_mode") || ContainsModel("G733");
    }

    public static bool IsResetRequired()
    {
        return ContainsModel("GA403UI") || ContainsModel("GA403UU") || ContainsModel("GA403UV") || ContainsModel("FA507XV");
    }

    public static bool IsFanRequired()
    {
        return ContainsModel("GA402X") || ContainsModel("GU604") || ContainsModel("G513") || ContainsModel("G713R") || ContainsModel("G713P") || ContainsModel("GU605") || ContainsModel("GA605") || ContainsModel("G634J") || ContainsModel("G834J") || ContainsModel("G614J") || ContainsModel("G814J") || ContainsModel("FX507V") || ContainsModel("FX507ZV") || ContainsModel("FX608") || ContainsModel("FA608P") || ContainsModel("G614F") || ContainsModel("G614R") || ContainsModel("G733") || ContainsModel("H7606");
    }

    public static bool IsCPULight()
    {
        return ContainsModel("GA402X") || ContainsModel("GA605") || ContainsModel("GA403") || ContainsModel("FA507N") || ContainsModel("FA507X") || ContainsModel("FA707N") || ContainsModel("FA707X") || ContainsModel("GZ302") || ContainsModel("GU405") || ContainsModel("GX651");
    }

    public static bool IsPowerRequired()
    {
        return ContainsModel("GU605M") || ContainsModel("FX507") || ContainsModel("FX517") || ContainsModel("FX707");
    }

    public static bool IsModeReapplyRequired()
    {
        return Is("mode_reapply") || ContainsModel("FA401") || ContainsModel("GA403");
    }

    public static bool IsStandardModeFix()
    {
        return Is("shutdown_gpu") || ((ContainsModel("FX506HC") || ContainsModel("FA808U")) && IsNotFalse("shutdown_gpu"));
    }

    public static bool IsShutdownReset()
    {
        return Is("shutdown_reset") || ContainsModel("FX507Z");
    }

    public static bool IsNVPlatform()
    {
        return Is("nv_platform");
    }

    public static bool IsForceSetGPUMode()
    {
        return Is("gpu_mode_force_set") || (ContainsModel("503") && IsNotFalse("gpu_mode_force_set"));
    }

    public static bool IsAMDiGPU()
    {
        return ContainsModel("GV301RA") || ContainsModel("GV302XA") || ContainsModel("GZ302") || IsOnlyAIMAX() || IsAlly();
    }

    public static bool NoGpu()
    {
        return Is("no_gpu") || ContainsModel("UX540") || ContainsModel("M560") || ContainsModel("GZ302") || IsOnlyAIMAX();
    }

    public static bool IsOnlyAIMAX()
    {
        return ContainsModel("FA401EA") || ContainsModel("HN7306EA");
    }

    public static bool IsHardwareTouchpadToggle()
    {
        return ContainsModel("FA507");
    }

    public static bool IsIntelHX()
    {
        return ContainsModel("G814") || ContainsModel("G614") || ContainsModel("G834") || ContainsModel("G634") || ContainsModel("G835") || ContainsModel("G635") || ContainsModel("G815") || ContainsModel("G615");
    }

    public static bool Is8Ecores()
    {
        return ContainsModel("FX507Z") || ContainsModel("GU603ZV");
    }

    public static bool IsNoFNV()
    {
        return ContainsModel("FX507") || ContainsModel("FX707");
    }

    public static bool IsROG()
    {
        return false;
    }
    public static bool IsASUS()
    {
        return false;
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

    // OMEN 4-zone RGB keyboards
    public static bool IsOmen4ZoneRGB()
    {
        // Trust the per-model capability database first
        var caps = GetModelCapabilities();
        if (caps.HasFourZoneRgb || caps.HasPerKeyRgb)
            return true;

        // Fallback: Transcend 14 string match (covers models not yet in the DB)
        return IsOmenTranscend14();
    }

    // Any HP Omen keyboard that uses the WMI 0x20009 BIOS interface.
    // Used to decide whether to show the Omen keyboard lighting panel
    // instead of the ASUS Aura HID path. The runtime WMI probe in
    // HpACPI.GetKeyboardType() / HasBacklight() confirms the keyboard
    // is actually present and reachable.
    public static bool IsOmenKeyboardSupported()
    {
        if (!IsKeyboardLightingControlEnabled()) return false;
        if (!IsOmen()) return false;

        // Victus and Desktop entries in the DB disable 4-zone/per-key RGB.
        var caps = GetModelCapabilities();
        if (!caps.HasKeyboardBacklight) return false;
        if (!caps.HasFourZoneRgb && !caps.HasPerKeyRgb) return false;

        // Respect the existing config escape hatch used to hide the
        // ASUS Aura panel; reuse it for the Omen panel as well.
        if (Is("no_rgb")) return false;

        return true;
    }

    public static bool IsKeyboardLightingControlEnabled()
    {
        return Is("enable_keyboard_lighting_control");
    }

    public static bool IsOmenKeyboardRgb()
    {
        return IsOmenKeyboardSupported() && IsOmen4ZoneRGB();
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

    // Check if this is an HP system (for feature gating)
    public static bool IsHP()
    {
        return IsOmen() || ContainsModel("HP");
    }

    // Workaround gates for HP models
    public static bool HasOmenLightBoost()
    {
        return IsOmen() && !Is("no_overdrive");
    }

    public static bool IsOmenFanControllable()
    {
        if (!IsOmen()) return false;
        var caps = GetModelCapabilities();
        return caps.SupportsFanControlWmi || caps.SupportsFanControlEc;
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

    public static bool IsStopAC()
    {
        return IsAlly() || Is("stop_ac");
    }

    public static bool IsChargeLimit6080()
    {
        return ContainsModel("GU405") || ContainsModel("GU606") || ContainsModel("H760") || ContainsModel("GA403") || ContainsModel("GU605") || ContainsModel("GA605") || ContainsModel("GA503R") || (IsTUF() && !(ContainsModel("FX507Z") || ContainsModel("FA617") || ContainsModel("FA607")));

    }

    // 2024 Models support Dynamic Lighting
    public static bool IsDynamicLighting()
    {
        return IsSlash() || IsIntelHX() || IsTUF() || IsZ13();
    }

    public static bool IsDynamicLightingOnly()
    {
        return ContainsModel("S560") || ContainsModel("M540") || ContainsModel("UX760");
    }

    public static bool IsDynamicLightingInit()
    {
        return ContainsModel("FA608") || Is("lighting_init");
    }

    public static bool IsForceMiniled()
    {
        return ContainsModel("G834JYR") || ContainsModel("G834JZR") || ContainsModel("G634JZR") || ContainsModel("G835LW") || ContainsModel("G835LX") || ContainsModel("G635LW") || ContainsModel("G635LX") || Is("force_miniled");
    }

    public static bool IsKeystone()
    {
        return ContainsModel("G531") || ContainsModel("G731") ||
               ContainsModel("G532") || ContainsModel("G732") ||
               ContainsModel("G533") || ContainsModel("G733");
    }

    public static bool IsSleepReset()
    {
        return Is("sleep_reset") || ContainsModel("GU605MI") || ContainsModel("GU605MV");
    }

    public static bool SaveDimming()
    {
        return Is("save_dimming");
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
