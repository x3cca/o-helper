using OHelper.Display;
using OHelper.Gpu.AMD;
using OHelper.Helpers;
using OHelper.Input;
using OHelper.Mode;
using OHelper.UI;
using System.Diagnostics;

namespace OHelper
{
    public partial class Extra : RForm
    {

        ClamshellModeControl clamshellControl = new ClamshellModeControl();

        const string EMPTY = "--------------";


        private void SetKeyCombo(ComboBox combo, TextBox txbox, string name)
        {
            if (combo is RComboBox rcombo) rcombo.NativeHeight = true;

            Dictionary<string, string> customActions = new Dictionary<string, string>
            {
              {"", EMPTY},
              {"mute", Properties.Strings.VolumeMute},
              {"screenshot", Properties.Strings.PrintScreen},
              {"play", Properties.Strings.PlayPause},
              {"performance", Properties.Strings.PerformanceMode},
              {"screen", Properties.Strings.ToggleScreen},
              {"lock", Properties.Strings.LockScreen},
              {"miniled", Properties.Strings.ToggleMiniled},
              {"fnlock", Properties.Strings.ToggleFnLock},
              {"brightness_down", Properties.Strings.BrightnessDown},
              {"brightness_up", Properties.Strings.BrightnessUp},
              {"visual", Properties.Strings.VisualMode},
              {"touchscreen", Properties.Strings.ToggleTouchscreen },
              {"micmute", Properties.Strings.MuteMic},
              {"OHelper", Properties.Strings.OpenOHelper},
              {"overlay", Properties.Strings.Overlay},
              {"custom", Properties.Strings.Custom}
            };

            switch (name)
            {
                case "m1":
                    customActions[""] = Properties.Strings.VolumeDown;
                    break;
                case "m2":
                    customActions[""] = Properties.Strings.VolumeUp;
                    break;
                case "m3":
                    customActions[""] = Properties.Strings.MuteMic;
                    customActions.Remove("micmute");
                    break;
                case "m4":
                    customActions[""] = Properties.Strings.OpenOHelper;
                    customActions.Remove("OHelper");
                    break;
                case "fnf4":
                    customActions[""] = EMPTY;
                    break;
                case "fnc":
                    customActions[""] = Properties.Strings.ToggleFnLock;
                    customActions.Remove("fnlock");
                    break;
                case "fnv":
                    customActions[""] = Properties.Strings.VisualMode;
                    customActions.Remove("visual");
                    break;
                case "fne":
                    customActions[""] = "Calculator";
                    break;
                case "paddle":
                    customActions[""] = EMPTY;
                    break;
                case "cc":
                    customActions[""] = EMPTY;
                    break;
            }

            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DataSource = new BindingSource(customActions, null);
            combo.DisplayMember = "Value";
            combo.ValueMember = "Key";

            string? action = AppConfig.GetString(name);

            combo.SelectedValue = (action is not null) ? action : "";
            if (combo.SelectedValue is null) combo.SelectedValue = "";

            combo.SelectedValueChanged += delegate
            {
                if (combo.SelectedValue is not null)
                    AppConfig.Set(name, combo.SelectedValue.ToString()!);

                if (name == "m1" || name == "m2")
                    Program.inputDispatcher.RegisterKeys();

            };

            txbox.Text = AppConfig.GetString(name + "_custom");
            txbox.TextChanged += delegate
            {
                AppConfig.Set(name + "_custom", txbox.Text);
            };
        }

