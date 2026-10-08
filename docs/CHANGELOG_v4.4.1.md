# OmenCore v4.4.1

**Local Prophecy integration (October 7, 2026):** this checkout adds an in-app Prophecy control window on Tuning, carrying over laptop NVIDIA power/tuning/telemetry, the gated extended Victus CPU fields and optional mVolt imports. Fan Max uses OmenCore's fan service; CURRENT preparation pauses fan reapplication and requests HPCM first. Implemented and covered by hardware-free checks; hosted hardware behavior remains pending confirmation. This is not part of the official v4.4.1 release. See `PROPHECY-INTEGRATION.md`.

**Release Date:** 2026-10-03
**Release Status:** Release candidate. Started 2026-09-25, one day after v4.4.0 shipped. Suite: 1773 app tests and 63 Linux tests passing, 0 warnings.
**Type:** Field-report follow-up to 4.4.0, and the first release delivered by the fixed in-app updater.
Twenty-one fixes: fan control (stuck Max after verification and after fan cleaning, the firmware's own
Max ceiling, two-fan boards cut to one, watchdog failsafe, keepalive/diagnostic guard), RGB
(backlight-only keyboards, Dynamic Lighting detection, keyboard fallback), tuning (revert and recover
pending tests, a false "Degraded" undervolt, the GPU-mode panel), the RAM optimizer and system cleaner,
Linux CPU sensor selection with a config override, silent-exit evidence, and Ctrl+1…9 page shortcuts;
five new board entries (`8BBE`, `88F8`, `88F7`, `8C2D`, `878A`) plus keyboard entries for `8BA9` and
`88F7`; experimental per-key colour for 2021-2024 OMEN 16/17 Primax keyboards; and one RGB fix awaiting
hardware confirmation (`#212`). Sources: post-release diagnostics exports, GitHub issues, Discord, a
community fork, PR `#210`, and the Ohman project's published research.
**Base Version:** v4.4.0
**Tracking doc:** `docs/ROADMAP_v4.4.1.md` — full investigation detail, evidence trails, and what's
still open live there; this file stays short.

---

**Updating to 4.4.1:** from the republished 4.4.0, use the in-app updater (the update button in the header, or F5);
this is the first release it can install end to end. From 4.3.1 or earlier, or from a 4.4.0
installed before the same-day republish, download the installer from the release page once;
settings are kept.

---

## Fixed

### In-App Updater Fixed (Shipped in the 4.4.0 Republish, First Exercised by This Release)

Reported the day 4.4.0 shipped: in-app updates from 4.3.1 downloaded, passed the hash check, then
failed with "Downloaded file is not a valid Windows executable". Three causes, all fixed in the
same-day 4.4.0 republish and carried into 4.4.1:

- The updater read the self-extraction temp folder as its install location, so installed copies
  looked portable. It now uses the running exe's own folder.
- It looked for the `OmenCore` uninstall key, but Inno Setup registers `{AppId}_is1`; both are
  now checked, in both registry views and hives.
- Portable builds took the last `.zip` in the release, which could be the Linux package. Asset
  selection is now a tested function: Setup exe for installed builds, Windows zip for portable.

A portable download now ends with "close OmenCore and extract this" and opens its folder instead of
a false "corrupted" error, and the About window no longer sticks on "Installing update..." after a
failed install. Listed here because 4.4.1 is the first update the fixed updater delivers.

### Watchdog Failsafe Could Release Onto a Still-Hot Machine, and Never Re-Applied Itself

Found reviewing a community fork (`ujjawalkaushik1110/omencore`), reimplemented directly against
`main`. Two gaps: the frozen-sensor failsafe released back to BIOS Auto on the very next telemetry
sample regardless of what that sample actually read, and once active it applied the 90% fan speed
exactly once — anything that reset fan state afterward (OGH, a firmware reassert) could undo it with
nothing watching. Release now needs temperatures at or below 65°C held for 15 seconds; the failsafe
reapplies itself every 15 seconds for as long as it stays active.

### Guided Fan Verification's 100% Test Could Report a Board as Failing When Firmware Just Ignored Max

