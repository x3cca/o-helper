using OHelper.AutoUpdate;
using OHelper.Battery;
using OHelper.Display;
using OHelper.Fan;
using OHelper.Gpu;
using OHelper.Helpers;
using OHelper.Input;
using OHelper.Mode;
using OHelper.Properties;
using OHelper.UI;
using System.Diagnostics;
using System.Timers;

namespace OHelper
{
    public partial class SettingsForm : RForm
    {
        ContextMenuStrip contextMenuStrip = new CustomContextMenu();
        ToolStripMenuItem menuEco = default!, menuStandard = default!, menuUltimate = default!, menuOptimized = default!;
        DonateControl donateControl;

        AutoUpdateControl updateControl;

        public static System.Timers.Timer sensorTimer = default!;
        private static readonly bool sensorsAlways = AppConfig.Is("sensors_always");

        public Fans? fansForm;
        public Extra? extraForm;

        static long lastRefresh;
        static long lastBatteryRefresh;
        static long lastLostFocus;

        bool isGpuSection = true;
        bool isMuxGpu = true;

        bool batteryMouseOver = false;
        bool batteryFullMouseOver = false;
        readonly BatteryChargeLimitBackendKind batteryLimitBackend;

        bool sliderGammaIgnore = false;
        bool activateCheck = false;
        RButton buttonDynamic = default!;

        public SettingsForm()
        {

            InitializeComponent();
            batteryLimitBackend = AppConfig.GetBatteryChargeLimitBackend();
            panelBattery.Visible = batteryLimitBackend != BatteryChargeLimitBackendKind.None;
            sliderBattery.Visible = batteryLimitBackend != BatteryChargeLimitBackendKind.None;
            panelPerformance.Visible = AppConfig.SupportsPerformanceModes();
            InitTheme(true);
            InitDynamicRefreshButton();

            updateControl = new AutoUpdateControl(this);

            buttonSilent.Text = Properties.Strings.Silent;
            buttonBalanced.Text = Properties.Strings.Balanced;
            buttonTurbo.Text = Properties.Strings.Turbo;
            buttonUnleashed.Text = Properties.Strings.Unleashed;
            buttonFans.Text = Properties.Strings.FansPower;
            buttonMaxFans.Text = Properties.Strings.MaxFans;

            buttonEco.Text = Properties.Strings.EcoMode;
            buttonUltimate.Text = Properties.Strings.UltimateMode;
            buttonStandard.Text = Properties.Strings.StandardMode;
            buttonOptimized.Text = Properties.Strings.Optimized;
            buttonStopGPU.Text = Properties.Strings.StopGPUApps;

            buttonScreenAuto.Text = Properties.Strings.AutoMode;
            buttonMiniled.Text = Properties.Strings.Multizone;

            buttonKeyboardColor.Text = Properties.Strings.Color;
            buttonKeyboard.Text = Properties.Strings.Extra;

            labelPerf.Text = Properties.Strings.PerformanceMode;
            labelGPU.Text = Properties.Strings.GPUMode;
            labelSreen.Text = Properties.Strings.LaptopScreen;
            UpdateKeyboardLabel();
            labelBatteryTitle.Text = Properties.Strings.BatteryChargeLimit;

            checkStartup.Text = Properties.Strings.RunOnStartup;

            buttonQuit.Text = Properties.Strings.Quit;
            buttonUpdates.Text = Properties.Strings.Updates;
            buttonDonate.Text = Properties.Strings.Donate;

            // Accessible Labels

            sliderBattery.AccessibleName = Properties.Strings.BatteryChargeLimit;
            buttonQuit.AccessibleName = Properties.Strings.Quit;
            buttonUpdates.AccessibleName = Properties.Strings.BiosAndDriverUpdates;
            panelPerformance.AccessibleName = Properties.Strings.PerformanceMode;
            buttonSilent.AccessibleName = Properties.Strings.Silent;
            buttonBalanced.AccessibleName = Properties.Strings.Balanced;
            buttonTurbo.AccessibleName = Properties.Strings.Turbo;
            buttonUnleashed.AccessibleName = Properties.Strings.Unleashed;
            buttonFans.AccessibleName = Properties.Strings.FansAndPower;
            buttonMaxFans.AccessibleName = Properties.Strings.MaxFans;
            panelGPU.AccessibleName = Properties.Strings.GPUMode;
            buttonEco.AccessibleName = Properties.Strings.EcoMode;
            buttonStandard.AccessibleName = Properties.Strings.StandardMode;
            buttonOptimized.AccessibleName = Properties.Strings.Optimized;
            buttonUltimate.AccessibleName = Properties.Strings.UltimateMode;
            panelScreen.AccessibleName = Properties.Strings.LaptopScreen;

            buttonScreenAuto.AccessibleName = Properties.Strings.AutoMode;
            //button60Hz.AccessibleName = "60Hz Refresh Rate";
            //button120Hz.AccessibleName = "Maximum Refresh Rate";

            panelKeyboard.AccessibleName = Properties.Strings.LaptopKeyboard;
            buttonKeyboard.AccessibleName = Properties.Strings.ExtraSettings;
            buttonKeyboardColor.AccessibleName = Properties.Strings.LaptopKeyboard + " " + Properties.Strings.Color;
            comboKeyboard.AccessibleName = Properties.Strings.LaptopBacklight;

            FormClosing += SettingsForm_FormClosing;
            Deactivate += SettingsForm_LostFocus;
            Activated += SettingsForm_Focused;

            buttonSilent.BorderColor = colorEco;
            buttonBalanced.BorderColor = colorStandard;
            buttonTurbo.BorderColor = colorTurbo;
            buttonUnleashed.BorderColor = colorCustom;
            buttonFans.BorderColor = colorCustom;
            buttonMaxFans.BorderColor = colorTurbo;

            buttonEco.BorderColor = colorEco;
            buttonStandard.BorderColor = colorStandard;
            buttonUltimate.BorderColor = colorTurbo;
            buttonOptimized.BorderColor = colorEco;

            button60Hz.BorderColor = colorGray;
            button120Hz.BorderColor = colorGray;
            buttonScreenAuto.BorderColor = colorGray;
            buttonMiniled.BorderColor = colorTurbo;

            buttonEnergySaver.BackColor = colorEco;
            buttonEnergySaver.ForeColor = RForm.foreMain;
            buttonEnergySaver.Click += ButtonEnergySaver_Click;

            buttonAmdOled.BackColor = colorTurbo;
            buttonAmdOled.ForeColor = RForm.foreMain;
            buttonAmdOled.Click += ButtonAmdOled_Click;

            buttonSilent.Click += ButtonSilent_Click;
            buttonBalanced.Click += ButtonBalanced_Click;
            buttonTurbo.Click += ButtonTurbo_Click;
            buttonUnleashed.Click += ButtonUnleashed_Click;

            buttonEco.Click += ButtonEco_Click;
            buttonStandard.Click += ButtonStandard_Click;
            buttonUltimate.Click += ButtonUltimate_Click;
            buttonOptimized.Click += ButtonOptimized_Click;
            buttonStopGPU.Click += ButtonStopGPU_Click;

            VisibleChanged += SettingsForm_VisibleChanged;

            button60Hz.Click += Button60Hz_Click;
            button120Hz.Click += Button120Hz_Click;
            buttonScreenAuto.Click += ButtonScreenAuto_Click;
            buttonDynamic.Click += ButtonDynamic_Click;
            buttonMiniled.Click += ButtonMiniled_Click;
            buttonFHD.Click += ButtonFHD_Click;
            buttonHDRControl.Click += ButtonHDRControl_Click;

            buttonQuit.Click += ButtonQuit_Click;

            buttonKeyboardColor.Click += ButtonKeyboardColor_Click;

            buttonFans.Click += ButtonFans_Click;
            buttonMaxFans.Click += ButtonMaxFans_Click;
            buttonKeyboard.Click += ButtonKeyboard_Click;

            pictureColor.Click += PictureColor_Click;
            pictureColor2.Click += PictureColor2_Click;

            labelCPUFan.Click += LabelCPUFan_Click;
            labelGPUFan.Click += LabelCPUFan_Click;

            checkStartup.Checked = Startup.IsScheduled();
            checkStartup.CheckedChanged += CheckStartup_CheckedChanged;

            labelVersion.Click += LabelVersion_Click;
            labelVersion.ForeColor = Color.FromArgb(128, RForm.foreMain);

            buttonOptimized.MouseMove += ButtonOptimized_MouseHover;
            buttonOptimized.MouseLeave += ButtonGPU_MouseLeave;

            buttonEco.MouseMove += ButtonEco_MouseHover;
            buttonEco.MouseLeave += ButtonGPU_MouseLeave;

            buttonStandard.MouseMove += ButtonStandard_MouseHover;
            buttonStandard.MouseLeave += ButtonGPU_MouseLeave;

            buttonUltimate.MouseMove += ButtonUltimate_MouseHover;
            buttonUltimate.MouseLeave += ButtonGPU_MouseLeave;

            buttonScreenAuto.MouseMove += ButtonScreenAuto_MouseHover;
            buttonScreenAuto.MouseLeave += ButtonScreen_MouseLeave;

            button60Hz.MouseMove += Button60Hz_MouseHover;
            button60Hz.MouseLeave += ButtonScreen_MouseLeave;

            button120Hz.MouseMove += Button120Hz_MouseHover;
            button120Hz.MouseLeave += ButtonScreen_MouseLeave;

            buttonDynamic.MouseMove += ButtonDynamic_MouseHover;
            buttonDynamic.MouseLeave += ButtonScreen_MouseLeave;

            buttonFHD.MouseMove += ButtonFHD_MouseHover;
            buttonFHD.MouseLeave += ButtonScreen_MouseLeave;

            buttonMiniled.MouseMove += ButtonMiniled_MouseHover;
            buttonMiniled.MouseLeave += ButtonScreen_MouseLeave;

            buttonUpdates.Click += ButtonUpdates_Click;
            // O-Helper uses this button as a static link to the release page.

            if (batteryLimitBackend != BatteryChargeLimitBackendKind.None)
            {
                sliderBattery.MouseUp += SliderBattery_MouseUp;
                sliderBattery.KeyUp += SliderBattery_KeyUp;
                sliderBattery.ValueChanged += SliderBattery_ValueChanged;
                if (batteryLimitBackend == BatteryChargeLimitBackendKind.HpBatteryCare)
                {
                    sliderBattery.Min = BatteryControl.HpBatteryCareLimit;
                    sliderBattery.Max = BatteryControl.FullChargeLimit;
                    sliderBattery.supportedValues = new() { BatteryControl.HpBatteryCareLimit, BatteryControl.FullChargeLimit };
                    sliderBattery.SnapToSupportedValues = true;
                }
                else if (AppConfig.IsChargeLimit6080())
                    sliderBattery.supportedValues = new() { 60, 65, 70, 75, 80, 100 };
            }

            sensorTimer = new System.Timers.Timer(AppConfig.Get("sensor_timer", 1000));
            sensorTimer.Elapsed += OnTimedEvent;
            sensorTimer.Enabled = sensorsAlways;

            labelCharge.MouseEnter += PanelBattery_MouseEnter;
            labelCharge.MouseLeave += PanelBattery_MouseLeave;
            labelBattery.Click += LabelBattery_Click;


            buttonBatteryFull.MouseEnter += ButtonBatteryFull_MouseEnter;
            buttonBatteryFull.MouseLeave += ButtonBatteryFull_MouseLeave;
            buttonBatteryFull.Click += ButtonBatteryFull_Click;

            buttonOverlay.Click += ButtonOverlay_Click;
            buttonAutoTDP.BorderColor = colorTurbo;

            Text = "O-Helper " + (ProcessHelper.IsUserAdministrator() ? "*" : "-") + " " + AppConfig.GetModelShort();
            TopMost = AppConfig.Is("topmost");

            //This will auto position the window again when it resizes. Might mess with position if people drag the window somewhere else.
            this.Resize += SettingsForm_Resize;

            VisualiseFnLock();
            buttonFnLock.Click += ButtonFnLock_Click;

            labelVisual.Click += LabelVisual_Click;
            labelCharge.Click += LabelCharge_Click;

            donateControl = new DonateControl(this, buttonDonate);
            donateControl.Init();

            labelBacklight.ForeColor = colorStandard;
            labelBacklight.Click += LabelBacklight_Click;

            panelPerformance.Focus();
            InitVisual();
        }

