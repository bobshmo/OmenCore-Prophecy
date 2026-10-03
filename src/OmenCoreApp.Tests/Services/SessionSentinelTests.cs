using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>GitHub #211: an unclean exit must be detectable on the next start.</summary>
    public class SessionSentinelTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "omencore-sentinel-" + Guid.NewGuid().ToString("N"));

        public SessionSentinelTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private static SessionSentinel.SessionRecord Record(bool clean) => new()
        {
            Pid = 4242, StartedUtc = DateTime.UtcNow.AddMinutes(-30), LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-5),
            Version = "4.4.0", CleanExit = clean
        };

        [Fact]
        public void UncleanAndProcessGone_IsReported()
        {
            SessionSentinel.ShouldReportUnclean(Record(false), sameProcessStillAlive: false).Should().BeTrue();
        }

        [Fact]
        public void CleanExit_IsNotReported()
        {
            SessionSentinel.ShouldReportUnclean(Record(true), false).Should().BeFalse();
        }

        [Fact]
        public void ProcessStillRunning_IsNotReported()
        {
            SessionSentinel.ShouldReportUnclean(Record(false), sameProcessStillAlive: true).Should().BeFalse();
        }

        [Theory]
        [InlineData("Application Error", 1000, true)]
        [InlineData("Application Hang", 1002, true)]
        [InlineData(".NET Runtime", 1026, true)]
        [InlineData("Windows Error Reporting", 1001, true)]
        [InlineData("Application Error", 1001, false)]
        [InlineData("Some Other Source", 1000, false)]
        [InlineData(null, 1000, false)]
        public void OnlyCrashShapedEvents_CountAsEvidence(string? provider, int id, bool expected)
        {
            SessionSentinel.IsCrashEvent(provider, id).Should().Be(expected);
        }

        [Fact]
        public void EvidenceWindow_CoversALateCrashEntry()
        {
            var start = new DateTime(2026, 9, 25, 11, 47, 0, DateTimeKind.Utc);
            var beat = start.AddMinutes(24);
            var (from, to) = SessionSentinel.EvidenceWindow(start, beat);
            from.Should().Be(start);
            to.Should().Be(beat.AddMinutes(5));
        }

        [Fact]
        public void FirstEverStart_ReportsNothing_AndWritesTheRecord()
        {
            using var sentinel = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            sentinel.StartSession("4.4.1").Should().BeNull();
            File.Exists(Path.Combine(_dir, SessionSentinel.FileName)).Should().BeTrue();
        }

        [Fact]
        public void KilledSession_IsReportedOnTheNextStart()
        {
            var first = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            first.StartSession("4.4.0");
            first.Dispose(); // never marked clean: the process "died"

            using var second = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            var report = second.StartSession("4.4.1");

            report.Should().NotBeNull();
            report!.Session.Version.Should().Be("4.4.0");
            report.Summarise().Should().Contain("did not exit cleanly");
        }

        [Fact]
        public void CleanlyClosedSession_IsNotReportedOnTheNextStart()
        {
            var first = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            first.StartSession("4.4.0");
            first.MarkCleanExit();
            first.Dispose();

            using var second = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            second.StartSession("4.4.1").Should().BeNull();
        }

        [Fact]
        public void CorruptRecord_NeverBlocksStartup()
        {
            File.WriteAllText(Path.Combine(_dir, SessionSentinel.FileName), "{ not json");
            using var sentinel = new SessionSentinel(_dir, isSameProcessAlive: (_, _) => false);
            var act = () => sentinel.StartSession("4.4.1");
            act.Should().NotThrow();
        }

        [Fact]
        public void NoEvidenceFound_SaysLikelyTerminatedFromOutside()
        {
            var report = new SessionSentinel.PreviousSessionReport { Session = Record(false) };
            report.Summarise().Should().Contain("terminated from outside");
        }
    }
}
