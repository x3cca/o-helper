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
    private const long StartRetryCooldownMilliseconds = 60_000;

    private static Computer? _computer;
    private static readonly object _lock = new();
    private static bool _startRequested;
    private static int _generation;
    private static long _nextStartAttempt;
    private static long _lastCpuTempLog;

    private static void EnsureStarted()
    {
        int generation;

        lock (_lock)
        {
            if (_computer != null || _startRequested || Program.IsExiting) return;
            if (Environment.TickCount64 < _nextStartAttempt) return;

            _startRequested = true;
            generation = ++_generation;
        }

        // Computer.Open() probes every enabled hardware backend and can take a long
        // time on some systems. Run it only when a sensor is first requested, and do
        // not make that first sensor read wait for discovery to finish.
        _ = Task.Run(() => Start(generation));
    }

    private static void Start(int generation)
    {
        Computer? computer = null;
        bool started = false;

        try
        {
            computer = new Computer
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
            computer.Open();

            lock (_lock)
            {
                if (generation == _generation && !Program.IsExiting)
                {
                    Volatile.Write(ref _computer, computer);
                    computer = null;
                    started = true;
                }
            }

            if (started)
                Logger.WriteLine("HardwareMonitor: LibreHardwareMonitor started");
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (generation == _generation && _computer == null)
                {
                    _startRequested = false;
                    _nextStartAttempt = Environment.TickCount64 + StartRetryCooldownMilliseconds;
                }
            }

            Logger.WriteLine($"HardwareMonitor: Failed to start: {ex.Message}");
        }
        finally
        {
            if (computer != null)
            {
                try { computer.Close(); } catch { }
            }
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            ++_generation;

            if (_computer != null)
            {
                try { _computer.Close(); } catch { }
                Volatile.Write(ref _computer, null);
            }

            _startRequested = false;
            _nextStartAttempt = 0;
        }
    }

    public static float? GetCpuTemperature()
    {
        EnsureStarted();

        var hw = Volatile.Read(ref _computer);
        if (hw == null) return null;

        lock (_lock)
        {
            if (!ReferenceEquals(hw, _computer)) return null;

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
        EnsureStarted();

        var hw = Volatile.Read(ref _computer);
        if (hw == null) return null;

        lock (_lock)
        {
            if (!ReferenceEquals(hw, _computer)) return null;

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
