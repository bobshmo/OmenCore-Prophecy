using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace OmenCore.Services
{
    /// <summary>
    /// Records whether the previous OmenCore process ended cleanly, so a silent exit leaves evidence.
    ///
    /// WHY. GitHub #211: the main app's log simply stopped, with no shutdown line, while the
    /// separate HardwareWorker kept running and logged "Parent process exited". A normal close
    /// (<c>App.OnExit</c>) always logs, so the missing line proves it wasn't one - but nothing could
    /// say what it WAS. <see cref="LastShutdownProbe"/> doesn't help: it answers for the whole
    /// machine (bugcheck / power loss), not for one process dying under a healthy Windows.
    ///
    /// HOW. A tiny file is written at start, refreshed by a heartbeat, and marked clean in
    /// <c>OnExit</c>. On the next start an unmarked file whose process is gone is a previous session
    /// that did not exit cleanly. The report then asks the Windows Application log what it recorded
    /// for OmenCore in that window: an Application Error / .NET Runtime / WER entry means a crash
    /// (with the faulting module and exception code), and the absence of one is itself informative -
    /// the process was most likely terminated from outside (Task Manager, security software, a
    /// restart manager).
    ///
    /// Everything here is best effort: a file or event-log failure must never get in the way of
    /// starting the app.
    /// </summary>
    public sealed class SessionSentinel : IDisposable
    {
        public const string FileName = "session.json";
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

        private readonly string _path;
        private readonly LoggingService? _logging;
        private readonly Func<int, DateTime, bool> _isSameProcessAlive;
        private readonly Func<DateTime> _systemBootUtc;
        private Timer? _heartbeat;
        private SessionRecord _current = new();
        private readonly object _gate = new();

        /// <summary>
        /// The same folder <see cref="ConfigurationService"/> uses, computed without constructing it:
        /// that constructor loads (and may rewrite) the config file, and the sentinel needs to start
        /// before anything that can block.
        /// </summary>
        public static string DefaultDirectory()
        {
            var overrideDir = Environment.GetEnvironmentVariable("OMENCORE_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenCore");
        }

        public SessionSentinel(string directory, LoggingService? logging = null,
                               Func<int, DateTime, bool>? isSameProcessAlive = null,
                               Func<DateTime>? systemBootUtc = null)
        {
            _path = Path.Combine(directory, FileName);
            _logging = logging;
            _isSameProcessAlive = isSameProcessAlive ?? IsSameProcessAlive;
            _systemBootUtc = systemBootUtc ?? (() => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64));
        }

        public sealed class SessionRecord
        {
            public int Pid { get; set; }
            public DateTime StartedUtc { get; set; }
            public DateTime LastHeartbeatUtc { get; set; }
            public string Version { get; set; } = string.Empty;
            public bool CleanExit { get; set; }
        }

        public sealed class PreviousSessionReport
        {
            public SessionRecord Session { get; init; } = new();
            public IReadOnlyList<string> WindowsEvidence { get; init; } = Array.Empty<string>();
            public TimeSpan RanFor => Session.LastHeartbeatUtc - Session.StartedUtc;

            public string Summarise() =>
                $"Previous OmenCore session (PID {Session.Pid}, v{Session.Version}) did not exit cleanly: started " +
                $"{Session.StartedUtc:u}, last alive {Session.LastHeartbeatUtc:u} (ran about {RanFor.TotalMinutes:F0} min). " +
                (WindowsEvidence.Count > 0
                    ? "Windows recorded: " + string.Join(" | ", WindowsEvidence)
                    : "Windows recorded no crash for it - it was most likely terminated from outside " +
                      "(Task Manager, security software, or a restart/update), not a crash inside OmenCore.");
        }

        /// <summary>
        /// Begin this session and report on the previous one if it ended uncleanly. Returns null when
        /// there was no previous record, it ended cleanly, or that process is somehow still running.
        /// </summary>
        public PreviousSessionReport? StartSession(string version)
        {
            PreviousSessionReport? report = null;
            try
            {
                var previous = ReadRecord();
                if (previous != null && ShouldReportUnclean(
                        previous,
                        _isSameProcessAlive(previous.Pid, previous.StartedUtc),
                        machineRestartedSince: MachineRestartedAfter(previous.LastHeartbeatUtc, _systemBootUtc())))
                {
                    report = new PreviousSessionReport
                    {
                        Session = previous,
                        WindowsEvidence = ReadCrashEvidence(previous.StartedUtc, previous.LastHeartbeatUtc)
                    };
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"[SessionSentinel] Could not evaluate previous session: {ex.Message}");
            }

            lock (_gate)
            {
                _current = new SessionRecord
                {
                    Pid = Environment.ProcessId,
                    StartedUtc = DateTime.UtcNow,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    Version = version
                };
                Write(_current);
            }

            _heartbeat = new Timer(_ => Beat(), null, HeartbeatInterval, HeartbeatInterval);
            return report;
        }

        /// <summary>Mark this session as exited cleanly. Call from <c>OnExit</c>, before the log is disposed.</summary>
        public void MarkCleanExit()
        {
            lock (_gate)
            {
                _current.CleanExit = true;
                _current.LastHeartbeatUtc = DateTime.UtcNow;
                Write(_current);
            }
        }

        /// <summary>The decision, separated from the file and process lookups so it can be tested.</summary>
        internal static bool ShouldReportUnclean(
            SessionRecord previous, bool sameProcessStillAlive, bool machineRestartedSince = false) =>
            !previous.CleanExit && !sameProcessStillAlive && !machineRestartedSince;

        /// <summary>
        /// True when Windows booted after the previous session's last heartbeat. A session cut off by a
        /// restart or power loss is not an OmenCore problem (a bugcheck is LastShutdownProbe's job), and
        /// reporting it would cry wolf after every hard reboot. A minute of slack covers clock jitter.
        /// </summary>
        internal static bool MachineRestartedAfter(DateTime lastHeartbeatUtc, DateTime systemBootUtc) =>
            systemBootUtc > lastHeartbeatUtc.AddMinutes(1);

        /// <summary>
        /// Application-log providers/IDs that mean a process crashed or hung: Application Error 1000,
        /// Application Hang 1002, .NET Runtime 1026, Windows Error Reporting 1001.
        /// </summary>
        internal static bool IsCrashEvent(string? provider, int eventId) => provider switch
        {
            "Application Error" => eventId == 1000,
            "Application Hang" => eventId == 1002,
            ".NET Runtime" => eventId == 1026,
            "Windows Error Reporting" => eventId == 1001,
            _ => false
        };

        /// <summary>Wide enough to catch a crash logged after the last heartbeat, narrow enough to stay relevant.</summary>
        internal static (DateTime From, DateTime To) EvidenceWindow(DateTime startedUtc, DateTime lastHeartbeatUtc) =>
            (startedUtc, lastHeartbeatUtc + TimeSpan.FromMinutes(5));

        private static IReadOnlyList<string> ReadCrashEvidence(DateTime startedUtc, DateTime lastHeartbeatUtc)
        {
            var found = new List<string>();
            try
            {
                var (from, to) = EvidenceWindow(startedUtc, lastHeartbeatUtc);
                string xpath =
                    "*[System[(EventID=1000 or EventID=1001 or EventID=1002 or EventID=1026) and " +
                    $"TimeCreated[@SystemTime>='{from:o}' and @SystemTime<='{to:o}']]]";

                using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, xpath));
                for (var record = reader.ReadEvent(); record != null && found.Count < 3; record = reader.ReadEvent())
                {
                    using (record)
                    {
                        if (!IsCrashEvent(record.ProviderName, record.Id)) continue;

                        string text;
                        try { text = record.FormatDescription() ?? string.Empty; }
                        catch { text = string.Empty; }

                        if (text.IndexOf("OmenCore", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        found.Add($"{record.ProviderName} {record.Id} at {record.TimeCreated:u}: {Trim(text)}");
                    }
                }
            }
            catch
            {
                // Event-log access can be denied or the log trimmed; that is not worth reporting.
            }
            return found;
        }

        private static string Trim(string text)
        {
            var flat = string.Join(" ", text.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            return flat.Length <= 320 ? flat : flat[..320] + "...";
        }

        private static bool IsSameProcessAlive(int pid, DateTime startedUtc)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                // A recycled PID is a different process; treat a start time well after ours as gone.
                return process.StartTime.ToUniversalTime() <= startedUtc.AddMinutes(1);
            }
            catch
            {
                return false;
            }
        }

        private void Beat()
        {
            lock (_gate)
            {
                _current.LastHeartbeatUtc = DateTime.UtcNow;
                Write(_current);
            }
        }

        private SessionRecord? ReadRecord()
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(_path));
        }

        private void Write(SessionRecord record)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(record));
            }
            catch (Exception ex)
            {
                _logging?.Debug($"[SessionSentinel] Could not write {FileName}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
        }
    }
}
