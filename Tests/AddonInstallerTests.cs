using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class AddonInstallerTests
    {
        /// <summary>A release zip like the BigWigs packager makes: BPGP\BPGP.toc with a version</summary>
        private static string MakeZip(TempFolder temp, string name, string version)
        {
            var source = temp.Folder(@"zips\" + name);
            if (version != null) temp.File(@"zips\" + name + @"\BPGP\BPGP.toc", "## Interface: 110200\n## Title: BPGP\n## Version: " + version + "\n");
            else temp.File(@"zips\" + name + @"\BPGP\Core.lua", "-- no toc\n");
            var zip = temp.Combine(@"zips\" + name + ".zip");
            ZipFile.CreateFromDirectory(source, zip);
            return zip;
        }

        [TestMethod]
        public void InstallsAndUpdates()
        {
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"_retail_\Interface\AddOns");
                Assert.AreEqual(AddonState.NotInstalled, AddonInstaller.Inspect(addOns).State);

                AddonInstaller.Install(MakeZip(temp, "one", "v1.0.0"), addOns);
                var installed = AddonInstaller.Inspect(addOns);
                Assert.AreEqual(AddonState.Installed, installed.State);
                Assert.AreEqual("1.0.0", installed.Version.ToString());

                AddonInstaller.Install(MakeZip(temp, "two", "v1.1.0"), addOns);
                Assert.AreEqual("1.1.0", AddonInstaller.Inspect(addOns).Version.ToString());
                // Nothing left over next to AddOns
                Assert.IsFalse(Directory.Exists(temp.Combine(@"_retail_\Interface\BPGPUpdater.tmp")));
                Assert.IsFalse(Directory.Exists(temp.Combine(@"_retail_\Interface\AddOns\BPGP.old")));
            }
        }

        [TestMethod]
        public void BrokenZipKeepsTheOldVersion()
        {
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"_retail_\Interface\AddOns");
                AddonInstaller.Install(MakeZip(temp, "good", "v1.0.0"), addOns);

                Assert.ThrowsException<InvalidDataException>(() => AddonInstaller.Install(MakeZip(temp, "broken", null), addOns));
                Assert.AreEqual("1.0.0", AddonInstaller.Inspect(addOns).Version.ToString());
            }
        }

        [TestMethod]
        public void SourceCopyIsADevBuild()
        {
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"_retail_\Interface\AddOns");
                temp.File(@"_retail_\Interface\AddOns\BPGP\BPGP.toc", "## Version: @project-version@\n");
                Assert.AreEqual(AddonState.DevBuild, AddonInstaller.Inspect(addOns).State);
            }
        }

        [TestMethod]
        public void UninstallRemovesOnlyTheAddon()
        {
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"_retail_\Interface\AddOns");
                AddonInstaller.Install(MakeZip(temp, "one", "v1.0.0"), addOns);
                var other = temp.File(@"_retail_\Interface\AddOns\Other\Other.toc", "## Version: 1\n");
                var settings = temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua", "BPGP_DB = {}\n");

                AddonInstaller.Uninstall(addOns);
                Assert.AreEqual(AddonState.NotInstalled, AddonInstaller.Inspect(addOns).State);
                Assert.IsTrue(File.Exists(other));
                Assert.IsTrue(File.Exists(settings));
            }
        }

        [TestMethod]
        public void FindsOnlyBpgpSettingsFiles()
        {
            using (var temp = new TempFolder())
            {
                var retail = temp.Folder("_retail_");
                var expected = new[]
                {
                    temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua", "x"),
                    temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua.bak", "x"),
                    temp.File(@"_retail_\WTF\Account\ACC\Realm\Char\SavedVariables\BPGP.lua", "x"),
                };
                temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP_Other.lua", "x");
                temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua.old", "x");
                temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\Details.lua", "x");
                temp.File(@"_retail_\WTF\Account\ACC\BPGP.lua", "x");

                CollectionAssert.AreEquivalent(expected, AddonInstaller.FindSettings(retail).ToArray());
                Assert.AreEqual(3, AddonInstaller.DeleteSettings(retail));
                Assert.AreEqual(0, AddonInstaller.FindSettings(retail).Length);
            }
        }

        [TestMethod]
        public void LeavesALinkedFolderAlone()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Inconclusive("Junctions are Windows only.");
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"_retail_\Interface\AddOns");
                var clone = temp.Folder("bpgp-clone");
                var code = temp.File(@"bpgp-clone\BPGP.toc", "## Version: @project-version@\n");
                var mklink = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(addOns, "BPGP") + "\" \"" + clone + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                mklink.WaitForExit();
                if (mklink.ExitCode != 0) Assert.Inconclusive("Couldn't make a junction here.");

                Assert.AreEqual(AddonState.Linked, AddonInstaller.Inspect(addOns).State);
                Assert.ThrowsException<InvalidOperationException>(() => AddonInstaller.Uninstall(addOns));
                Assert.ThrowsException<InvalidOperationException>(() => AddonInstaller.Install(MakeZip(temp, "one", "v1.0.0"), addOns));
                Assert.IsTrue(File.Exists(code));
                Directory.Delete(Path.Combine(addOns, "BPGP")); // removes the junction only
            }
        }
    }
}
