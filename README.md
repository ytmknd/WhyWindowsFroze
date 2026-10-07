# WhyWindowsFroze

A GUI tool that diagnoses why a Windows PC froze, using the **existing event logs** and the current system state.

[日本語版 README](README.ja.md)

- Self-contained single executable with the .NET 10 runtime bundled (about 47 MB, Windows 10/11 x64). No installation, no resident process, no system changes.
- Runs as administrator (UAC elevation on start). Needed to read the Security log, Prefetch, file system filters and disk reliability data.
- The UI, findings and report are shown in Japanese on Japanese Windows and in English elsewhere (based on the Windows display language). Event message text comes from Windows itself and is in the OS language.

## Build

```
dotnet publish -c Release -o publish
```
Output: `publish\WhyWindowsFroze.exe` (distribute this single file). For ARM64, add `-r win-arm64`.

## What it detects

| Type | How it is detected |
|---|---|
| Forced power-off (power button) | Kernel-Power 41 without a bugcheck, where the OS recorded a power button press |
| Unexpected power loss / reset | Kernel-Power 41 without a bugcheck or power button record (power button, power outage, PSU/AC adapter, hardware reset, etc. — not determined) |
| Blue screen | Kernel-Power 41 / BugCheck 1001 with a bugcheck code |
| Display / system hang (recovered) | Events indicating a whole-display/system stop, grouped within 5 minutes: TDR 4101, live kernel events, DWM exit/hang, commit exhaustion (2004), kernel pool exhaustion (newest 30) |
| App hang / I/O delay | Clusters containing only app/Explorer hangs (1002), disk I/O retry/reset, delayed write failure or slow Winlogon notifications. Listed separately because the whole PC did not necessarily stop (newest 30) |
| Reported time | A time the user specifies with **Specify time** or `/at:` (also covers freezes that left nothing in the logs) |

Whole-Windows stops and app-only hangs are shown in separate groups in the list and the report.

## How causes are diagnosed

For each incident, the following evidence is scored per cause category. Each piece of evidence is labelled **[before]** (before the reference time), **[symptom]** (the triggering event itself), **[after]** (may be a consequence) or **[current state]** (health check now). **Confidence** (High/Medium/Low) is computed only from [before] evidence and the circumstances (bugcheck, right after resume/boot/logon, etc.); candidates are ordered by confidence, then score. The reference time is the last record before a power loss, the first symptom event of a recovered hang, or the specified time.

- **Bugcheck code**: name, meaning and category for about 50 codes (e.g. 0x9F → power, 0x116/0x117 → GPU, 0x7A/0xF4 → storage, 0x124 → hardware, 0x133 → driver)
- **Known events just before the reference time** (default window: 30 min). Events within 5 minutes before it count double; events after it count 1 point; the symptom events count once per type; each event type counts at most 3 times so that frequent noise does not dominate
- **Pattern**: right after resume from sleep, right after OS start, right after logon
- **Updates/installs in the previous 3 days**: Windows Update, drivers, services, MSI
- **Apps started just before** (Prefetch)
- **Current health problems** (see below)

| Category | Main evidence |
|---|---|
| GPU / Display | TDR 4101, LiveKernelEvent 141/117/193, DWM hang/crash, nvlddmkm/amdkmdag/igfx errors, DxgKrnl |
| Storage | disk 7/11/51/52/153/154/157, storahci/stornvme 129, NTFS, delayed write failure, Storport, low disk space, SSD wear / uncorrectable errors |
| Memory / Resource exhaustion | 2004 commit exhaustion, Srv 2019/2020 pool exhaustion, Resource-Exhaustion, small RAM, no page file |
| Hardware / Thermal | WHEA, Memory Diagnostic errors, CPU throttling, thermal warnings, old BIOS |
| Power / Sleep | SleepInProgress, bugcheck 9F/A0/14F/15F/1A8, LiveKernelEvent 1a8 etc., stop right after resume |
| Application | App hangs/crashes (Office/Teams weighted higher), Office alert dialogs, DCOM timeouts |
| Shell / Logon | Explorer/Start/Search/IME hangs, LiveKernelEvent 1a1, Winlogon 6005/6006/4005, profile problems, stop right after logon |
| Authentication | Entra ID / WAM (AAD) errors |
| Network | NIC reset (NDIS), NIC/Wi-Fi driver errors, SMB connectivity problems, TCP port exhaustion |
| Security software | Defender detections, Code Integrity blocks, filter load failures, two active antivirus products, third-party file system filters |
| Updates / Installs | Windows Update, driver installs, new services, MSI, pending restart |
| Driver / OS | Other bugchecks, service crashes/timeouts, critical process termination, problem devices, stop right after boot |

Causes common to several freezes, patterns (after resume / boot / logon, concentration in a time of day) and user bias are summarized under **Recommendations**.

## Health check (current state)

System drive free space, disk health and SSD wear/temperature/uncorrectable errors, physical memory and commit charge, page file, uptime, problem devices, old GPU driver and BIOS, running on Basic Display Adapter, two active antivirus products, third-party file system filters, pending restart for updates, and memory dump settings.

## Usage

1. Run `WhyWindowsFroze.exe` on the affected PC and click **Analyze**.
2. Detected freezes are listed at the top, newest first. Select one to see details in the tabs below:
   - **Causes & findings** / **Timeline** (color-coded by severity, filter, full message) / **Health check** / **Known events** / **App run history** / **Dumps / WER** / **User settings** / **System info** / **Recommendations** / **Coverage** (per log: read/failed/not present, events read, whether a limit was hit)
3. Use **Open HTML report** / **Open output folder** to view the saved results (send the ZIP to whoever investigates).

List rows can be copied with Ctrl+C. Command-line options for shortcuts: `/auto` (start analysis immediately), `/days:N` (period, default 30), `/window:N` (window in minutes, default 30), `/at:"yyyy-MM-dd HH:mm"` (time the freeze happened), `/lang:ja` or `/lang:en` (override the display language).

## Output

`WhyWindowsFroze_<PC name>_<timestamp>\` (and a `.zip` of the same name): `report.html`, `summary.txt`, `incidentNN_events.csv`, `evtx\` (raw related logs), `wer\` (WER reports)

## Limitations

- A forced power-off leaves no dump, and what happens during a freeze often is not logged. When there are no clues, the tool says "No clues".
- Cause categories are candidates inferred from combinations of events. Confirming them requires dump analysis or isolation (driver updates, clean boot, etc.).
- The health check shows the state at analysis time, not at the time of the freeze.
- Only the newest 30 hangs of each type are analyzed in detail; the total number of candidates is shown under Coverage. App hangs are also logged when a user closes a hung app.
- Searches are limited (10,000 events per query for the period, 5,000 per log per time window). Time windows are read newest first so the records right before the stop are kept. Logs that could not be read and searches that hit a limit are listed under Coverage, so "nothing detected" can be distinguished from "could not check".
- Freezes that recovered without a restart and left nothing in the logs cannot be detected automatically. Use **Specify time**.
- Per-user registry settings are available only for users who are logged on.
- The output may contain user names, app command lines and file URLs. Handle it with care.
