using System;
using System.Collections.Generic;
using System.Linq;
using Gamepulse.Models;
using LibreHardwareMonitor.Hardware;

namespace Gamepulse.Collectors
{
    public class GpuMetricsCollector : IDisposable
    {
        private readonly Computer _computer;

        public GpuMetricsCollector()
        {
            _computer = new Computer
            {
                IsGpuEnabled = true
            };

            _computer.Open();
        }

        public GpuMetrics Collect()
        {
            List<GpuDeviceMetrics> gpuDevices = new();

            try
            {
                foreach (IHardware hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                    {
                        continue;
                    }

                    hardware.Update();

                    double usagePercent = GetGpuUsagePercent(hardware);
                    double? temperatureC = GetGpuTemperatureC(hardware);
                    double? vramUsedMb = GetVramUsedMb(hardware);

                    gpuDevices.Add(new GpuDeviceMetrics(
                        hardware.Name,
                        usagePercent,
                        vramUsedMb,
                        temperatureC
                    ));
                }
            }
            catch
            {
                return new GpuMetrics(new List<GpuDeviceMetrics>());
            }

            List<GpuDeviceMetrics> orderedGpuDevices = gpuDevices
                .OrderBy(GetGpuSortOrder)
                .ToList();

            return new GpuMetrics(orderedGpuDevices);
        }

        private static int GetGpuSortOrder(GpuDeviceMetrics gpu)
        {
            if (gpu.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (gpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        private static double GetGpuUsagePercent(IHardware hardware)
        {
            ISensor? usageSensor = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Load)
                .FirstOrDefault(sensor =>
                    sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                    sensor.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase));

            if (usageSensor?.Value == null)
            {
                return 0;
            }

            double value = usageSensor.Value.Value;

            if (value < 0)
            {
                return 0;
            }

            if (value > 100)
            {
                return 100;
            }

            return value;
        }

        private static double? GetGpuTemperatureC(IHardware hardware)
        {
            ISensor? temperatureSensor = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature)
                .FirstOrDefault(sensor =>
                    sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                    sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase));

            return temperatureSensor?.Value;
        }

        private static double? GetVramUsedMb(IHardware hardware)
        {
            ISensor? memoryUsedSensor = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.SmallData)
                .FirstOrDefault(sensor =>
                    sensor.Name.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase) ||
                    sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));

            if (memoryUsedSensor?.Value == null)
            {
                return null;
            }

            return memoryUsedSensor.Value.Value;
        }

        public void Dispose()
        {
            _computer.Close();
        }
    }
}