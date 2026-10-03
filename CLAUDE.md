# CLAUDE.md — OmenCore

Guidance for AI agents working in this repo. Read this first, then the current cycle's
`docs/ROADMAP_v*.md` and `docs/CHANGELOG_v*.md`.

## What this is

OmenCore is an open-source replacement for HP OMEN Gaming Hub (OGH) for HP OMEN / Victus laptops
(and some desktops): fan control and curves, performance modes, GPU power (TGP/PPAB), MUX switch,
CPU undervolt / power limits, keyboard RGB, peripheral RGB (Corsair/Logitech/Razer), telemetry.
GitHub: `theantipopau/omencore`. Maintainer: Matt (theantipopau). Users report via GitHub issues,
diagnostics exports (zip/json attached to issues), Discord, forks and PRs.

## Layout

| Path | What |
|---|---|
| `src/OmenCore.Core/` | Hardware + service logic shared by all frontends (net8.0-windows) |
| `src/OmenCore.Core/Hardware/` | `HpWmiBios` (HP WMI BIOS calls), `WmiFanController`, `FanController` (EC), `ModelCapabilityDatabase`, `CapabilityDetectionService`, `DeviceCapabilities`, `HardwareWorkerClient`, PawnIO EC/MSR access |
| `src/OmenCore.Core/Services/` | `FanService`, `FanVerificationService`, `HardwareWatchdogService`, `ConfigurationService`, `KeyboardLighting/` backends (`WmiBiosBackend`, `EcDirectBackend`), power/perf services |
| `src/OmenCoreApp/` | Main WPF app (MVVM: `ViewModels/`, `Views/`, `App.xaml.cs`) |
| `src/OmenCore.HardwareWorker/` | Out-of-process LibreHardwareMonitor worker (isolates native crashes: NVML/AMD ADL) |
| `src/OmenCore.Cli/` | CLI |
| `src/OmenCore.Linux/` + `.Linux.Tests/` | Linux daemon/CLI (hp-wmi sysfs), net8.0 |
| `src/OmenCore.Avalonia/`, `src/OmenCore.Desktop/` | Cross-platform UI (Desktop csproj still on old 3.6.3 version — ignore on bumps unless asked) |
| `src/OmenCoreApp.Tests/` | xUnit tests for Core + App (~1650 tests) |
| `installer/OmenCoreInstaller.iss` | Inno Setup installer |
| `docs/` | Per-version CHANGELOG / ROADMAP, evidence docs, bug-report logs |
| `website/` | GitHub Pages site (`pages.yml`) |
| `.github/workflows/` | `ci.yml` (build+test on windows-latest, Linux job), `release.yml` (on tag), `alpha.yml`, `linux-qa.yml` |

## Build & test

```bash
dotnet build OmenCore.sln
dotnet test OmenCore.sln                       # full suite, ~7 min; run in background
dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj --filter "FullyQualifiedName~DeviceCapabilitiesTests"
```

- The full suite must stay green before every push (last known: 1658 App tests + 30 Linux tests).
- Build should be 0 warnings / 0 errors.
- Tests that touch config use `[Collection("Config Isolation")]` and `OMENCORE_CONFIG_DIR` temp dirs.
- Shell is Windows (Git Bash / PowerShell). Repo root is `E:\OmenCore\omencore`.
- Bash heredocs can collapse a doubled backslash to a single one: write C# containing escaped backslashes (e.g. `'\\'`) with the Edit/Write tools.
- `ReleaseGateCodeHygieneTests` keeps a line-number bare-`catch` baseline; prefer typed catches in new code.

## Core working discipline (non-negotiable)

1. **Evidence first.** Root-cause from real data: diagnostics exports, logs, the reporter's board ID
   and BIOS version. Download attachments (`github.com/user-attachments/...`) with curl and read them.
   Never guess a fix from the symptom alone.
2. **Evidence gate.** Distinguish clearly between *confirmed* (verified on real hardware by a
   reporter) and *implemented, pending confirmation*. Label them that way in changelogs, roadmap,
   model-database notes and GitHub replies. Never claim hardware behaviour you haven't seen evidence of.
3. **Narrow fixes.** Change only what the evidence supports. Check who else a gate/flag affects
   (e.g. every board in the DB) before broadening or narrowing it; write tests pinning both sides.
