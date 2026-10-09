# System monitor (Windows)

Spec: `docs/specs/03-system-monitor.md` (§3.1–3.17, §4, §6). Module class:
`Rivet.App.Features.SystemMonitor.SystemMonitorModule`. Feature ids: `monitorCPU`,
`monitorGPU`, `monitorMemory`, `monitorNetwork`, `monitorDisk`, `monitorPower`,
`connectedDevices` (and `killProcess` for Force Kill).

Everything runs as a standard user. No kernel driver, no LibreHardwareMonitor,
no `System.Management` (WMI goes through CsWin32's COM projections, see
"Design decisions").

## Status

| Area | State |
|---|---|
| Central sampler (plan, cadence, hold rules, history, publishing) | Done |
| CPU total, per core, core classes | Done |
| Per-process CPU / GPU / memory / energy lists, Force Kill | Done |
| GPU usage (overall, per adapter, per process), memory, temperature | Done (temperature only where the driver reports it) |
| Memory (used, app, compressed, cached, swap, pressure) | Done (pressure is a documented heuristic) |
| Disks (volumes, activity, eject, exclusions) | Done (SMART not implemented) |
| Network (speed, session totals, local IPv4, speed test) | Done (per-app traffic not available) |
| Power (charge, flow, time, health, cycles, battery temperature) | Done; the section hides on PCs without a battery |
| CPU temperature | WMI thermal zones when Windows allows it, otherwise an explained "unavailable" |
| Connected USB devices, accessory batteries | Done (accessory batteries from the Bluetooth battery property only) |
| Panel sections System 10 / Network 20 / Disks 30 / Power 40, detail views, edit mode | Done |
| Alerts (six rules, 12 s sustain, cooldown, toasts) | Done |
| Readouts: tray tooltip line and the mini monitor window | Done (redesign of the menu bar readouts) |
| Settings page | Done |

## Implemented

### Sampler (`Rivet.Core/SystemMonitor`)

* `SamplingPlanner.Compute` builds the plan from what is visible (panel
  sections, detail views, readouts, alerts, mini monitor) exactly as §3.1.2,
  hub overrides last. Nothing is sampled when nothing needs it: the worker
  thread exits when the plan is empty.
* `SamplingCadence`: target interval (1, 2 or 5 s), per-kind strides
  (background disk under 15 s, GPU at 10 s in the background, battery charge
  slow), gcd wake ticks and tick round-up (§3.1.3).
* `MonitorEngine` reads each kind on its own timestamp (CPU, network and disk
  rates use the time of their own read), applies the hold and bridge rules
  (memory 12 s, temperatures `max(12, 2.2 × stride × interval)`), keeps
  120-sample history rings, and suppresses the GPU anti-spike for 0.9 s when a
  surface opens.
* `SystemMonitorService` runs the engine on one `BelowNormal` background thread
  and publishes snapshots to the UI thread with coalescing (never blocks the
  UI thread). Leases (`AcquireSurface`, `AcquireDetail`) start and stop it.
* `MetricFormat` ports every §6.10 formatter (bytes, rates in B/s and bit/s,
  compact widths, watts, temperatures in °C/°F, battery time, uptime, Mbps)
  with `MidpointRounding.AwayFromZero` like the Swift code.

### Windows sensors (`Rivet.Platform.Windows/SystemMonitor`)

| Metric | Source |
|---|---|
| CPU total | `GetSystemTimes` (all processor groups) |
| CPU per logical processor | `NtQuerySystemInformation(SystemProcessorPerformanceInformation)` per processor group |
| Core classes | `GetLogicalProcessorInformationEx(RelationProcessorCore)` `EfficiencyClass`: highest = Performance, lowest = Efficiency, all equal = one "CPU" group |
| Processes | One `NtQuerySystemInformation(SystemProcessInformation)` snapshot (no handle per process); names from the executable's file description, cached by path |
| GPU | PDH `\GPU Engine(*)\Utilization Percentage` (Task Manager semantics: per adapter, sum per engine, busiest engine), `\GPU Adapter Memory(*)`; adapter names from DXGI; temperature from `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)` in tenths of °C |
| Memory | `GlobalMemoryStatusEx`, `GetPerformanceInfo`, page file pages from the kernel, `CreateMemoryResourceNotification(LowMemoryResourceNotification)`, "App Memory" = Σ private working sets and "Compressed" = the Memory Compression process working set (every 4 s at most) |
| Network | `GetIfTable2` 64-bit octets with the counting rule below; addresses from `GetAdaptersAddresses(AF_INET)` |
| Disks | `GetLogicalDrives`, `GetDriveType`, `GetDiskFreeSpaceEx`, `GetVolumeInformation` (with `SEM_FAILCRITICALERRORS` so empty card readers never prompt), bus type from `IOCTL_STORAGE_QUERY_PROPERTY`, disk number from `IOCTL_STORAGE_GET_DEVICE_NUMBER`, activity from `IOCTL_DISK_PERFORMANCE` per physical disk |
| Eject | `CM_Request_Device_Eject` on the disk's parent device node, three tries; "in use" is reported, not forced |
| Battery | `GetSystemPowerStatus` plus the battery class driver (`IOCTL_BATTERY_QUERY_INFORMATION/STATUS`): capacity, rate, cycle count, temperature (rarely implemented by firmware) |
| CPU temperature | WMI `root\WMI` `MSAcpi_ThermalZoneTemperature` |
| USB devices | SetupAPI over `GUID_DEVINTERFACE_USB_DEVICE`, minus root hubs, hubs (class 9), billboard (class 17) and non-removable devices |
| Accessory batteries | WinRT `PnpObject` with the Bluetooth battery level property (`{104EA319-…} 2`) |
| Full-screen detection (mini monitor) | `SHQueryUserNotificationState` |

