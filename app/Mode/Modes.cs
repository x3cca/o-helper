namespace OHelper.Mode
{
    internal class Modes
    {
        static Dictionary<string, string> settings = new Dictionary<string, string>
        {
            { "mode_base", "_" },
            { "mode_name", "_" },
            { "powermode", "string" },
            { "limit_total", "int" },
            { "limit_slow", "int" },
            { "limit_fast", "int" },
            { "limit_cpu", "int" },
            { "fan_profile_cpu", "string" },
            { "fan_profile_gpu", "string" },
            { "fan_profile_mid", "string" }, 
            { "gpu_power", "int" },
            { "gpu_boost", "int" },
            { "gpu_temp", "int" },
            { "gpu_core", "int" },
            { "gpu_memory", "int" },
            { "gpu_clock_limit", "int" },
            { "cpu_temp", "_" },
            { "cpu_uv", "_" },
            { "cpu_uv_cores", "_" },
            { "igpu_uv", "_" },
            { "auto_boost", "int" },
            { "auto_apply", "int" },
            { "auto_apply_power", "int" },
            { "auto_uv", "_" },
            { "hysteresis_up", "int" },
            { "hysteresis_down", "int" }
        };

        const int maxModes = 20;

        public static Dictionary<int, string> GetDictonary()
        {
            Dictionary<int, string> modes = new Dictionary<int, string>
            {
              {2, Properties.Strings.Eco},
              {0, Properties.Strings.Balanced},
              {1, Properties.Strings.Performance},
              {4, Properties.Strings.Unleashed}
            };

            for (int i = 3; i < maxModes; i++)
            {
                if (i == 4) continue;
                if (Exists(i)) modes.Add(i, GetName(i));
            }

            return modes;
        }

        public static List<int> GetList()
        {
            List<int> modes = new() { 2, 0, 1, 4 };
            for (int i = 3; i < maxModes; i++)
            {
                if (i == 4) continue;
                if (Exists(i)) modes.Add(i);
            }

            return modes;
        }

        public static void Remove(int mode)
        {
            foreach (string clean in settings.Keys)
            {
                AppConfig.Remove(clean + "_" + mode);
            }
        }

        public static int Add()
        {
            int currentMode = GetCurrent();

            for (int i = 3; i < maxModes; i++)
            {
                if (Exists(i)) continue;

                AppConfig.Set("mode_base_" + i, GetCurrentBase());
                AppConfig.Set("mode_name_" + i, "Custom " + (i - 2));

                if (Exists(currentMode))
                {
                    foreach (var kvp in settings)
                    {
                        if (kvp.Value == "_") continue;

                        string sourceKey = kvp.Key + "_" + currentMode;
                        string targetKey = kvp.Key + "_" + i;

                        if (!AppConfig.Exists(sourceKey)) continue;

                        if (kvp.Value == "int")
                            AppConfig.Set(targetKey, AppConfig.Get(sourceKey));
                        else
                        {
                            string? value = AppConfig.GetString(sourceKey);
                            if (value is not null) AppConfig.Set(targetKey, value);
                        }
                    }
                }

                return i;
            }
            return -1;
        }


        public static int GetCurrent()
        {
            int mode = AppConfig.Get("performance_mode", HpACPI.PerformanceBalanced);
            return Exists(mode) ? mode : HpACPI.PerformanceBalanced;
        }

        public static bool IsCurrentCustom()
        {
            int mode = GetCurrent();
            return mode > 4 || (mode == 3);
        }

        public static void SetCurrent(int mode)
        {
            if (!Exists(mode)) mode = HpACPI.PerformanceBalanced;

            AppConfig.Set("performance_" + Program.PerformanceKey(), mode);
            AppConfig.Set("performance_mode", mode);
            AppConfig.Set("performance_mode_base", GetBase(mode));
            AppConfig.Flush();
        }

        public static int GetCurrentBase()
        {
            int baseMode = GetBase(GetCurrent());
            if (baseMode >= 0) return baseMode;

            baseMode = AppConfig.Get("performance_mode_base", HpACPI.PerformanceBalanced);
            return IsBaseMode(baseMode) ? baseMode : HpACPI.PerformanceBalanced;
        }

        private static bool IsBaseMode(int mode)
        {
            return mode >= HpACPI.PerformanceBalanced && mode <= HpACPI.PerformanceSilent
                || mode == HpACPI.PerformanceManual;
        }

        public static string GetCurrentName()
        {
            return GetName(GetCurrent());
        }

        public static bool Exists(int mode)
        {
            return GetBase(mode) >= 0;
        }

        public static int GetBase(int mode)
        {
            if (mode >= 0 && mode <= 2)
                return mode;
            if (mode == 4)
                return 4;
            else
                return AppConfig.Get("mode_base_" + mode);
        }

        public static string GetName(int mode)
        {
            switch (mode)
            {
                case 0:
                    return Properties.Strings.Balanced;
                case 1:
                    return Properties.Strings.Performance;
                case 2:
                    return Properties.Strings.Eco;
                case 4:
                    return Properties.Strings.Unleashed;
                default:
                    return AppConfig.GetString("mode_name_" + mode) ?? $"Custom {mode - 2}";
            }
        }


        public static int GetNext(bool back = false)
        {
            var modes = GetList();
            int index = modes.IndexOf(GetCurrent());

            if (back)
            {
                index--;
                if (index < 0) index = modes.Count - 1;
                return modes[index];
            }
            else
            {
                index++;
                if (index > modes.Count - 1) index = 0;
                return modes[index];
            }
        }
    }
}