4. **Tests with every fix** where feasible — ideally one that fails on the old code.
5. **Honest replies.** When answering on GitHub, say what's fixed, what's pending, what you need from
   the reporter (usually a diagnostics export or a Guided Fan Verification run). Only claim what the
   repo actually contains (e.g. don't say "credited in changelog" before it is).
6. **Credit contributors.** If a PR/fork raised an issue first, credit it even if you reimplement it.
   Check open PRs and forks *before* implementing to avoid duplicate work.
7. **Don't release, tag, close issues or post publicly without the maintainer's go-ahead** unless
   the current instruction clearly covers it.

## Model capability database (`ModelCapabilityDatabase.cs`)

- Boards matched by exact **ProductId** (4-hex board ID, e.g. `8BBE`, `88F8`, `8C2F`).
  `ModelNamePattern` is a fallback only; `RequiredCpuVendor` prevents Intel/AMD variants inheriting
  each other's entry (cause of #115/#172). Unknown boards fall back to a family default
  (Victus family default = 1 fan, which is often wrong).
- New entries: `UserVerified = false` unless a user confirmed it; `Notes` must cite the evidence
  source (issue #, BIOS version, what was verified). Leave unproven features off (curves, GPU boost,
  undervolt) — enable later on evidence.
- Add a `ModelCapabilityDatabaseTests` test for each new entry (resolves exactly, key flags).
- `HasFourZoneRgb` does **not** control zone count; flipping it removes colour control entirely.

## Hard-won technical facts

- **HP WMI BIOS** is the primary control path; EC direct (PawnIO) is the fallback/older path.
  Firmware often *accepts* a command and ignores it (e.g. `SetFanMax`) — verify via readback and
  fall back to direct level writes. Respect each board's real `MaxFanLevel`, never hardcode.
- **Keyboard ColorTable byte 0** = declared zone count. Derived from live `GetKeyboardLightingType()`
  via `HpWmiBios.MapLightingTypeToZoneCount` (single-zone topologies → 1, else 4). #212 fix, pending
  confirmation. `EcDirectBackend` deliberately still hardcodes 4.
- **Victus GPU Power Boost**: backend (`SystemControlViewModel.DetectGpuPowerBoost`) refuses every
  Victus unless `SupportsGpuPowerBoost` is true. `DeviceCapabilities.ShowGpuPowerBoost` mirrors that.
  `HasGpuPowerControl` is set whenever WMI BIOS exists — it is NOT evidence of GPU power support.
- **Fan keepalive** (`WmiFanController.CountdownExtensionCallback`) re-applies state periodically;
  it must stand down while `FanService.IsAnyDiagnosticModeActive` (all branches: preset, Max, manual).
- **Watchdog failsafe** (`HardwareWatchdogService`): releases only after temps ≤65°C held 15s,
  re-applies every 15s while active.
- **Native crashes**: an `AccessViolationException` from NVML/P/Invoke cannot be caught in .NET Core.
  That's why telemetry runs in `OmenCore.HardwareWorker`; `HardwareWorkerClient.ShouldRecoverConnection`
  relaunches it. Skip/quarantine logic lives in the worker's `Program.cs`.
- **Shutdown**: `App.OnExit` always logs "OmenCore shutting down (restoring fans to auto control)...".
  If a user's log lacks it, the process did not exit normally.
- **Config**: `ConfigurationService` must hand out one shared `AppConfig`; `Load()` merges onto the
  existing instance (separate detached copies caused last-writer-wins data loss, #191).
- **Firmware Max flag outlives the process.** Every file that turns it on must release it
  (`FirmwareMaxPairingGateTests` fails the build otherwise). Use
  `HpWmiBios.ReleaseMaxAndHandBackToBios()` — a bare `SetFanMax(false)` has left V1 boards pinned.
  Anything that sets Max outside `WmiFanController` must run inside `FanService.EnterDiagnosticMode()`
  and restore the preset afterwards (see fan cleaning, guided verification).
- **Fan count**: a firmware count that was actually read (`HpWmiBios.FanCountFromFirmware`) beats an
  *unverified* database entry with fewer fans. Never trust the bare default of 2.
- **Backlight-only keyboards**: colour is suppressed only when the database AND firmware topology agree,
  on 2021+ boards (older boards can answer topology 0 just because they predate the probe).
- **Unclean exits**: `SessionSentinel` (session.json in the config folder) logs one WARN on the next
  launch with PID, last-alive time and Windows' Application-log crash record, if any.
- **Tuning**: startup recovery zeroes the *hardware*, not just the config; the undervolt "Degraded"
  state only appears after an apply was attempted; fresh configs default to 0/0.
- **OGH conflicts**: OGH services can reset fan/RGB state; conflict detection and cleanup services exist.

## Versioning & docs

- Current released: **4.4.0**. **4.4.1 is release-ready** (versions bumped, docs finalized) and waits only on the maintainer tagging `v4.4.1`.
- Version lives in: `VERSION.txt`, `installer/OmenCoreInstaller.iss` (`MyAppVersion`), and the
  `<Version>` in the six 4.x `.csproj` files.
- Each cycle has `docs/CHANGELOG_vX.Y.Z.md` (short, user-facing: Fixed / Added / Investigated, Not
  Fixed / Issue Housekeeping) and `docs/ROADMAP_vX.Y.Z.md` (full investigation detail, evidence
  trails, open items). Update both as work lands; keep the changelog header "Type:" summary current.
- `README.md`: download links/SHA256/version stay on the latest *released* version; unreleased work
  goes in "Current Development Focus" and "Known Limits".
- Release flow (only when asked): bump versions → full suite green → tag `vX.Y.Z` → `release.yml`
  builds/publishes → record SHA256 hashes → update README downloads + website → close issues
  earmarked "close on ship".

## Git & GitHub

- Commit directly to `main` in small, focused commits with descriptive messages referencing issues
  (e.g. `#212: declare the real zone count...`). Push after the suite is green.
- **Attribution: everything is Matthew Hurley (theantipopau).** Commits are authored as him and carry NO `Co-Authored-By` trailer for any AI tool, and PR descriptions and GitHub comments carry no "generated with" line or AI mention. This is the owner's explicit rule and overrides any tool default. Credit real contributors (forks, PRs, reporters) in the changelog and `CONTRIBUTORS.md` instead.
- Use `gh` for issues/PRs: `gh issue view N --comments`, `gh pr view N --comments`, `gh pr diff N`,
  `gh issue comment N --body-file file.md` (write long bodies to a scratch file first).
- Don't commit stray local files (e.g. untracked screenshots in `website/assets/`) unless asked.
- Reply tone on GitHub: friendly, concrete, transparent about uncertainty; ask for the specific
  artefact you need (diagnostics export, HardwareWorker.log, Guided Fan Verification export).

## Open threads (as of 2026-10-03 — verify on GitHub before acting)

- Field confirmation wanted: see "Needs Field Confirmation" in `docs/CHANGELOG_v4.4.1.md`; the full
  item→code→test map is `docs/V4.4.1_IMPLEMENTATION_STATUS.md`.
- PR #216 (guided RGB check + a controller handoff that fixes the stuck-level hole in our #198 fix):
  reviewed, ready for the maintainer to merge.
- #211 awaiting the first sentinel line; #217 (88EE), #218 (878A), #220 (8C30) need a 4.4.1 rerun;
  #219 (Linux 8D2F) is a kernel `hp-wmi` allowlist limit; #213, #212, #207, #195 as before.
- Close on 4.4.1 ship: #115, #172, #214, #215, #205. #199 stays open for 8BA9.
- Issue sweep done 2026-10-03: every open issue has a maintainer reply or is waiting on a reporter; see the roadmap's *Issue sweep*. Linux kernel-limit threads (#219 explains) and Linux Darfon RGB (#179) are the recurring classes.
- Primax per-key: next step is an owner test, then an index map from our own source (Ohman is
  GPL-3 — use its documented facts only, never its code or key maps).

## Constraints

- No usage-billed Cloudflare products (Durable Objects, Browser Rendering) for the website or
  anything else without asking first — stay on free tier (static assets, KV, D1, plain Workers).
- Never touch real user hardware settings from tests; hardware is behind interfaces (`IHpWmiBios`,
  `IEcAccess`, `IMsrAccess`) — mock them.
