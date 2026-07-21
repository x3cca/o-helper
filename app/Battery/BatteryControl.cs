using OHelper.Helpers;
using System.Diagnostics;

namespace OHelper.Battery
{
    public static class BatteryControl
    {
        // HP exposes Battery Care as an on/off firmware feature, not a percentage limit.
        public const int HpBatteryCareLimit = 80;
        public const int FullChargeLimit = 100;

        static bool _chargeFull = AppConfig.Is("charge_full");
        public static bool chargeFull
        {
            get
            {
                return _chargeFull;
            }
            set
            {
                AppConfig.Set("charge_full", value ? 1 : 0);
                _chargeFull = value;
            }
        }

        public static void ToggleBatteryLimitFull()
        {
            if (chargeFull)
            {
                int limit = AppConfig.GetBatteryChargeLimitBackend() == BatteryChargeLimitBackendKind.HpBatteryCare
                    ? HpBatteryCareLimit
                    : -1;
                SetBatteryChargeLimit(limit);
            }
            else SetBatteryLimitFull();
        }

        public static void SetBatteryLimitFull()
        {
            var backend = AppConfig.GetBatteryChargeLimitBackend();
            if (backend == BatteryChargeLimitBackendKind.None) return;

            chargeFull = true;
            if (backend == BatteryChargeLimitBackendKind.HpBatteryCare)
            {
                AppConfig.Set("charge_limit", FullChargeLimit);
                Program.acpi.DeviceSet(HpACPI.BatteryLimit, 0, "BatteryLimit");
                AppConfig.Flush();
            }
            else
            {
                Program.acpi.DeviceSet(HpACPI.BatteryLimit, FullChargeLimit, "BatteryLimit");
            }
            Program.settingsForm.VisualiseBatteryFull();
        }

        public static void UnSetBatteryLimitFull()
        {
            var backend = AppConfig.GetBatteryChargeLimitBackend();
            if (backend == BatteryChargeLimitBackendKind.None) return;

            chargeFull = false;
            if (backend == BatteryChargeLimitBackendKind.HpBatteryCare)
            {
                AppConfig.Set("charge_limit", HpBatteryCareLimit);
                Program.acpi.DeviceSet(HpACPI.BatteryLimit, 1, "BatteryLimit");
                AppConfig.Flush();
                Logger.WriteLine("Battery Care enabled after full charge");
                Program.settingsForm.Invoke(() => Program.settingsForm.VisualiseBattery(HpBatteryCareLimit));
                return;
            }

            Logger.WriteLine("Battery fully charged");
            Program.settingsForm.Invoke(Program.settingsForm.VisualiseBatteryFull);
        }

        public static void AutoBattery(bool init = false)
        {
            if (AppConfig.GetBatteryChargeLimitBackend() == BatteryChargeLimitBackendKind.None) return;

            if (chargeFull && !init) SetBatteryLimitFull();
            else SetBatteryChargeLimit();
        }

        public static void SetBatteryChargeLimit(int setLimit = -1)
        {
            var backend = AppConfig.GetBatteryChargeLimitBackend();
            if (backend == BatteryChargeLimitBackendKind.None) return;

            int limit = setLimit;
            if (limit < 0) limit = AppConfig.Get("charge_limit");
            if (limit < 40 || limit > 100) return;

            if (backend == BatteryChargeLimitBackendKind.HpBatteryCare)
            {
                // Battery Care is binary on supported HP models. Keep only the two UI/config states
                // that accurately describe the firmware operation.
                limit = limit >= FullChargeLimit ? FullChargeLimit : HpBatteryCareLimit;
                bool batteryCareEnabled = limit != FullChargeLimit;

                Program.acpi.DeviceSet(HpACPI.BatteryLimit, batteryCareEnabled ? 1 : 0, "BatteryLimit");

                AppConfig.Set("charge_limit", limit);
                chargeFull = !batteryCareEnabled;
                AppConfig.Flush();

                Program.settingsForm.VisualiseBattery(limit);
                return;
            }

        }

        public static void BatteryReport()
        {
            var reportDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                var cmd = new Process();
                cmd.StartInfo.WorkingDirectory = reportDir;
                cmd.StartInfo.UseShellExecute = false;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.StartInfo.FileName = "powershell";
                cmd.StartInfo.Arguments = "powercfg /batteryreport; explorer battery-report.html";
                cmd.Start();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }
        }

    }
}
