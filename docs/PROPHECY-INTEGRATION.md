# OmenCore + Prophecy

[Download the integrated Windows build](https://github.com/bobshmo/OmenCore-Prophecy/releases/latest).

Custom integration built from the latest GitHub `main` snapshot checked on October 7, 2026:
`0a7835befb7b2b1a3bcebbca4901d49e3755b8e3`. Published as the OmenCore + Prophecy custom fork.

Open **Tuning**. GPU power, GPU tuning, Victus, profiles and device diagnostics now use native WPF controls directly on that page, sharing OmenCore's typography, cards, tabs and styling. The separate hosted Windows Forms interface has been removed. The current shared `OmenCore.Core` hardware library still backs HP and CPU actions.

The Tuning page has one tab set: **GPU power**, **GPU tuning**, **CPU tuning**, **Victus**, **Profiles** and **Device**. Original CPU undervolt, generic power-limit and TCC tools are kept in CPU tuning. GPU Test Apply and stability recovery remain available in the collapsed recovery section of GPU tuning. Diagnostics and Safe Rollback appear in Device. The old tuning sections are no longer appended beneath the main controls.

Saved Fan Max is not restored automatically unless **Settings → Startup Hardware Restore → Restore saved Fan Max** is explicitly enabled. With ordinary fan restore enabled, an unapproved saved Max is replaced by BIOS Auto for that launch. Choosing Quiet or Auto also updates the preset used after sleep, so a previous Max choice cannot come back on resume. Manual Max and thermal protection remain available.

## Included

- MAX override, CURRENT power target, driver and VBIOS validation, restore actions, NVIDIA voltage/clock tuning and telemetry.
- 22 reviewed PCI mappings for 11 RTX 40/50-series laptop models; unknown and conflicting identities are rejected.
- The extended Victus CPU sustained/STAPM, Fast, Slow, APU Slow, skin power and temperature fields. The Ryzen AI 7 350 / Family 26 Model 96 gate remains exact.
- HP TPP, PCF GPU maximum and PLGPU controls on Victus.
- Fan Max routed through OmenCore's fan service, with firmware readback. Turning it off uses OmenCore's Auto preset and its established controller handoff.
- HPCM before NVIDIA CURRENT on Victus. OmenCore fan diagnostics block the operation; during the write its fan engine is paused. A native Max preset is preserved, otherwise performance cooling is selected through the native service before HPCM.
- Optional official mVolt selection and profile import. Imported profiles stay in memory; adapter, VBIOS and unchanged saved-profile checks remain required.
- Scheduled CURRENT arguments are recognized by OmenCore. An already-running OmenCore instance blocks a second unattended writer.

No personal overclock profile, ROM, resolver cache, registry backup or saved settings is included. NVIDIA controls use the installed NVIDIA driver without a custom unsigned driver or Windows test-signing mode. CPU controls separately require official PawnIO.

The extra CPU controls use a private, gated SMU instance and never modify OmenCore's global `RyzenControl.Family` or native capability flags. CPU reapplication starts only after an explicit apply with its keep-active option enabled. App shutdown stops reapplication; exit is deferred while an apply is running. Fan ownership remains with OmenCore, whose normal shutdown restores Auto. Telemetry updates preserve edited values. Failed tuning restores preserve selected fields. Invalid numeric input blocks writes.

Voltage controls distinguish a driver-reported offset from a voltage-curve target. The transport path is selected explicitly, and offset requests require an exposed voltage-offset field. An inherited rollback-state routing error that could send an offset as a curve target is corrected.

NVIDIA support is identification coverage, not a claim of unlocking verified on every mapped laptop. Driver, VBIOS, OEM policy and power readback still gate writes. Hardware validation of this new hosted integration remains pending; no increased limits were applied during its tests.

Validation includes hardware-free checks for PCI mapping, CPU gates, HPCM ordering, input validation, concurrent applies, disposal, edit preservation, truthful errors, voltage routing and mVolt profile identity. All five native tabs have been rendered using simulated data. No increased hardware limits were applied during verification.

Release verification: 1,816 Windows tests and 63 Linux tests passed, including 43 focused Prophecy checks. The native app builds with zero warnings and zero errors.

## Run and build

Extract the whole portable folder and run `OmenCore.exe` as administrator. Keep its hardware worker, libraries and drivers folder. Optional mVolt setup is supplied next to the executable. Supply your own GPU ROM or read-only NVFlash dump tool for VBIOS resolution.

```powershell
.\build-prophecy.ps1
```

This builds the app and its hardware worker and creates a portable ZIP under `publish`. To run the regression suites:

```powershell
dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj -c Release
dotnet test src/OmenCore.Linux.Tests/OmenCore.Linux.Tests.csproj -c Release
```

The integration requires one NVIDIA adapter. Existing OmenCore features and Linux source are retained. A future official OmenCore update may replace this custom build; retain the portable folder if you want to keep these additional controls.

## Attribution

Integration source derives from [bobshmo/Prophecy-Power-Unlocker](https://github.com/bobshmo/Prophecy-Power-Unlocker), whose NVIDIA backend credits [timmyy123/nvidia-power-control](https://github.com/timmyy123/nvidia-power-control). See the included Prophecy third-party notices and license texts. The shared NVIDIA wrapper is updated to `LLT.NvAPIWrapper.Net` 1.1.24-pre.38 to provide the carried-over PCF controls. It is supplied as a replaceable DLL in the portable build.
