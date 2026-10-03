# OmenCore v4.4.1 Deep-Dive Audit and AI-Agent Implementation Brief

**Prepared for:** progression of OmenCore from v4.4.0/main toward v4.4.1  
**Audit date:** 2026-10-03  
**Primary repository:** https://github.com/theantipopau/omencore  
**Comparators:** https://github.com/MasonDye/OmenXHub and https://github.com/P4R1H/Ohman  
**Additional comparator:** https://github.com/OmenMon/OmenMon  

> This document is an implementation brief for another coding agent. It combines the current public repository, the supplied rolling v4.4.1 changelog and roadmap, and public research from adjacent projects. The supplied changelog may describe work not yet synced to GitHub. Treat it as the intended state, verify every item against the working tree, and never overwrite newer local work.

---

## 1. Executive assessment

OmenCore is already broader and more production-oriented than the two direct comparators. It has Windows and Linux paths, a large model database, multiple hardware backends, guided diagnostics, safety gating, configuration persistence, updater/install packaging, tuning, RGB integrations, game profiles, tray/OSD tooling, and substantial automated tests. Its main engineering risk is not lack of features. It is the number of overlapping control paths that can write the same hardware state.

The highest-value v4.4.1 work is therefore **control-plane consolidation and correctness**, not feature accumulation:

1. Make one coordinator the authority for each mutable hardware domain.
2. Make telemetry provenance explicit and stable.
3. Make capability decisions evidence-based and runtime-confirmed.
4. Separate requested, applied, verified, degraded, and unsupported states.
5. Ensure every temporary/test operation has deterministic rollback.
6. Add trace/replay tests around real diagnostics exports.
7. Adopt useful ideas from Ohman and OmenXHub only after clean-room reimplementation and board-specific validation.

### Recommended release decision

Do **not** add OmenXHub's aggressive 254 W presets, blanket Dynamic Boost unlocks, generic EC writes, or model-wide claims to v4.4.1. They conflict with OmenCore's evidence gate and could create thermal, stability, warranty, or hardware-state risks.

Do adopt or adapt:

- Ohman's explicit WMI command documentation and measured keepalive semantics.
- Ohman's asymmetric temperature smoothing and small downward deadband.
- Ohman's idle-aware dGPU polling policy.
- Ohman's measured ceiling promotion and cautious generic-profile construction.
- OmenXHub's max(CPU, GPU) fan-demand concept, but only as a selectable policy with per-fan routing retained.
- OmenXHub's dual-path CPU-power experiment only as a diagnostics-only probe until validated by board family.
- Both projects' stronger user-facing visibility into active hardware state.

---

## 2. What OmenCore currently is

### 2.1 Architectural shape

The visible repository indicates a layered .NET solution with separate application, hardware-worker, Linux, CLI, tests, installer, packaging, diagnostics, and website concerns. Important classes and concepts visible in current sources and release documentation include:

- `HpWmiBios` / `IHpWmiBios`: HP BIOS mailbox transport and command wrappers.
- `WmiFanController`: WMI fan state, Max mode, direct fan levels, verification and keepalive.
- `FanControllerFactory`: backend selection and capability-informed construction.
- `FanService`: higher-level profile, thermal protection and fan-control orchestration.
- `HardwareWatchdogService`: frozen-telemetry detection and failsafe fan behaviour.
- `FanVerificationService`: guided 30/60/100 percent validation and evidence classification.
- `LibreHardwareMonitorImpl`: CPU/GPU temperature, load, clocks, power and sensor enumeration.
- `SystemControlViewModel`: performance mode and GPU-power feature presentation.
- Model/capability database: ProductId and family-specific feature gates, quirks and verification status.
- Lighting backends: WMI colour-table, direct EC, HID/MCU and external RGB integrations.
- Windows hardware worker: isolates or centralises background telemetry activity.
- Linux daemon/CLI/Avalonia path: `hp-wmi`, `ec_sys`, `sysfs` and hwmon-based control/telemetry.

### 2.2 Control flow model

The likely effective flow is:

```text
UI / tray / hotkey / profile / game automation
                 |
                 v
        service-level request
                 |
       capability + safety gate
                 |
      backend selection / routing
        |          |          |
      WMI BIOS     EC      vendor API
        |          |          |
        +----- readback / verification -----+
                                             |
                                      runtime state/UI
```

This is a sound shape, but the v4.4.1 defects show that state can be changed by multiple routes:

- preset application;
- manual fan control;
- Max fan mode;
- guided verification;
- thermal emergency;
- watchdog failsafe;
- startup restoration;
- game-profile automation;
- exit/disposal rollback;
- external software or firmware reassertion.

The repeated failure pattern is **a writer bypassing the state tracked by another writer**. The stuck-Max bug is the clearest example: guided verification directly asserted the firmware Max flag, while the normal controller did not know it owned a Max state that later needed release.

### 2.3 Core design principle for 4.4.1

All hardware mutations should become leases/transactions coordinated by one domain owner:

```text
FanControlCoordinator
  - Normal profile lease
  - Manual lease
  - Max lease
  - Diagnostic lease
  - Thermal-emergency lease
  - Watchdog-failsafe lease

Priority:
  Watchdog/Thermal > Diagnostic > Manual/Max > Profile > BIOS Auto
```

A lower-priority path must not reapply during a higher-priority lease. Every lease must define acquire, refresh, verify and release behaviour.

---

## 3. Hardware access and firmware protocol

### 3.1 HP WMI BIOS mailbox

The strongest public protocol documentation comes from Ohman's measured research. HP exposes `root\\wmi`, class `hpqBIntM`, using methods such as `hpqBIOSInt0`, `hpqBIOSInt4`, `hpqBIOSInt128`, `hpqBIOSInt1024` and `hpqBIOSInt4096`. The input structure uses `SECU`, command family `0x20008`, a command type/opcode, payload size and payload bytes. Successful output uses `PASS` with `rwReturnCode == 0`.

Important command types for OmenCore:

| Opcode | Function | Key caution |
|---|---|---|
| `0x10` | Read fan count and refresh/arm user-defined fan state | It is not an innocent metadata query on all boards. Pair it intentionally with control ownership. |
| `0x1A` | Set performance/thermal mode | V0, V1 and Victus variants use different bytes. Never flatten to one mapping. |
| `0x21` / `0x22` | Read/write GPU power tuple | Capability must be proven by read support and board policy, not WMI presence alone. |
| `0x23` | Chassis/IR sensor | Do not label as CPU die temperature. |
| `0x26` / `0x27` | Read/write Max fan flag | Firmware can acknowledge yet ignore it. Verify physically meaningful readback. |
| `0x28` | System design data | Useful evidence, but individual bits can be absent or misleading on older firmware. |
| `0x29` | Power-limit/concurrent budget write | Bytes are platform-specific; `0xFF` means unchanged in the researched implementation. |
| `0x2B` | Keyboard topology/type | Runtime topology should beat static family guesses. |
| `0x2C` | Fan type layout | Useful for CPU/GPU fan routing. |
| `0x2D` | Fan levels/readback | Often RPM divided by 100, but validate per platform/version. |
| `0x2E` | Set fan levels | Level 0 can mean fan off, not Auto. |
| `0x2F` | Firmware fan table | Third byte is noise dB in published research, not temperature. |
| `0x52` | Graphics mode/MUX | Some changes are accepted-pending-restart rather than immediately applied. |

### 3.2 Performance mode mappings

Preserve version-aware policy mappings:

```csharp
internal static byte MapPerformanceMode(
    ThermalPolicyVersion version,
    DeviceFamily family,
    PerformanceMode requested)
{
    if (family == DeviceFamily.VictusS)
    {
        return requested switch
        {
            PerformanceMode.Default => 0x00,
            PerformanceMode.Performance => 0x01,
            _ => throw new NotSupportedException()
        };
    }

    if (version == ThermalPolicyVersion.V1)
    {
        return requested switch
        {
            PerformanceMode.Default => 0x30,
            PerformanceMode.Performance => 0x31,
            PerformanceMode.Cool => 0x50,
            PerformanceMode.Eco => 0x30, // Eco includes software policy elsewhere
            _ => throw new ArgumentOutOfRangeException(nameof(requested))
        };
    }

    return requested switch
    {
        PerformanceMode.Default => 0x00,
        PerformanceMode.Performance => 0x01,
        PerformanceMode.Cool => 0x02,
        _ => throw new NotSupportedException()
    };
}
```

**Agent instruction:** compare this shape with the actual mapping already in OmenCore. Do not paste it blindly. Add parameterised tests for every policy/family branch and existing exception board.

### 3.3 Return-code handling

Do not interpret all non-zero return codes as “unsupported”. Preserve transport status, firmware return code, response signature and output length independently.

```csharp
public sealed record BiosCallResult(
    bool TransportSucceeded,
    int ReturnCode,
    string Signature,
    byte[] Data,
    TimeSpan Duration)
{
    public bool IsSuccess =>
        TransportSucceeded && ReturnCode == 0 && Signature == "PASS";
}
```

Recommended fault classes:

```csharp
public enum HardwareFaultKind
{
    None,
    PermissionDenied,
    TransportFailure,
    InvalidParameters,
    UnknownCommand,
    AcceptedUnverified,
    ReadbackMismatch,
    Timeout,
    ConflictingController,
    UnsafeSensorState
}
```

This lets diagnostics distinguish “command unsupported”, “request shape wrong”, “accepted but firmware ignored it”, and “applied but readback source is unreliable”.

---

## 4. Temperature acquisition audit

### 4.1 Current approach

OmenCore uses LibreHardwareMonitor and platform-specific readings, with WMI/EC/worker paths contributing additional telemetry. The public README identifies CPU/GPU temperature, load, clocks, power and fan data from WMI BIOS, NVAPI and EC. The v4.4.0 history also records an ACPI thermal zone repeatedly becoming CPU-temperature authority and causing unstable readings. The supplied v4.4.1 work fixes the Linux equivalent by ranking `k10temp`, `coretemp` and `zenpower` above `acpitz` and allowing a configured hwmon driver name.

