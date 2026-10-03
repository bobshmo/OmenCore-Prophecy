using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace OmenCoreApp.Tests
{
    /// <summary>
    /// Release gate for the stuck-Max class of bug (GitHub #198, #213, #220; Ohman #57): the firmware's Max
    /// fan flag outlives the process that set it, so every code path that turns it on must also be able to
    /// turn it off, and new paths must be added here deliberately rather than by accident.
    ///
    /// This is a static scan, not a behavioural test - it can't prove a release runs, only that no file can
    /// set Max without containing a release. The behavioural coverage lives with each owner
    /// (FanVerificationCeilingTests, controller reset tests).
    /// </summary>
    public class FirmwareMaxPairingGateTests
    {
        // Files allowed to enable firmware Max. WmiFanController owns the tracked state; the others set it
        // directly and have their own paired release (see HpWmiBios.ReleaseMaxAndHandBackToBios).
        private static readonly HashSet<string> MaxEnablingFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "WmiFanController.cs",
            "FanControllerFactory.cs",    // OGH-proxy controllers; lifecycle owned by their own Dispose/reset
            "FanVerificationService.cs",  // guided verification 100% steps
            "FanCleaningService.cs"       // fan cleaning / boost cycle
        };

        private static readonly Regex EnablesMax = new(@"\.(SetFanMax|SetMaxFan)\(\s*true\s*\)", RegexOptions.Compiled);
        private static readonly Regex ReleasesMax = new(
            @"\.(SetFanMax|SetMaxFan)\(\s*false\s*\)|ReleaseMaxAndHandBackToBios|ReleaseFirmwareMaxFlag|ResetFromMaxMode",
            RegexOptions.Compiled);

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OmenCore.sln")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "installer")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private static IEnumerable<string> ProductionSources()
        {
            var root = FindRepoRoot();
            if (root == null) yield break;

            foreach (var project in new[] { "OmenCore.Core", "OmenCoreApp", "OmenCore.Cli" })
            {
                var dir = Path.Combine(root, "src", project);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
                    if (normalized.Contains("/obj/") || normalized.Contains("/bin/")) continue;
                    yield return file;
                }
            }
        }

        [Fact]
        public void OnlyKnownFiles_CanEnableFirmwareMax()
        {
            var sources = ProductionSources().ToList();
            if (sources.Count == 0) return; // source tree not available (packaged test run)

            var offenders = sources
                .Where(f => EnablesMax.IsMatch(StripComments(File.ReadAllText(f))))
                .Select(Path.GetFileName)
                .Where(name => !MaxEnablingFiles.Contains(name!))
                .Distinct()
                .ToList();

            offenders.Should().BeEmpty(
                "a new file that enables firmware Max must be added to MaxEnablingFiles on purpose, with a " +
                "paired release (use HpWmiBios.ReleaseMaxAndHandBackToBios) - found: " + string.Join(", ", offenders));
        }

        [Fact]
        public void EveryMaxEnablingFile_AlsoContainsAReleasePath()
        {
            var sources = ProductionSources().ToList();
            if (sources.Count == 0) return;

            var unpaired = sources
                .Where(f => MaxEnablingFiles.Contains(Path.GetFileName(f)))
                .Select(f => (Name: Path.GetFileName(f), Text: StripComments(File.ReadAllText(f))))
                .Where(f => EnablesMax.IsMatch(f.Text) && !ReleasesMax.IsMatch(f.Text))
                .Select(f => f.Name)
                .ToList();

            unpaired.Should().BeEmpty("setting firmware Max without any release in the same file is the stuck-Max bug");
        }

        [Fact]
        public void ScannerSanity_RecognisesBothShapes()
        {
            EnablesMax.IsMatch("_wmiBios.SetFanMax(true);").Should().BeTrue();
            EnablesMax.IsMatch("_proxy.SetMaxFan( true )").Should().BeTrue();
            EnablesMax.IsMatch("_wmiBios.SetFanMax(false);").Should().BeFalse();
            ReleasesMax.IsMatch("_wmiBios.ReleaseMaxAndHandBackToBios();").Should().BeTrue();
        }

        // Doc comments mention "SetFanMax(true)" in prose; only code should count.
        private static string StripComments(string text) =>
            Regex.Replace(text, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }
}
