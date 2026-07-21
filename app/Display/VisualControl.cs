namespace OHelper.Display
{
    public enum SplendidGamut
    {
        Native = 50
    }

    public enum SplendidCommand
    {
        None = -1,
        Default = 11,
        Disabled = 18
    }

    /// <summary>
    /// Placeholder for future HP display-gamut and OLED-dimming support.
    /// No confirmed HP control path is currently available, so every operation
    /// is intentionally hidden and side-effect free.
    /// </summary>
    public static class VisualControl
    {
        public const int DefaultColorTemp = 50;
        public static bool forceVisual;

        public static bool IsSplendidSupported() => false;

        public static SplendidGamut GetDefaultGamut() => SplendidGamut.Native;

        public static SplendidCommand GetDefaultVisualMode() => SplendidCommand.Default;

        public static Dictionary<SplendidGamut, string> GetGamutModes() => new();

        public static Dictionary<SplendidCommand, string> GetVisualModes() => new();

        public static Dictionary<int, string> GetTemperatures() => new();

        public static void SetGamut(int mode = -1) { }

        public static void SetVisual(SplendidCommand mode = SplendidCommand.Default, int whiteBalance = DefaultColorTemp, bool init = false) { }

        public static void InitBrightness() { }

        public static int GetBrightness() => 100;

        public static int SetBrightness(int delta) => -1;
    }
}
