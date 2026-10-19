using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class WowLocatorTests
    {
        [TestMethod]
        public void FindsRetailFromAnyFolderInTheInstall()
        {
            using (var temp = new TempFolder())
            {
                var addOns = temp.Folder(@"World of Warcraft\_retail_\Interface\AddOns");
                var retail = temp.Combine(@"World of Warcraft\_retail_");

                Assert.AreEqual(retail, WowLocator.ToRetailFolder(temp.Combine("World of Warcraft")));
                Assert.AreEqual(retail, WowLocator.ToRetailFolder(retail));
                Assert.AreEqual(retail, WowLocator.ToRetailFolder(temp.Combine(@"World of Warcraft\_retail_\Interface")));
                Assert.AreEqual(retail, WowLocator.ToRetailFolder(addOns));
                Assert.AreEqual(retail, WowLocator.ToRetailFolder(addOns + System.IO.Path.DirectorySeparatorChar));
            }
        }

        [TestMethod]
        public void RefusesFoldersOutsideAnInstall()
        {
            using (var temp = new TempFolder())
            {
                temp.Folder(@"World of Warcraft\_retail_\Interface\AddOns");
                Assert.IsNull(WowLocator.ToRetailFolder(temp.Path));
                Assert.IsNull(WowLocator.ToRetailFolder(temp.Folder("Games")));
                Assert.IsNull(WowLocator.ToRetailFolder(temp.Combine("does not exist")));
                Assert.IsNull(WowLocator.ToRetailFolder(""));
                Assert.IsNull(WowLocator.ToRetailFolder(null));
            }
        }

        [TestMethod]
        public void NeedsAnAddOnsFolder()
        {
            using (var temp = new TempFolder())
            {
                temp.Folder(@"World of Warcraft\_retail_\Interface");
                Assert.IsNull(WowLocator.ToRetailFolder(temp.Combine("World of Warcraft")));
            }
        }
    }
}