        private void InitDynamicRefreshButton()
        {
            buttonDynamic = new RButton
            {
                Activated = false,
                BackColor = RForm.buttonMain,
                BorderColor = colorCustom,
                BorderRadius = 5,
                CausesValidation = false,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4),
                Name = "buttonDynamic",
                Secondary = false,
                TabIndex = 15,
                Text = Properties.Strings.DynamicMode,
                UseVisualStyleBackColor = false,
                Visible = false
            };
            buttonDynamic.FlatAppearance.BorderSize = 0;
            tableScreen.Controls.Add(buttonDynamic, 3, 0);
        }

        private void SetScreenTableColumns(int count)
        {
            if (tableScreen.ColumnCount == count && tableScreen.ColumnStyles.Count == count) return;

            tableScreen.ColumnStyles.Clear();
            tableScreen.ColumnCount = count;
            float width = 100F / count;
            for (int i = 0; i < count; i++) tableScreen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        }

        private void ButtonAmdOled_Click(object? sender, EventArgs e)
        {
            AmdDisplay.RunAdrenaline();
            activateCheck = true;
        }

        private void LabelBattery_Click(object? sender, EventArgs e)
        {
            HardwareControl.chargeWatt = !HardwareControl.chargeWatt;
            _ = RefreshSensors(true);
        }

        private void ButtonEnergySaver_Click(object? sender, EventArgs e)
        {
            KeyboardHook.KeyKeyPress(Keys.LWin, Keys.A);
            activateCheck = true;
        }

        private void LabelBacklight_Click(object? sender, EventArgs e)
        {
            if (AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled()) DynamicLightingHelper.OpenSettings();
        }

        private void ButtonFHD_Click(object? sender, EventArgs e)
        {
            ScreenControl.ToogleFHD();
        }

        private void ButtonHDRControl_Click(object? sender, EventArgs e)
        {
            ScreenControl.ToogleHDRControl();
        }

        private void SliderBattery_ValueChanged(object? sender, EventArgs e)
        {
            VisualiseBatteryTitle(sliderBattery.Value);
        }

        private void SliderBattery_KeyUp(object? sender, KeyEventArgs e)
        {
            BatteryControl.SetBatteryChargeLimit(sliderBattery.Value);
        }

        private void SliderBattery_MouseUp(object? sender, MouseEventArgs e)
        {
            BatteryControl.SetBatteryChargeLimit(sliderBattery.Value);
        }

        private void LabelCharge_Click(object? sender, EventArgs e)
        {
            BatteryControl.BatteryReport();
        }

        private void LabelVisual_Click(object? sender, EventArgs e)
        {
            labelVisual.Visible = false;
            VisualControl.forceVisual = true;
        }

        public void InitVisual()
        {

            if (AppConfig.Is("hide_visual")) return;

            if (!VisualControl.IsSplendidSupported())
            {
                panelGamma.Visible = false;
                tableVisual.Visible = false;
                buttonInstallColor.Visible = false;
                comboVisual.Visible = false;
                comboColorTemp.Visible = false;
                comboGamut.Visible = false;
                labelVisual.Visible = false;
                return;
            }

            if (AppConfig.IsOLED())
            {
                panelGamma.Visible = true;
                sliderGamma.Visible = true;
                labelGammaTitle.Text = Properties.Strings.FlickerFreeDimming + " / " + Properties.Strings.VisualMode;

                VisualiseBrightness();

                sliderGamma.ValueChanged += SliderGamma_ValueChanged;
                sliderGamma.MouseUp += SliderGamma_ValueChanged;

            }
            else
            {
                labelGammaTitle.Text = Properties.Strings.VisualMode;
            }

            var gamuts = VisualControl.GetGamutModes();
            tableVisual.ColumnCount = 3;
            buttonInstallColor.Visible = false;

            panelGamma.Visible = true;
            tableVisual.Visible = true;

            var visualValue = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
            var colorTempValue = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);

            comboVisual.DropDownStyle = ComboBoxStyle.DropDownList;
            comboVisual.DataSource = new BindingSource(VisualControl.GetVisualModes(), null);
            comboVisual.DisplayMember = "Value";
            comboVisual.ValueMember = "Key";
            comboVisual.SelectedValue = visualValue;

            comboColorTemp.DropDownStyle = ComboBoxStyle.DropDownList;
            comboColorTemp.DataSource = new BindingSource(VisualControl.GetTemperatures(), null);
            comboColorTemp.DisplayMember = "Value";
            comboColorTemp.ValueMember = "Key";
            comboColorTemp.SelectedValue = colorTempValue;

            VisualControl.SetVisual(visualValue, colorTempValue, true);

            comboVisual.SelectedValueChanged += ComboVisual_SelectedValueChanged;
            comboVisual.Visible = true;
            VisualiseDisabled();

            comboColorTemp.SelectedValueChanged += ComboVisual_SelectedValueChanged;
            comboColorTemp.Visible = true;

            if (gamuts.Count <= 1) return;

