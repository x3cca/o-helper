using OHelper.Display;
using OHelper.Gpu.NVidia;
using OHelper.Helpers;
using System.Diagnostics;

namespace OHelper.Gpu
{
    public class GPUModeControl
    {
        SettingsForm settings;

        public static int gpuMode;
        public static bool? gpuExists = null;

        static bool nvRestartPending;


        public GPUModeControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
        }

        private void RefreshFansGpuTab()
        {
            if (settings.fansForm is not null && !settings.fansForm.IsDisposed && settings.fansForm.Text != "")
                settings.fansForm.BeginInvoke((Action)settings.fansForm.InitGPU);
        }

        public void InitGPUMode()
        {
            if (AppConfig.NoGpu())
            {
                settings.HideGPUModes(false);
                return;
            }

            bool switchingSupported = Program.acpi.SupportsGpuModeSwitching();
            bool showEco = switchingSupported && Program.acpi.SupportsGpuMode(HpACPI.GPUModeEco);
            bool showStandard = switchingSupported && Program.acpi.SupportsGpuMode(HpACPI.GPUModeStandard);
            bool showUltimate = switchingSupported && Program.acpi.SupportsGpuMode(HpACPI.GPUModeUltimate);

            int mode = Program.acpi.GetGpuMode();
            Logger.WriteLine($"GPU mode: {mode} (switching supported: {switchingSupported}, eco: {showEco}, std: {showStandard}, ultimate: {showUltimate})");

            gpuMode = mode;

            if (switchingSupported)
            {
            bool showOptimized = (showEco || showStandard) && showUltimate;

            settings.VisualiseGPUButtons(showEco, showStandard, showUltimate, showOptimized);
            }
            else
            {
                if (gpuExists is null) gpuExists = Program.acpi.GetFan(HpFan.GPU) >= 0;
                settings.HideGPUModes((bool)gpuExists);
                return;
            }

            if (mode == HpACPI.GPUModeEco && HardwareControl.GpuControl?.IsValid == true)
            {
                Logger.WriteLine("GPU in iGPU-only mode but dGPU control still active - disposing");
                HardwareControl.DisposeGpuControl();
            }

            AppConfig.Set("gpu_mode", gpuMode);
            settings.VisualiseGPUMode(gpuMode);
            RefreshFansGpuTab();

            Task.Run(CheckGpuError);

        }



        public void SetGPUMode(int GPUMode, int auto = 0)
        {
            if (!Program.acpi.SupportsGpuMode(GPUMode))
            {
                Logger.WriteLine($"GPU mode {GPUMode} not supported on this model");
                settings.VisualiseGPUMode();
                return;
            }

            int CurrentGPU = AppConfig.Get("gpu_mode");
            AppConfig.Set("gpu_auto", auto);

            if (CurrentGPU == GPUMode)
            {
                settings.VisualiseGPUMode();
                return;
            }

            string modeName = GPUMode switch
            {
                HpACPI.GPUModeEco => Properties.Strings.GPUModeEco,
                HpACPI.GPUModeStandard => Properties.Strings.GPUModeStandard,
                HpACPI.GPUModeUltimate => Properties.Strings.GPUModeUltimate,
                _ => "GPU Mode"
            };

            DialogResult dialogResult = MessageBox.Show(
                string.Format(Properties.Strings.AlertGpuModeReboot, modeName),
                Properties.Strings.AlertUltimateTitle,
                MessageBoxButtons.YesNo);

            if (dialogResult != DialogResult.Yes)
            {
                settings.VisualiseGPUMode();
                return;
            }

            int status = Program.acpi.DeviceSet(HpACPI.GPUEco, GPUMode, "GPUMode");
            Logger.WriteLine($"GPU mode set result: {status}");

            if (status == 1)
            {
                AppConfig.Set("gpu_mode", GPUMode);
                settings.VisualiseGPUMode();
                Process.Start("shutdown", "/r /t 1");
            }
            else
            {
                Logger.WriteLine("GPU mode change failed - no reboot");
                settings.VisualiseGPUMode();
            }
        }



