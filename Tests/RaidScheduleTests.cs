using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class RaidScheduleTests
    {
        /// <summary>
        /// Fixtures\raid-schedule.csv is built like the guild's sheet: role labels above some names, spacer
        /// columns between the groups, an Offspec row, past raids, and a year change after December.
        /// </summary>
        private static RaidSchedule Fixture(DateTime today)
        {
            var csv = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "raid-schedule.csv"));
            return RaidSchedule.Parse(csv, today);
        }

        [TestMethod]
        public void ReadsThePlayersFromTheHeader()
        {
            var schedule = Fixture(new DateTime(2026, 10, 19));
            CollectionAssert.AreEqual(new[]
            {
                "Stefan", "Sander", "Jelmer", "Melanie", "Jeroen", "Ruben", "Marc", "Hans", "Prizzy", "Patrick",
                "Rodrigo", "Dennis", "Jurre", "Kevin", "Zjacky", "Geoffey", "Stefan2", "Jerry",
            }, schedule.Players);
        }

        [TestMethod]
        public void ReadsEveryRaidWithItsNote()
        {
            var schedule = Fixture(new DateTime(2026, 10, 19));
            Assert.AreEqual(18, schedule.Raids.Count);
            var thursday = schedule.Raids.Single(r => r.Start.Day == 15 && r.Start.Month == 10);
            Assert.AreEqual(new DateTime(2026, 10, 15, 20, 0, 0), thursday.Start);
            Assert.AreEqual("Donderdag Kithix raid!", thursday.Note);
            Assert.AreEqual("Farm night, \"bring flasks\"", schedule.Raids[1].Note);
            Assert.AreEqual("", schedule.Raids[0].Note);
        }

        [TestMethod]
        public void ReadsWhoIsComing()
        {
            var thursday = Fixture(new DateTime(2026, 10, 19)).Raids.Single(r => r.Start == new DateTime(2026, 10, 15, 20, 0, 0));
            CollectionAssert.AreEqual(new[] { "Melanie", "Hans", "Patrick", "Rodrigo", "Jurre", "Kevin", "Jerry" }, thursday.Attending);
            Assert.AreEqual(11, thursday.Absent.Count);
            Assert.IsTrue(thursday.IsAttending("melanie").Value);
            Assert.IsFalse(thursday.IsAttending("Stefan").Value);
            Assert.IsNull(thursday.IsAttending("Nobody"));
            Assert.IsNull(thursday.IsAttending(null));
        }

        [TestMethod]
        public void YearGoesUpAfterDecember()
        {
            var raids = Fixture(new DateTime(2026, 10, 19)).Raids;
            Assert.AreEqual(new DateTime(2026, 7, 29, 20, 0, 0), raids.First().Start);
            Assert.AreEqual(new DateTime(2026, 12, 30, 20, 0, 0), raids[raids.Count - 2].Start);
            Assert.AreEqual(new DateTime(2027, 1, 6, 20, 0, 0), raids.Last().Start);
        }

        [TestMethod]
        public void YearFollowsToday()
        {
            // In January the October raids are last year's
            var raids = Fixture(new DateTime(2027, 1, 2)).Raids;
            Assert.AreEqual(new DateTime(2026, 10, 21, 20, 0, 0), raids.Single(r => r.Start.Month == 10 && r.Start.Day == 21).Start);
            Assert.AreEqual(2027, raids.Last().Start.Year);
        }

        [TestMethod]
        public void TakesCheckboxesAndOtherDateFormats()
        {
            var csv = "Note,Date,Anna,Bert\n" +
                      ",\"Wed 21 Oct\",TRUE,FALSE\n" +
                      "Extra,28 okt,1,\n" +
                      ",4-11-2026,0,1\n";
            var schedule = RaidSchedule.Parse(csv, new DateTime(2026, 10, 19));
            CollectionAssert.AreEqual(new[] { "Anna", "Bert" }, schedule.Players);
            Assert.AreEqual(3, schedule.Raids.Count);
            Assert.AreEqual(new DateTime(2026, 10, 28, 20, 0, 0), schedule.Raids[1].Start);
            Assert.IsNull(schedule.Raids[1].IsAttending("Bert"));
            Assert.AreEqual(new DateTime(2026, 11, 4, 20, 0, 0), schedule.Raids[2].Start);
            CollectionAssert.AreEqual(new[] { "Bert" }, schedule.Raids[2].Attending);
        }

        [TestMethod]
        public void SheetWithoutDatesIsRefused()
        {
            Assert.ThrowsException<InvalidDataException>(() => RaidSchedule.Parse("Name,Score\nAnna,10\n", DateTime.Today));
            Assert.ThrowsException<InvalidDataException>(() => RaidSchedule.Parse("", DateTime.Today));
        }

        [TestMethod]
        public void TurnsSheetLinksIntoCsvLinks()
        {
            const string id = "1djX8sRRq03jbTjrbD_8xz4ygfxvurO5ZG7nsDmxYla8";
            Assert.AreEqual("https://docs.google.com/spreadsheets/d/" + id + "/export?format=csv&gid=1137501992",
                RaidSchedule.ToCsvUrl("https://docs.google.com/spreadsheets/d/" + id + "/edit?gid=1137501992#gid=1137501992"));
            Assert.AreEqual("https://docs.google.com/spreadsheets/d/" + id + "/export?format=csv&gid=42",
                RaidSchedule.ToCsvUrl("  https://docs.google.com/spreadsheets/d/" + id + "/edit#gid=42 "));
            Assert.AreEqual("https://docs.google.com/spreadsheets/d/" + id + "/export?format=csv",
                RaidSchedule.ToCsvUrl("https://docs.google.com/spreadsheets/d/" + id + "/edit?usp=sharing"));
            Assert.AreEqual("https://docs.google.com/spreadsheets/d/e/2PACX-abc_1/pub?output=csv&gid=7",
                RaidSchedule.ToCsvUrl("https://docs.google.com/spreadsheets/d/e/2PACX-abc_1/pubhtml?gid=7&single=true"));
        }

        [TestMethod]
        public void OnlyFetchesGoogleSheets()
        {
            Assert.IsNull(RaidSchedule.ToCsvUrl("https://example.com/spreadsheets/d/abc"));
            Assert.IsNull(RaidSchedule.ToCsvUrl("https://docs.google.com.evil.example/spreadsheets/d/abc"));
            Assert.IsNull(RaidSchedule.ToCsvUrl("file:///C:/schedule.csv"));
            Assert.IsNull(RaidSchedule.ToCsvUrl(""));
            Assert.IsNull(RaidSchedule.ToCsvUrl(null));
        }

        [TestMethod]
        public void CsvHandlesQuotesAndLineBreaks()
        {
            var rows = Csv.Parse("\uFEFFa,\"b, c\",\"say \"\"hi\"\"\"\r\n\"two\nlines\",,end\nlast");
            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b, c", "say \"hi\"" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "two\nlines", "", "end" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "last" }, rows[2]);
        }
    }
}
