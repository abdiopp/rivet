// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Rivet.Core.Modules;
using Rivet.Core.SystemMonitor;

namespace Rivet.Platform.Windows.SystemMonitor;

/// <summary>The system monitor's sensors on Windows (no administrator rights, no drivers).</summary>
public sealed class SystemMonitorRegistrar : IPlatformRegistrar
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IMonitorClock, WindowsMonitorClock>();
        services.AddSingleton<ICpuSensor, WindowsCpuSensor>();
        services.AddSingleton<IMemorySensor, WindowsMemorySensor>();
        services.AddSingleton<IGpuSensor, WindowsGpuSensor>();
        services.AddSingleton<INetworkSensor, WindowsNetworkSensor>();
        services.AddSingleton<WindowsDiskSensor>();
        services.AddSingleton<IDiskSensor>(sp => sp.GetRequiredService<WindowsDiskSensor>());
        services.AddSingleton<IDiskActions>(sp => sp.GetRequiredService<WindowsDiskSensor>());
        services.AddSingleton<IPowerSensor, WindowsPowerSensor>();
        services.AddSingleton<ITemperatureSensor, WindowsTemperatureSensor>();
        services.AddSingleton<IUsbSensor, WindowsUsbSensor>();
        services.AddSingleton<IPeripheralBatterySensor, WindowsPeripheralBatterySensor>();
        services.AddSingleton<IProcessSampler, WindowsProcessSampler>();
        services.AddSingleton<IProcessControl, WindowsProcessControl>();
        services.AddSingleton<IFullScreenDetector, WindowsFullScreenDetector>();
    }
}
