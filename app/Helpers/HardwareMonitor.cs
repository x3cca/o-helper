using LibreHardwareMonitor.Hardware;
using OHelper.Helpers;
using System.Linq;

namespace OHelper.Helpers;

internal class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware)
            sub.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

public static class HardwareMonitor
{
    private static Computer? _computer;
    private static readonly object _lock = new();
    private static bool _initialized;
    private static long _lastCpuTempLog;

    public static void Start()
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,
                    IsStorageEnabled = false,
                    IsMotherboardEnabled = false,
                    IsNetworkEnabled = false,
                    IsBatteryEnabled = false,
                    IsControllerEnabled = false
                };
                _computer.Open();
                Logger.WriteLine("HardwareMonitor: LibreHardwareMonitor started");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"HardwareMonitor: Failed to start: {ex.Message}");
                _computer = null;
            }
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            if (_computer != null)
            {
                try { _computer.Close(); } catch { }
                _computer = null;
            }
            _initialized = false;
        }
    }

    public static float? GetCpuTemperature()
    {
        var hw = _computer;
        if (hw == null) return null;

        lock (_lock)
        {
            try
            {
                foreach (IHardware hardware in hw.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Cpu) continue;
                    hardware.Accept(new UpdateVisitor());

                    var allCoreSensors = hardware.Sensors
                        .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0)
                        .ToList();

                    if (allCoreSensors.Count == 0)
                        continue;

                    // Use the hottest pair: averaging every core can conceal a real hotspot,
                    // while one briefly boosted core is too noisy to drive the fans by itself.
                    var coreSensors = allCoreSensors
                        .Where(s => s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.Value!.Value)
                        .Take(2)
                        .ToList();

                    float temp;
                    string sensorName;

                    if (coreSensors.Count > 0)
                    {
                        temp = coreSensors.Average(s => s.Value!.Value);
                        sensorName = $"Average of {coreSensors.Count} hottest cores";
                    }
                    else
                    {
                        // Fallback: prioritize aggregate sensors
                        ISensor? sensor = GetSensorExact(hardware, SensorType.Temperature, "CPU Package")
                            ?? GetSensorExact(hardware, SensorType.Temperature, "CPU DTS")
                            ?? GetSensorExact(hardware, SensorType.Temperature, "Core (Tctl/Tdie)")
                            ?? GetSensor(hardware, SensorType.Temperature, "Tctl/Tdie")
                            ?? GetSensor(hardware, SensorType.Temperature, "Core Max")
                            ?? GetSensor(hardware, SensorType.Temperature, "Core Average")
                            ?? GetSensor(hardware, SensorType.Temperature, "Tctl")
                            ?? GetSensor(hardware, SensorType.Temperature, "Tdie")
                            ?? GetSensorExact(hardware, SensorType.Temperature, "CPU (Tctl/Tdie)")
                            ?? GetSensor(hardware, SensorType.Temperature, "CCDs Max")
                            ?? GetSensor(hardware, SensorType.Temperature, "CCDs Average")
                            ?? GetSensor(hardware, SensorType.Temperature, "CPU")
                            ?? GetSensor(hardware, SensorType.Temperature, "SoC")
                            ?? GetSensor(hardware, SensorType.Temperature, "Socket")
                            ?? allCoreSensors.FirstOrDefault();

                        if (sensor?.Value == null || sensor.Value.Value <= 0)
                            continue;

                        temp = (float)sensor.Value.Value;
                        sensorName = sensor.Name;
                    }

                    var now = DateTimeOffset.Now.ToUnixTimeSeconds();
                    if (now - _lastCpuTempLog >= 30)
                    {
                        _lastCpuTempLog = now;
                        Logger.WriteLine($"HardwareMonitor: CPU temp = {temp:F1}°C (sensor: {sensorName})");
                    }
                    return temp;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"HardwareMonitor: CPU temp read failed: {ex.Message}");
            }
        }

        var tsNow = DateTimeOffset.Now.ToUnixTimeSeconds();
        if (tsNow - _lastCpuTempLog >= 30)
        {
            _lastCpuTempLog = tsNow;
            Logger.WriteLine("HardwareMonitor: CPU temp returned null, falling back to WMI");
        }
        return null;
    }

    public static float? GetGpuTemperature()
    {
        var hw = _computer;
        if (hw == null) return null;

        lock (_lock)
        {
            try
            {
                foreach (IHardware hardware in hw.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel) continue;
                    hardware.Accept(new UpdateVisitor());

                    ISensor? sensor = GetSensor(hardware, SensorType.Temperature, "GPU Core")
                        ?? GetSensor(hardware, SensorType.Temperature, "Core")
                        ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);

                    if (sensor?.Value != null && sensor.Value.Value > 0)
                        return (float)sensor.Value.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"HardwareMonitor: GPU temp read failed: {ex.Message}");
            }
        }

        return null;
    }

    private static ISensor? GetSensor(IHardware hardware, SensorType type, string namePattern)
    {
        return hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == type &&
            s.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase));
    }

    private static ISensor? GetSensorExact(IHardware hardware, SensorType type, string exactName)
    {
        return hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == type &&
            s.Name.Equals(exactName, StringComparison.OrdinalIgnoreCase));
    }
}