### 4.2 Main risks

1. **Sensor identity drift:** enumeration order changes across boots and resumes.
2. **Authority flapping:** two readable sensors alternately “win”, producing artificial jumps.
3. **Stale-but-valid values:** a sensor can remain in range while frozen.
4. **dGPU wake cost:** some polling paths wake a sleeping discrete GPU.
5. **mixed semantics:** CPU package, CPU die, ACPI zone and chassis/IR are not interchangeable.
6. **UI/control coupling:** the best sensor for display is not always the best one for a fan controller.

### 4.3 Recommended sensor authority model

Create stable identities and separate selection from sampling:

```csharp
public sealed record SensorCandidate(
    string StableId,
    string Provider,
    string Device,
    string Sensor,
    SensorKind Kind,
    double? ValueC,
    DateTimeOffset SampledAt,
    bool WakesDiscreteGpu,
    int AuthorityRank);

public sealed record ThermalReading(
    double ValueC,
    string StableId,
    string Provider,
    DateTimeOffset SampledAt,
    ReadingQuality Quality);
```

Suggested CPU authority order:

```text
1. CPU package/die from a stable low-level provider
2. k10temp / coretemp / zenpower on Linux
3. LibreHardwareMonitor CPU package
4. validated vendor telemetry
5. ACPI thermal zone only as a last-resort fallback
```

Selection requirements:

- Pin authority until it fails health checks. Do not reselect every poll.
- Mark stale if value and timestamp do not progress for a configured window while corroborating activity changes.
- Reject impossible ranges and single-sample jumps, but never delay upward thermal emergencies.
- Keep CPU, GPU, chassis and ambient readings as different sensor kinds.
- Export the selected sensor's full stable identity and rejection reasons for alternatives.

### 4.4 Fan-curve smoothing

Ohman's recent measured approach is worth clean-room adoption: accept rising temperature immediately, decay falling temperature gradually, and suppress tiny downward fan-level changes. This addresses idle fan hunting without weakening heat response.

```csharp
public sealed class AsymmetricThermalFilter
{
    private double? _filtered;

    public double Update(double sampleC, double fallAlpha = 0.40)
    {
        if (_filtered is null || sampleC >= _filtered.Value)
            return (_filtered = sampleC).Value;

        _filtered += (sampleC - _filtered.Value) * fallAlpha;
        return _filtered.Value;
    }
}
```

Then apply a level deadband only on downward changes:

```csharp
static int ApplyDownwardDeadband(int current, int requested, int deadband = 2)
{
    if (requested >= current) return requested;
    return current - requested < deadband ? current : requested;
}
```

Safety constraints:

- Never smooth away an upward spike.
- Thermal emergency consumes raw validated readings as well as filtered readings.
- A stale/frozen sensor is not “cool”. It is an unsafe unknown.
- Unit-test the filter separately from hardware.

### 4.5 Adaptive dGPU polling

Ohman documents that repeated `nvidia-smi` polling can wake the dGPU and increase system thermals. OmenCore should ensure every provider declares whether a read can wake the GPU, then use activity-aware cadence.

```csharp
public sealed record PollPolicy(
    TimeSpan ActiveInterval,
    TimeSpan IdleInterval,
    int IdleSamplesRequired,
    double IdleUtilisationPercent,
    double IdlePackagePowerW);
```

Recommended behaviour:

- Active GPU: normal high-frequency polling required by the fan curve.
- Hybrid, confirmed idle for three samples: back off expensive/wake-capable reads to about two minutes.
- iGPU-only mode: skip dGPU wake-capable reads entirely.
- UI can display “sleeping/not polled” rather than stale zeroes.
- On process/game start, AC transition, display change or GPU activity evidence, return immediately to active cadence.

Do not hard-code the exact thresholds above without checking OmenCore's current provider behaviour and tests. They are a validated comparator pattern, not universal firmware truth.

---

## 5. Fan-control audit

### 5.1 How fan control works

OmenCore's WMI path uses firmware-reported fan count/levels and issues performance mode, Max and direct-level commands. `WmiFanController` documents the HP firmware tendency to revert user fan state and therefore uses a countdown/keepalive mechanism. The controller tracks last mode, whether Max is active, last manual percentage and a discovered maximum level. Newer boards may use 0–100 semantics, while classic boards commonly use level units around RPM/100.

The backend factory appears designed to choose among WMI, EC and fallback implementations based on runtime capability and model evidence. This is preferable to a single generic write path.

### 5.2 v4.4.1 corrections already described

The supplied changelog/roadmap describes:

- clearing firmware Max after guided verification;
- retrying a failed/ignored Max test with a direct fan-level write;
- using the board's real ceiling instead of hardcoded 55;
- accepting a stable 80 percent-or-greater firmware ceiling after both methods are tried;
- suspending keepalive during diagnostics from preset, Max and manual states;
- maintaining watchdog failsafe every 15 seconds;
- requiring ≤65 °C for 15 continuous seconds before failsafe release;
- correcting unknown-board and two-fan entries for 8BBE, 88F8, 88F7 and 8C2D.