            comboGamut.DropDownStyle = ComboBoxStyle.DropDownList;
            comboGamut.DataSource = new BindingSource(gamuts, null);
            comboGamut.DisplayMember = "Value";
            comboGamut.ValueMember = "Key";
            comboGamut.SelectedValue = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());

            comboGamut.SelectedValueChanged += ComboGamut_SelectedValueChanged;
            comboGamut.Visible = true;

        }

        public void CycleVisualMode(int delta)
        {

            if (comboVisual.Items.Count < 1) return;

            if (delta > 0)
            {
                if (comboVisual.SelectedIndex < comboVisual.Items.Count - 1)
                    comboVisual.SelectedIndex += 1;
                else
                    comboVisual.SelectedIndex = 0;
            }
            else
            {
                if (comboVisual.SelectedIndex > 0)
                    comboVisual.SelectedIndex -= 1;
                else
                    comboVisual.SelectedIndex = comboVisual.Items.Count - 1;
            }

            Program.toast.RunToast(comboVisual.GetItemText(comboVisual.SelectedItem) ?? string.Empty, ToastIcon.BrightnessUp);
        }

        private void ComboGamut_SelectedValueChanged(object? sender, EventArgs e)
        {
            if (comboGamut.SelectedValue is int gamut) VisualControl.SetGamut(gamut);
        }

        private void ComboVisual_SelectedValueChanged(object? sender, EventArgs e)
        {
            if (comboVisual.SelectedValue is SplendidCommand visual && comboColorTemp.SelectedValue is int temperature)
                VisualControl.SetVisual(visual, temperature);
            VisualiseDisabled();
        }

        public void VisualiseBrightness()
        {
            if (InvokeRequired) { Invoke(VisualiseBrightness); return; }
            sliderGammaIgnore = true;
            sliderGamma.Value = VisualControl.GetBrightness();
            labelGamma.Text = sliderGamma.Value + "%";
            sliderGammaIgnore = false;
        }

        public void VisualiseAmdOled(bool status = false)
        {
            if (InvokeRequired) { Invoke(() => VisualiseAmdOled(status)); return; }
            buttonAmdOled.Visible = status;
        }

        public void VisualiseDisabled()
        {
            comboGamut.Enabled = comboColorTemp.Enabled = (SplendidCommand)AppConfig.Get("visual") != SplendidCommand.Disabled;
        }

        public void VisualiseGamut()
        {
            if (InvokeRequired) { Invoke(VisualiseGamut); return; }
            if (comboGamut.Items.Count > 0) comboGamut.SelectedIndex = 0;
        }

        private void SliderGamma_ValueChanged(object? sender, EventArgs e)
        {
            if (sliderGammaIgnore) return;
            VisualControl.SetBrightness(sliderGamma.Value);
        }

        private void ButtonOverlay_Click(object? sender, EventArgs e)
        {
            KeyboardHook.KeyKeyKeyPress(Keys.LControlKey, Keys.LShiftKey, Keys.O);
        }

        private void SettingsForm_Focused(object? sender, EventArgs e)
        {
            if (activateCheck)
            {
                buttonEnergySaver.Visible = PowerNative.GetBatterySaverStatus();
                buttonAmdOled.Visible = AmdDisplay.IsOledPowerOptimization();
                activateCheck = false;
            }
        }
        private void SettingsForm_LostFocus(object? sender, EventArgs e)
        {
            lastLostFocus = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private void ButtonBatteryFull_Click(object? sender, EventArgs e)
        {
            BatteryControl.ToggleBatteryLimitFull();
        }

        private void ButtonBatteryFull_MouseLeave(object? sender, EventArgs e)
        {
            batteryFullMouseOver = false;
            _ = RefreshSensors(true);
        }

        private void ButtonBatteryFull_MouseEnter(object? sender, EventArgs e)
        {
            batteryFullMouseOver = true;
            labelCharge.Text = Properties.Strings.BatteryLimitFull;
        }

        private void SettingsForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Normal)
            {
                WindowState = FormWindowState.Normal;
                return;
            }

            Left = Screen.FromControl(this).WorkingArea.Width - 10 - Width;
            Top = Screen.FromControl(this).WorkingArea.Height - 10 - Height;
        }

        private void PanelBattery_MouseEnter(object? sender, EventArgs e)
        {
            batteryMouseOver = true;
            ShowBatteryWear();
        }

        private void PanelBattery_MouseLeave(object? sender, EventArgs e)
        {
            batteryMouseOver = false;
            _ = RefreshSensors(true);
        }

        private void ShowBatteryWear()
        {
            //Refresh again only after 15 Minutes since the last refresh
            if (lastBatteryRefresh == 0 || Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) > 15 * 60_000)
            {
                lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                HardwareControl.RefreshBatteryHealth();
            }

            if (HardwareControl.batteryHealth != -1)
            {
                labelCharge.Text = Properties.Strings.BatteryHealth + ": " + Math.Round(HardwareControl.batteryHealth, 1) + "%";
            }
        }

        private void SettingsForm_VisibleChanged(object? sender, EventArgs e)
        {
            sensorTimer.Enabled = this.Visible || sensorsAlways;
            if (this.Visible)
            {
                updateControl.CheckForUpdates();
                BeginInvoke(new Action(() =>
                {
                    buttonEnergySaver.Visible = PowerNative.GetBatterySaverStatus();
                }));
            }
        }

        private void ButtonUpdates_Click(object? sender, EventArgs e)
        {
            updateControl.LoadReleases();
        }

        protected override void WndProc(ref Message m)
        {

            if (m.Msg == NativeMethods.WM_POWERBROADCAST && m.WParam == (IntPtr)NativeMethods.PBT_APMSUSPEND)
            {
                Logger.WriteLine("System Suspend");
                Program.modeControl.SleepReset();
                m.Result = (IntPtr)1;
            }

            if (m.Msg == NativeMethods.WM_POWERBROADCAST && m.WParam == (IntPtr)NativeMethods.PBT_APMRESUMEAUTOMATIC)
            {
                Logger.WriteLine("System Resume");
                BatteryControl.AutoBattery();
                m.Result = (IntPtr)1;
            }

            if (m.Msg == NativeMethods.WM_POWERBROADCAST && m.WParam == (IntPtr)NativeMethods.PBT_POWERSETTINGCHANGE)
            {
                if (m.GetLParam(typeof(NativeMethods.POWERBROADCAST_SETTING)) is NativeMethods.POWERBROADCAST_SETTING settings)
                {
                    if (settings.PowerSetting == NativeMethods.PowerSettingGuid.LIDSWITCH_STATE_CHANGE)
                    {
                        switch (settings.Data)
                        {
                            case 0:
                                Logger.WriteLine("Lid Closed");
                                BatteryControl.AutoBattery();
                                InputDispatcher.lidClose = true;
                                break;
                            case 1:
                                Logger.WriteLine("Lid Open");
                                InputDispatcher.InitFNLock();
                                InputDispatcher.lidClose = false;
                                break;
                        }
                    }
                    else
                    {
                        switch (settings.Data)
                        {
                            case 0:
                                Logger.WriteLine("Monitor Power Off");
                                Program.hardwareOverlay?.SuspendForDisplayOff();
                                break;
                            case 1:
                                Logger.WriteLine("Monitor Power On");
                                BatteryControl.AutoBattery();
                                Program.hardwareOverlay?.ResumeForDisplayOn();
                                break;
                            case 2:
                                Logger.WriteLine("Monitor Dimmed");
                                break;
                        }
                    }
                    m.Result = (IntPtr)1;
                }
            }

            if (m.Msg == Program.WM_TASKBARCREATED)
            {
                Logger.WriteLine("Taskbar created, re-creating tray icon");
                if (Program.trayIcon is not null) Program.trayIcon.Visible = true;
            }

            try
            {
                base.WndProc(ref m);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public void SetContextMenu()
        {
            var currentMode = Modes.GetCurrent();

            foreach (ToolStripItem item in contextMenuStrip.Items.Cast<ToolStripItem>().ToList())
            {
                if (item is ToolStripMenuItem menuItem) menuItem.Dispose();
            }
            contextMenuStrip.Items.Clear();
            contextMenuStrip.ShowCheckMargin = true;
            contextMenuStrip.ImageScalingSize = new Size(16, 16);
            contextMenuStrip.ShowImageMargin = false;
            Padding padding = new Padding(5, 5, 5, 5);

            var title = new ToolStripMenuItem(Properties.Strings.PerformanceMode);
            title.Margin = padding;
            title.Enabled = false;
            contextMenuStrip.Items.Add(title);

            foreach (var mode in Modes.GetDictonary())
            {
                var menuMode = new ToolStripMenuItem(mode.Value);
                menuMode.Tag = mode.Key;
                menuMode.Click += (sender, args) => { Program.modeControl.SetPerformanceMode(mode.Key); };
                menuMode.Margin = padding;
                menuMode.Checked = (mode.Key == currentMode);
                contextMenuStrip.Items.Add(menuMode);
            }

            var menuAutoPowerMode = new ToolStripMenuItem(Properties.Strings.AutoPowerSourceMode);
            menuAutoPowerMode.Margin = padding;
            menuAutoPowerMode.Checked = AppConfig.Is("auto_mode_enabled");
            menuAutoPowerMode.Click += (sender, args) =>
            {
                bool enabled = !AppConfig.Is("auto_mode_enabled");
                AppConfig.Set("auto_mode_enabled", enabled ? 1 : 0);
                AppConfig.Flush();
                fansForm?.RefreshRuntimeSettings();
                Program.modeControl.ApplyAutoModeForPowerSource();
                SetContextMenu();
            };
            contextMenuStrip.Items.Add(menuAutoPowerMode);
            contextMenuStrip.Items.Add("-");

            if (isGpuSection)
            {
                var titleGPU = new ToolStripMenuItem(Properties.Strings.GPUMode);
                titleGPU.Margin = padding;
                titleGPU.Enabled = false;
                contextMenuStrip.Items.Add(titleGPU);

                menuEco = new ToolStripMenuItem(Properties.Strings.EcoMode + " (" + Properties.Strings.GPUModeEco + ")");
                menuEco.Click += ButtonEco_Click;
                menuEco.Margin = padding;
                menuEco.Checked = buttonEco.Activated;
                contextMenuStrip.Items.Add(menuEco);

                menuStandard = new ToolStripMenuItem(Properties.Strings.StandardMode + " (" + Properties.Strings.GPUModeStandard + ")");
                menuStandard.Click += ButtonStandard_Click;
                menuStandard.Margin = padding;
                menuStandard.Checked = buttonStandard.Activated;
                contextMenuStrip.Items.Add(menuStandard);

                menuUltimate = new ToolStripMenuItem(Properties.Strings.UltimateMode + " (" + Properties.Strings.GPUModeUltimate + ")");
                menuUltimate.Click += ButtonUltimate_Click;
                menuUltimate.Margin = padding;
                menuUltimate.Checked = buttonUltimate.Activated;
                menuUltimate.Visible = isMuxGpu;
                contextMenuStrip.Items.Add(menuUltimate);

                menuOptimized = new ToolStripMenuItem(Properties.Strings.Optimized);
                menuOptimized.Click += ButtonOptimized_Click;
                menuOptimized.Margin = padding;
                menuOptimized.Checked = buttonOptimized.Activated;
                contextMenuStrip.Items.Add(menuOptimized);

                contextMenuStrip.Items.Add("-");
            }

            var bwIcon = new ToolStripMenuItem(Properties.Strings.BWTrayIcon);
            bwIcon.Margin = padding;
            bwIcon.Checked = AppConfig.IsBWIcon();
            bwIcon.Click += (sender, args) =>
            {
                bwIcon.Checked = !bwIcon.Checked;
                AppConfig.Set("bw_icon", bwIcon.Checked ? 1 : 0);
                VisualiseIcon();
            };
            contextMenuStrip.Items.Add(bwIcon);

            contextMenuStrip.Items.Add("-");

            var menuOverlay = new ToolStripMenuItem(Properties.Strings.Overlay);
            menuOverlay.Click += (sender, args) => ToggleOverlay();
            menuOverlay.Margin = padding;
            menuOverlay.Checked = AppConfig.IsOverlay();
            contextMenuStrip.Items.Add(menuOverlay);

            var menuOverlayGameOnly = new ToolStripMenuItem(Properties.Strings.OverlayOnlyInGames);
            menuOverlayGameOnly.Click += (sender, args) => ToggleOverlayGameOnly();
            menuOverlayGameOnly.Margin = padding;
            menuOverlayGameOnly.Checked = AppConfig.IsOverlayGameOnly();
            menuOverlayGameOnly.Enabled = AppConfig.IsOverlay();
            contextMenuStrip.Items.Add(menuOverlayGameOnly);

            var quit = new ToolStripMenuItem(Properties.Strings.Quit);
            quit.Click += ButtonQuit_Click;
            quit.Margin = padding;
            contextMenuStrip.Items.Add(quit);

            //contextMenuStrip.ShowCheckMargin = true;
            contextMenuStrip.Renderer = new CustomMenuRenderer();

            InitContextMenuTheme();

            if (Program.trayIcon is not null) Program.trayIcon.ContextMenuStrip = contextMenuStrip;


        }

        public void InitContextMenuTheme()
        {
            if (contextMenuStrip is not null)
            {
                contextMenuStrip.BackColor = this.BackColor;
                contextMenuStrip.ForeColor = this.ForeColor;
            }

            donateControl?.ApplyTheme();
        }

        public void SetVersionLabel(string label, bool update = false)
        {
            if (InvokeRequired)
                Invoke(delegate
                {
                    labelVersion.Text = label;
                    if (update) labelVersion.ForeColor = colorTurbo;
                });
            else
            {
                labelVersion.Text = label;
                if (update) labelVersion.ForeColor = colorTurbo;
            }
        }


        private void LabelVersion_Click(object? sender, EventArgs e)
        {
            updateControl.Update();
        }


        private static void OnTimedEvent(Object? source, ElapsedEventArgs? e)
        {
            _ = Program.settingsForm.RefreshSensors();
        }

        private void ButtonFHD_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = "Switch to " + ((buttonFHD.Text == "FHD") ? "UHD" : "FHD") + " Mode";
        }

        private void Button120Hz_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = Properties.Strings.MaxRefreshTooltip;
        }

        private void Button60Hz_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = Properties.Strings.MinRefreshTooltip.Replace("60", ScreenControl.MIN_RATE.ToString());
        }

        private void ButtonScreen_MouseLeave(object? sender, EventArgs e)
        {
            labelTipScreen.Text = "";
        }

        private void ButtonScreenAuto_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = Properties.Strings.AutoRefreshTooltip.Replace("60", ScreenControl.MIN_RATE.ToString());
        }

        private void ButtonMiniled_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = Properties.Strings.ToggleMiniled;
        }

        private void ButtonDynamic_MouseHover(object? sender, EventArgs e)
        {
            labelTipScreen.Text = Properties.Strings.DynamicRefreshTooltip;
        }

        private void ButtonUltimate_MouseHover(object? sender, EventArgs e)
        {
            labelTipGPU.Text = Properties.Strings.UltimateGPUTooltip;
        }

        private void ButtonStandard_MouseHover(object? sender, EventArgs e)
        {
            labelTipGPU.Text = Properties.Strings.StandardGPUTooltip;
        }

        private void ButtonEco_MouseHover(object? sender, EventArgs e)
        {
            labelTipGPU.Text = Properties.Strings.EcoGPUTooltip;
        }

        private void ButtonOptimized_MouseHover(object? sender, EventArgs e)
        {
            labelTipGPU.Text = Properties.Strings.OptimizedGPUTooltip;
        }

        private void ButtonGPU_MouseLeave(object? sender, EventArgs e)
        {
            labelTipGPU.Text = "";
        }

        private void ButtonScreenAuto_Click(object? sender, EventArgs e)
        {
            if (AppConfig.HasDisplayModes())
            {
                ScreenControl.SetRefreshRateMode(RefreshRateMode.Auto);
                return;
            }
            ScreenControl.SetAutoRefresh(1);
            ScreenControl.AutoScreen();
        }


        private void CheckStartup_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is null) return;
            CheckBox chk = (CheckBox)sender;

            if (chk.Checked)
                Startup.Schedule();
            else
                Startup.UnSchedule();
        }

        private void LabelCPUFan_Click(object? sender, EventArgs e)
        {
            FanSensorControl.fanRpm = !FanSensorControl.fanRpm;
            _ = RefreshSensors(true);
        }

        private void PictureColor2_Click(object? sender, EventArgs e)
        {
            if (AppConfig.IsOmenKeyboardSupported())
            {
                OmenCycleZone();
                return;
            }
        }

        private void PictureColor_Click(object? sender, EventArgs e)
        {
            if (AppConfig.IsOmenKeyboardSupported())
            {
                OmenPickZoneColor();
                return;
            }
            buttonKeyboardColor.PerformClick();
        }

        private void ButtonKeyboard_Click(object? sender, EventArgs e)
        {
            if (extraForm == null || extraForm.Text == "")
            {
                extraForm = new Extra();
                AddOwnedForm(extraForm);
            }

            if (extraForm.Visible)
            {
                extraForm.Close();
            }
            else
            {
                extraForm.Show();
            }
        }

        public void FansInit()
        {
            if (fansForm == null || fansForm.Text == "") return;
            Invoke(fansForm.InitAll);
        }

        public void GPUInit()
        {
            if (fansForm == null || fansForm.Text == "") return;
            Invoke(fansForm.InitGPU);
        }

        public void FansToggle(int index = 0)
        {
            if (fansForm == null || fansForm.Text == "")
            {
                fansForm = new Fans();
                AddOwnedForm(fansForm);
            }

            if (fansForm.Visible)
            {
                fansForm.Close();
            }
            else
            {
                fansForm.FormPosition();
                fansForm.Show();
                fansForm.ToggleNavigation(index);
            }

        }

        private void ButtonFans_Click(object? sender, EventArgs e)
        {
            FansToggle();
        }

        private bool _maxFansActive = false;

        internal void InitMaxFans()
        {
            var current = Program.acpi?.GetFanMax();
            if (current.HasValue)
            {
                _maxFansActive = current.Value;
            }
            else
            {
                _maxFansActive = false;
            }
            Program.modeControl.SetFanMaxActive(_maxFansActive);
            UpdateMaxFansVisual();
        }

        private void UpdateMaxFansVisual()
        {
            buttonMaxFans.Activated = _maxFansActive;
            buttonMaxFans.BorderColor = _maxFansActive ? colorTurbo : colorCustom;
            buttonMaxFans.Invalidate();
        }

        private void ButtonMaxFans_Click(object? sender, EventArgs e)
        {
            if (Program.acpi == null || !Program.acpi.IsWmiReady())
            {
                Logger.WriteLine("ButtonMaxFans: WMI not ready, ignoring toggle");
                return;
            }

            bool enable = !_maxFansActive;
            if (enable)
            {
                // Pause and drain temperature-driven fan updates before issuing the
                // max-fan command so a falling-temperature tick cannot undo it.
                Program.modeControl.SetFanMaxActive(true);
            }

            int result = Program.acpi.SetFanMax(enable);

            if (result == 1)
            {
                _maxFansActive = enable;
                if (!enable)
                    Program.modeControl.SetFanMaxActive(false);
                UpdateMaxFansVisual();
                Program.toast?.RunToast(
                    Properties.Strings.MaxFans + " " + (enable ? Properties.Strings.On : Properties.Strings.Off),
                    ToastIcon.Fan);

                if (!enable)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(250);
                        Program.settingsForm.BeginInvoke(() => Program.modeControl.AutoFans(true));
                    });
                }
            }
            else
            {
                if (enable)
                {
                    Program.modeControl.SetFanMaxActive(false);
                    Program.modeControl.AutoFans(true);
                }
                Logger.WriteLine($"ButtonMaxFans: SetFanMax({enable}) failed");
            }
        }

        private void ButtonKeyboardColor_Click(object? sender, EventArgs e)
        {
            if (AppConfig.IsOmenKeyboardSupported())
            {
                OmenPickZoneColor();
            }
        }

        public void InitKeyboardLighting()
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled() || !AppConfig.IsOmenKeyboardSupported())
            {
                panelKeyboard.Visible = false;
                Logger.WriteLine("OMEN keyboard lighting control unavailable");
                return;
            }

            InitOmenKeyboard();
        }

        // ---- Omen WMI keyboard path ----
        // The Omen keyboard uses the WMI BiosCmd.Keyboard (0x20009) interface via
        // HpACPI. We reuse the existing keyboard panel
        // controls (comboKeyboard, buttonKeyboardColor, pictureColor, pictureColor2)
        // but drive them with Omen semantics:
        //   * comboKeyboard  -> effect list (Static/Breathing/ColorCycle/Wave)
        //   * buttonKeyboardColor / pictureColor -> opens color picker for the
        //     currently selected zone; clicking cycles Right -> Middle -> Left -> WASD
        //   * pictureColor2 -> preview of the next zone (visual hint)
        // Brightness is handled by the existing backlight hotkey path
        // (InputDispatcher.SetBacklight -> OmenApplyBacklight).

        private bool _omenKbInit;
        private int _omenKbZone = 0; // 0=Right, 1=Middle, 2=Left, 3=WASD
        private static readonly string[] _omenZoneNames =
            { Properties.Strings.OmenKeyboardZoneRight,
              Properties.Strings.OmenKeyboardZoneMiddle,
              Properties.Strings.OmenKeyboardZoneLeft,
              Properties.Strings.OmenKeyboardZoneWasd };

        private void InitOmenKeyboard()
        {
            if (_omenKbInit) return;
            _omenKbInit = true;

            int kbType = Program.acpi.GetKeyboardType();
            bool hasBacklight = Program.acpi.HasBacklight();

            Logger.WriteLine($"OmenKeyboard: type={kbType} hasBacklight={hasBacklight}");

            if (!hasBacklight && kbType < 0)
            {
                // No keyboard reachable via WMI - hide the whole panel.
                panelKeyboard.Visible = false;
                return;
            }

            // Omen uses the keyboard panel directly.
            buttonKeyboard.Visible = false;

            // Effect list keyed by integer (0=Static,1=Breathing,2=ColorCycle,3=Wave)
            comboKeyboard.DropDownStyle = ComboBoxStyle.DropDownList;
            comboKeyboard.Items.Clear();
            comboKeyboard.Items.Add(Properties.Strings.OmenEffectStatic);
            comboKeyboard.Items.Add(Properties.Strings.OmenEffectBreathing);
            comboKeyboard.Items.Add(Properties.Strings.OmenEffectColorCycle);
            comboKeyboard.Items.Add(Properties.Strings.OmenEffectWave);
            comboKeyboard.SelectedIndex = Math.Max(0, Math.Min(3, AppConfig.Get("omen_kb_effect", 0)));
            comboKeyboard.SelectedIndexChanged += ComboKeyboard_OmenEffectChanged;

            // Color picker buttons: clicking the color swatch cycles to the next zone
            // and opens the color picker for that zone. This reuses the existing
            // pictureColor / pictureColor2 / buttonKeyboardColor controls.
            buttonKeyboardColor.Text = _omenZoneNames[_omenKbZone];
            pictureColor2.Visible = true;

            // Load persisted zone colors (stored as ARGB ints under omen_kb_zone_<n>).
            for (int z = 0; z < HpACPI.KbZoneCount; z++)
            {
                int argb = AppConfig.Get($"omen_kb_zone_{z}", OmenDefaultZoneColor.ToArgb());
                _omenZoneColors[z] = Color.FromArgb(argb);
            }

            // Pre-fill the keyboard with the persisted colors so the hardware matches
            // the UI on startup.
            Task.Run(ApplyOmenZoneColors);

            VisualiseOmenKeyboard();
        }

        private static readonly Color OmenDefaultZoneColor = Color.FromArgb(255, 255, 255);

        private readonly Color[] _omenZoneColors = Enumerable
            .Repeat(OmenDefaultZoneColor, HpACPI.KbZoneCount)
            .ToArray();

        private void VisualiseOmenKeyboard()
        {
            buttonKeyboardColor.Text = _omenZoneNames[_omenKbZone];
            pictureColor.BackColor = _omenZoneColors[_omenKbZone];
            int nextZone = (_omenKbZone + 1) % HpACPI.KbZoneCount;
            pictureColor2.BackColor = _omenZoneColors[nextZone];
        }

        private void OmenPickZoneColor()
        {
            ColorDialog colorDlg = new ColorDialog();
            colorDlg.AllowFullOpen = true;
            colorDlg.Color = _omenZoneColors[_omenKbZone];

            try
            {
                colorDlg.CustomColors = (AppConfig.GetString("keyboard_color_custom",
                    AppConfig.GetString("aura_color_custom", "")) ?? "")
                    .Split('-').Select(int.Parse).ToArray();
            }
            catch { }

            if (colorDlg.ShowDialog() == DialogResult.OK)
            {
                AppConfig.Set("keyboard_color_custom", string.Join("-", colorDlg.CustomColors));
                AppConfig.Set($"omen_kb_zone_{_omenKbZone}", colorDlg.Color.ToArgb());
                _omenZoneColors[_omenKbZone] = colorDlg.Color;

                // Apply this one zone through the WMI color-table path. The table is
                // read-modify-written inside SetZoneColor so other zones are preserved.
                Task.Run(() =>
                {
                    try
                    {
                        Program.acpi.SetZoneColor(_omenKbZone,
                            colorDlg.Color.R, colorDlg.Color.G, colorDlg.Color.B);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"OmenKeyboard: SetZoneColor failed: {ex.Message}");
                    }
                });

                VisualiseOmenKeyboard();
            }
        }

        private void OmenCycleZone()
        {
            _omenKbZone = (_omenKbZone + 1) % HpACPI.KbZoneCount;
            VisualiseOmenKeyboard();
        }

        private void ComboKeyboard_OmenEffectChanged(object? sender, EventArgs e)
        {
            int effect = comboKeyboard.SelectedIndex;
            AppConfig.Set("omen_kb_effect", effect);
            Task.Run(() => ApplyOmenEffect(effect));
        }

        /// <summary>
        /// Pushes the persisted 4-zone colors down to the keyboard via WMI.
        /// Safe to call from a background thread.
        /// </summary>
        private void ApplyOmenZoneColors()
        {
            try
            {
                byte[] zones = new byte[HpACPI.KbZoneCount * HpACPI.KbZoneColorStride];
                for (int z = 0; z < HpACPI.KbZoneCount; z++)
                {
                    Color c = _omenZoneColors[z];
                    int o = z * HpACPI.KbZoneColorStride;
                    zones[o] = c.R;
                    zones[o + 1] = c.G;
                    zones[o + 2] = c.B;
                }
                Program.acpi.SetColorTable(zones);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"OmenKeyboard: SetColorTable failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies an LED animation effect via WMI CMD 0x07.
        /// Effect layout: zone=0xFF(all), colorMode, speed, brightness, colorCount, R,G,B, R,G,B.
        /// </summary>
        private void ApplyOmenEffect(int effect)
        {
            try
            {
                Color primary = _omenZoneColors[0];
                Color secondary = _omenZoneColors[1];

                byte[] anim = new byte[HpACPI.KbColorTableSize];
                anim[0] = 0xFF; // all zones
                anim[1] = (byte)effect; // 0=static,1=breathing,2=color cycle,3=wave
                anim[2] = 0x05; // speed (mid; lower = faster)
                anim[3] = 0x64; // brightness 100
                anim[4] = (byte)(effect == 0 ? 1 : 2); // color count (1 for static, 2 otherwise)
                anim[5] = primary.R;
                anim[6] = primary.G;
                anim[7] = primary.B;
                anim[8] = secondary.R;
                anim[9] = secondary.G;
                anim[10] = secondary.B;

                Program.acpi.SetLedAnimation(anim);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"OmenKeyboard: SetLedAnimation failed: {ex.Message}");
            }
        }

        public void CycleKeyboardEffect(int delta)
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;

            if (delta > 0)
            {
                if (comboKeyboard.SelectedIndex < comboKeyboard.Items.Count - 1)
                    comboKeyboard.SelectedIndex += 1;
                else
                    comboKeyboard.SelectedIndex = 0;
            }
            else
            {
                if (comboKeyboard.SelectedIndex > 0)
                    comboKeyboard.SelectedIndex -= 1;
                else
                    comboKeyboard.SelectedIndex = comboKeyboard.Items.Count - 1;
            }

            Program.toast.RunToast(comboKeyboard.GetItemText(comboKeyboard.SelectedItem) ?? string.Empty, ToastIcon.BacklightUp);
        }

        private void Button120Hz_Click(object? sender, EventArgs e)
        {
            if (AppConfig.HasDisplayModes())
            {
                ScreenControl.SetRefreshRateMode(RefreshRateMode.Hz120);
                return;
            }
            ScreenControl.SetAutoRefresh(0);
            ScreenControl.SetScreen(ScreenControl.MAX_REFRESH, 1);
        }

        private void Button60Hz_Click(object? sender, EventArgs e)
        {
            if (AppConfig.HasDisplayModes())
            {
                ScreenControl.SetRefreshRateMode(RefreshRateMode.Hz60);
                return;
            }
            ScreenControl.SetAutoRefresh(0);
            ScreenControl.SetScreen(ScreenControl.MIN_RATE, 0);
        }

        private void ButtonMiniled_Click(object? sender, EventArgs e)
        {
            ScreenControl.ToogleMiniled();
        }

        private void ButtonDynamic_Click(object? sender, EventArgs e)
        {
            ScreenControl.SetRefreshRateMode(RefreshRateMode.Dynamic);
        }



        public void VisualiseScreen(bool screenEnabled, bool screenAuto, int frequency, int maxFrequency, int overdrive, bool overdriveSetting, int miniled1, int miniled2, bool hdr, bool acm, int fhd, int hdrControl)
        {
            bool advancedColor = hdr || acm;

            ButtonEnabled(button60Hz, screenEnabled);
            ButtonEnabled(button120Hz, screenEnabled);
            ButtonEnabled(buttonScreenAuto, screenEnabled);
            ButtonEnabled(buttonMiniled, screenEnabled);
            ButtonEnabled(buttonDynamic, screenEnabled);

            labelSreen.Text = screenEnabled
                ? Properties.Strings.LaptopScreen + ": " + frequency + "Hz" + ((overdrive == 1) ? " + " + Properties.Strings.Overdrive : "")
                : Properties.Strings.LaptopScreen + ": " + Properties.Strings.TurnedOff;

            panelScreen.AccessibleName = labelSreen.Text;

            button60Hz.Activated = false;
            button120Hz.Activated = false;
            buttonScreenAuto.Activated = false;
            buttonDynamic.Activated = false;

            // Refresh rate mode UI (Auto/60Hz/120Hz/Dynamic) for Transcend 14 and similar
            if (AppConfig.HasDisplayModes())
            {
                var mode = ScreenControl.GetRefreshRateMode();
                bool hasMiniled = miniled1 >= 0 || miniled2 >= 0;
                SetScreenTableColumns(hasMiniled ? 5 : 4);
                tableScreen.SetColumn(buttonScreenAuto, 0);
                tableScreen.SetColumn(button60Hz, 1);
                tableScreen.SetColumn(button120Hz, 2);
                tableScreen.SetColumn(buttonDynamic, 3);
                if (hasMiniled) tableScreen.SetColumn(buttonMiniled, 4);

                buttonScreenAuto.Text = Properties.Strings.AutoMode;
                button60Hz.Text = "60Hz";
                button120Hz.Text = maxFrequency > ScreenControl.MIN_RATE ? maxFrequency + "Hz" : "120Hz";

                buttonDynamic.Text = Properties.Strings.DynamicMode;
                buttonDynamic.BorderColor = colorCustom;
                buttonDynamic.Visible = true;
                bool dynamicRefreshAvailable = ScreenNative.IsDynamicRefreshAvailable();
                buttonDynamic.Enabled = screenEnabled && dynamicRefreshAvailable;

                buttonMiniled.Visible = hasMiniled;
                buttonMiniled.Enabled = screenEnabled && !hdr;
                buttonMiniled.Activated = miniled1 == 1 || miniled2 == 0 || miniled2 == 1 || hdr;

                if (mode == RefreshRateMode.Auto)
                {
                    buttonScreenAuto.Activated = true;
                }
                else if (mode == RefreshRateMode.Hz60)
                {
                    button60Hz.Activated = true;
                }
                else if (mode == RefreshRateMode.Hz120)
                {
                    button120Hz.Activated = true;
                }
                else if (mode == RefreshRateMode.Dynamic && dynamicRefreshAvailable)
                {
                    buttonDynamic.Activated = true;
                }
                else if (mode == RefreshRateMode.Dynamic)
                {
                    buttonScreenAuto.Activated = true;
                }

                panelScreen.Visible = true;
                tableScreen.Visible = true;
                buttonFHD.Visible = false;
                buttonHDRControl.Visible = false;

                if (!screenEnabled)
                {
                    labelVisual.Text = Properties.Strings.VisualModesScreen;
                    labelVisual.Location = tableVisual.Location;
                    labelVisual.Width = tableVisual.Width;
                    labelVisual.Height = tableVisual.Height;
                    labelVisual.Visible = true;
                }
                else
                {
                    labelVisual.Visible = false;
                }

                return;
            }

            SetScreenTableColumns(4);
            tableScreen.SetColumn(buttonMiniled, 3);
            tableScreen.SetColumn(buttonFHD, 3);
            tableScreen.SetColumn(buttonHDRControl, 3);
            buttonDynamic.Visible = false;

            if (screenAuto)
            {
                buttonScreenAuto.Activated = true;
            }
            else if (frequency == ScreenControl.MIN_RATE)
            {
                button60Hz.Activated = true;
            }
            else if (frequency > ScreenControl.MIN_RATE)
            {
                button120Hz.Activated = true;
            }

            button60Hz.Text = ScreenControl.MIN_RATE + "Hz";

            if (maxFrequency > ScreenControl.MIN_RATE)
            {
                button120Hz.Text = maxFrequency.ToString() + "Hz" + (overdriveSetting ? " + OD" : "");
                panelScreen.Visible = true;
                tableScreen.Visible = true;
            }
            else if (maxFrequency > 0)
            {
                tableScreen.Visible = false;
                panelScreen.Visible = AppConfig.NoGpu();
            }

            if (fhd >= 0)
            {
                buttonFHD.Visible = true;
                buttonFHD.Text = fhd > 0 ? "FHD" : "UHD";
            }

            bool hdrControlVisible = (hdr && hdrControl >= 0);

            if (miniled1 >= 0)
            {
                buttonMiniled.Visible = !hdrControlVisible;
                buttonMiniled.Enabled = !hdr;
                buttonMiniled.Activated = miniled1 == 1 || hdr;
            }
            else if (miniled2 >= 0)
            {
                buttonMiniled.Visible = !hdrControlVisible;
                buttonMiniled.Enabled = !hdr;
                if (hdr) miniled2 = 1; // Show HDR as Multizone Strong

                switch (miniled2)
                {
                    // Multizone On
                    case 0:
                        buttonMiniled.Text = Properties.Strings.Multizone;
                        buttonMiniled.BorderColor = colorStandard;
                        buttonMiniled.Activated = true;
                        break;
                    // Multizone Strong
                    case 1:
                        buttonMiniled.Text = Properties.Strings.MultizoneStrong;
                        buttonMiniled.BorderColor = colorTurbo;
                        buttonMiniled.Activated = true;
                        break;
                    // Multizone Off
                    case 2:
                        buttonMiniled.Text = Properties.Strings.OneZone;
                        buttonMiniled.BorderColor = colorStandard;
                        buttonMiniled.Activated = false;
                        break;
                }
            }
            else
            {
                buttonMiniled.Visible = false;
            }

            if (hdrControlVisible)
            {
                buttonHDRControl.Visible = true;
                buttonHDRControl.Activated = hdrControl > 0;
                buttonHDRControl.BorderColor = colorTurbo;
            } else
            {
                buttonHDRControl.Visible = false;
            }

            if (advancedColor) labelVisual.Text = Properties.Strings.VisualModesHDR;
            if (!screenEnabled) labelVisual.Text = Properties.Strings.VisualModesScreen;

            if (!screenEnabled || advancedColor)
            {
                labelVisual.Location = tableVisual.Location;
                labelVisual.Width = tableVisual.Width;
                labelVisual.Height = tableVisual.Height;
                labelVisual.Visible = true;
            }
            else
            {
                labelVisual.Visible = false;
            }


        }

        private void ButtonQuit_Click(object? sender, EventArgs e)
        {
            Close();
            Program.trayIcon.Visible = false;
            Application.Exit();
        }

        /// <summary>
        /// Closes all forms except the settings. Hides the settings
        /// </summary>
        public void HideAll()
        {
            this.Hide();
            if (fansForm != null && fansForm.Text != "") fansForm.Close();
            if (extraForm != null && extraForm.Text != "") extraForm.Close();
            MemoryHelper.TrimAfter();
        }

        /// <summary>
        /// Brings all visible windows to the top, with settings being the focus
        /// </summary>
        public void ShowAll()
        {
            this.Activate();
            this.TopMost = true;
            this.TopMost = AppConfig.Is("topmost");
        }

        /// <summary>
        /// Check if any of fans, keyboard, update, or itself has focus
        /// </summary>
        /// <returns>Focus state</returns>
        public bool HasAnyFocus(bool lostFocusCheck = false)
        {
            return (fansForm != null && fansForm.ContainsFocus) ||
                   (extraForm != null && extraForm.ContainsFocus) ||
                   this.ContainsFocus ||
                   (lostFocusCheck && Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastLostFocus) < 300);
        }

        private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideAll();
            }
        }

        private void ButtonUltimate_Click(object? sender, EventArgs e)
        {
            Program.gpuControl.SetGPUMode(HpACPI.GPUModeUltimate);
        }

        private void ButtonStandard_Click(object? sender, EventArgs e)
        {
            Program.gpuControl.SetGPUMode(HpACPI.GPUModeStandard);
        }

        private void ButtonEco_Click(object? sender, EventArgs e)
        {
            Program.gpuControl.SetGPUMode(HpACPI.GPUModeEco);
        }


        private void ButtonOptimized_Click(object? sender, EventArgs e)
        {
            AppConfig.Set("gpu_auto", (AppConfig.Get("gpu_auto") == 1) ? 0 : 1);
            VisualiseGPUMode();
            Program.gpuControl.AutoGPUMode(true);
        }

        private void ButtonStopGPU_Click(object? sender, EventArgs e)
        {
            Program.gpuControl.KillGPUApps();
        }

        public async Task RefreshSensors(bool force = false)
        {
            int throttle = (!Visible && sensorsAlways) ? 6000 : 2000;
            if (!force && Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRefresh) < throttle) return;
            lastRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            string cpuTemp = "";
            string gpuTemp = "";

            string cpuFan = "";
            string gpuFan = "";

            string battery = "";
            string charge = "";

            try
            {
                await Task.Run(() => HardwareControl.ReadSensors());
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Sensor refresh failed: " + ex.Message);
                return;
            }
            if (HardwareControl.cpuTemp > 0)
                cpuTemp = ": " + TempHelper.FormatTemp((double)HardwareControl.cpuTemp);

            if (HardwareControl.batteryCapacity > 0)
            {
                charge = Properties.Strings.BatteryCharge + ": " + HardwareControl.batteryCharge;
            }

            if (HardwareControl.batteryRate < 0)
                battery = Properties.Strings.Discharging + ": " + Math.Round(-(decimal)HardwareControl.batteryRate, 1).ToString() + "W";
            else if (HardwareControl.batteryRate > 0)
                battery = Properties.Strings.Charging + ": " + Math.Round((decimal)HardwareControl.batteryRate, 1).ToString() + "W";


            if (HardwareControl.gpuTemp > 0)
            {
                gpuTemp = ": " + TempHelper.FormatTemp((double)HardwareControl.gpuTemp);
            }

            if (HardwareControl.cpuFan is not null) cpuFan = Strings.FanSpeed + ": " + HardwareControl.cpuFan;
            if (HardwareControl.gpuFan is not null) gpuFan = Strings.FanSpeed + ": " + HardwareControl.gpuFan;

            string trayTip = "CPU" + cpuTemp + " " + cpuFan;
            if (gpuTemp.Length > 0) trayTip += "\nGPU" + gpuTemp + " " + gpuFan;
            if (battery.Length > 0) trayTip += "\n" + battery;
            
            if (Program.settingsForm.IsHandleCreated)
                Program.settingsForm.BeginInvoke(delegate
                {
                    labelCPUFan.Text = "CPU" + cpuTemp + "  " + cpuFan;
                    labelGPUFan.Text = "GPU" + gpuTemp + "  " + gpuFan;

                    
                    labelBattery.Text = battery;
                    if (!batteryMouseOver && !batteryFullMouseOver) labelCharge.Text = charge;
                });

            if (Program.trayIcon is not null) Program.trayIcon.Text = trayTip;
        }

        public void Shutdown()
        {
            sensorTimer?.Stop();
            sensorTimer?.Dispose();
            fansForm?.Close();
            extraForm?.Close();
        }

        public void LabelFansResult(string text)
        {
            if (fansForm != null && !fansForm.IsDisposed && fansForm.Text != "")
                fansForm.LabelFansResult(text);
        }

        public void ToggleOverlay(bool fromHotkey = false)
        {
            bool enable = !AppConfig.IsOverlay();
            AppConfig.Set("overlay", enable ? 1 : 0);
            Logger.WriteLine("Overlay " + (enable ? "On" : "Off") + (AppConfig.IsOverlayGameOnly() ? " (game only)" : ""));
            if (enable)
                Program.hardwareOverlay?.StartOverlay();
            else
                Program.hardwareOverlay?.StopOverlay();

            if (fromHotkey && AppConfig.IsOverlayGameOnly())
                Program.toast.RunToast(Properties.Strings.Overlay + " " + (enable ? Properties.Strings.On : Properties.Strings.Off));

            SetContextMenu();
        }

        public void ToggleOverlayGameOnly()
        {
            AppConfig.Set("overlay_game_only", AppConfig.IsOverlayGameOnly() ? 0 : 1);
            if (AppConfig.IsOverlay())
            {
                Program.hardwareOverlay?.StopOverlay();
                Program.hardwareOverlay?.StartOverlay();
            }
            SetContextMenu();
        }

        public void ShowMode(int mode)
        {
            if (InvokeRequired)
                Invoke(delegate
                {
                    VisualiseMode(mode);
                });
            else
                VisualiseMode(mode);
        }

        protected void VisualiseMode(int mode)
        {
            buttonSilent.Activated = false;
            buttonBalanced.Activated = false;
            buttonTurbo.Activated = false;
            buttonUnleashed.Activated = false;
            buttonFans.Activated = false;

            switch (mode)
            {
                case HpACPI.PerformanceSilent:
                    buttonSilent.Activated = true;
                    break;
                case HpACPI.PerformanceTurbo:
                    buttonTurbo.Activated = true;
                    break;
                case HpACPI.PerformanceBalanced:
                    buttonBalanced.Activated = true;
                    break;
                case HpACPI.PerformanceManual:
                    buttonUnleashed.Activated = true;
                    break;
                default:
                    buttonFans.Activated = true;
                    buttonFans.BorderColor = Modes.GetBase(mode) switch
                    {
                        HpACPI.PerformanceSilent => colorEco,
                        HpACPI.PerformanceTurbo => colorTurbo,
                        HpACPI.PerformanceManual => colorCustom,
                        _ => colorStandard,
                    };
                    break;
            }

            foreach (var item in contextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Tag is not null)
                {
                    menuItem.Checked = ((int)menuItem.Tag == mode);
                }
            }
        }


        public void SetModeLabel(string modeText)
        {
            if (InvokeRequired)
            {
                Invoke(delegate
                {
                    labelPerf.Text = modeText;
                    panelPerformance.AccessibleName = labelPerf.Text;
                });
            }
            else
            {
                labelPerf.Text = modeText;
                panelPerformance.AccessibleName = labelPerf.Text;
            }

        }



        public void VisualiseGPUButtons(bool eco = true, bool standard = true, bool ultimate = true, bool optimized = true)
        {
            isMuxGpu = ultimate;

            buttonEco.Visible = eco;
            buttonStandard.Visible = standard;
            buttonUltimate.Visible = ultimate;
            buttonOptimized.Visible = optimized;
            buttonStopGPU.Visible = !eco && !standard;

            menuEco.Visible = eco;
            menuStandard.Visible = standard;
            menuUltimate.Visible = isMuxGpu;
            menuOptimized.Visible = optimized;

            tableGPU.SetColumn(buttonEco, 0);
            tableGPU.SetColumnSpan(buttonEco, 1);
            tableGPU.SetColumn(buttonStandard, 1);
            tableGPU.SetColumnSpan(buttonStandard, 1);
            tableGPU.SetColumn(buttonOptimized, 2);
            tableGPU.SetColumnSpan(buttonOptimized, 1);
            tableGPU.SetColumn(buttonUltimate, 3);
            tableGPU.SetColumnSpan(buttonUltimate, 1);
        }

        public void HideGPUModes(bool gpuExists)
        {
            isGpuSection = false;

            buttonEco.Visible = false;
            buttonStandard.Visible = false;
            buttonUltimate.Visible = false;
            buttonOptimized.Visible = false;
            buttonStopGPU.Visible = true;

            tableGPU.Visible = false;

            SetContextMenu();

            panelGPU.Visible = gpuExists;

        }


        public void LockGPUModes(string? text = null)
        {
            if (InvokeRequired) { Invoke(() => LockGPUModes(text)); return; }
            if (text is null) text = Properties.Strings.GPUMode + ": " + Properties.Strings.GPUChanging + " ...";

            ButtonEnabled(buttonOptimized, false);
            ButtonEnabled(buttonEco, false);
            ButtonEnabled(buttonStandard, false);
            ButtonEnabled(buttonUltimate, false);

            labelGPU.Text = text;
        }

        public void VisualiseGPUMode(int GPUMode = -1)
        {
            if (InvokeRequired) { Invoke(() => VisualiseGPUMode(GPUMode)); return; }

            if (toolTip.GetToolTip(pictureGPU) != (GPUModeControl.gpuError ?? ""))
            {
                pictureGPU.BackgroundImage = GPUModeControl.gpuError is null ? Properties.Resources.icons8_video_card_32 : SystemIcons.Warning.ToBitmap();
                toolTip.SetToolTip(pictureGPU, GPUModeControl.gpuError);
            }
            ButtonEnabled(buttonOptimized, true);
            ButtonEnabled(buttonEco, true);
            ButtonEnabled(buttonStandard, true);
            ButtonEnabled(buttonUltimate, true);

            if (GPUMode == -1)
                GPUMode = AppConfig.Get("gpu_mode");

            bool GPUAuto = AppConfig.Is("gpu_auto");

            buttonEco.Activated = false;
            buttonStandard.Activated = false;
            buttonUltimate.Activated = false;
            buttonOptimized.Activated = false;

            switch (GPUMode)
            {
                case HpACPI.GPUModeEco:
                    buttonOptimized.BorderColor = colorEco;
                    buttonEco.Activated = !GPUAuto;
                    buttonOptimized.Activated = GPUAuto;
                    labelGPU.Text = Properties.Strings.GPUMode + ": " + Properties.Strings.GPUModeEco;
                    panelGPU.AccessibleName = Properties.Strings.GPUMode + " - " + (GPUAuto ? Properties.Strings.Optimized : Properties.Strings.EcoMode);
                    break;
                case HpACPI.GPUModeUltimate:
                    buttonUltimate.Activated = true;
                    labelGPU.Text = Properties.Strings.GPUMode + ": " + Properties.Strings.GPUModeUltimate;
                    panelGPU.AccessibleName = Properties.Strings.GPUMode + " - " + Properties.Strings.UltimateMode;
                    break;
                default:
                    buttonOptimized.BorderColor = colorStandard;
                    buttonStandard.Activated = !GPUAuto;
                    buttonOptimized.Activated = GPUAuto;
                    if (AppConfig.IsAlwaysUltimate())
                        labelGPU.Text = Properties.Strings.GPUMode + ": " + Properties.Strings.GPUModeUltimate;
                    else
                        labelGPU.Text = Properties.Strings.GPUMode + ": " + Properties.Strings.GPUModeStandard;
                    panelGPU.AccessibleName = Properties.Strings.GPUMode + " - " + (GPUAuto ? Properties.Strings.Optimized : Properties.Strings.StandardMode);
                    break;
            }

            VisualiseIcon();
            if (isGpuSection)
            {
                menuEco.Checked = buttonEco.Activated;
                menuStandard.Checked = buttonStandard.Activated;
                menuUltimate.Checked = buttonUltimate.Activated;
                menuOptimized.Checked = buttonOptimized.Activated;
            }

            // UI Fix for small screeens
            if (Top < 0)
            {
                labelTipGPU.Visible = false;
                labelTipScreen.Visible = false;
                Top = 5;
            }

        }


        private (int Mode, bool IsDark, bool IsBlackAndWhite)? _lastIcon;
        private bool _isDark = CheckSystemDarkModeStatus();

        public void VisualiseIcon(bool themeChange = false)
        {
            if (Program.trayIcon is null) return;
            if (themeChange) _isDark = CheckSystemDarkModeStatus();

            int GPUMode = AppConfig.Get("gpu_mode");
            bool blackAndWhite = AppConfig.IsBWIcon();

            if (_lastIcon == (GPUMode, _isDark, blackAndWhite)) return;
            _lastIcon = (GPUMode, _isDark, blackAndWhite);

            Icon newIcon = GPUMode switch
            {
                HpACPI.GPUModeEco => blackAndWhite ? (_isDark ? Properties.Resources.light_eco : Properties.Resources.dark_eco) : Properties.Resources.eco,
                HpACPI.GPUModeUltimate => blackAndWhite ? (_isDark ? Properties.Resources.light_standard : Properties.Resources.dark_standard) : Properties.Resources.ultimate,
                _ => blackAndWhite ? (_isDark ? Properties.Resources.light_standard : Properties.Resources.dark_standard) : Properties.Resources.standard,
            };

            Icon? oldIcon = Program.trayIcon.Icon;
            Program.trayIcon.Icon = newIcon;
            oldIcon?.Dispose();
        }

        private void ButtonSilent_Click(object? sender, EventArgs e)
        {
            Program.modeControl.SetPerformanceMode(HpACPI.PerformanceSilent);
        }

        private void ButtonBalanced_Click(object? sender, EventArgs e)
        {
            Program.modeControl.SetPerformanceMode(HpACPI.PerformanceBalanced);
        }

        private void ButtonTurbo_Click(object? sender, EventArgs e)
        {
            Program.modeControl.SetPerformanceMode(HpACPI.PerformanceTurbo);
        }

        private void ButtonUnleashed_Click(object? sender, EventArgs e)
        {
            Program.modeControl.SetPerformanceMode(HpACPI.PerformanceManual);
        }


        public void ButtonEnabled(RButton but, bool enabled)
        {
            but.Enabled = enabled;
            but.BackColor = but.Enabled ? Color.FromArgb(255, but.BackColor) : Color.FromArgb(100, but.BackColor);
        }

        public void VisualiseBatteryTitle(int limit)
        {
            labelBatteryTitle.Text = Properties.Strings.BatteryChargeLimit + ": " + limit.ToString() + "%";
        }

        public void VisualiseBattery(int limit)
        {
            VisualiseBatteryTitle(limit);
            sliderBattery.Value = limit;
            sliderBattery.AccessibleName = Properties.Strings.BatteryChargeLimit + ": " + limit.ToString() + "%";
            //sliderBattery.AccessibilityObject.Select(AccessibleSelection.TakeFocus);

            VisualiseBatteryFull();
        }

        public void VisualiseBatteryFull()
        {
            if (batteryLimitBackend == BatteryChargeLimitBackendKind.HpBatteryCare)
            {
                int limit = BatteryControl.chargeFull ? BatteryControl.FullChargeLimit : BatteryControl.HpBatteryCareLimit;
                VisualiseBatteryTitle(limit);
                sliderBattery.Value = limit;
                sliderBattery.AccessibleName = Properties.Strings.BatteryChargeLimit + ": " + limit.ToString() + "%";
            }

            if (BatteryControl.chargeFull)
            {
                buttonBatteryFull.BackColor = colorStandard;
                buttonBatteryFull.ForeColor = RForm.foreMain;
                buttonBatteryFull.AccessibleName = Properties.Strings.BatteryChargeLimit + "100% on";
            }
            else
            {
                buttonBatteryFull.BackColor = buttonSecond;
                buttonBatteryFull.ForeColor = SystemColors.ControlDark;
                buttonBatteryFull.AccessibleName = Properties.Strings.BatteryChargeLimit + "100% off";
            }

        }


        public void UpdateKeyboardLabel()
        {
            string type = "";
            if (AppConfig.IsOmen())
            {
                // Prefer the runtime probe from WMI CMD 0x01 over the DB flags,
                // falling back to model DB flags if the probe hasn't run / failed.
                int kbType = Program.acpi?.GetKeyboardType() ?? -1;
                if (kbType == HpACPI.KbTypePerKeyRgb)
                    type = "Per-Key RGB";
                else if (kbType >= HpACPI.KbTypeStandard)
                    type = HpACPI.KeyboardTypeName(kbType) + " (4-Zone)";
                else
                {
                    var caps = AppConfig.GetModelCapabilities();
                    if (caps.HasPerKeyRgb)
                        type = "Per-Key RGB";
                    else if (caps.HasFourZoneRgb)
                        type = "4-Zone RGB";
                }
            }
            labelKeyboard.Text = Properties.Strings.LaptopKeyboard + (type.Length > 0 ? ": " + type : "");
        }

        public void VisualiseFnLock()
        {

            if (AppConfig.Is("fn_lock"))
            {
                buttonFnLock.BackColor = colorStandard;
                buttonFnLock.ForeColor = RForm.foreMain;
                buttonFnLock.AccessibleName = "Fn-Lock on";
            }
            else
            {
                buttonFnLock.BackColor = buttonSecond;
                buttonFnLock.ForeColor = SystemColors.ControlDark;
                buttonFnLock.AccessibleName = "Fn-Lock off";
            }
        }


        private void ButtonFnLock_Click(object? sender, EventArgs e)
        {
            InputDispatcher.ToggleFnLock();
        }

    }


}