        public void SetGPUEco(int eco)
        {

            settings.LockGPUModes();

            Task.Run(async () =>
            {
                try
                {
                    Program.modeControl.WaitForApply();

                    int targetMode = eco == 1 ? HpACPI.GPUModeEco : HpACPI.GPUModeStandard;

                    if (eco == 1)
                    {
                        HardwareControl.KillGPUApps();
                        HardwareControl.DisposeGpuControl();
                        if (AppConfig.IsNVPlatform()) NvidiaGpuControl.StopNVService();
                    }

                    Logger.WriteLine($"Running eco command {eco} (mode {targetMode})");

                    int status = Program.acpi.DeviceSet(HpACPI.GPUEco, targetMode, "GPUEco");
                    await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("refresh_delay", 500)));

                    settings.Invoke(delegate
                    {
                        InitGPUMode();
                        ScreenControl.AutoScreen();
                    });

                    if (eco == 0)
                    {
                        if (AppConfig.IsNVPlatform() || nvRestartPending)
                        {
                            settings.LockGPUModes(Properties.Strings.RestartingNVServices);
                            await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("nv_delay", 5000)));
                            if (AppConfig.IsNVPlatform()) NvidiaGpuControl.RestartNVService();
                            else NvidiaGpuControl.RestartNvContainer();
                            nvRestartPending = false;
                            settings.Invoke(delegate { InitGPUMode(); });
                            await Task.Delay(TimeSpan.FromMilliseconds(1000));
                        }

                        for (int i = 0; i < 3; i++)
                        {
                            HardwareControl.RecreateGpuControl();
                            if (HardwareControl.GpuControl is not null) break;
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                        settings.Invoke(RefreshFansGpuTab);
                        Program.modeControl.SetGPUClocks(false);
                    }

                    if (AppConfig.IsModeReapplyRequired())
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(3000));
                        Program.modeControl.AutoPerformance();
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Error setting GPU Eco: " + ex.Message);
                }

            });


        }

        public static bool IsPlugged() =>
            Program.currentSource == Program.PowerSource.Barrel ||
            (Program.currentSource == Program.PowerSource.USBC && !AppConfig.Is("optimized_usbc"));

        public bool AutoGPUMode(bool optimized = false, int delay = 0)
        {

            bool GpuAuto = AppConfig.Is("gpu_auto");
            bool ForceGPU = AppConfig.IsForceSetGPUMode() && !GpuAuto;

            int GpuMode = AppConfig.Get("gpu_mode");

            if (!GpuAuto && !ForceGPU) return false;

            int currentMode = Program.acpi.GetGpuMode();

            if (currentMode == HpACPI.GPUModeUltimate)
            {
                if (optimized) SetGPUMode(HpACPI.GPUModeStandard, 1);
                return false;
            }
            else
            {

                if (currentMode == HpACPI.GPUModeEco)
                    if ((GpuAuto && IsPlugged()) || (ForceGPU && GpuMode == HpACPI.GPUModeStandard))
                    {
                        if (delay > 0) Thread.Sleep(delay);
                        SetGPUEco(0);
                        return true;
                    }
                if (currentMode == HpACPI.GPUModeStandard)
                    if ((GpuAuto && !IsPlugged()) || (ForceGPU && GpuMode == HpACPI.GPUModeEco))
                    {

                        if (HardwareControl.IsUsedGPU())
                        {
                            DialogResult dialogResult = MessageBox.Show(Properties.Strings.AlertDGPU, Properties.Strings.AlertDGPUTitle, MessageBoxButtons.YesNo);
                            if (dialogResult == DialogResult.No) return false;
                        }

                        if (delay > 0) Thread.Sleep(delay);
                        SetGPUEco(1);
                        return true;
                    }
            }

            return false;

        }


        public void KillGPUApps()
        {
            HardwareControl.KillGPUApps();
        }

        public void CaptureNvBootState()
        {
            nvRestartPending = Program.acpi.IsNVidiaGPU()
                && Program.acpi.GetGpuMode() == HpACPI.GPUModeEco;
        }

        public void StandardModeFix()
        {
            if (!AppConfig.IsStandardModeFix()) return;
            if (Program.acpi.GetGpuMode() == HpACPI.GPUModeUltimate) return;

            Logger.WriteLine("Forcing Standard Mode on shutdown");
            Program.acpi.DeviceSet(HpACPI.GPUEco, HpACPI.GPUModeStandard, "GPUEco Standard Fix");
        }

        public static string? gpuError = null;

        void CheckGpuError()
        {
            string? error = DeviceHelper.GetGpuError();

            if (gpuError != error)
            {
                gpuError = error;
                if (error != null) Logger.WriteLine(error);
                settings.VisualiseGPUMode();
            }
        }

    }
}
