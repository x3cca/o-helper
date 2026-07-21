public enum OmenModelFamily
{
    Unknown = 0,
    OMEN16,
    OMEN17,
    Victus,
    Transcend,
    OMEN2024Plus,
    Desktop,
    Legacy
}

public enum BatteryChargeLimitBackendKind
{
    None = 0,
    HpBatteryCare
}

public class ModelCapabilities
{
    public string ProductId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string? ModelNamePattern { get; set; }
    public int ModelYear { get; set; }
    public OmenModelFamily Family { get; set; } = OmenModelFamily.Unknown;

    public bool SupportsFanControlWmi { get; set; } = true;
    public bool SupportsFanControlEc { get; set; } = true;
    // Legacy name retained for the firmware-stored curve path. False means that
    // firmware curve read/write and calibration support is not confirmed.
    public bool SupportsFanCurves { get; set; } = true;
    private bool? _supportsSoftwareFanCurves;
    public bool SupportsSoftwareFanCurves
    {
        // Preserve existing WMI-capable model entries until they explicitly
        // split the software target loop from firmware-stored curve support.
        get => _supportsSoftwareFanCurves ?? (SupportsFanCurves && SupportsFanControlWmi);
        set => _supportsSoftwareFanCurves = value;
    }
    public bool SupportsIndependentFanCurves { get; set; } = true;
    public bool SupportsRpmReadback { get; set; } = true;
    public int FanZoneCount { get; set; } = 2;
    public int? MaxFanLevel { get; set; }

    public bool SupportsPerformanceModes { get; set; } = true;
    public string[] PerformanceModes { get; set; } = new[] { "Default", "Performance", "Cool" };

    public bool HasMuxSwitch { get; set; } = false;
    public bool SupportsGpuPowerBoost { get; set; } = true;
    public bool SupportsDynamicRefresh { get; set; } = false;

    public bool HasKeyboardBacklight { get; set; } = true;
    public bool HasFourZoneRgb { get; set; } = true;
    public bool HasPerKeyRgb { get; set; } = false;

    public bool SupportsUndervolt { get; set; } = true;
    public bool SupportsPowerLimits { get; set; } = true;
    public BatteryChargeLimitBackendKind BatteryChargeLimitBackend { get; set; } = BatteryChargeLimitBackendKind.None;

    public int? PerformanceCpuPl1Watts { get; set; }
    public int? PerformanceCpuPl2Watts { get; set; }
    public int? BalancedCpuPl1Watts { get; set; }
    public int? EcoCpuPl1Watts { get; set; }
    public int? PerformanceGpuTgpWatts { get; set; }
    public int? BalancedGpuTgpWatts { get; set; }

    public bool SupportsOverboost { get; set; } = false;
    public string? Notes { get; set; }
    public bool UserVerified { get; set; } = false;
}