### UI (`Rivet.App/Features/SystemMonitor`)

* Panel sections `system` (Order 10), `network` (20), `disk` (30) and
  `power` (40, visible only with a battery or accessory batteries), each with
  blocks that can be reordered and hidden in edit mode, sparklines over the
  120-sample history, a core matrix, process lists with Force Kill, and
  detail views per metric.
* Speed test against `speed.cloudflare.com` (latency, download, upload; a
  non-2xx answer fails the phase and is never counted).
* Alerts: CPU usage, CPU temperature, battery temperature, memory pressure,
  low disk space (only volumes of at least 10 GB) and low battery; each
  must hold for 12 s (`SustainedGate`), one shared cooldown per kind
  (2–60 min); toasts through `INotificationService`.
* Readouts: the menu bar text has no Windows equivalent, so pinned readouts go
  to one line of the tray icon's tooltip (`ITrayPresence`, source
  `systemMonitor.readouts`, at most 100 characters) and to an optional
  always-on-top **mini monitor** window: draggable, remembers its position
  per machine, optional click-through, hides in full-screen apps.
* Actions `systemMonitor.toggleMiniMonitor` and `systemMonitor.openTaskManager`.
* Settings page `monitor`: readings, panel items, graphs, readouts (tokens,
  order, preview, bar colours), alerts and excluded drives.

## Not implemented (and why)

| Item | Reason |
|---|---|
| Per-app network traffic (Network → Apps) | Needs ETW kernel network events, which require administrator rights. The block is not offered. |
| SMART disk health | Needs `IOCTL_SMART_*`/`MSStorageDriver_FailurePredict*`, which require administrator rights. Not offered. |
| Power adapter (watts, voltage) and whole-system watts on AC | No Windows API. The "System" row shows the battery rate on battery and is hidden on AC. |
| CPU temperature on most PCs | No supported driver-free API. Thermal zones are often admin-only or static; the panel explains why instead of showing a number. The app never installs a driver. |
| Fan RPM and fan control | Deferred by the spec (needs EC/SuperIO access). |
| Real app icons in process lists | Rows use a generic icon. Needs an icon extraction service (see requests). |
| Accessory batteries from vendor protocols (Logitech, Razer, …) | Only what Windows exposes through the Bluetooth battery property. |

## Deviations from macOS

* **Readouts** are a tooltip line plus the mini monitor instead of menu bar
  text. Token, order, style and colour settings keep the macOS keys.
* **Section visibility** uses the shell's `PanelHiddenItems` instead of the
  macOS `monitorShow*` keys.
* **"Open Activity Monitor"** became "Open Task Manager".
* **Process grouping**: Windows has no "responsible process", so helpers are
  grouped by executable name under the topmost process of that name.
* **Force Kill safety** (`ProcessSafety`), the Windows counterpart of the
  macOS list (kernel_task, launchd, WindowServer, loginwindow): PIDs 0–4,
  this app, `System`, `Registry`, `Memory Compression`, `Secure System`,
  `smss`, `csrss`, `wininit`, `winlogon`, `services`, `lsass`, `LsaIso` and
  `dwm` are never offered. Rows without a verified start time cannot be
  killed, and the kill re-checks the process creation time so a reused PID is
  never killed. There is no elevation prompt (macOS asks for an administrator
  once): killing a process of another user or an elevated app fails with an
  error HUD.

## Design decisions

* **CPU %**: `GetSystemTimes`/kernel times give "% Processor Time". Task
  Manager shows the frequency-scaled "% Processor Utility", which reads higher
  at turbo clocks. We keep the time-based figure (it matches macOS semantics
  and the per-core matrix).
* **Memory pressure heuristic** (`MemoryPressureHeuristic`):
  critical when Windows signals low memory, or available < 5 %, or commit ≥ 95 %
  of the limit; warning when available < 15 % or commit ≥ 85 %; otherwise
  normal. The "Critical memory pressure" alert uses the critical level.