These are coherent fixes, but they remain symptoms of distributed ownership. Consolidate them behind a state coordinator.

### 5.3 Proposed fan-control state machine

```csharp
public enum FanOwner
{
    None,
    BiosAuto,
    Profile,
    Manual,
    Max,
    Diagnostic,
    ThermalEmergency,
    WatchdogFailsafe
}

public sealed record FanIntent(
    FanOwner Owner,
    int Priority,
    int? CpuLevel,
    int? GpuLevel,
    bool RequestFirmwareMax,
    DateTimeOffset ExpiresAt,
    string Reason);
```

Coordinator rules:

1. Highest-priority non-expired intent wins.
2. Only the coordinator calls the backend write methods.
3. Every write records desired state, transport result, verification source and observed state.
4. Exiting Max always sends `SetFanMax(false)` before a normal level or Auto handoff.
5. Diagnostic acquisition suspends normal keepalive and records the previous effective intent.
6. Diagnostic release restores through the coordinator, never by calling the backend directly.
7. Watchdog/thermal intents are renewable leases, not one-shot writes.
8. BIOS Auto release must explicitly clear Max and manual/user-defined state in a board-safe sequence.

### 5.4 Keepalive design

The Ohman research indicates that `0x10` can arm or refresh the user-defined fan state and that firmware may expire it after roughly 120 seconds. OmenCore's existing keepalive is therefore justified, but it must be explicit and observable.

```csharp
private async Task RefreshLeaseAsync(FanIntent intent, CancellationToken ct)
{
    if (intent.Owner is FanOwner.Diagnostic)
        return;

    var result = await _backend.RefreshAndApplyAsync(intent, ct);
    _state.Record(result);

    if (!result.Verified && intent.Priority >= ThermalPriority)
        _alerts.RaiseCritical("Safety fan state could not be verified");
}
```

Avoid a timer per feature. Use one monotonic scheduler for all fan leases. Timers should never overlap; use a `SemaphoreSlim`, channel/actor loop, or single hosted worker.

### 5.5 Fan demand policy

OmenXHub uses the higher of CPU and GPU temperature so neither fan is underestimated when the other component is hot. This is useful as a **policy option**, but OmenCore should retain independent curves where hardware supports them.

Recommended modes:

```text
Independent: CPU fan follows CPU curve; GPU fan follows GPU curve.
CoupledMax: both fans use max(CPU demand, GPU demand).
CrossWeighted: each fan uses its own demand plus a bounded contribution from the other device.
FirmwareAuto: no software curve ownership.
```

Example cross-weighting:

```csharp
cpuDemand = Math.Max(cpuCurve(cpuC), gpuCurve(gpuC) - crossPenalty);
gpuDemand = Math.Max(gpuCurve(gpuC), cpuCurve(cpuC) - crossPenalty);
```

Only expose `CoupledMax` or `CrossWeighted` after verification confirms two writable fans. One-fan/family fallback must remain conservative.

### 5.6 RPM and level normalization

Never let the UI or policy layer assume that “55” means 100 percent. Introduce a board-calibrated scale:

```csharp
public sealed record FanScale(
    int MinimumControllableLevel,
    int FirmwareTableCeiling,
    int MeasuredCeiling,
    bool IsPercentageNative,
    FanEvidence Evidence);

public int PercentToLevel(int percent, FanScale scale)
{
    percent = Math.Clamp(percent, 0, 100);
    if (percent == 0) return 0;
    int ceiling = scale.MeasuredCeiling > 0
        ? scale.MeasuredCeiling
        : scale.FirmwareTableCeiling;
    return (int)Math.Round(
        scale.MinimumControllableLevel +
        (ceiling - scale.MinimumControllableLevel) * percent / 100.0);
}
```

Keep “fan off” semantically separate from “Auto”. Level zero may physically stop the fan while BIOS Auto is a control-mode handoff.

---

## 6. GPU power, boosts and tuning

### 6.1 Distinguish the mechanisms

OmenCore's UI and backend should use unambiguous names for four different operations:

1. **WMI GPU power policy:** firmware tuple read/write via `0x21`/`0x22` such as cTGP, PPAB, dState and peak temperature.
2. **Concurrent CPU+GPU budget / Smart Performance Gain:** `0x29`, often changing the shared budget rather than directly setting GPU TGP.
3. **NVIDIA clock offsets:** NVAPI core/memory offset controls.
4. **MUX/graphics mode:** `0x52`, potentially requiring restart.

A UI label such as “GPU Power Boost” must state which mechanism is active and whether it was requested, accepted and read back.

### 6.2 Capability gating defect

The v4.4.1 documents identify a real mismatch: the tray displayed GPU Power Boost because WMI BIOS existed, while the backend refused it for Victus unless explicitly opted in. The correct rule is shared, not copied:

```csharp
public sealed record GpuPowerCapability(
    bool ReadCommandSupported,
    bool WriteCommandSupported,
    bool BoardExplicitlyAllowed,
    bool FamilyExplicitlyDenied,
    string Evidence);

public bool CanOfferGpuPowerControl(GpuPowerCapability c) =>
    c.ReadCommandSupported &&
    c.WriteCommandSupported &&
    c.BoardExplicitlyAllowed &&
    !c.FamilyExplicitlyDenied;
```

The UI, tray, CLI and automation engine must all consume the same result object.

### 6.3 Transactional tuning

The changelog notes that test-applied GPU overclock and CPU undervolt values could survive process exit. Make all test tuning transaction-based:

```csharp
public interface ITuningTransaction : IAsyncDisposable
{
    Guid Id { get; }
    DateTimeOffset RevertAt { get; }
    Task KeepAsync(CancellationToken ct);
    Task RevertAsync(CancellationToken ct);
}
```

Implementation requirements:

- Capture the pre-test device state, not merely saved settings.
- Register the transaction before applying the experimental value.
- Revert on timeout, cancellation, normal close and handled fatal exit.
- On next startup, detect an unclosed journal and offer/perform recovery.
- Keep thermal emergency independent from tuning rollback.
- Tests must cover dispose-before-keep, timeout, failed apply, partial apply, restart recovery and service shutdown ordering.

### 6.4 What not to copy from OmenXHub

OmenXHub advertises presets including 254 W values, TGP/PPAB 255 W and driver-version-specific Dynamic Boost unlocking. These may be appropriate to its target machine or coding assumptions but are not portable evidence. Do not import values, support claims or dual-write behaviour into OmenCore's general path.

Potentially useful as a diagnostics-only experiment:

```text
Probe A: current OmenCore WMI power write, then read back and record.
Probe B: alternative path supported by the specific board, then read back.
Compare: requested bytes, return code, PM-table/telemetry evidence and stability.
Promote only after repeatable field confirmation on that board family.
```

---

## 7. Board and capability database

### 7.1 Current 4.4.1 board changes

| Board | Product family | 4.4.1 evidence/status |
|---|---|---|
| `8BBE` | Victus 16-r0xxx Intel | Exact entry; WMI fan and V1 policy evidenced; curves/GPU boost/RGB/UV remain conservative. |
| `88F8` | Victus 16-d0xxx Intel | Exact entry; two fans; 30/60 writes verified; backlight only; Max ignored in prior test. |
| `8C2D` | Victus 15-fa1xxx Intel | Exact entry; V0; two fans; 55-level ceiling; curves await 4.4.x guided verification. |
| `88F7` | OMEN 17-ck0xxx Intel | Exact entry; WMI V1; two fans; curves enabled; four-zone RGB confirmed; Max ceiling below table maximum. |
| `8BA9` | OMEN 16-wd0xxx | Keyboard identity entry; colour still needs an apply log. |
| `8BD4` | 2023 board in #212 | Live topology says one zone; WMI zone-count byte fix implemented, awaiting hardware confirmation. |
| `8E35` | OMEN 16-ap0xxx Ryzen 9 8940HX | Performance mode firmware table evidence; fan/RGB also observed; overall UserVerified remains deliberately conservative. |
| `8BCA` | Ryzen 9 7940HS Linux report | CPU sensor selection issue; prefer `k10temp` over frozen `acpitz`. |
| `8DD0` | existing verified fan/RPM entry | Intermittent stuck-Max/high-temperature report lacks live diagnostic evidence. |
| `8D87` | OMEN MAX 16 | Per-key work exists; GPU power EC write remains gated because path is unproven. |

### 7.2 Recommended schema

Static booleans cannot express enough nuance. Move toward claims with evidence and scope:

```csharp
public sealed record CapabilityClaim(
    CapabilityKind Capability,
    SupportState State,
    EvidenceLevel Evidence,
    string Source,
    string? BiosRange,
    CpuVendor? RequiredCpuVendor,
    DateOnly? VerifiedOn,
    string Notes);

public enum SupportState
{
    Unsupported,
    Hidden,
    DiagnosticOnly,
    Experimental,
    Supported,
    Degraded
}

public enum EvidenceLevel
{
    AssumedFromFamily,
    FirmwareReported,
    WriteAccepted,
    ReadbackConfirmed,
    UserObserved,
    IndependentlyMeasured
}
```

Runtime output should merge static claims with probes to produce one `ResolvedCapabilities` object. Export both the final decision and the evidence chain.

### 7.3 Resolution precedence

```text
1. Explicit board + CPU vendor + BIOS-scoped override
2. Explicit board entry
3. Tested family entry
4. Runtime firmware probe
5. Conservative generic fallback
6. Unsupported/diagnostic-only
```

A family match must never override a board's observed fan count. That is exactly what the new 88F8 and 8C2D entries correct.

### 7.4 Board onboarding workflow

For each new ProductId:

1. Capture model, family, ProductId, BIOS version, CPU/GPU identity and keyboard topology.
2. Read system-design data and record raw bytes.
3. Read fan count, fan types, fan levels and fan table without writes.
4. Detect policy version and permitted mode mapping.
5. Run guided fan 30/60/100 tests with cancellation and emergency escape.
6. Confirm Max release and BIOS Auto restoration.
7. Probe GPU power read before exposing writes.
8. Probe RGB topology, transport success and physical/readback result separately.
9. Export scrubbed logs with stable correlation IDs.
10. Promote only the capabilities proven by the submitted evidence.

---

## 8. Lighting and keyboard observations

### 8.1 WMI colour tables

Published research identifies keyboard type through `0x20008/0x2B`, while colour/backlight commands use command family `0x20009`. Four-zone colour tables are read-modify-write structures. OmenCore's v4.4.1 zone-count change is appropriately narrow: change the declared count byte derived from live topology, while leaving the still-unknown one-zone payload layout unchanged.

Recommended safeguards:

- Record topology source: firmware type byte, database override, HID probe or fallback.
- Do not use `HasFourZoneRgb` as a zone-count integer.
- Maintain separate “lighting exists” and “specific colour protocol works” claims.
- Verification must distinguish transport success, firmware readback and visible hardware effect.
- If Windows Dynamic Lighting owns the virtual lamp device, explain contention rather than reporting a failed firmware write as success.

### 8.2 Primax and Darfon per-key paths

The supplied documents state an experimental Primax path for 2021–2024 OMEN 16/17 devices (`0461:4E9A` and `0461:4E9B`) with uniform static colour only, a handshake requirement, command whitelist and refusal of flash/restore/update commands. That is the correct safety posture.

Do not broaden this to animations or arbitrary per-key mapping in v4.4.1. First obtain:

- positive device-info handshake;
- non-persistent static colour confirmation;
- restore-on-exit confirmation;
- OGH mutex coexistence test;
- at least two board/SKU reports if claiming a family range;
- an independently built key-index map before four-zone-to-per-key translation.

For the 2025 Darfon/OMEN MAX path, preserve the rule that flash commit is never invoked per frame.

---

## 9. Comparison matrix

| Area | OmenCore | Ohman | OmenXHub | Recommendation |
|---|---|---|---|---|
| Scope | Broad suite, Windows/Linux, diagnostics and integrations | Minimal single-purpose replacement | Broad Windows utility suite | Keep OmenCore breadth, simplify ownership internally. |
| Fan control | Multiple backends, presets, curves, verification, watchdog | Explicit measured WMI semantics, simple curve loop | Rich modes and coupled hottest-source policy | Adopt Ohman's documented semantics and smoothing; adapt coupled policy optionally. |
| Temperature | Multiple providers and worker path | Strong attention to wake cost and sensor noise | LHM/PawnIO/HWiNFO integrations | Add stable authority/provenance and provider cost metadata. |
| GPU power | WMI/firmware gating plus NVAPI tuning | Clear separation of gain/GPU mode/MUX | Aggressive boost/unlock options | Preserve conservative evidence gate; improve naming and transactions. |
| Board support | Rich ProductId database | Runtime-first generic profile plus verified overrides | Broad claims, target-machine bias | Use runtime-first evidence but retain OmenCore's explicit database. |
| Diagnostics | Strong export and guided fan checks | Small privacy-aware support script and research notes | Extensive features, less visible evidence discipline | Add privacy scrub manifest and trace/replay fixtures. |
| Safety | Strong, but several competing writers | Simple thermal guard and minimum-level refusal | High-temperature protection but aggressive tuning | Consolidate fan authority and tuning rollback. |
| Licensing | MIT | GPL-3.0 | GPL-3.0 | Reimplement ideas/protocol facts; do not copy GPL code into MIT codebase. |

---

## 10. Prioritised 4.4.1 implementation plan

### P0: release blockers

#### P0.1 Verify the supplied changelog against the working tree

Create a script/report that maps every changelog item to:

- commit hash;
- touched files;
- tests added;
- build status;
- hardware confirmation status;
- documentation status.

Fail the release check if an item is documented as fixed but absent or untested.

#### P0.2 Centralise fan write ownership

At minimum, add an internal arbiter so verification, watchdog, thermal protection, manual, Max and profile paths cannot write concurrently. If a full refactor is too invasive for 4.4.1, add a shared `FanOperationGate` and explicit operation reason/correlation ID.

```csharp
public sealed class FanOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(
        string reason,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await operation(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
```

This is a stabilisation bridge, not the final lease coordinator.

#### P0.3 Max exit invariant

Add a single helper used by every path:

```csharp
private async Task ExitFirmwareMaxAsync(
    int fallbackLevel,
    CancellationToken ct)
{
    await _wmiBios.SetFanMaxAsync(false, ct);
    await _wmiBios.SetFanLevelsAsync(
        fallbackLevel, fallbackLevel, ct);
    _isMaxModeActive = false;
}
```

