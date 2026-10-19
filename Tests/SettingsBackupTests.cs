using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class SettingsBackupTests
    {
        [TestMethod]
        public void CopiesTheSettingsWithTheirPaths()
        {
            using (var temp = new TempFolder())
            {
                var retail = temp.Folder("_retail_");
                temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua", "account");
                temp.File(@"_retail_\WTF\Account\ACC\Realm\Char\SavedVariables\BPGP.lua", "character");
                temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\Details.lua", "other addon");
                var backups = temp.Combine("backups");

                var folder = SettingsBackup.Create(retail, "before updating to v1.1.0", backups);
                Assert.AreEqual("account", File.ReadAllText(Path.Combine(folder, "Account", "ACC", "SavedVariables", "BPGP.lua")));
                Assert.AreEqual("character", File.ReadAllText(Path.Combine(folder, "Account", "ACC", "Realm", "Char", "SavedVariables", "BPGP.lua")));
                Assert.IsFalse(File.Exists(Path.Combine(folder, "Account", "ACC", "SavedVariables", "Details.lua")));

                var listed = SettingsBackup.List(backups).Single();
                Assert.AreEqual(folder, listed.Folder);
                Assert.AreEqual("before updating to v1.1.0", listed.Reason);
            }
        }

        [TestMethod]
        public void NothingToCopyMakesNoBackup()
        {
            using (var temp = new TempFolder())
            {
                var backups = temp.Combine("backups");
                Assert.IsNull(SettingsBackup.Create(temp.Folder("_retail_"), "before installing v1.0.0", backups));
                Assert.AreEqual(0, SettingsBackup.List(backups).Count);
            }
        }

        [TestMethod]
        public void KeepsTheNewest()
        {
            using (var temp = new TempFolder())
            {
                var retail = temp.Folder("_retail_");
                var settings = temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua", "");
                var backups = temp.Combine("backups");
                for (int i = 1; i <= 12; i++)
                {
                    File.WriteAllText(settings, "version " + i);
                    SettingsBackup.Create(retail, "backup " + i, backups);
                }

                SettingsBackup.Prune(backups, SettingsBackup.Keep);
                var kept = SettingsBackup.List(backups);
                CollectionAssert.AreEqual(new[] { "backup 12", "backup 11", "backup 10", "backup 9", "backup 8" }, kept.Select(b => b.Reason).ToArray());
                Assert.AreEqual(SettingsBackup.Keep, Directory.GetDirectories(backups).Length);
            }
        }

        [TestMethod]
        public void RestorePutsTheFilesBack()
        {
            using (var temp = new TempFolder())
            {
                var retail = temp.Folder("_retail_");
                var settings = temp.File(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua", "good");
                var backups = temp.Combine("backups");
                var folder = SettingsBackup.Create(retail, "before uninstalling", backups);

                File.Delete(settings);
                Assert.AreEqual(1, SettingsBackup.Restore(folder, retail));
                Assert.AreEqual("good", File.ReadAllText(settings));
            }
        }

        [TestMethod]
        public void RestoreWritesNothingButBpgpSettings()
        {
            using (var temp = new TempFolder())
            {
                var retail = temp.Folder("_retail_");
                var folder = temp.Folder(@"backups\20261019-210000");
                temp.File(@"backups\20261019-210000\Account\ACC\SavedVariables\BPGP.lua", "ok");
                temp.File(@"backups\20261019-210000\Account\ACC\SavedVariables\Details.lua", "not ours");
                temp.File(@"backups\20261019-210000\Account\ACC\BPGP.lua", "not in SavedVariables");
                temp.File(@"backups\20261019-210000\Config.wtf", "not ours");

                Assert.AreEqual(1, SettingsBackup.Restore(folder, retail));
                var written = Directory.GetFiles(temp.Combine(@"_retail_\WTF"), "*", SearchOption.AllDirectories);
                CollectionAssert.AreEqual(new[] { temp.Combine(@"_retail_\WTF\Account\ACC\SavedVariables\BPGP.lua") }, written);
            }
        }

        [TestMethod]
        public void ListSkipsForeignFolders()
        {
            using (var temp = new TempFolder())
            {
                temp.Folder(@"backups\20261019-210000");
                temp.Folder(@"backups\20261019-210000-2");
                temp.Folder(@"backups\something else");
                var backups = SettingsBackup.List(temp.Combine("backups"));
                Assert.AreEqual(2, backups.Count);
                Assert.AreEqual(2, backups[0].Sequence);
            }
        }
    }
}
