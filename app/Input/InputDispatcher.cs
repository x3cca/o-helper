using OHelper.Display;
using OHelper.Helpers;
using OHelper.Mode;
using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace OHelper.Input
{

    public class InputDispatcher : IDisposable
    {
        System.Timers.Timer timer = new System.Timers.Timer(AppConfig.Get("keyboard_timeout_refresh", 1000));
        public static bool backlightActivity = true;
        public static bool lidClose = false;
        public static bool tentMode = false;
        private static bool? _fnLock = null;

        private static long lastSleep;

        public static Keys keyProfile = (Keys)AppConfig.Get("keybind_profile", (int)Keys.F5);
        public static Keys keyApp = (Keys)AppConfig.Get("keybind_app", (int)Keys.F12);

        public static Keys keyProfile0 = (Keys)AppConfig.Get("keybind_profile_0", (int)Keys.F17);
        public static Keys keyProfile1 = (Keys)AppConfig.Get("keybind_profile_1", (int)Keys.F18);
        public static Keys keyProfile2 = (Keys)AppConfig.Get("keybind_profile_2", (int)Keys.F16);
        public static Keys keyProfile3 = (Keys)AppConfig.Get("keybind_profile_3", (int)Keys.F19);
        public static Keys keyProfile4 = (Keys)AppConfig.Get("keybind_profile_4", (int)Keys.F20);
        public static Keys keyOverlay = (Keys)AppConfig.Get("keybind_overlay", (int)Keys.O);

        public static ModifierKeys keyModifier = GetModifierKeys("modifier_keybind", ModifierKeys.Shift | ModifierKeys.Control);
        public static ModifierKeys keyModifierAlt = GetModifierKeys("modifier_keybind_alt", ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt);

        static ModeControl modeControl = Program.modeControl;

        KeyboardHook hook = new KeyboardHook();

        public InputDispatcher()
        {

            byte[] result = Program.acpi.DeviceInit();
            Debug.WriteLine($"Init: {BitConverter.ToString(result)}");

            //Task.Run(Program.acpi.RunListener);

            hook.KeyPressed += new EventHandler<KeyPressedEventArgs>(KeyPressed);

            RegisterKeys();

            timer.Elapsed += Timer_Elapsed;

        }

        private void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (Program.IsExiting) return;
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;
            if (GetBacklight() == 0) return;

            TimeSpan iddle = NativeMethods.GetIdleTime();
            int kb_timeout;

            if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
                kb_timeout = AppConfig.Get("keyboard_ac_timeout", 0);
            else
                kb_timeout = AppConfig.Get("keyboard_timeout", 60);

            if (kb_timeout == 0) return;

            if (backlightActivity && iddle.TotalSeconds > kb_timeout)
            {
                backlightActivity = false;
                if (AppConfig.IsOmenKeyboardSupported())
                    OmenApplyBacklight(0, "Timeout");
            }

            if (!backlightActivity && iddle.TotalSeconds < kb_timeout)
            {
                backlightActivity = true;
                SetBacklightAuto();
            }

            //Logger.WriteLine("Iddle: " + iddle.TotalSeconds);
        }

        public void Init()
        {
            Program.acpi.DeviceInit();

            InitBacklightTimer();
            MuteLEDInit();
        }

        public static void InitFNLock()
        {
            if (!IsHardwareFnLock()) return;
            HardwareFnLock(AppConfig.Is("fn_lock"));
        }

        public void InitBacklightTimer()
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled())
            {
                timer.Enabled = false;
                return;
            }

            timer.Enabled = AppConfig.Get("keyboard_timeout") > 0 && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online ||
                            AppConfig.Get("keyboard_ac_timeout") > 0 && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        }

        private static ModifierKeys GetModifierKeys(string configKey, ModifierKeys defaultModifiers)
        {
            string? configValue = AppConfig.GetString(configKey, "");
                
            if (string.IsNullOrWhiteSpace(configValue))
                return defaultModifiers;

            ModifierKeys modifiers = ModifierKeys.None;
            HashSet<string> keys = new HashSet<string>(configValue.Split('-'), StringComparer.OrdinalIgnoreCase);

            if (keys.Contains("win")) modifiers |= ModifierKeys.Win;
            if (keys.Contains("shift")) modifiers |= ModifierKeys.Shift;
            if (keys.Contains("control")) modifiers |= ModifierKeys.Control;
            if (keys.Contains("alt")) modifiers |= ModifierKeys.Alt;

            return modifiers;
        }

        public void RegisterKeys()
        {
            hook.UnregisterAll();
            hook.SetWinLock(AppConfig.Is("win_lock"));

            string? actionM1 = AppConfig.GetString("m1");
            string? actionM2 = AppConfig.GetString("m2");

            if (keyProfile != Keys.None)
            {
                hook.RegisterHotKey(keyModifier, keyProfile);
                hook.RegisterHotKey(keyModifierAlt, keyProfile);
            }

            if (keyApp != Keys.None) hook.RegisterHotKey(keyModifier, keyApp);

            if (!AppConfig.Is("skip_hotkeys"))
            {
                hook.RegisterHotKey(keyModifierAlt, Keys.F13);

                hook.RegisterHotKey(keyModifierAlt, Keys.F14);
                hook.RegisterHotKey(keyModifierAlt, Keys.F15);

                hook.RegisterHotKey(keyModifierAlt, keyProfile0);
                hook.RegisterHotKey(keyModifierAlt, keyProfile1);
                hook.RegisterHotKey(keyModifierAlt, keyProfile2);
                hook.RegisterHotKey(keyModifierAlt, keyProfile3);
                hook.RegisterHotKey(keyModifierAlt, keyProfile4);
                hook.RegisterHotKey(ModifierKeys.Control, Keys.VolumeDown);
                hook.RegisterHotKey(ModifierKeys.Control, Keys.VolumeUp);
                hook.RegisterHotKey(ModifierKeys.Shift, Keys.VolumeDown);
                hook.RegisterHotKey(ModifierKeys.Shift, Keys.VolumeUp);
                hook.RegisterHotKey(keyModifier, Keys.F20);
            }

            if (keyOverlay != Keys.None) hook.RegisterHotKey(keyModifierAlt, keyOverlay);

            if (actionM1 is not null && actionM1.Length > 0) hook.RegisterHotKey(ModifierKeys.None, Keys.VolumeDown);
            if (actionM2 is not null && actionM2.Length > 0) hook.RegisterHotKey(ModifierKeys.None, Keys.VolumeUp);

            // FN-Lock group

            if (AppConfig.Is("fn_lock") && !IsHardwareFnLock())
                for (Keys i = Keys.F1; i <= Keys.F11; i++) hook.RegisterHotKey(ModifierKeys.None, i);

            // Win-lock group - suppress Left/Right Windows keys when locked
            if (AppConfig.Is("win_lock"))
            {
                hook.RegisterHotKey(ModifierKeys.None, Keys.LWin);
                hook.RegisterHotKey(ModifierKeys.None, Keys.RWin);
            }

        }


        public static int[] ParseHexValues(string input)
        {
            string pattern = @"\b(0x[0-9A-Fa-f]{1,2}|[0-9A-Fa-f]{1,2})\b";

            if (!Regex.IsMatch(input, $"^{pattern}(\\s+{pattern})*$")) return new int[0];

            MatchCollection matches = Regex.Matches(input, pattern);

            int[] hexValues = new int[matches.Count];

            for (int i = 0; i < matches.Count; i++)
            {
                string hexValueStr = matches[i].Value;
                int hexValue = int.Parse(hexValueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? hexValueStr.Substring(2)
                    : hexValueStr, System.Globalization.NumberStyles.HexNumber);

                hexValues[i] = hexValue;
            }

            return hexValues;
        }


        static void RunKeyCommand(string? command, bool launchOnNoKeys = true)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            int[] hexKeys = new int[0];
            try { hexKeys = ParseHexValues(command); } catch { }

            switch (hexKeys.Length)
            {
                case 1:
                    KeyboardHook.KeyPress((Keys)hexKeys[0]);
                    break;
                case 2:
                    KeyboardHook.KeyKeyPress((Keys)hexKeys[0], (Keys)hexKeys[1]);
                    break;
                case 3:
                    KeyboardHook.KeyKeyKeyPress((Keys)hexKeys[0], (Keys)hexKeys[1], (Keys)hexKeys[2]);
                    break;
                case 4:
                    KeyboardHook.KeyKeyKeyKeyPress((Keys)hexKeys[0], (Keys)hexKeys[1], (Keys)hexKeys[2], (Keys)hexKeys[3]);
                    break;
                default:
                    if (launchOnNoKeys && !string.IsNullOrWhiteSpace(command)) LaunchProcess(command);
                    break;
            }
        }

        static void CustomKey(string configKey = "m3")
        {
            RunKeyCommand(AppConfig.GetString(configKey + "_custom"));
        }


        static void SetBrightness(bool up, bool hotkey = false)
        {
            int step = AppConfig.Get("brightness_step", 10);
            if (step != 10)
            {
                Program.toast.RunToast(ScreenBrightness.Adjust(up ? step : -step) + "%", up ? ToastIcon.BrightnessUp : ToastIcon.BrightnessDown);
                return;
            }

            Program.acpi.DeviceSet(HpACPI.UniversalControl, up ? HpACPI.Brightness_Up : HpACPI.Brightness_Down, "Brightness");

        }

        static void SetBrightnessDimming(int delta)
        {
            int brightness = VisualControl.SetBrightness(delta: delta);
            if (brightness >= 0)
                Program.toast.RunToast(brightness + "%", (delta < 0) ? ToastIcon.BrightnessDown : ToastIcon.BrightnessUp);
        }

        public void KeyPressed(object? sender, KeyPressedEventArgs e)
        {

            Logger.WriteLine(e.Key.ToString() + " " + e.Modifier.ToString());

            if (e.Modifier == ModifierKeys.None)
            {
                switch (e.Key)
                {
                    case Keys.F1:
                        KeyboardHook.KeyPress(Keys.VolumeMute);
                        break;
                    case Keys.F2:
                        SetBacklight(-1, true);
                        break;
                    case Keys.F3:
                        SetBacklight(1, true);
                        break;
                    case Keys.F4:
                        KeyProcess("fnf4");
                        break;
                    case Keys.F5:
                        KeyProcess("fnf5");
                        break;
                    case Keys.F6:
                        KeyboardHook.KeyPress(Keys.Snapshot);
                        break;
                    case Keys.F7:
                        SetBrightness(false);
                        break;
                    case Keys.F8:
                        SetBrightness(true);
                        break;
                    case Keys.F9:
                        KeyboardHook.KeyKeyPress(Keys.LWin, Keys.P);
                        break;
                    case Keys.F10:
                        ToggleTouchpadEvent(true);
                        break;
                    case Keys.F11:
                        SleepEvent();
                        break;
                    case Keys.VolumeDown:
                        KeyProcess("m1");
                        break;
                    case Keys.VolumeUp:
                        KeyProcess("m2");
                        break;
                    case Keys.Left:
                        KeyboardHook.KeyPress(Keys.Home);
                        break;
                    case Keys.Right:
                        KeyboardHook.KeyPress(Keys.End);
                        break;
                    case Keys.Up:
                        KeyboardHook.KeyPress(Keys.PageUp);
                        break;
                    case Keys.Down:
                        KeyboardHook.KeyPress(Keys.PageDown);
                        break;
                    default:
                        break;
                }

            }

            if (e.Modifier == keyModifier)
            {
                if (e.Key == keyProfile) modeControl.CyclePerformanceMode();
                if (e.Key == keyApp) Program.SettingsToggle();
                if (e.Key == Keys.F20) ToggleMic();
            }

            if (e.Modifier == keyModifierAlt)
            {
                if (e.Key == keyProfile) modeControl.CyclePerformanceMode(true);

                if (e.Key == keyProfile0) modeControl.SetPerformanceMode(0, true);
                if (e.Key == keyProfile1) modeControl.SetPerformanceMode(1, true);
                if (e.Key == keyProfile2) modeControl.SetPerformanceMode(2, true);
                if (e.Key == keyProfile3) modeControl.SetPerformanceMode(3, true);
                if (e.Key == keyProfile4) modeControl.SetPerformanceMode(4, true);
                if (e.Key == keyOverlay) Program.settingsForm.BeginInvoke(() => Program.settingsForm.ToggleOverlay(true));

                switch (e.Key)
                {
                    case Keys.F1:
                        SetBrightness(false);
                        break;
                    case Keys.F2:
                        SetBrightness(true);
                        break;
                    case Keys.F6:
                        ToggleTouchScreen();
                        break;
                    case Keys.F7:
                        SetBrightnessDimming(-10);
                        break;
                    case Keys.F8:
                        SetBrightnessDimming(10);
                        break;
                    case Keys.F13:
                        ToggleScreenRate();
                        break;
                    case Keys.F14:
                        Program.toast.RunToast(Properties.Strings.EcoMode);
                        Program.gpuControl.SetGPUMode(HpACPI.GPUModeEco);
                        break;
                    case Keys.F15:
                        Program.toast.RunToast(Properties.Strings.StandardMode);
                        Program.gpuControl.SetGPUMode(HpACPI.GPUModeStandard);
                        break;
                }
            }

            if (e.Modifier == (ModifierKeys.Control))
            {
                switch (e.Key)
                {
                    case Keys.VolumeDown:
                        // Screen brightness down on CTRL+VolDown
                        SetBrightness(false);
                        break;
                    case Keys.VolumeUp:
                        // Screen brightness up on CTRL+VolUp
                        SetBrightness(true);
                        break;
                }
            }

            if (e.Modifier == (ModifierKeys.Shift))
            {
                switch (e.Key)
                {
                    case Keys.VolumeDown:
                        // Keyboard backlight down on SHIFT+VolDown
                        SetBacklight(-1);
                        break;
                    case Keys.VolumeUp:
                        // Keyboard backlight up on SHIFT+VolUp
                        SetBacklight(1);
                        break;
                }
            }
        }


        public static void KeyProcess(string name = "m3")
        {
            string? action = AppConfig.GetString(name);

            if (action is null || action.Length <= 1)
            {
                if (name == "m4")
                    action = "OHelper";
                if (name == "fnf4")
                    action = "";
                if (name == "fnf5")
                    action = "performance";
                if (name == "m3")
                    action = "micmute";
                if (name == "fnc")
                    action = "fnlock";
                if (name == "fnv")
                    action = "visual";
                if (name == "fne")
                    action = "calculator";
            }

            switch (action)
            {
                case "mute":
                    KeyboardHook.KeyPress(Keys.VolumeMute);
                    break;
                case "play":
                    KeyboardHook.KeyPress(Keys.MediaPlayPause);
                    break;
                case "screenshot":
                    KeyboardHook.KeyPress(Keys.Snapshot);
                    break;
                case "lock":
                    Logger.WriteLine("Screen lock");
                    NativeMethods.LockScreen();
                    break;
                case "screen":
                    Logger.WriteLine("Screen off toggle");
                    NativeMethods.TurnOffScreen();
                    break;
                case "miniled":
                    if (ScreenCCD.GetHDRStatus()) return;
                    string miniledName = ScreenControl.ToogleMiniled();
                    Program.toast.RunToast(miniledName, miniledName == Properties.Strings.OneZone ? ToastIcon.BrightnessDown : ToastIcon.BrightnessUp);
                    break;
                case "keyboard":
                case "aura": // Legacy action name retained for existing configurations.
                    Program.settingsForm.BeginInvoke(Program.settingsForm.CycleKeyboardEffect, Control.ModifierKeys == Keys.Shift ? -1 : 1);
                    break;
                case "visual":
                    Program.settingsForm.BeginInvoke(Program.settingsForm.CycleVisualMode, Control.ModifierKeys == Keys.Shift ? -1 : 1);
                    break;
                case "performance":
                    modeControl.CyclePerformanceMode(Control.ModifierKeys == Keys.Shift);
                    break;
                case "OHelper":
                    try
                    {
                        Program.settingsForm.BeginInvoke(delegate
                        {
                            Program.SettingsToggle();
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    break;
                case "fnlock":
                    ToggleFnLock();
                    break;
                case "overlay":
                    Program.settingsForm.BeginInvoke(() => Program.settingsForm.ToggleOverlay(true));
                    break;
                case "micmute":
                    ToggleMic();
                    break;
                case "brightness_up":
                    SetBrightness(true);
                    break;
                case "brightness_down":
                    SetBrightness(false);
                    break;
                case "custom":
                    CustomKey(name);
                    break;
                case "calculator":
                    LaunchProcess("calc");
                    break;
                case "touchscreen":
                    ToggleTouchScreen();
                    break;
                default:
                    break;
            }
        }


        static void MuteLED()
        {
            // HP Omen has no confirmed speaker-mute LED command, so skip the call.
            if (AppConfig.IsOmen()) return;
            Thread.Sleep(500);
            Program.acpi.DeviceSet(HpACPI.SoundMuteLed, Audio.IsMuted() ? 1 : 0, "SoundLed");
        }

        static void ToggleTouchScreen()
        {
            var status = !TouchscreenHelper.GetStatus();
            Logger.WriteLine("Touchscreen status: " + status);
            if (status is not null)
            {
                Program.toast.RunToast(Properties.Strings.Touchscreen + " " + ((bool)status ? Properties.Strings.On : Properties.Strings.Off), ToastIcon.Touchpad);
                TouchscreenHelper.ToggleTouchscreen((bool)status);
            }
        }

        static void ToggleMic()
        {
            bool muteStatus = Audio.ToggleMicMute();
            Program.toast.RunToast(muteStatus ? Properties.Strings.Muted : Properties.Strings.Unmuted, muteStatus ? ToastIcon.MicrophoneMute : ToastIcon.Microphone);
        }

        static void MuteLEDInit()
        {
            // HP microphone and speaker LEDs do not have a confirmed control command.
        }

        static bool GetTouchpadState()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status", false))
            {
                Logger.WriteLine("Touchpad status:" + key?.GetValue("Enabled")?.ToString());
                return key?.GetValue("Enabled")?.ToString() == "1";
            }
        }

        static void ToggleTouchpadEvent(bool hotkey = false)
        {
            if (hotkey || !AppConfig.IsHardwareTouchpadToggle()) ToggleTouchpad();
            Thread.Sleep(200);
            Program.toast.RunToast(GetTouchpadState() ? Properties.Strings.On : Properties.Strings.Off, ToastIcon.Touchpad);
        }

        static void ToggleTouchpad()
        {
            KeyboardHook.KeyKeyKeyPress(Keys.LWin, Keys.LControlKey, Keys.F24, 50);

        }

        static void SleepEvent()
        {
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastSleep) < 1000) return;
            lastSleep = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Program.acpi.DeviceSet(HpACPI.UniversalControl, HpACPI.KB_Sleep, "Sleep");
        }

        public static bool IsHardwareFnLock()
        {
            if (AppConfig.IsHardwareFnLock()) return true;
            if (_fnLock is null)
            {
                var fnLockStatus = Program.acpi.DeviceGet(HpACPI.FnLock);
                Logger.WriteLine("FnLock Support: " + fnLockStatus);
                _fnLock = fnLockStatus >= 0;
            }
            return (bool)_fnLock;
        }

        public static void HardwareFnLock(bool fnLock)
        {
            Program.acpi.DeviceSet(HpACPI.FnLock, fnLock ? 1 : 0, "FnLock");
        }

        public static void ToggleFnLock()
        {
            bool fnLock = !AppConfig.Is("fn_lock");
            AppConfig.Set("fn_lock", fnLock ? 1 : 0);

            if (IsHardwareFnLock())
                HardwareFnLock(fnLock);
            else
                Program.settingsForm.BeginInvoke(new Action(Program.inputDispatcher.RegisterKeys));

            Program.settingsForm.BeginInvoke(Program.settingsForm.VisualiseFnLock);

            Program.toast.RunToast(fnLock ? Properties.Strings.FnLockOn : Properties.Strings.FnLockOff, ToastIcon.FnLock);
        }

        public static void ToggleWinLock()
        {
            bool winLock = !AppConfig.Is("win_lock");
            AppConfig.Set("win_lock", winLock ? 1 : 0);

            Program.settingsForm.BeginInvoke(new Action(Program.inputDispatcher.RegisterKeys));

            Program.toast.RunToast("Win-Lock " + (winLock ? Properties.Strings.On : Properties.Strings.Off), ToastIcon.FnLock);
        }

        public static void SetSlateMode(int status)
        {
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\PriorityControl", "ConvertibleSlateMode", status, RegistryValueKind.DWord);
                Logger.WriteLine("Setting ConvertibleSlateMode : " + status);
            } catch (Exception ex)
            {
                Logger.WriteLine("Can't set ConvertibleSlateMode: " + ex.Message);
            }
        }

        public static void TabletMode()
        {
            if (AppConfig.Is("disable_tablet")) return;

            int tabletStateValue = Program.acpi.DeviceGet(HpACPI.TabletState);
            int slateState = Program.acpi.DeviceGet(HpACPI.SlateMode);
            if (tabletStateValue < 0 && slateState < 0)
            {
                Logger.WriteLine("Tablet/slate state unsupported");
                return;
            }

            bool touchpadState = GetTouchpadState();
            bool tabletState = tabletStateValue > 0;

            Logger.WriteLine($"Tablet: {tabletState} | SlateMode: {slateState} | Touchpad: {touchpadState}");

            if (slateState >= 0) SetSlateMode(slateState);
            if (tabletState && touchpadState || !tabletState && !touchpadState) ToggleTouchpad();
        }

        static int GetTentState()
        {
            var tentState = Program.acpi.DeviceGet(HpACPI.TentState);
            // TentState can be sticky on convertibles; cross-check TabletState.
            if (tentState > 0 && Program.acpi.DeviceGet(HpACPI.TabletState) == HpACPI.Tablet_Notebook) tentState = 0;
            Logger.WriteLine($"Tent: {tentState}");
            return tentState;
        }

        public static void TentMode()
        {
            var tentState = GetTentState();
            if (tentState < 0) return;
            tentMode = tentState > 0;
            if (AppConfig.IsOmenKeyboardSupported())
                OmenApplyBacklight(tentMode ? 0 : GetBacklight(), "Tent");
        }

        static void HandleEvent(int EventID)
        {
            switch (EventID)
            {
                    case 95:     // Configurable auxiliary button
                        KeyProcess("m4");
                        return;
                    case 134:     // FN + F12 ON OLD DEVICES
                    case 139:
                        KeyProcess("m4");
                        return;
                    case 124:    // M3
                        KeyProcess("m3");
                        return;
                    case 56:    // Configurable auxiliary button
                        KeyProcess("m4");
                        return;
                    case 55:    // Legacy auxiliary-button event
                        KeyProcess("m6");
                        return;
                    case 181:    // FN + Numpad Enter
                        KeyProcess("fne");
                        return;
                    case 93:    // GoPro key
                    case 174:   // FN+F5
                    case 153:   // FN+F5 OLD MODELS
                        modeControl.CyclePerformanceMode(Control.ModifierKeys == Keys.Shift);
                        return;
                    case 178:   // FN+LEFT ARROW / FN + F4
                        Program.settingsForm.BeginInvoke(Program.settingsForm.CycleKeyboardEffect, -1);
                        return;
                    case 179:   // FN+F4
                        KeyProcess("fnf4");
                        return;
                    case 138:   // Fn + V
                        KeyProcess("fnv");
                        return;
                    case 158:   // Fn + C
                        KeyProcess("fnc");
                        return;
                    case 189: // Tablet mode
                        AutoKeyboard();
                        return;
                    case 197: // FN+F2
                        SetBacklight(-1);
                        return;
                    case 196: // FN+F3
                        SetBacklight(1);
                        return;
                    case 199: // Legacy backlight-cycle event
                        SetBacklight(4);
                        return;
                    case 46: // Legacy brightness-down event
                        if (Control.ModifierKeys == Keys.Control && AppConfig.IsOLED())
                        {
                            SetBrightnessDimming(-10);
                        }
                        break;
                    case 47: // Legacy brightness-up event
                        if (Control.ModifierKeys == Keys.Control && AppConfig.IsOLED())
                        {
                            SetBrightnessDimming(10);
                        }
                        break;
            }

            HandleOptimizationEvent(EventID);

        }

        // Firmware hotkey events
        static void HandleOptimizationEvent(int EventID)
        {
            switch (EventID)
            {
                case 16: // FN+F7
                    if (Control.ModifierKeys == Keys.Control && AppConfig.IsOLED())
                    {
                        SetBrightnessDimming(-10);
                    }
                    else
                    {
                        SetBrightness(false, true);
                    }
                    break;
                case 32: // FN+F8
                    if (Control.ModifierKeys == Keys.Control && AppConfig.IsOLED())
                    {
                        SetBrightnessDimming(10);
                    }
                    else
                    {
                        SetBrightness(true, true);
                    }
                    break;
                case 133: // Camera Toggle
                    ToggleCamera();
                    break;
                case 107: // FN+F10
                    ToggleTouchpadEvent();
                    break;
                case 108: // FN+F11
                    if (!AppConfig.IsHardwareHotkeys()) SleepEvent();
                    else lastSleep = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    break;
                case 51:    // Legacy display-off event
                case 53:
                    NativeMethods.TurnOffScreen();
                    return;
                case 126:    // Fn+F8 emojis popup
                    KeyboardHook.KeyKeyPress(Keys.LWin, Keys.OemSemicolon);
                    return;
                case 78:    // Fn + ESC
                    ToggleFnLock();
                    return;
                case 79:    // Fn + Win
                    ToggleWinLock();
                    return;
                case 136:    // FN + F12
                    if (!AppConfig.IsHardwareHotkeys()) Program.acpi.DeviceSet(HpACPI.UniversalControl, HpACPI.Airplane, "Airplane");
                    return;
                case 50:
                    // Sound Mute Event
                    MuteLED();
                    return;
                case 157:   // Legacy performance-mode event
                    modeControl.CyclePerformanceMode(Control.ModifierKeys == Keys.Shift);
                    return;
                case 250:
                    // Tent Mode
                    TentMode();
                    return;
            }
        }


        public static int GetBacklight()
        {
            int backlight_power = AppConfig.Get("keyboard_brightness", 1);
            int backlight_battery = AppConfig.Get("keyboard_brightness_ac", 1);
            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;

            int backlight;

            //backlight = onBattery ? Math.Min(backlight_battery, backlight_power) : Math.Max(backlight_battery, backlight_power);
            backlight = onBattery ? backlight_battery : backlight_power;

            return Math.Max(Math.Min(3, backlight), 0);
        }

        public static void AutoKeyboard()
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled())
            {
                Logger.WriteLine("AutoKeyboard: keyboard lighting control disabled");
                return;
            }

            if (AppConfig.HasTabletMode()) TabletMode();
            if (lidClose)
            {
                Logger.WriteLine("Skipping Backlight Init: Lid Closed");
                return;
            }

            if (tentMode)
            {
                tentMode = GetTentState() > 0;
                if (tentMode)
                {
                    Logger.WriteLine("Skipping Backlight Init: Tent Mode");
                    return;
                }
            }

            if (AppConfig.IsOmenKeyboardSupported())
            {
                Logger.WriteLine("AutoKeyboard: Omen path (IsOmenKeyboardSupported)");
                SetBacklightAuto();
            }
            else if (AppConfig.IsOmen())
            {
                Logger.WriteLine("AutoKeyboard: Omen keyboard control unsupported");
            }
        }


        public static void SetBacklightAuto()
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;
            if (lidClose || tentMode) return;
            if (AppConfig.IsOmenKeyboardSupported())
                OmenApplyBacklight(GetBacklight(), "Auto");
            backlightActivity = true;
        }

        public static void StartupBacklight()
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled())
            {
                Logger.WriteLine("StartupBacklight: keyboard lighting control disabled");
                return;
            }

            if (AppConfig.IsOmenKeyboardSupported())
            {
                OmenApplyBacklight(GetBacklight(), "Startup");
                return;
            }
            if (AppConfig.IsOmen()) Logger.WriteLine("StartupBacklight: Omen keyboard control unsupported");
        }

        public static void SetBacklight(int delta, bool force = false)
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;

            int backlight_power = AppConfig.Get("keyboard_brightness", 1);
            int backlight_battery = AppConfig.Get("keyboard_brightness_ac", 1);
            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;

            int backlight = onBattery ? backlight_battery : backlight_power;
            int backlightMax = AppConfig.Get("max_brightness", 3);

            if (delta > backlightMax)
                backlight = ++backlight % (backlightMax + 1);
            else
                backlight = Math.Max(Math.Min(backlightMax, backlight + delta), 0);

            if (onBattery)
                AppConfig.Set("keyboard_brightness_ac", backlight);
            else
                AppConfig.Set("keyboard_brightness", backlight);

            if (AppConfig.IsOmenKeyboardSupported())
            {
                OmenApplyBacklight(backlight, "HotKey");
            }
            if (AppConfig.IsOmenKeyboardSupported())
            {
                // Omen has no OSD service - always show our own toast.
                string[] backlightNames = new string[] { Properties.Strings.BacklightOff, Properties.Strings.BacklightLow, Properties.Strings.BacklightMid, Properties.Strings.BacklightMax };
                Program.toast.RunToast(backlightNames[backlight], delta > 0 ? ToastIcon.BacklightUp : ToastIcon.BacklightDown);
            }

        }

        public static void SetBacklightLevel(int level)
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;

            int backlight = Math.Clamp(level, 0, AppConfig.Get("max_brightness", 3));
            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
            AppConfig.Set(onBattery ? "keyboard_brightness_ac" : "keyboard_brightness", backlight);
            if (AppConfig.IsOmenKeyboardSupported())
                OmenApplyBacklight(backlight, "Slider");
        }

        // Translate the 0..3 backlight level the rest of the app uses into the
        // raw byte range HP firmware expects (0x64 = off, 0xE4 = full on) and
        // apply it through the WMI keyboard interface.
        static void OmenApplyBacklight(int backlight, string log)
        {
            if (!AppConfig.IsKeyboardLightingControlEnabled()) return;
            if (Program.acpi == null) return;

            byte raw;
            switch (backlight)
            {
                case 0: raw = HpACPI.KbBrightnessOff; break;
                case 1: raw = 0xA4; break;       // ~33%
                case 2: raw = 0xC4; break;       // ~66%
                case 3: raw = HpACPI.KbBrightnessFull; break;
                default: raw = HpACPI.KbBrightnessFull; break;
            }

            Logger.WriteLine($"OmenBacklight ({log}): level={backlight} raw=0x{raw:X2}");
            try
            {
                Program.acpi.SetBrightnessLevel(raw);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"OmenBacklight failed: {ex.Message}");
            }
        }

        public static void ToggleScreenRate()
        {
            AppConfig.Set("screen_auto", 0);
            ScreenControl.ToggleScreenRate();
        }

        public static void ToggleCamera()
        {
            Logger.WriteLine("Camera shutter hotkey ignored: no confirmed HP control command");
        }

        public static void SetStatusLED(bool status)
        {
            Program.acpi.DeviceSet(HpACPI.StatusLed, status ? 7 : 0, "StatusLED");
        }

        public static void InitStatusLed()
        {
            if (AppConfig.IsAutoStatusLed()) SetStatusLED(true);
        }

        public static void ShutdownStatusLed()
        {
            if (AppConfig.IsAutoStatusLed()) SetStatusLED(false);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
            hook.Dispose();
        }

        static void LaunchProcess(string command = "")
        {
            if (string.IsNullOrEmpty(command)) return;
            try
            {
                RestrictedProcessHelper.RunAsRestrictedUser(command);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to run: {command} {ex.Message}");
            }
        }
    }
}
