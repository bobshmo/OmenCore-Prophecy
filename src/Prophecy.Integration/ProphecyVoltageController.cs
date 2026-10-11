using Microsoft.Win32;
namespace NvpwrControlBlackwell;
internal sealed class ProphecyVoltageController {
private readonly PowerBackend _power=new();
        public OperationResult SetVoltage(int millivolts)
        {
            CompatibilityState c = _power.CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("Unsupported GPU profile.");

            try
            {
                string native;
                RegistryKey key = _power.OpenNvidiaDriverKey(true, c.GpuPnpId, out native);
                if (key != null)
                {
                    using (key)
                    {
                        // RmPerfLimitsOverride = 0x55 clears all 4 limits (power, thermal, VRel 940 mV, VOp) in nvlddmkm.sys
                        key.SetValue("RmPerfLimitsOverride", 0x55, RegistryValueKind.DWord);
                        key.SetValue("RMEnableOverclockingAllPstates", 1, RegistryValueKind.DWord);
                    }
                }
                WriteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", "RmPerfLimitsOverride", 0x55, RegistryValueKind.DWord);
                WriteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", "RMEnableOverclockingAllPstates", 1, RegistryValueKind.DWord);
                WriteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm", "RmPerfLimitsOverride", 0x55, RegistryValueKind.DWord);
                WriteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm", "RMEnableOverclockingAllPstates", 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLog.Write("Warning writing driver registry voltage override: " + ex.Message);
            }

            TunerState post;
            OperationResult r = NvApiTuner.ApplyVoltageTarget(millivolts, out post);
            if (r.Success)
            {
                AppLog.Write("SetVoltage applied target " + millivolts.ToString() + " mV");
            }
            return r;
        }
        public OperationResult RestoreVoltage()
        {
            CompatibilityState c = _power.CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("Unsupported GPU profile.");

            try
            {
                string native;
                RegistryKey key = _power.OpenNvidiaDriverKey(true, c.GpuPnpId, out native);
                if (key != null)
                {
                    using (key)
                    {
                        key.DeleteValue("RmPerfLimitsOverride", false);
                        key.DeleteValue("RMEnableOverclockingAllPstates", false);
                    }
                }
                DeleteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", "RmPerfLimitsOverride");
                DeleteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters", "RMEnableOverclockingAllPstates");
                DeleteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm", "RmPerfLimitsOverride");
                DeleteRegistryOverride(@"SYSTEM\CurrentControlSet\Services\nvlddmkm", "RMEnableOverclockingAllPstates");
            }
            catch (Exception ex)
            {
                AppLog.Write("Warning removing driver registry voltage override: " + ex.Message);
            }

            TunerState post;
            OperationResult r = NvApiTuner.ApplyVoltageTarget(940, out post);
            if (r.Success)
            {
                AppLog.Write("RestoreVoltage restored to stock 940 mV");
            }
            return r;
        }
        private static void WriteRegistryOverride(string subKeyPath, string name, object value, RegistryValueKind kind)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(subKeyPath))
                {
                    if (k != null) k.SetValue(name, value, kind);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("WriteRegistryOverride failed for " + subKeyPath + "\\" + name + ": " + ex.Message);
            }
        }
        private static void DeleteRegistryOverride(string subKeyPath, string name)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(subKeyPath, true))
                {
                    if (k != null) k.DeleteValue(name, false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("DeleteRegistryOverride failed for " + subKeyPath + "\\" + name + ": " + ex.Message);
            }
        }
}