        public Extra()
        {
            InitializeComponent();

            labelBindings.Text = Properties.Strings.KeyBindings;
            labelBacklightTitle.Text = Properties.Strings.LaptopBacklight;
            labelSettings.Text = Properties.Strings.Other;

            checkAwake.Text = Properties.Strings.Awake;
            checkSleep.Text = Properties.Strings.Sleep;
            checkBoot.Text = Properties.Strings.Boot;
            checkShutdown.Text = Properties.Strings.Shutdown;
            checkBattery.Text = checkBatteryLogo.Text = checkBatteryBar.Text = checkBatteryLid.Text = Properties.Strings.Battery;
            checkStatusLed.Text = Properties.Strings.LEDStatusIndicators;

            labelSpeed.Text = Properties.Strings.AnimationSpeed;
            //labelBrightness.Text = Properties.Strings.Brightness;

            labelBacklightTimeout.Text = Properties.Strings.BacklightTimeout;
            //labelBacklightTimeoutPlugged.Text = Properties.Strings.BacklightTimeoutPlugged;

            checkNoOverdrive.Text = Properties.Strings.DisableOverdrive;
            checkTopmost.Text = Properties.Strings.WindowTop;
            checkUSBC.Text = Properties.Strings.OptimizedUSBC;
            checkAutoToggleClamshellMode.Text = Properties.Strings.ToggleClamshellMode;

            labelBacklightKeyboard.Text = Properties.Strings.Keyboard;
            labelBacklightBar.Text = Properties.Strings.Lightbar;
            labelBacklightLid.Text = Properties.Strings.Lid;
            labelBacklightLogo.Text = Properties.Strings.Logo;

            checkGpuApps.Text = Properties.Strings.KillGpuApps;
            checkAspm.Text = Properties.Strings.DisablePCIeASPM;
            checkStandbyNetworking.Text = Properties.Strings.DisableStandbyNetworking;
            checkNVPlatform.Text = Properties.Strings.StopStartNVServices;
            labelHibernateAfter.Text = Properties.Strings.HibernateAfter;
            numericHibernateAfter.OffText = Properties.Strings.Off;
            numericBacklightTime.OffText = Properties.Strings.Off;
            numericBacklightPluggedTime.OffText = Properties.Strings.Off;

            labelAPUMem.Text = Properties.Strings.APUMemory;
            labelCores.Text = Properties.Strings.CPUCoresConfiguration;

            labelOptimalBrightness.Text = Properties.Strings.OptimalDisplayBrightness;
            comboOptimalBrightness.Items[0] = Properties.Strings.Off;
            comboOptimalBrightness.Items[1] = Properties.Strings.OnAlways;
            comboOptimalBrightness.Items[2] = Properties.Strings.OnBattery;

            Text = Properties.Strings.ExtraSettings;

            // Accessible Labels

            panelServices.AccessibleName = string.Empty;
            panelBindings.AccessibleName = Properties.Strings.KeyBindings;
            tableBindings.AccessibleName = Properties.Strings.KeyBindings;

            comboM1.AccessibleName = "M1 Action";
            comboM2.AccessibleName = "M2 Action";
            comboM3.AccessibleName = "M3 Action";
            comboM4.AccessibleName = "M4 Action";
            comboFNF4.AccessibleName = "Fn+F4 Action";
            comboFNC.AccessibleName = "Fn+C Action";
            comboFNV.AccessibleName = "Fn+V Action";
            comboFNE.AccessibleName = "Fn+Numpad Action";

            numericBacklightPluggedTime.AccessibleName = Properties.Strings.BacklightTimeoutPlugged;
            numericBacklightTime.AccessibleName = Properties.Strings.BacklightTimeoutBattery;

            comboKeyboardSpeed.AccessibleName = Properties.Strings.LaptopBacklight + " " + Properties.Strings.AnimationSpeed;
            comboAPU.AccessibleName = Properties.Strings.LaptopBacklight + " " + Properties.Strings.AnimationSpeed;

            checkBoot.AccessibleName = Properties.Strings.Boot + " " + Properties.Strings.LaptopBacklight;
            checkAwake.AccessibleName = Properties.Strings.Awake + " " + Properties.Strings.LaptopBacklight;
            checkSleep.AccessibleName = Properties.Strings.Sleep + " " + Properties.Strings.LaptopBacklight;
            checkShutdown.AccessibleName = Properties.Strings.Shutdown + " " + Properties.Strings.LaptopBacklight;

            panelSettings.AccessibleName = Properties.Strings.ExtraSettings;
            numericHibernateAfter.AccessibleName = Properties.Strings.HibernateAfter;

            labelFNF4.Visible = comboFNF4.Visible = textFNF4.Visible = false;
            labelFNC.Visible = comboFNC.Visible = textFNC.Visible = false;
            labelFNV.Visible = comboFNV.Visible = textFNV.Visible = false;
            labelFNE.Visible = comboFNE.Visible = textFNE.Visible = false;

            if (!Program.acpi.IsSupported(HpACPI.GPUEco))
            {
                checkGpuApps.Visible = false;
                checkUSBC.Visible = false;
            }

            checkNoOverdrive.Visible = Program.acpi.IsOverdriveSupported();

            SetKeyCombo(comboM1, textM1, "m1");
            SetKeyCombo(comboM2, textM2, "m2");
            SetKeyCombo(comboM3, textM3, "m3");
            SetKeyCombo(comboM4, textM4, "m4");


            InitTheme();
            Shown += Keyboard_Shown;

            comboKeyboardSpeed.Visible = false;
            labelSpeed.Visible = false;

            // Keyboard
            checkAwake.Checked = AppConfig.IsNotFalse("keyboard_awake");
            checkBattery.Checked = AppConfig.IsOnBattery("keyboard_awake");
            checkBoot.Checked = AppConfig.IsNotFalse("keyboard_boot");
            checkSleep.Checked = AppConfig.IsNotFalse("keyboard_sleep");
            checkShutdown.Checked = AppConfig.IsNotFalse("keyboard_shutdown");

            // Lightbar
            checkAwakeBar.Checked = AppConfig.IsNotFalse("keyboard_awake_bar");
            checkBatteryBar.Checked = AppConfig.IsOnBattery("keyboard_awake_bar");
            checkBootBar.Checked = AppConfig.IsNotFalse("keyboard_boot_bar");
            checkSleepBar.Checked = AppConfig.IsNotFalse("keyboard_sleep_bar");
            checkShutdownBar.Checked = AppConfig.IsNotFalse("keyboard_shutdown_bar");

            // Lid
            checkAwakeLid.Checked = AppConfig.IsNotFalse("keyboard_awake_lid");
            checkBatteryLid.Checked = AppConfig.IsOnBattery("keyboard_awake_lid");
            checkBootLid.Checked = AppConfig.IsNotFalse("keyboard_boot_lid");
            checkSleepLid.Checked = AppConfig.IsNotFalse("keyboard_sleep_lid");
            checkShutdownLid.Checked = AppConfig.IsNotFalse("keyboard_shutdown_lid");

            // Logo
            checkAwakeLogo.Checked = AppConfig.IsNotFalse("keyboard_awake_logo");
            checkBatteryLogo.Checked = AppConfig.IsOnBattery("keyboard_awake_logo");
            checkBootLogo.Checked = AppConfig.IsNotFalse("keyboard_boot_logo");
            checkSleepLogo.Checked = AppConfig.IsNotFalse("keyboard_sleep_logo");
            checkShutdownLogo.Checked = AppConfig.IsNotFalse("keyboard_shutdown_logo");

            tableBacklight.Visible = false;

            //checkAutoToggleClamshellMode.Visible = clamshellControl.IsExternalDisplayConnected();
            checkAutoToggleClamshellMode.Checked = AppConfig.Is("toggle_clamshell_mode");
            checkAutoToggleClamshellMode.CheckedChanged += checkAutoToggleClamshellMode_CheckedChanged;

            checkTopmost.Checked = AppConfig.Is("topmost");
            checkTopmost.CheckedChanged += CheckTopmost_CheckedChanged; ;

            checkNoOverdrive.Checked = AppConfig.IsNoOverdrive();
            checkNoOverdrive.CheckedChanged += CheckNoOverdrive_CheckedChanged;

            checkUSBC.Checked = AppConfig.Is("optimized_usbc");
            checkUSBC.CheckedChanged += CheckUSBC_CheckedChanged;

            sliderBrightness.Value = InputDispatcher.GetBacklight();
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
            sliderBrightness.ValueChanged += SliderBrightness_ValueChanged;

            numericBacklightTime.Value = AppConfig.Get("keyboard_timeout", 60);
            numericBacklightPluggedTime.Value = AppConfig.Get("keyboard_ac_timeout", 0);

            numericBacklightTime.ValueChanged += NumericBacklightTime_ValueChanged;
            numericBacklightPluggedTime.ValueChanged += NumericBacklightTime_ValueChanged;

            checkGpuApps.Checked = AppConfig.Is("kill_gpu_apps");
            checkGpuApps.CheckedChanged += CheckGpuApps_CheckedChanged;

            var statusLed = Program.acpi.DeviceGet(HpACPI.StatusLed);
            checkStatusLed.Visible = statusLed >= 0;
            checkStatusLed.Checked = (statusLed > 0);
            checkStatusLed.CheckedChanged += CheckLEDStatus_CheckedChanged;

            var optimalBrightness = ScreenControl.GetOptimalBrightness();
            if (optimalBrightness >= 0)
            {
                panelOptimalBrightness.Visible = true;
                comboOptimalBrightness.DropDownStyle = ComboBoxStyle.DropDownList;
                comboOptimalBrightness.SelectedIndex = AppConfig.Get("optimal_brightness", optimalBrightness);
                comboOptimalBrightness.SelectedIndexChanged += OptimalBrightness_Changed;
            }

            pictureHelp.Click += PictureHelp_Click;
            pictureLog.Click += PictureLog_Click;

            checkNVPlatform.Visible = Program.acpi.IsNVidiaGPU();
            checkNVPlatform.Checked = AppConfig.IsNVPlatform();
            checkNVPlatform.CheckedChanged += CheckNVPlatform_CheckedChanged;

            checkAspm.Checked = AppConfig.IsAutoASPM();
            checkAspm.CheckedChanged += CheckAspm_CheckedChanged;

            checkStandbyNetworking.Checked = AppConfig.IsAutoStandbyNetworking();
            checkStandbyNetworking.CheckedChanged += CheckStandbyNetworking_CheckedChanged;

            toolTip.SetToolTip(checkAutoToggleClamshellMode, Properties.Strings.ClamshellModeTooltip);
            toolTip.SetToolTip(checkNVPlatform, Properties.Strings.NVPlatformTooltip);
            toolTip.SetToolTip(checkAspm, Properties.Strings.DisablePCIeASPMTooltip);
            toolTip.SetToolTip(checkStandbyNetworking, Properties.Strings.DisableStandbyNetworkingTooltip);

            panelServices.Visible = false;

            InitCores();
            InitHibernate();

            InitACPITesting();

        }