* **Network counting rule**: an interface counts when it is up, a hardware
  interface, not a filter (QoS/WFP lightweight filters would double count),
  not a tunnel, of type Ethernet, Wi-Fi or mobile broadband, and its
  description does not mark it virtual (Hyper-V, vEthernet, VMware,
  VirtualBox, TAP/TUN, WireGuard/Wintun, loopback, Npcap, WAN Miniport,
  OpenVPN, ZeroTier, Tailscale, NordLynx, Docker). Local IPv4 addresses use
  the same rule. Counter resets give 0 for that sample and keep the totals.
* **WMI** goes through `WmiClient`, a small client on CsWin32's COM types
  (`IWbemLocator`, `IWbemServices`, `IEnumWbemClassObject`), because the
  shared projects do not reference `System.Management`. It sets the proxy
  blanket on every proxy, serializes calls and is only used from background
  threads. The Displays module reuses it for panel brightness.
* **GPU temperature** is read once per GPU stride and dropped for the session
  after the driver says "not supported".

## Risks

* `D3DKMT_ADAPTER_PERFDATA` layout and units come from the WDK headers and
  were reviewed but never run on hardware here; verify on NVIDIA, AMD and
  Intel drivers.
* PDH GPU instance lists can hold hundreds of entries on busy PCs; the GPU is
  sampled at most every stride and only while something shows it.
* `NtQuerySystemInformation` structure offsets follow the documented x64
  layout (checked against phnt); ARM64 is untested.
* Battery temperature is almost never reported by firmware; the row then
  shows "unavailable".
* Toasts need the app identity the shell registers (AUMID shortcut or
  package); without it Windows drops them silently.

## Windows manual test checklist

1. Install CPU, GPU, Memory, Network, Disk, Power and Connected devices in the
   Features hub. Open the panel: System, Network, Disks (and Power on a
   laptop) appear in that order.
2. System: CPU % follows Task Manager's "Processes" CPU column within a few
   points at idle and under load (a `while` loop in PowerShell); the core
   matrix shows one square per logical processor; on a hybrid CPU the P and E
   groups are labelled.
3. Open the CPU list: the busy PowerShell is on top. Install "Force Kill",
   kill it from the list, confirm the dialog: it disappears. Check that
   Desktop Window Manager (`dwm.exe`), `csrss.exe` and Rivet itself offer no
   Force Kill, and that killing an elevated app shows the error HUD.
4. GPU: play a video or a game; usage and per-process GPU match Task
   Manager's GPU column; on a discrete GPU the temperature appears.
5. Memory: the pressure pill is green at idle. Open many browser tabs or run
   a memory hog: it turns yellow/red at the documented thresholds.
6. Network: download a large file; the speed matches Task Manager's
   Ethernet/Wi-Fi graph. Turn on a VPN or Hyper-V switch and check the
   traffic is not counted twice. Run the speed test (latency, download,
   upload) and cancel one mid-way.
7. Disks: plug a USB stick; it appears with its file system; eject it from
   the panel; with a file open on it the eject reports "in use". Add it to
   the excluded drives and check it disappears. Insert an empty SD reader:
   no "No disk" dialog appears.
8. Power (laptop): charge, charging state, time remaining, health and cycles
   match `powercfg /batteryreport`; unplug and replug the charger: the state
   changes within a few seconds. On a desktop the Power section is absent.
9. Temperatures: as a standard user the CPU temperature row explains it is
   unavailable; run as administrator on a PC with ACPI thermal zones and a
   value appears.
10. Connected devices: plug a USB mouse; it is listed with its name; the root
    hub and built-in camera are not. Pair Bluetooth earbuds that report a
    battery in Windows Settings; the accessory battery appears in Power.
11. Alerts: set the CPU threshold to 50 %, run a load for 15 s: one toast; no
    second toast before the cooldown ends.
12. Readouts: pin CPU and Memory; hover the tray icon: one tooltip line shows
    them. Turn on the mini monitor, drag it, restart the app: it returns to
    the same place. Turn on click-through: clicks reach the window below.
    Start a full-screen game or presentation: it hides.
13. Close the panel and the mini monitor and unpin readouts: CPU use of
    Rivet drops to ~0 % (the sampler thread exits).

## Requests for shared code

1. **Tray menu submenus**: `TrayMenuItem` has no children, so Keep Awake's
   "Activate for…" presets are flat items (Orders −39…−33). A `Children`
   list would restore the macOS submenu.
2. **Tray icon re-render**: `TrayController` should re-render the icon only
   when the tint or badge changes; the readout tooltip line changes every
   sample and must not cause an icon redraw.
3. **App icons**: a shared service that returns an icon for an executable path
   (`SHGetFileInfo`/`IShellItemImageFactory`, cached) would let the process
   lists show real app icons.
4. **Panel visibility refresh**: `PanelViewModel` re-evaluates
   `PanelSectionDescriptor.IsVisible` only when the registry or feature
   runtime changes. An explicit `PanelRegistry.Invalidate()` (or observing
   visibility keys) would replace the re-`AddSection` workaround used by the
   Displays section.
