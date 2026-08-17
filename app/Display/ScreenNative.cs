using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OHelper.Display
{

    internal static class ScreenNative
    {
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const string DefaultDevice = @"\\.\DISPLAY1";

        /// <summary>
        /// Returns true if at least one active display is not the built-in internal panel.
        /// </summary>
        public static bool IsExternalDisplayConnected(bool log = false)
        {
            try
            {
                string? internalName = AppConfig.GetString("internal_display");
                foreach (var device in GetAllDevices())
                {
                    if (device.outputTechnology != DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL &&
                        device.outputTechnology != DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED &&
                        device.monitorFriendlyDeviceName != internalName)
                    {
                        if (log) Logger.WriteLine("Found external screen: " + device.monitorFriendlyDeviceName + ":" + device.outputTechnology);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }

            return false;
        }

        private static bool IsInternalDisplay(DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME device)
        {
            string? internalName = AppConfig.GetString("internal_display");
            return device.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                || device.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                || device.monitorFriendlyDeviceName == internalName;
        }

        /// <summary>
        /// Returns all active display target device names.
        /// </summary>
        public static IEnumerable<DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME> GetAllDevices()
        {
            var err = DisplayNative.GetDisplayConfigBufferSizes(
                DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != 0) throw new Win32Exception(err);

            var paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
            err = DisplayNative.QueryDisplayConfig(
                DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (err != 0) throw new Win32Exception(err);

            for (int i = 0; i < modeCount; i++)
            {
                if (modes[i].infoType != DisplayNative.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
                    continue;

                var deviceName = new DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                deviceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                deviceName.header.size = (uint)Marshal.SizeOf(deviceName);
                deviceName.header.adapterId = modes[i].adapterId;
                deviceName.header.id = modes[i].id;

                err = DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName);
                if (err == 0)
                    yield return deviceName;
                else
                    Logger.WriteLine("DisplayConfigGetDeviceInfo error: " + new Win32Exception(err).Message);
            }
        }

        /// <summary>
        /// Finds the GDI device name of the internal laptop screen (e.g. \\.\DISPLAY1).
        /// Optimized: single QueryDisplayConfig pass resolves the GDI name via
        /// DISPLAYCONFIG_SOURCE_DEVICE_NAME, eliminating the EnumDisplayDevices loop.
        /// </summary>
        public static string? FindLaptopScreen(bool log = false)
        {
            try
            {
                var err = DisplayNative.GetDisplayConfigBufferSizes(
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (err != 0) throw new Win32Exception(err);

                var paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
                err = DisplayNative.QueryDisplayConfig(
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount, paths, ref modeCount, modes, nint.Zero);
                if (err != 0) throw new Win32Exception(err);

                foreach (var path in paths)
                {
                    var targetName = new DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    targetName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = (uint)Marshal.SizeOf(targetName);
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;

                    if (DisplayNative.DisplayConfigGetDeviceInfo(ref targetName) != 0) continue;
                    if (!IsInternalDisplay(targetName)) continue;

                    if (log) Logger.WriteLine(targetName.monitorDevicePath + " " + targetName.outputTechnology);
                    AppConfig.Set("internal_display", targetName.monitorFriendlyDeviceName);

                    // Resolve GDI device name directly from the source path entry - no EnumDisplayDevices needed
                    var sourceName = new DisplayNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    sourceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    sourceName.header.size = (uint)Marshal.SizeOf(sourceName);
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;

                    if (DisplayNative.DisplayConfigGetDeviceInfo(ref sourceName) == 0)
                        return ExtractDisplay(sourceName.viewGdiDeviceName);

                    return Screen.PrimaryScreen?.DeviceName;
                }

                if (log) Logger.WriteLine("Internal screen off");
                return null;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
                return null;
            }
        }

        public static int GetMaxRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;

            var dm = CreateDevmode();
            int frequency = -1;
            int i = 0;
            while (DisplayNative.EnumDisplaySettingsEx(laptopScreen, i, ref dm) != 0)
            {
                if (dm.dmDisplayFrequency > frequency) frequency = dm.dmDisplayFrequency;
                i++;
            }

            if (frequency > 0) AppConfig.Set("screen_max", frequency);
            else frequency = AppConfig.Get("screen_max");

            return frequency;
        }

        public static int GetRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;

            var dm = CreateDevmode();
            return DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm) != 0
                ? dm.dmDisplayFrequency
                : -1;
        }

        public static int SetRefreshRate(string? laptopScreen, int frequency = 120)
        {
            if (string.IsNullOrEmpty(laptopScreen)) return 0;

            var dm = CreateDevmode();
            if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm) == 0) return 0;
            if (dm.dmDisplayFrequency == frequency) return 0;

            dm.dmDisplayFrequency = frequency;
            int result = DisplayNative.ChangeDisplaySettingsEx(
                laptopScreen, ref dm, IntPtr.Zero, DisplayNative.DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
            Logger.WriteLine("Screen = " + frequency + "Hz : " + (result == 0 ? "OK" : result));
            return result;
        }

        /// <summary>
        /// Enumerates distinct refresh rates available at the current resolution/bpp
        /// for the given display, sorted ascending.
        /// </summary>
        public static List<int> EnumRefreshRates(string? laptopScreen)
        {
            var rates = new SortedSet<int>();
            if (laptopScreen is null) return rates.ToList();

            var dm = CreateDevmode();
            if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm) == 0)
                return rates.ToList();

            int width = dm.dmPelsWidth;
            int height = dm.dmPelsHeight;
            int bpp = dm.dmBitsPerPel;

            int i = 0;
            while (DisplayNative.EnumDisplaySettingsEx(laptopScreen, i, ref dm) != 0)
            {
                if (dm.dmPelsWidth == width && dm.dmPelsHeight == height && dm.dmBitsPerPel == bpp
                    && dm.dmDisplayFrequency > 0)
                {
                    rates.Add(dm.dmDisplayFrequency);
                }
                i++;
            }

            return rates.ToList();
        }

        /// <summary>
        /// Sets a specific refresh rate on the given display and returns true on success.
        /// </summary>
        public static bool SetRefreshRateExact(string? laptopScreen, int hz)
        {
            if (string.IsNullOrEmpty(laptopScreen)) return false;
            return SetRefreshRate(laptopScreen, hz) == 0;
        }

        /// <summary>
        /// Windows DRR is exposed through CCD virtual refresh rate paths: the target path's
        /// refreshRate is the virtual floor and BOOST_REFRESH_RATE lets Windows boost to the
        /// selected physical timing.
        /// </summary>
        public static bool IsDynamicRefreshAvailable()
        {
            return SetDynamicRefresh(enable: true, apply: false);
        }

        public static bool EnableDynamicRefresh(bool enable)
        {
            return SetDynamicRefresh(enable, apply: true);
        }

        private static bool SetDynamicRefresh(bool enable, bool apply)
        {
            try
            {
                var queryFlags =
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS |
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_VIRTUAL_MODE_AWARE |
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_VIRTUAL_REFRESH_RATE_AWARE;

                int err = DisplayNative.GetDisplayConfigBufferSizes(queryFlags, out uint pathCount, out uint modeCount);
                if (err != 0) throw new Win32Exception(err);

                var paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
                err = DisplayNative.QueryDisplayConfig(queryFlags, ref pathCount, paths, ref modeCount, modes, nint.Zero);
                if (err != 0) throw new Win32Exception(err);

                int pathIndex = FindInternalPathIndex(paths, pathCount);
                if (pathIndex < 0)
                {
                    if (apply) Logger.WriteLine("EnableDynamicRefresh(" + enable + ") = no internal display path");
                    return false;
                }

                if (!paths[pathIndex].flags.HasFlag(DisplayNative.DISPLAYCONFIG_PATH_FLAGS.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE))
                {
                    if (apply) Logger.WriteLine("EnableDynamicRefresh(" + enable + ") = virtual refresh not supported");
                    return false;
                }

                if (enable)
                {
                    paths[pathIndex].flags |= DisplayNative.DISPLAYCONFIG_PATH_FLAGS.DISPLAYCONFIG_PATH_BOOST_REFRESH_RATE;
                    paths[pathIndex].targetInfo.refreshRate = new DisplayNative.DISPLAYCONFIG_RATIONAL
                    {
                        Numerator = (uint)ScreenControl.MIN_RATE,
                        Denominator = 1
                    };
                }
                else
                {
                    paths[pathIndex].flags &= ~DisplayNative.DISPLAYCONFIG_PATH_FLAGS.DISPLAYCONFIG_PATH_BOOST_REFRESH_RATE;
                }

                var setFlags =
                    DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG |
                    DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_ALLOW_CHANGES |
                    DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_VIRTUAL_MODE_AWARE |
                    DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_VIRTUAL_REFRESH_RATE_AWARE;

                setFlags |= apply
                    ? DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_SAVE_TO_DATABASE
                    : DisplayNative.SET_DISPLAY_CONFIG_FLAGS.SDC_VALIDATE;

                err = DisplayNative.SetDisplayConfig(pathCount, paths, modeCount, modes, setFlags);
                if (apply) Logger.WriteLine("EnableDynamicRefresh(" + enable + ") = " + (err == 0 ? "OK" : ("err " + err)));
                return err == 0;
            }
            catch (Exception ex)
            {
                if (apply) Logger.WriteLine("EnableDynamicRefresh: " + ex.Message);
                return false;
            }
        }

        private static int FindInternalPathIndex(DisplayNative.DISPLAYCONFIG_PATH_INFO[] paths, uint pathCount)
        {
            for (int i = 0; i < pathCount; i++)
            {
                var targetName = new DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                targetName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                targetName.header.size = (uint)Marshal.SizeOf(targetName);
                targetName.header.adapterId = paths[i].targetInfo.adapterId;
                targetName.header.id = paths[i].targetInfo.id;

                if (DisplayNative.DisplayConfigGetDeviceInfo(ref targetName) != 0) continue;
                if (IsInternalDisplay(targetName)) return i;
            }

            return -1;
        }

        public static DisplayNative.DEVMODE CreateDevmode()
        {
            var dm = new DisplayNative.DEVMODE
            {
                dmDeviceName = new string(new char[32]),
                dmFormName = new string(new char[32]),
            };
            dm.dmSize = (short)Marshal.SizeOf(dm);
            return dm;
        }

        private static string ExtractDisplay(string input)
        {
            // Find the first backslash after the UNC prefix (\\.\)
            int index = input.IndexOf((char)92, 4);
            return index != -1 ? input.Substring(0, index) : input;
        }
    }
}