        private void CheckAspm_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("aspm", (checkAspm.Checked ? 1 : 0));
            PowerNative.SetBalancedASPM(checkAspm.Checked ? 0 : 2);
        }

        private void CheckStandbyNetworking_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("standby_networking", (checkStandbyNetworking.Checked ? 1 : 0));
            if (checkStandbyNetworking.Checked) PowerNative.SetConnectivityInStandby(0, 0);
            else PowerNative.SetConnectivityInStandby(1, 2);
        }

        private void CheckNVPlatform_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("nv_platform", (checkNVPlatform.Checked ? 1 : 0));
        }

        private void OptimalBrightness_Changed(object? sender, EventArgs e)
        {
            ScreenControl.SetOptimalBrightness(comboOptimalBrightness.SelectedIndex);
        }

        private void CheckLEDStatus_CheckedChanged(object? sender, EventArgs e)
        {
            InputDispatcher.SetStatusLED(checkStatusLed.Checked);
        }

        private void InitACPITesting()
        {
            pictureScan.Visible = true;
            pictureScan.Click += PictureScan_Click;

            if (!AppConfig.Is("debug")) return;

            panelACPI.Visible = true;
            textACPICommand.Text = "110034";
            textACPIParam.Text = "0x0303";
            buttonACPISend.Click += ButtonACPISend_Click;
        }

        private void ButtonACPISend_Click(object? sender, EventArgs e)
        {
            try
            {
                int deviceID = Convert.ToInt32(textACPICommand.Text, 16);
                int status = Convert.ToInt32(textACPIParam.Text, textACPIParam.Text.Contains("x") ? 16 : 10);
                int result = Program.acpi.DeviceSet((uint)deviceID, status, "TestACPI " + deviceID.ToString("X8") + " " + status.ToString("X4"));
                labelACPITitle.Text = "ACPI DEVS Test : " + result.ToString();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }
        }

        private void InitCores()
        {
            (int eCores, int pCores) = Program.acpi.GetCores();
            (int eCoresMax, int pCoresMax) = Program.acpi.GetCores(true);

            if (eCores < 0 || pCores < 0 || eCoresMax < 0 || pCoresMax < 0)
            {
                panelCores.Visible = false;
                return;
            }

            if (eCoresMax == 0) eCoresMax = 8;
            if (pCoresMax == 0) pCoresMax = 6;

            eCoresMax = Math.Max(4, eCoresMax);

            panelCores.Visible = true;

            comboCoresE.DropDownStyle = ComboBoxStyle.DropDownList;
            comboCoresP.DropDownStyle = ComboBoxStyle.DropDownList;

            for (int i = HpACPI.PCoreMin; i <= pCoresMax; i++) comboCoresP.Items.Add(i.ToString() + " Pcores");
            for (int i = HpACPI.ECoreMin; i <= eCoresMax; i++) comboCoresE.Items.Add(i.ToString() + " Ecores");

            comboCoresP.SelectedIndex = Math.Max(Math.Min(pCores - HpACPI.PCoreMin, comboCoresP.Items.Count - 1), 0);
            comboCoresE.SelectedIndex = Math.Max(Math.Min(eCores - HpACPI.ECoreMin, comboCoresE.Items.Count - 1), 0);

            buttonCores.Click += ButtonCores_Click;

        }

        private void ButtonCores_Click(object? sender, EventArgs e)
        {
            DialogResult dialogResult = MessageBox.Show(Properties.Strings.AlertAPUMemoryRestart, Properties.Strings.AlertAPUMemoryRestartTitle, MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes)
            {
                Program.acpi.SetCores(HpACPI.ECoreMin + comboCoresE.SelectedIndex, HpACPI.PCoreMin + comboCoresP.SelectedIndex);
                Process.Start("shutdown", "/r /t 1");
            }
        }


        private void PictureScan_Click(object? sender, EventArgs e)
        {
            string logFile = Program.acpi.ScanRange();
            new Process
            {
                StartInfo = new ProcessStartInfo(logFile)
                {
                    UseShellExecute = true
                }
            }.Start();
        }

        private void ComboAPU_SelectedIndexChanged(object? sender, EventArgs e)
        {
            int mem = comboAPU.SelectedIndex;
            Program.acpi.SetAPUMem(mem);

            DialogResult dialogResult = MessageBox.Show(Properties.Strings.AlertAPUMemoryRestart, Properties.Strings.AlertAPUMemoryRestartTitle, MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                Process.Start("shutdown", "/r /t 1");
            }

        }

        private void InitHibernate()
        {
            try
            {
                int hibernate = PowerNative.GetHibernateAfter();
                if (hibernate < 0 || hibernate > numericHibernateAfter.Maximum) hibernate = 0;
                numericHibernateAfter.Value = hibernate;
                numericHibernateAfter.ValueChanged += NumericHibernateAfter_ValueChanged;

            }
            catch (Exception ex)
            {
                panelPower.Visible = false;
                Logger.WriteLine(ex.ToString());
            }

        }

        private void NumericHibernateAfter_ValueChanged(object? sender, EventArgs e)
        {
            PowerNative.SetHibernateAfter((int)numericHibernateAfter.Value);
        }

        private void PictureLog_Click(object? sender, EventArgs e)
        {
            new Process
            {
                StartInfo = new ProcessStartInfo(Logger.logFile)
                {
                    UseShellExecute = true
                }
            }.Start();
        }

        private void SliderBrightness_ValueChanged(object? sender, EventArgs e)
        {
            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;

            if (onBattery)
                AppConfig.Set("keyboard_brightness_ac", sliderBrightness.Value);
            else
                AppConfig.Set("keyboard_brightness", sliderBrightness.Value);

            InputDispatcher.SetBacklightLevel(sliderBrightness.Value);
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
        }

        public void VisualiseBacklight(int backlight)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(() => VisualiseBacklight(backlight)); }
                catch (InvalidOperationException) { }
                return;
            }
            sliderBrightness.ValueChanged -= SliderBrightness_ValueChanged;
            sliderBrightness.Value = backlight;
            sliderBrightness.AccessibleName = Properties.Strings.LaptopBacklight + ": " + sliderBrightness.Value;
            sliderBrightness.ValueChanged += SliderBrightness_ValueChanged;
        }

        private void CheckGpuApps_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("kill_gpu_apps", (checkGpuApps.Checked ? 1 : 0));
        }

        private void NumericBacklightTime_ValueChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("keyboard_timeout", (int)numericBacklightTime.Value);
            AppConfig.Set("keyboard_ac_timeout", (int)numericBacklightPluggedTime.Value);
            Program.inputDispatcher.InitBacklightTimer();
        }

        private void CheckUSBC_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("optimized_usbc", (checkUSBC.Checked ? 1 : 0));
        }

        private void PictureHelp_Click(object? sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/x3cca/o-helper") { UseShellExecute = true });
        }

        private void CheckNoOverdrive_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("no_overdrive", (checkNoOverdrive.Checked ? 1 : 0));
            ScreenControl.AutoScreen(true);
        }


        private void CheckTopmost_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("topmost", (checkTopmost.Checked ? 1 : 0));
            Program.settingsForm.TopMost = checkTopmost.Checked;
        }

        private void Keyboard_Shown(object? sender, EventArgs e)
        {
            if (Height > Program.settingsForm.Height)
            {
                var top = Program.settingsForm.Top + Program.settingsForm.Height - Height;

                if (top < 0)
                {
                    MaximumSize = new Size(Width, Program.settingsForm.Height);
                    Top = Program.settingsForm.Top;
                }
                else
                {
                    Top = top;
                }

            }
            else
            {
                Top = Program.settingsForm.Top;
            }

            Left = Program.settingsForm.Left - Width - 5;
        }


        private void checkAutoToggleClamshellMode_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("toggle_clamshell_mode", checkAutoToggleClamshellMode.Checked ? 1 : 0);

            if (checkAutoToggleClamshellMode.Checked)
            {
                clamshellControl.ToggleLidAction();
            }
            else
            {
                ClamshellModeControl.DisableClamshellMode();
            }

        }

    }
}
