using OHelper.Mode;

namespace OHelper.Fan
{
    public class FanSensorControl
    {
        public const int DEFAULT_FAN_MIN = 18;
        public const int DEFAULT_FAN_MAX = 58;


        public const int INADEQUATE_MAX = 104;

        const int FAN_COUNT = 3;

        Fans fansForm;
        ModeControl modeControl = Program.modeControl;

        static int[] measuredMax = Array.Empty<int>();
        static int sameCount = 0;

        static System.Timers.Timer timer = default!;

        static int[] _fanMax = InitFanMax();
        static int[] _fanMin = GetDefaultMin();
        static bool _fanRpm = AppConfig.IsNotFalse("fan_rpm");

        public FanSensorControl(Fans fansForm)
        {
            this.fansForm = fansForm;
            bool calibrating = timer is not null && timer.Enabled;
            timer?.Dispose();
            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.Enabled = calibrating;
        }

        static int[] InitFanMax()
        {
            int[] defaultMax = GetDefaultMax();

            return new int[3] {
                AppConfig.Get("fan_max_" + (int)HpFan.CPU, defaultMax[(int)HpFan.CPU]),
                AppConfig.Get("fan_max_" + (int)HpFan.GPU, defaultMax[(int)HpFan.GPU]),
                AppConfig.Get("fan_max_" + (int)HpFan.Mid, defaultMax[(int)HpFan.Mid])
            };
        }


        static int[] GetDefaultMax()
        {
            if (AppConfig.ContainsModel("GA401I")) return new int[3] { 78, 76, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("GA401")) return new int[3] { 71, 73, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("GA402")) return new int[3] { 55, 56, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("G513R")) return new int[3] { 58, 60, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("G513Q")) return new int[3] { 69, 69, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("GA503")) return new int[3] { 64, 64, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("GU603")) return new int[3] { 62, 64, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("FA507R")) return new int[3] { 63, 57, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("FA507X")) return new int[3] { 63, 68, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("FX607J")) return new int[3] { 74, 72, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("GX650")) return new int[3] { 62, 62, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("G732")) return new int[3] { 61, 60, DEFAULT_FAN_MAX };
            if (AppConfig.ContainsModel("G713")) return new int[3] { 56, 60, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("Z301")) return new int[3] { 72, 64, DEFAULT_FAN_MAX };

            if (AppConfig.ContainsModel("GV601")) return new int[3] { 78, 59, 85 };

            if (AppConfig.ContainsModel("GA403")) return new int[3] { 68, 68, 80 };
            if (AppConfig.ContainsModel("GU605")) return new int[3] { 62, 62, 92 };

            // HP OMEN models - fan curve defaults
            if (AppConfig.IsOmenTranscend()) return new int[3] { 65, 65, DEFAULT_FAN_MAX };  // Thin-and-light: conservative fan limits (updated to 6500 RPM max)
            if (AppConfig.IsOmenSlim()) return new int[3] { 52, 52, DEFAULT_FAN_MAX };       // Slim chassis
            if (AppConfig.IsOmenMax()) return new int[3] { 60, 60, 80 };                    // Flagship with mid fan
            if (AppConfig.IsOmen16()) return new int[3] { 55, 55, DEFAULT_FAN_MAX };        // Standard OMEN 16

            return new int[3] { DEFAULT_FAN_MAX, DEFAULT_FAN_MAX, DEFAULT_FAN_MAX };
        }

        static int[] GetDefaultMin()
        {
            if (AppConfig.ContainsModel("GA403")) return new int[3] { 22, 22, 22 };
            if (AppConfig.ContainsModel("GU605")) return new int[3] { 22, 22, 22 };
            if (AppConfig.ContainsModel("HN7306")) return new int[3] { 22, 22, 22 };
            return new int[3] { DEFAULT_FAN_MIN, DEFAULT_FAN_MIN, DEFAULT_FAN_MIN };
        }