Board `88F8` ([#207](https://github.com/theantipopau/omencore/issues/207)): the 100% test's
`SetFanMax` call was accepted by firmware but never moved the fans past the previous test point —
the same "accepts Max, ignores it" bug already fixed elsewhere this cycle, reached through this
method's own one-shot apply instead. Now retries with a direct level write before giving up. Also
fixed: the existing fallback path sent a hardcoded level regardless of the board's real ceiling.

### Tray Offered GPU Power Boost on Victus Boards Whose Backend Always Refuses It

Three independent Victus exports (`8C2F` in [#155](https://github.com/theantipopau/omencore/issues/155)
and [#184](https://github.com/theantipopau/omencore/issues/184), `88F8` in
[#207](https://github.com/theantipopau/omencore/issues/207)) showed "Show GPU Power Boost: Yes"
beside the backend's own "skipped - HP Victus does not support WMI TGP/PPAB control". The display
gate treated "WMI BIOS is present" as GPU-power evidence. It now mirrors the backend's Victus rule
exactly: hidden unless the board's entry explicitly opts in. Non-Victus boards are unchanged.
First raised in PR [#210](https://github.com/theantipopau/omencore/pull/210); narrowed to Victus here.

### Fans Stuck at Full Speed After Guided Fan Verification

[#198](https://github.com/theantipopau/omencore/issues/198) (board `8BBE`): the verification's 100%
steps set the firmware's Max flag directly, bypassing the fan controller's own Max tracking. When
the test finished it restored BIOS auto by setting fan mode Default, but nothing knew to clear Max,
so the fans stayed at full speed and ignored profile changes until something happened to route
through the Max-exit sequence (or the firmware timed it out). After a 100% step, the Max flag is now
released and replaced with a normal level write at the same ceiling, which every preset change and
auto restore already hands back. Also covers the Fan Control page's post-apply check, which uses
the same routine. Possibly also behind the stuck-at-max report in `#213`.

### Guided Fan Verification Failed Boards Whose Firmware Max Is Just Below the Table Ceiling

Some boards accept Max but settle a little short of the 55-level table ceiling: `8E35` at 48/55
with both fans audibly at full speed ([#195](https://github.com/theantipopau/omencore/issues/195)),
`88F7` at 46/55 ([#215](https://github.com/theantipopau/omencore/issues/215)). The 100% step
reported those as failures. Once both Max and a direct level write have been tried, a stable
readback at 80% of the ceiling or above now passes as evidence `FirmwareCeiling`, the board's own
maximum. A board where Max never moved past the 60% step (`88F8`, 33/55) still fails.

### Guided Fan Diagnostic Could Be Overridden by the Keepalive From Max or Manual Mode

The fan keepalive timer already stood down during a Guided Fan Diagnostic, but only from preset
modes. Started from Max or manual control, its reasserts could fight the diagnostic's own writes.
Now covers all three. Found reviewing PR
[#210](https://github.com/theantipopau/omencore/pull/210).

### Performance Mode Label Showed the Saved Mode When Startup Restore Was Off

[#199](https://github.com/theantipopau/omencore/issues/199): with startup restore disabled (the
recommended setting on OMEN 16 / Victus), the saved mode was pre-selected for the picker and also
reported as *active* - the System Control status label and the tray said "Performance" while the dashboard's
runtime-confirmed label said "Default", which is what firmware was actually running. Those labels
now report "Default" until a mode is actually applied; the picker still remembers your choice.

### Linux: Fan Curve and Thermal Emergency Read a Dead ACPI Zone Instead of the CPU

[#214](https://github.com/theantipopau/omencore/issues/214) (board `8BCA`, Ryzen 9 7940HS): the
daemon took the first readable CPU sensor in sysfs enumeration order, which on this laptop was an
`acpitz` zone frozen at +20.0°C, ahead of `k10temp`. The custom curve only ever reacted to the GPU,
and the 95°C emergency never fired, while the CPU hit 99°C and throttled. CPU sensors are now
ranked: `k10temp`/`coretemp`/`zenpower` first, `acpitz` only as a last resort.

### RAM Optimizer's Working-Set Trim Never Trimmed Anything

The Windows memory-list command for "empty working sets" was declared as `0`, which is actually
`MemoryCaptureAccessedBits` (the real value is `2`). Windows accepted it, the log said "Working sets
cleaned", and nothing was trimmed. Only the default path was affected; with process exclusions set,
the per-process trim was already correct. Values are now pinned by tests.

### RAM Optimizer Could Run Two Full Cleans Back to Back

A clean requested while another was running (e.g. auto-clean landing during a manual one) queued
behind it and then ran a second full clean, instead of reporting one was already in progress.

### System Cleaner Now Uses the Vendor's Own Silent Uninstall Command

Win32 uninstalls always appended guessed `/S /silent /quiet` switches to the interactive
uninstall command. Where the vendor registers a `QuietUninstallString`, that is now used verbatim.

### Windows Dynamic Lighting Detected on Four-Zone Keyboards

On Windows 11, Dynamic Lighting can take over HP's four-zone keyboard (published to Windows as a
virtual lamp device) and keep repainting it, so colours set in OmenCore don't appear or revert,
even though the firmware reports them written. The Lighting page now shows a banner with a button
to the Windows setting when this is the case (read-only; OmenCore never changes the setting), and
diagnostics exports record it. Previously only detected for OMEN MAX per-key keyboards.

### Boards That Report Two Fans Were Cut Down to One by the Database

`8C30` ([#208](https://github.com/theantipopau/omencore/issues/208)/[#220](https://github.com/theantipopau/omencore/issues/220)),
`88F8` and `8C2D` all showed the same defect: the firmware answered `Fan Count: 2`, and the capability
layer overwrote it with the model database's value, always, even downwards, so an entry that said 1
hid the second fan. A firmware count that was actually read now beats an *unverified* entry that says
fewer; a user-verified entry still wins, since that is evidence rather than a family guess. `8C30` and
its sibling entry are corrected from 1 to 2. Covers every board whose entry carries a family-default
count, not only the ones reported.

### Backlight-Only Keyboards Were Offered Colour Controls

[#217](https://github.com/theantipopau/omencore/issues/217) (board `88EE`): the firmware reports the
keyboard as "Normal" (backlit, not colour-addressable) and the database agrees, so the lighting engine
correctly found no colour backend, but the older fallback saw "WMI BIOS is present" and offered
four-zone colour writes anyway. They were accepted and did nothing visible. When the database **and**
the firmware topology both say backlight-only, on 2021+ boards, colour control is now hidden and the
Lighting page says so in plain words. The model-year gate matters: pre-2021 boards can answer the
topology probe with 0 simply because they predate it, and some of those have working colour.

### GPU Mode Switching "Apply" Did Nothing, Silently

[#215](https://github.com/theantipopau/omencore/issues/215) (board `88F7`): on a BIOS that doesn't
expose GPU mode switching over WMI, pressing Apply logged `GPU mode switching not available` to the
log file only and showed nothing. The panel's support/status properties existed but were bound nowhere.
Apply and the mode picker are now disabled when switching isn't available, with the reason on screen.

### Tuning Page Showed a Failed Undervolt on a Fresh Install

[#220](https://github.com/theantipopau/omencore/issues/220): the shipped default config pre-filled a
−90 / −60 mV undervolt as "requested", the page compared it with the live 0/0 and reported
"Degraded", and the rollback coordinator counted it as unsafe state, though nothing had ever been
applied. A mismatch is now reported only after an apply, test-apply or startup reapply has actually
been attempted, and new configs default to 0/0. Existing saved values are left alone.

### Fan Cleaning Could Leave Fans Pinned, and Fought the Keepalive

Fan cleaning set the firmware Max flag itself and restored with a bare `SetFanMax(false)`. On V1
boards that alone has left fans pinned (OmenCore's #198; Ohman's #57 on `8BCD`/`8C78`). It also ran
without diagnostic mode, so the keepalive and curve engine could overwrite the cycle, and nothing put
the user's preset back. Cleaning now uses the same Max-exit sequence as the controller
(`HpWmiBios.ReleaseMaxAndHandBackToBios`: Max off → Default mode → a low level hint on V0/V1), runs
inside diagnostic mode, and restores the preset or BIOS auto afterwards. The calibration cleanup
fallback uses the same sequence. A static release gate now fails the build if any new file turns on
firmware Max without a release path.

### Silent Exits Now Leave Evidence

[#211](https://github.com/theantipopau/omencore/issues/211): the main app's log simply stopped, with
no shutdown line, while the HardwareWorker kept running ("Parent process exited"), so the worker's
NVML crash was not the cause and nothing could say what was. OmenCore now keeps a small session record
(PID, start, a 30-second heartbeat) and marks it clean on a normal exit. If the next launch finds one
that was never marked clean, it logs one warning with the PID, last-alive time and any Windows
Application Error / .NET Runtime / Error Reporting entry naming OmenCore, or says Windows recorded
nothing, which points to the process being killed from outside.

### Closing OmenCore During a Tuning Test Left the Untested Overclock or Undervolt Applied

GPU overclocking and CPU undervolting both offer Test Apply: the change runs for 30 seconds and
reverts unless you press Keep. If OmenCore was closed inside that window, nothing reverted it; the
untested values weren't saved, but NVAPI offsets and undervolt writes stayed live until a reboot
or driver reset. Exit now reverts any pending test first, while the GPU and undervolt services
are still running.
If OmenCore was killed or crashed mid-test instead, the next launch used to reset only the saved
values and show a notice; it now also writes the defaults back to the GPU and CPU.

### Keyboard Backend Fallback When Model Detection Returns Nothing

Minor: if keyboard model detection returned no config at all (a non-OMEN/Victus identity or a
detection error), a failed colour apply had no fallback backend to try, because a null-conditional
type check evaluated to false. Unrecognised OMEN/Victus boards were never affected; they get a
default four-zone config.

---

## Added

### Board `8BBE` (Victus 16-r0xxx, Intel) Given an Exact Entry

[#211](https://github.com/theantipopau/omencore/issues/211): was Family fallback only since `#172`;
WMI fan control and V1 policy confirmed live in a real diagnostics export.

### Board `88F8` (Victus 16-d0xxx, Intel) Given an Exact Entry

[#207](https://github.com/theantipopau/omencore/issues/207): was Family fallback, which also cut the
firmware's two fans down to one. Flags from the reporter's 4.4.0 export: WMI level writes verified at
30%/60%, two fans, backlight-only keyboard. Curves, GPU boost and undervolt left off pending evidence.

### Per-Key Keyboards on 2021-2024 OMEN 16/17: Uniform Colour via the Keyboard's Own MCU — Experimental

On these boards the BIOS reports a per-key keyboard and keeps accepting the four-zone colour
commands, but nothing written there reaches the keys, so colour changes silently did nothing. The
keyboard is a Primax USB MCU (`0461:4E9A` on OMEN 17, `0461:4E9B` on OMEN 16). OmenCore now probes
it first whenever firmware reports per-key, and sets one colour across the whole keyboard through
the MCU's static colour map, the same frame format our OMEN MAX (Darfon) support already speaks.
Boards OMEN Gaming Hub routes this way include `88F7`/`88FE`/`8A17`–`8A1A`/`8BAD`/`8BB0` (OMEN 17)
and `88F4`–`88F6`/`88FD`/`8A13`–`8A16` (OMEN 16), when the SKU has the per-key keyboard.

- **Uniform colour only.** Per-zone colours aren't mapped onto these keys yet; zone 1's colour is
  applied to every key. Effects other than static and off aren't supported on this path.
- **Guarded.** Only device-info reads, lighting on/off and the three colour pages can be sent; the
  flash-store (`0x0A`) and restore / firmware-update (`0x10`) commands are refused in code. Nothing is
  written to flash, and the keyboard must answer a device-info handshake before anything else is sent.
- **Not yet confirmed on hardware by OmenCore.** The Primax-specific facts come from the
  [Ohman](https://github.com/P4R1H/ohman) project's published research (confirmed there on board
  `8BAD`), used as documentation only; no Ohman code was copied.

### Board `8C2D` (Victus 15-fa1xxx, Intel) Given an Exact Entry

[#205](https://github.com/theantipopau/omencore/issues/205): resolved by the `15-fa1` name pattern
to the `8BB1` Victus profile, which reports one fan; the firmware reports two. From the export:
V0 policy, two fans, 55 levels, backlight-only keyboard. Fan curves stay off until a Guided Fan
Verification run on 4.4.x (the export came from 4.3.1, where fan writes were wrongly disabled).

### Linux: CPU Temperature Sensor Can Be Chosen in Config

Requested in [#214](https://github.com/theantipopau/omencore/issues/214). New `thermal.cpu_sensor`
(e.g. `"k10temp"`) picks the CPU temperature source by hwmon driver name, never by index, which
reshuffles between boots. Honoured by the daemon, `status`, `diagnose` and `monitor`; an unknown name
falls back to automatic ranking. Also settable with `omencore-cli config --set thermal.cpu_sensor=k10temp`.

### Linux: DKMS Four-Zone Keyboard Nodes and the Brightness-Gate Trap

Ported, clean-room, from the OMEN Slim 16 (board `8D40`) fork by
[saikiranworks](https://github.com/saikiranworks/omencore). The community DKMS `hp-wmi` driver exposes
`fourzone_color` (all four zones in one 24-hex-character file) and a **separate** `fourzone_brightness`
gate (0-255). Colour writes can succeed while the gate reads 0, so the keyboard looks dead, which is
what many "RGB does nothing on Linux" reports look like. OmenCore now writes these nodes when they
exist (it never touches them otherwise), and raises a zero gate when a visible colour is set unless
`OMENCORE_SKIP_BRIGHTNESS_INIT=1`. The Linux install guide gains a four-zone section with a
group-based udev rule instead of the fork's world-writable one, and a note never to open the fan PWM
nodes.

### Board `8DD0` Corrected From the Same Diagnostics as PR #210

From [#210](https://github.com/theantipopau/omencore/pull/210) (ujjawalkaushik1110): the entry said one
fan, but the board reports two (RPM is estimated from fan level), and it has a backlit but not
colour-addressable keyboard. `FanZoneCount` is now 2 with a 55-level ceiling, and a backlight-only
keyboard entry is added, so colour controls stay hidden. The PR's watchdog and GPU-boost parts were
already on `main`.

### Board `878A` (OMEN 15-ek0xxx, i7-10750H + RTX 2060) Given an Exact Entry

[#218](https://github.com/theantipopau/omencore/issues/218): was the generic legacy fallback. From the
export: V1 policy, two fans, 55 levels, fan-mode writes accepted, fan level readback works, RPM
readback null (so none claimed). Flags otherwise follow sibling `878C`. Curves, MUX, GPU boost and
undervolt left off pending a Guided Fan Verification run.

### Board `88F7` (OMEN 17-ck0xxx, Intel) Given Exact Capability and Keyboard Entries

[#215](https://github.com/theantipopau/omencore/issues/215): was Family fallback, which also hid the
keyboard model. From the reporter's 4.4.0 export: WMI V1, two fans, 55 levels, level writes verified
at 30%/60% (curves enabled), four-zone keyboard with per-zone RGB confirmed working. The 100% miss
in their fan test is the "accepts Max, ignores it" issue already fixed above. MUX, GPU boost and
undervolt left off pending evidence.

### Board `8BA9` (OMEN 16-wd0xxx) Given a Keyboard Entry

Resolved as "Keyboard: Unknown" despite having a capability entry. Identity-only: it now names the
same ColorTable four-zone path the default config already used. A Discord report says lighting still
doesn't change colour on this board; that is **not** fixed here and needs a log from an apply attempt.

### Victus 16-r0xxx Intel No Longer Misidentified as the Ryzen Board

[#115](https://github.com/theantipopau/omencore/issues/115),
[#172](https://github.com/theantipopau/omencore/issues/172): board `8BBE` resolved to the AMD `8C2F`
profile by name pattern. The new `8BBE` entry above resolves it by ProductId.

### Zone-Colour RGB: WMI Backend Now Declares Its Real Zone Count — Implemented, Pending Confirmation

[#212](https://github.com/theantipopau/omencore/issues/212) (board `8BD4`): the firmware's own
topology probe reports a single RGB zone, but `WmiBiosBackend`/`EcDirectBackend` both hardcoded
"4 zones" and never checked, so `HpWmiBios.SetColorTable`'s own payload told the firmware something
it wasn't — even on a follow-up test sending the identical colour into all four zone slots.
`WmiBiosBackend.ZoneCount` now reads the live topology probe and passes the real count into
`SetColorTable`'s byte 0 instead of a hardcoded `4`; the 4-slot colour payload itself is unchanged,
since the real single-zone byte layout still isn't known — this tests only the one piece there's hard
evidence for. `EcDirectBackend` untouched (different, older-generation mechanism, no declared-zone-
count byte). **Not yet confirmed on real hardware.** The flag that looked like a quick fix
(`HasFourZoneRgb`) still doesn't control zone count at all and was left at `true`; flipping it
removes colour control entirely instead of correcting it. See the roadmap for the full write-up.

### Ctrl+1…9 Jumps Between Pages

Keyboard navigation for the page rail: Ctrl+1 through Ctrl+9 opens the Nth page as it appears on
screen, counting only visible pages, so the numbers stay right when advanced pages are hidden.
Ignored while typing in a text field.

### Board `8E35` (OMEN 16-ap0xxx, Ryzen 9 8940HX): Performance Mode Confirmed at the Firmware

[#195](https://github.com/theantipopau/omencore/issues/195): the WMI policy fallback enabled in 4.4.0
is now confirmed by direct PM-table readback. Balanced held STAPM/fast/slow at 75/75/65 W in every
sample; only Performance reached 80/80/80 W (the firmware's own governor then alternates within it).
Guided Fan Verification (30%/60% on both fans, audible) and four-zone RGB are confirmed too. With the
firmware-ceiling change above, the 100% step should pass on this build, the last item before
`UserVerified`. Notes only; no flag changed.

---

## Investigated, Not Fixed

### Zone-Colour RGB On `EcDirectBackend`, and the Real Single-Zone Byte Layout If the Above Doesn't Confirm

`EcDirectBackend.ZoneCount` still hardcodes `4` — deliberately left alone this pass, see above. And
if the `WmiBiosBackend` byte-0 fix doesn't resolve `#212` on real hardware, the real single-zone
`ColorTable` layout is still unknown. See the roadmap for what's needed either way.

---

## Contributors

Thank you to the people whose reports and code shaped this release: WoofahRayetCode (the guided RGB
check and the controller handoff in PR #216, plus the `8E35` power evidence), ujjawalkaushik1110
(PR #210, the `8DD0` evidence), saikiranworks (the DKMS four-zone write-up), murilopontes (OMEN 15-dc0
EC fan-boost test data), and every reporter whose export became a board entry. Protocol facts from the
Ohman project's published research informed the Primax keyboard path and the Max-exit sequence; no
Ohman code was used.

The full list is in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

---

## Issue Housekeeping

Closed during this cycle as already resolved in 4.4.0 or duplicated elsewhere: `#170` (`8A3E`
already in the database), `#188` (`8D26` entry shipped), `#174` (duplicate of `#199`), `#156`
(duplicate of `#149`). Close when this release ships: `#115` and `#172` (8BBE misidentification),
`#214` (Linux CPU sensor), `#215` (88F7 entry), `#205` (8C2D entry; the 4.3.1 regression it
reports was fixed in 4.4.0). `#217`, `#218` and `#220` once their reporters confirm on 4.4.1. `#198` (stuck Max after verification) was already closed; fixed here. `#199` stays open for `8BA9` verification.

---

## Needs Field Confirmation

Shipped on evidence but not yet seen working on the affected hardware. Each is safe to be wrong
about (no change in behaviour on boards it doesn't apply to), but reports are wanted:

- `#212` single-zone keyboard colour (byte-0 zone count) — board `8BD4`.
- Primax per-key uniform colour — any 2021-2024 per-key OMEN 16/17.
- Windows Dynamic Lighting banner on four-zone keyboards — detection relies on the documented
  virtual-device id `0461:0000`; if Windows names it differently the banner simply won't show.
- Guided Fan Verification 100% fallback — `88F8` (`#207`), `88F7` (`#215`).
- Linux CPU sensor ranking — `8BCA` (`#214`).
- Backlight-only colour suppression — `88EE` (`#217`), `88F8` (`#207`).
- Firmware fan count over the database — `8C30` (`#220`); a rerun should show two fans.
- First session-sentinel report — `#211`.
- GPU-mode panel now disabled with a reason — `88F7` (`#215`).

---