public static class ModelCapabilityDatabase
{
    private static readonly Dictionary<string, ModelCapabilities> _knownModels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _ambiguousProductIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "8BB1"
    };
    private static bool _initialized;

    public static ModelCapabilities DefaultCapabilities { get; } = new ModelCapabilities
    {
        ProductId = "DEFAULT",
        ModelName = "Unknown OMEN",
        ModelYear = 0,
        Family = OmenModelFamily.Unknown,
        SupportsFanControlWmi = false,
        SupportsFanControlEc = false,
        SupportsFanCurves = false,
        SupportsSoftwareFanCurves = false,
        SupportsIndependentFanCurves = false,
        SupportsRpmReadback = true,
        FanZoneCount = 0,
        MaxFanLevel = null,
        SupportsPerformanceModes = false,
        PerformanceModes = Array.Empty<string>(),
        SupportsGpuPowerBoost = false,
        HasKeyboardBacklight = false,
        HasFourZoneRgb = false,
        HasPerKeyRgb = false,
        SupportsUndervolt = false,
        SupportsPowerLimits = false,
        Notes = "Unknown model: write-capable hardware features remain disabled until explicitly confirmed"
    };

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        InitializeDatabase();
    }

    private static void InitializeDatabase()
    {
        // OMEN 15 (2020-2021)
        AddModel(new ModelCapabilities
        {
            ProductId = "8A14",
            ModelName = "OMEN 15 (2020)",
            ModelYear = 2020,
            Family = OmenModelFamily.Legacy,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8A15",
            ModelName = "OMEN 15 (2020)",
            ModelYear = 2020,
            Family = OmenModelFamily.Legacy,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8574",
            ModelName = "OMEN 15 (2020)",
            ModelYear = 2020,
            Family = OmenModelFamily.Legacy,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8787",
            ModelName = "OMEN 15 (2020)",
            ModelYear = 2020,
            Family = OmenModelFamily.Legacy,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "88D2",
            ModelName = "OMEN 15 (2021)",
            ModelYear = 2021,
            Family = OmenModelFamily.Legacy,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BAD",
            ModelName = "OMEN 15 (2021)",
            ModelYear = 2021,
            Family = OmenModelFamily.Legacy,
        });

        // OMEN 16 (2021-2025)
        AddModel(new ModelCapabilities
        {
            ProductId = "8BAF",
            ModelName = "OMEN 16 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BB0",
            ModelName = "OMEN 16 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8CD0",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8CD1",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8A44",
            ModelName = "OMEN 16 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8A43",
            ModelName = "OMEN 16 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BCA",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BAB",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8C76",
            ModelName = "OMEN 16 (2024)",
            ModelYear = 2024,
            Family = OmenModelFamily.OMEN16,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = true,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8B2J",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BCD",
            ModelName = "OMEN 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN16,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8D24",
            ModelName = "OMEN 16 (2024)",
            ModelYear = 2024,
            Family = OmenModelFamily.OMEN2024Plus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8E35",
            ModelName = "OMEN 16 (2024)",
            ModelYear = 2024,
            Family = OmenModelFamily.OMEN2024Plus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8D2F",
            ModelName = "OMEN 16 (2025)",
            ModelYear = 2025,
            Family = OmenModelFamily.OMEN2024Plus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
        });

        // OMEN MAX 16 (2025)
        AddModel(new ModelCapabilities
        {
            ProductId = "8D41",
            ModelName = "OMEN MAX 16 (2025)",
            ModelYear = 2025,
            Family = OmenModelFamily.OMEN2024Plus,
            FanZoneCount = 3,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
            SupportsOverboost = true,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8D42",
            ModelName = "OMEN MAX 16 (2025)",
            ModelYear = 2025,
            Family = OmenModelFamily.OMEN2024Plus,
            FanZoneCount = 3,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
            SupportsOverboost = true,
        });

        // OMEN 17 (2021-2023)
        AddModel(new ModelCapabilities
        {
            ProductId = "8BB1",
            ModelName = "OMEN 17 (2022)",
            ModelNamePattern = "17-ck2",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN17,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8B9D",
            ModelName = "OMEN 17 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.OMEN17,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8B9E",
            ModelName = "OMEN 17 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.OMEN17,
        });

        // OMEN Transcend
        AddModel(new ModelCapabilities
        {
            ProductId = "8C3A",
            ModelName = "OMEN Transcend 14 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.Transcend,
            SupportsFanControlWmi = false,
            SupportsFanControlEc = true,
            SupportsFanCurves = true,
            SupportsIndependentFanCurves = false,
            FanZoneCount = 1,
            MaxFanLevel = 65,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            SupportsDynamicRefresh = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
            Notes = "Transcend uses different WMI interface - may require OGH proxy for fan control"
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8C3B",
            ModelName = "OMEN Transcend 16 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.Transcend,
            SupportsFanControlWmi = false,
            SupportsFanControlEc = true,
            SupportsFanCurves = true,
            HasMuxSwitch = true,
            HasFourZoneRgb = false,
            HasPerKeyRgb = true,
            Notes = "Transcend uses different WMI interface - may require OGH proxy for fan control"
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8C58",
            ModelName = "OMEN Transcend 14 (2024) fb1xxx",
            ModelNamePattern = "14-fb1",
            ModelYear = 2024,
            Family = OmenModelFamily.Transcend,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            SupportsIndependentFanCurves = false,
            FanZoneCount = 1,
            MaxFanLevel = 65,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            SupportsDynamicRefresh = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
            SupportsUndervolt = false,
            UserVerified = false,
            Notes = "Transcend 14 board family (8C58). Prefer WMI BIOS paths; direct legacy EC writes are unverified."
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8E41",
            ModelName = "OMEN Transcend 14 (2024) fb1xxx",
            ModelNamePattern = "14-fb1",
            ModelYear = 2024,
            Family = OmenModelFamily.Transcend,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            SupportsSoftwareFanCurves = true,
            SupportsIndependentFanCurves = false,
            FanZoneCount = 1,
            MaxFanLevel = 65,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
            BatteryChargeLimitBackend = BatteryChargeLimitBackendKind.HpBatteryCare,
            SupportsUndervolt = false,
            UserVerified = false,
            Notes = "Transcend 14-fb1xxx. Windows field report confirms WMI V1 behavior; use the software WMI fan target loop, avoid legacy EC writes and firmware-stored curve operations."
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8F2A",
            ModelName = "OMEN Transcend 14 (2025) fb2xxx",
            ModelNamePattern = "14-fb2",
            ModelYear = 2025,
            Family = OmenModelFamily.Transcend,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            SupportsIndependentFanCurves = false,
            FanZoneCount = 1,
            MaxFanLevel = 65,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            SupportsDynamicRefresh = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = false,
            SupportsUndervolt = false,
            UserVerified = false,
            Notes = "Transcend 14 (2025). 4-zone RGB keyboard, no per-key. WMI BIOS paths."
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8D41",
            ModelName = "OMEN MAX 16 (2025) ah0xxx Intel",
            ModelNamePattern = "16-ah0",
            ModelYear = 2025,
            Family = OmenModelFamily.OMEN2024Plus,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            SupportsIndependentFanCurves = false,
            SupportsRpmReadback = true,
            FanZoneCount = 2,
            MaxFanLevel = 60,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = true,
            SupportsOverboost = true,
            UserVerified = true,
            Notes = "OMEN MAX 16 ah0xxx uses WMI fan control with a 60-level ceiling."
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8D87",
            ModelName = "OMEN MAX 16 (2025) ak0xxx AMD",
            ModelNamePattern = "16-ak0",
            ModelYear = 2025,
            Family = OmenModelFamily.OMEN2024Plus,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = true,
            SupportsIndependentFanCurves = false,
            SupportsRpmReadback = true,
            FanZoneCount = 2,
            MaxFanLevel = 60,
            HasMuxSwitch = true,
            SupportsGpuPowerBoost = true,
            HasFourZoneRgb = true,
            HasPerKeyRgb = true,
            SupportsUndervolt = false,
            UserVerified = false,
            Notes = "OMEN MAX 16 ak0xxx uses WMI fan control with a 60-level ceiling."
        });

        // HP Victus
        AddModel(new ModelCapabilities
        {
            ProductId = "88D9",
            ModelName = "HP Victus 15 (2021)",
            ModelYear = 2021,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanControlWmi = true,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "88DA",
            ModelName = "HP Victus 16 (2021)",
            ModelYear = 2021,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanControlWmi = true,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8A3E",
            ModelName = "HP Victus 15 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8C30",
            ModelName = "HP Victus 15 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8A26",
            ModelName = "HP Victus 16 (2022)",
            ModelYear = 2022,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8BD4",
            ModelName = "HP Victus 15 (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "8C2F",
            ModelName = "HP Victus (2023)",
            ModelYear = 2023,
            Family = OmenModelFamily.Victus,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
            SupportsFanCurves = false,
            SupportsGpuPowerBoost = false,
        });

        // OMEN Desktop
        AddModel(new ModelCapabilities
        {
            ProductId = "DESKTOP-25L",
            ModelName = "OMEN 25L Desktop",
            ModelYear = 2020,
            Family = OmenModelFamily.Desktop,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "DESKTOP-30L",
            ModelName = "OMEN 30L Desktop",
            ModelYear = 2020,
            Family = OmenModelFamily.Desktop,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "DESKTOP-40L",
            ModelName = "OMEN 40L Desktop",
            ModelYear = 2022,
            Family = OmenModelFamily.Desktop,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
        });

        AddModel(new ModelCapabilities
        {
            ProductId = "DESKTOP-45L",
            ModelName = "OMEN 45L Desktop",
            ModelYear = 2022,
            Family = OmenModelFamily.Desktop,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            SupportsFanCurves = false,
            HasFourZoneRgb = false,
            HasPerKeyRgb = false,
        });
    }

    private static void AddModel(ModelCapabilities model)
    {
        _knownModels[model.ProductId] = model;
    }

    public static ModelCapabilities GetCapabilities(string? productId)
    {
        EnsureInitialized();

        if (!string.IsNullOrEmpty(productId) && _knownModels.TryGetValue(productId, out var caps))
            return caps;

        return DefaultCapabilities;
    }

    public static ModelCapabilities? GetPreferredCapabilities(string? productId, string? wmiModelName)
    {
        EnsureInitialized();

        if (!string.IsNullOrEmpty(productId) && _knownModels.TryGetValue(productId, out var exact))
        {
            if (_ambiguousProductIds.Contains(productId))
            {
                var byName = GetCapabilitiesByModelName(wmiModelName);
                if (byName != null) return byName;
            }
            return exact;
        }

        return GetCapabilitiesByModelName(wmiModelName);
    }

    public static ModelCapabilities? GetCapabilitiesByModelName(string? wmiModelName)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(wmiModelName)) return null;

        foreach (var model in _knownModels.Values)
        {
            if (!string.IsNullOrEmpty(model.ModelNamePattern) &&
                wmiModelName.Contains(model.ModelNamePattern, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return null;
    }

    public static ModelCapabilities GetCapabilitiesByFamily(OmenModelFamily family)
    {
        EnsureInitialized();

        foreach (var model in _knownModels.Values)
        {
            if (model.Family == family)
            {
                var clone = new ModelCapabilities
                {
                    ProductId = $"FAMILY_{family}",
                    ModelName = model.ModelName,
                    ModelYear = model.ModelYear,
                    Family = model.Family,
                    SupportsFanControlWmi = model.SupportsFanControlWmi,
                    SupportsFanControlEc = model.SupportsFanControlEc,
                    SupportsFanCurves = model.SupportsFanCurves,
                    SupportsSoftwareFanCurves = model.SupportsSoftwareFanCurves,
                    SupportsIndependentFanCurves = model.SupportsIndependentFanCurves,
                    SupportsRpmReadback = model.SupportsRpmReadback,
                    FanZoneCount = model.FanZoneCount,
                    MaxFanLevel = model.MaxFanLevel,
                    SupportsPerformanceModes = model.SupportsPerformanceModes,
                    PerformanceModes = model.PerformanceModes,
                    HasMuxSwitch = model.HasMuxSwitch,
                    SupportsGpuPowerBoost = model.SupportsGpuPowerBoost,
                    SupportsDynamicRefresh = model.SupportsDynamicRefresh,
                    HasKeyboardBacklight = model.HasKeyboardBacklight,
                    HasFourZoneRgb = model.HasFourZoneRgb,
                    HasPerKeyRgb = model.HasPerKeyRgb,
                    SupportsUndervolt = model.SupportsUndervolt,
                    SupportsPowerLimits = model.SupportsPowerLimits,
                    SupportsOverboost = model.SupportsOverboost,
                    Notes = $"Family defaults for {family}",
                };
                return clone;
            }
        }

        return DefaultCapabilities;
    }

    public static bool IsKnownModel(string? productId)
    {
        EnsureInitialized();
        return !string.IsNullOrEmpty(productId) && _knownModels.ContainsKey(productId);
    }

    public static bool IsAmbiguousProductId(string? productId)
    {
        return !string.IsNullOrEmpty(productId) && _ambiguousProductIds.Contains(productId);
    }

    public static IReadOnlyCollection<ModelCapabilities> GetAllModels()
    {
        EnsureInitialized();
        return _knownModels.Values;
    }

    public static IEnumerable<ModelCapabilities> GetModelsByFamily(OmenModelFamily family)
    {
        EnsureInitialized();
        return _knownModels.Values.Where(m => m.Family == family);
    }
}