        public static int GetFanMax(HpFan device)
        {

            if (_fanMax[(int)device] < 0 || _fanMax[(int)device] > INADEQUATE_MAX)
                SetFanMax(device, DEFAULT_FAN_MAX);

            return _fanMax[(int)device];
        }

        public static int GetFanMin(HpFan device)
        {
            return _fanMin[(int)device];
        }

        public static void SetFanMax(HpFan device, int value)
        {
            _fanMax[(int)device] = value;
            AppConfig.Set("fan_max_" + (int)device, value);
        }

        public static bool fanRpm
        {
            get
            {
                return _fanRpm;
            }
            set
            {
                AppConfig.Set("fan_rpm", value ? 1 : 0);
                _fanRpm = value;
            }
        }

        public static string? FormatFan(HpFan device, int value)
        {
            if (value < 0) return null;

            if (value > GetFanMax(device) && value <= INADEQUATE_MAX) SetFanMax(device, value);

            if (fanRpm)
                return (value * 100).ToString() + "RPM";
            else
                return Math.Min(Math.Round((float)value / GetFanMax(device) * 100), 100).ToString() + "%"; // relatively to max RPM
        }

        public void StartCalibration()
        {

            if (!AppConfig.SupportsFirmwareFanCurves()
                || !Program.acpi.IsSupported(HpACPI.DevsCPUFanCurve))
            {
                Logger.WriteLine("Fan calibration blocked: firmware-stored fan curves are unsupported");
                return;
            }

            measuredMax = new int[] { 0, 0, 0 };
            timer.Enabled = true;

            for (int i = 0; i < FAN_COUNT; i++)
                AppConfig.Remove("fan_max_" + i);

            Program.acpi.DeviceSet(HpACPI.PerformanceMode, HpACPI.PerformanceTurbo, "ModeCalibration");

            for (int i = 0; i < FAN_COUNT; i++)
                Program.acpi.SetFanCurve((HpFan)i, new byte[] { 20, 30, 40, 50, 60, 70, 80, 90, 100, 100, 100, 100, 100, 100, 100, 100 });

        }

        private void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            int fan;
            bool same = true;

            for (int i = 0; i < FAN_COUNT; i++)
            {
                fan = Program.acpi.GetFan((HpFan)i);
                if (fan > measuredMax[i])
                {
                    measuredMax[i] = fan;
                    same = false;
                }
            }

            if (same) sameCount++;
            else sameCount = 0;

            string label = "Measuring Max Speed - CPU: " + measuredMax[(int)HpFan.CPU] * 100 + ", GPU: " + measuredMax[(int)HpFan.GPU] * 100;
            if (measuredMax[(int)HpFan.Mid] > 10) label = label + ", Mid: " + measuredMax[(int)HpFan.Mid] * 100;
            label = label + " (" + sameCount + "s)";

            fansForm.LabelFansResult(label);

            if (sameCount >= 15)
            {
                for (int i = 0; i < FAN_COUNT; i++)
                {
                    if (measuredMax[i] > 30 && measuredMax[i] < INADEQUATE_MAX) SetFanMax((HpFan)i, measuredMax[i]);
                }

                sameCount = 0;
                FinishCalibration();
            }

        }

        private void FinishCalibration()
        {

            timer.Enabled = false;
            modeControl.SetPerformanceMode();

            string label = "Measured - CPU: " + AppConfig.Get("fan_max_" + (int)HpFan.CPU) * 100;

            if (AppConfig.Get("fan_max_" + (int)HpFan.GPU) > 0)
                label = label + ", GPU: " + AppConfig.Get("fan_max_" + (int)HpFan.GPU) * 100;

            if (AppConfig.Get("fan_max_" + (int)HpFan.Mid) > 0)
                label = label + ", Mid: " + AppConfig.Get("fan_max_" + (int)HpFan.Mid) * 100;

            fansForm.LabelFansResult(label);
            fansForm.InitAxis();
        }
    }
}