Adjust to actual signatures and fan count. The invariant matters more than this exact code: no direct `SetFanMax(true)` may exist without paired cleanup ownership.

#### P0.4 Transactional test rollback

Implement and test pending GPU OC/CPU UV rollback on normal shutdown, timer expiry and startup recovery journal.

#### P0.5 Linux sensor ranking

Confirm all Linux consumers use the same selected CPU sensor: daemon, status, diagnose, monitor, fan curve and thermal emergency. Test explicit configured driver and unknown-driver fallback.

### P1: high-value hardening

#### P1.1 Telemetry provenance

Expose a debug view/export section:

```json
{
  "cpuTemperature": {
    "valueC": 72.4,
    "provider": "LibreHardwareMonitor",
    "sensor": "CPU Package",
    "quality": "Healthy",
    "sampleAgeMs": 410
  }
}
```

#### P1.2 Trace/replay harness

Persist scrubbed hardware call traces:

```csharp
public sealed record HardwareTraceEntry(
    long Sequence,
    DateTimeOffset At,
    string Domain,
    string Operation,
    string RequestHex,
    int ReturnCode,
    string ResponseHex,
    string CorrelationId);
```

Build a fake `IHpWmiBios` that replays traces. This allows regression tests for board issues without owning each laptop.

#### P1.3 Sensor smoothing and deadband

Implement asymmetric filtering behind a feature flag. Compare raw and filtered traces before making it default.

#### P1.4 Idle GPU polling

Audit all NVAPI, LHM, worker and external process reads. Mark which can wake the dGPU; introduce active/idle cadence and “sleeping” UI semantics.

#### P1.5 Shared capability resolver

Make UI, tray, CLI and automation consume one resolved capability snapshot. Remove duplicate family logic from view models.

### P2: after 4.4.1 or field confirmation

- Automatic fan curves for #189 using measured ceilings, smoothing and lease ownership.
- Hardware confirmation of #212 and then single-zone payload investigation if still failing.
- Primax per-key index mapping after uniform colour proves safe.
- 8D87 GPU power path only after EC write validation.
- Optional CoupledMax/CrossWeighted fan-policy experiment.
- Structured board-evidence registry generated from diagnostics exports.

---

## 11. Test plan

### 11.1 Unit tests

Add tests for:

- policy byte mapping across V0, V1, Victus S and exception boards;
- Max flag release after every 100 percent diagnostic path;
- Max accepted-but-ignored fallback;
- firmware ceiling acceptance at exactly 80 percent and rejection below it;
- one-fan vs two-fan payload generation;
- keepalive suspension for preset, manual, Max and diagnostics;
- watchdog reapply every interval and 15-second cool hold;
- sensor authority pinning and fallback;
- frozen but plausible sensor values;
- upward-immediate/downward-smoothed thermal filter;
- dGPU idle cadence and iGPU-only skip;
- tuning transaction timeout, close, failure and journal recovery;
- WMI RGB zone-count mapping for every topology enum;
- UI/tray/CLI capability consistency.

### 11.2 Integration tests with mocked firmware

Model these traces:

```text
A. Set Max returns success; fan levels never exceed previous 60% level.
B. Set Max returns success; stable level reaches 48/55.
C. Two-fan board arrives through a one-fan family fallback.
D. CPU sensor remains exactly 20.0 C while package power/load changes.
E. External controller resets fans after watchdog applies 90%.
F. Diagnostic begins while manual/Max keepalive is active.
G. WMI colour write succeeds but readback mismatches.
H. GPU power read exists but board policy denies write.
```

### 11.3 Hardware acceptance checklist

For every board confirmation:

- record BIOS and ProductId;
- confirm OGH is not concurrently controlling the same domain;
- collect baseline temperatures and fan readback;
- apply low, medium and maximum requests;
- verify audible/physical response and readback independently;
- cancel midway and confirm safe restoration;
- sleep/resume and AC/battery transition;
- close OmenCore during a test and confirm rollback;
- export diagnostics while the fault is present, not after restart;
- label the result `confirmed`, `accepted-unverified`, `failed` or `inconclusive`.

---

## 12. Diagnostics and privacy

Adopt Ohman's useful support-bundle idea: collect only hardware and OmenCore state required for diagnosis, and scrub usernames, home paths, hostnames, serials, account identifiers, MAC addresses and unrelated process/window titles before a bundle is offered for public upload.

Recommended manifest:

```json
{
  "schemaVersion": 2,
  "appVersion": "4.4.1",
  "boardId": "88F8",
  "biosVersion": "user-provided-runtime-value",
  "redactionsApplied": [
    "userProfilePath",
    "userName",
    "computerName",
    "serialNumber",
    "macAddress"
  ],
  "containsHardwareWrites": true
}
```

The export should include operation correlation IDs so a UI action can be matched to service, backend and firmware results.

---

## 13. Licensing and clean-room rules

Ohman and OmenXHub are GPL-3.0 projects, while OmenCore is MIT-licensed. The agent must:

- use published protocol facts, measurements and behavioural observations as research;
- independently implement algorithms and interfaces in OmenCore's style;
- not paste GPL source code, comments or distinctive implementation structure;
- credit factual research in documentation where appropriate;
- retain provenance notes for board evidence;
- inspect each repository's current licence before using any file;
- avoid importing bundled binaries or drivers unless their redistribution terms are separately verified.

Code samples in this audit are original implementation sketches and must still be adapted to actual OmenCore interfaces.

---

## 14. Concrete instructions for the coding agent

### Phase 1: repository reconciliation

1. Read `CLAUDE.md`, `README.md`, `VERSION.txt`, the supplied v4.4.1 roadmap/changelog and all v4.4.1-related commits.
2. Run the full test suite and build with warnings treated as errors where the repo supports it.
3. Produce `docs/V4.4.1_IMPLEMENTATION_STATUS.md` mapping each intended item to code/tests/commit.
4. Do not modify code until the discrepancy list is complete.
5. Preserve all newer work found locally.

### Phase 2: control path inventory

Search for every direct call to:

```text
SetFanMax
SetFanLevel / SetFanLevels
SetFanMode / performance mode writes
0x10, 0x1A, 0x21, 0x22, 0x27, 0x29, 0x2E
NVAPI clock-offset writes
undervolt writes
RGB colour-table writes
```

Create a call graph showing caller, safety gate, state owner, verification and rollback. Any direct hardware write from a view model is a defect candidate.

### Phase 3: release blockers

Implement P0 items with minimal API churn:

- global fan operation gate or coordinator;
- Max cleanup invariant;
- tuning transaction rollback;
- Linux sensor selection consistency;
- changelog-to-code release validation.

### Phase 4: tests and replay

Add pure tests first, then mocked hardware traces. Do not require real WMI in CI. Add fixtures derived from scrubbed diagnostics for 8BBE, 88F8, 8C2D, 88F7, 8BD4, 8E35, 8BCA and 8DD0 where available.

### Phase 5: optional high-value improvements

Only after P0 is green:

- sensor provenance;
- asymmetric filtering;
- idle dGPU polling;
- shared capability snapshot;
- optional coupled fan-demand policy behind an experimental flag.

### Phase 6: release evidence

Before changing `VERSION.txt`:

- all tests pass;
- no warnings;
- installer and portable asset selection tests pass;
- updater dry-run validates target asset and SHA256;
- shutdown rollback test passes;
- diagnostics export contains provenance and redaction manifest;
- every changelog claim is tagged as code-verified, test-verified or field-confirmed;
- unresolved hardware items remain explicitly “pending confirmation”.

---

## 15. Definition of done for v4.4.1

A release candidate is ready only when:

- no fan path can leave firmware Max asserted unintentionally;
- diagnostic and normal keepalive cannot fight;
- watchdog failsafe remains asserted until a validated cool hold completes;
- CPU fan control cannot select a known dead ACPI zone over a real CPU sensor;
- the UI never advertises GPU power control that the backend will categorically refuse;
- pending tuning tests revert on close and recover after abnormal termination where possible;
- board entries do not overwrite observed fan count or topology with a family default;
- one-zone WMI RGB is described as pending hardware confirmation until proven;
- Primax support remains handshake-gated, non-flashing and experimental;
- every hardware mutation is logged with request, result, verification and reason;
- documentation accurately separates implemented, test-verified and field-confirmed states.

---

## 16. Final recommendation

OmenCore should not try to become OmenXHub feature-for-feature or Ohman line-for-line. Its competitive advantage is safer breadth, explicit diagnostics, board evidence and cross-platform support. For 4.4.1, the best outcome is a release that makes its existing hardware stack more deterministic and explainable.

The most important architectural move is a single authority for fan writes. The most important telemetry move is stable sensor provenance. The most important tuning move is transaction-backed rollback. The most important model-support move is replacing loose booleans with evidence-bearing capability claims. Those changes will reduce recurring board-specific regressions and make later work, including automatic curves, Primax mapping and 8D87 GPU power, materially safer.

---

## Sources reviewed

- OmenCore repository: https://github.com/theantipopau/omencore
- OmenCore WMI fan controller: https://github.com/theantipopau/omencore/blob/main/src/OmenCoreApp/Hardware/WmiFanController.cs
- OmenCore public site: https://omencore.info/
- Supplied `CHANGELOG_v4.4.1.md`
- Supplied `ROADMAP_v4.4.1.md`
- Ohman repository: https://github.com/P4R1H/Ohman
- Ohman firmware research: https://github.com/P4R1H/ohman/blob/main/docs/research.md
- Ohman laptop/support tooling: https://github.com/P4R1H/ohman/blob/main/tools/support-info.ps1
- OmenXHub repository: https://github.com/MasonDye/OmenXHub
- OmenXHub hardware implementation entry point: https://github.com/MasonDye/OmenXHub/blob/main/OmenHardware.cs
- OmenMon repository: https://github.com/OmenMon/OmenMon
- OmenMon documentation: https://omenmon.github.io/
