using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class SemVersionTests
    {
        [TestMethod]
        public void ParsesTagsWithAndWithoutV()
        {
            var version = SemVersion.Parse("v1.2.3");
            Assert.AreEqual(1, version.Major);
            Assert.AreEqual(2, version.Minor);
            Assert.AreEqual(3, version.Patch);
            Assert.IsFalse(version.IsPreRelease);
            Assert.AreEqual("1.2.3", SemVersion.Parse("1.2.3").ToString());
        }

        [TestMethod]
        public void ShortTagsFillInZeros()
        {
            Assert.AreEqual("0.1.0", SemVersion.Parse("v0.1").ToString());
            Assert.AreEqual("1.0.0", SemVersion.Parse("v1").ToString());
        }

        [TestMethod]
        public void RejectsWhatIsNoVersion()
        {
            Assert.IsNull(SemVersion.Parse(null));
            Assert.IsNull(SemVersion.Parse(""));
            Assert.IsNull(SemVersion.Parse("@project-version@"));
            Assert.IsNull(SemVersion.Parse("latest"));
        }

        [TestMethod]
        public void ReleaseRanksAboveItsPreReleases()
        {
            Assert.IsTrue(SemVersion.IsNewer(SemVersion.Parse("v1.0.0"), SemVersion.Parse("v1.0.0-beta3")));
            Assert.IsFalse(SemVersion.IsNewer(SemVersion.Parse("v1.0.0-beta3"), SemVersion.Parse("v1.0.0")));
            Assert.IsTrue(SemVersion.Parse("v1.0.0-beta1").IsPreRelease);
        }

        [TestMethod]
        public void PreReleasesCompareByNameThenNumber()
        {
            Assert.IsTrue(SemVersion.IsNewer(SemVersion.Parse("v1.0.0-beta10"), SemVersion.Parse("v1.0.0-beta2")));
            Assert.IsTrue(SemVersion.IsNewer(SemVersion.Parse("v1.0.0-beta1"), SemVersion.Parse("v1.0.0-alpha5")));
        }

        [TestMethod]
        public void ComparesNumbersNotText()
        {
            Assert.IsTrue(SemVersion.IsNewer(SemVersion.Parse("v1.10.0"), SemVersion.Parse("v1.9.9")));
            Assert.IsTrue(SemVersion.IsNewer(SemVersion.Parse("v2.0.0"), null));
            Assert.IsFalse(SemVersion.IsNewer(null, SemVersion.Parse("v1.0.0")));
            Assert.IsFalse(SemVersion.IsNewer(SemVersion.Parse("v1.0.0"), SemVersion.Parse("v1.0.0")));
        }
    }
}
