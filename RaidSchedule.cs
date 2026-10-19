using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BPGPUpdater
{
    /// <summary>One raid night from the schedule sheet</summary>
    internal sealed class Raid
    {
        public DateTime Start { get; set; }

        public DateTime End
        {
            get { return Start + RaidSchedule.Length; }
        }

        /// <summary>The text next to the date, e.g. "Skip + Ulatek kill"; empty when there's none</summary>
        public string Note { get; set; }
        /// <summary>Players with a 1 in this row</summary>
        public List<string> Attending { get; set; }
        /// <summary>Players with a 0 in this row</summary>
        public List<string> Absent { get; set; }

        /// <summary>True for a 1, false for a 0, null when the name isn't in the sheet or its cell is empty</summary>
        public bool? IsAttending(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Attending.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
            if (Absent.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            return null;
        }
    }

    /// <summary>
    /// The guild's raid schedule, a Google Sheet read through its CSV export. The sheet is a grid: one row per
    /// raid with a note, the date ("7 Oct", without a year) and a count, then one column per player with 1
    /// (coming) or 0 (not coming). The player names are in a header row above the raids. Nothing depends on
    /// fixed column letters, so adding a player or a spacer column doesn't break it.
    /// </summary>
    internal sealed class RaidSchedule
    {
        /// <summary>The sheet only has dates: raids start at 20:00 local time</summary>
        public static readonly TimeSpan StartTime = new TimeSpan(20, 0, 0);
        /// <summary>How long a raid counts as "now" after it starts</summary>
        public static readonly TimeSpan Length = TimeSpan.FromHours(3);

        private static readonly Regex SheetLink = new Regex(@"^https?://docs\.google\.com/spreadsheets/d/(e/)?([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        private static readonly Regex SheetTab = new Regex(@"[?#&]gid=(\d+)");
        // "7 Oct", "7-okt", "Wed 7 Oct", "7 October 2026"
        private static readonly Regex NamedDate = new Regex(@"^(?:[a-z]+\.?,?\s+)?(\d{1,2})[\s.\-/]*([a-z]+)\.?(?:[\s.,\-/]+(\d{4}))?$", RegexOptions.IgnoreCase);
        // "7-10-2026" or "7/10": day first
        private static readonly Regex NumericDate = new Regex(@"^(\d{1,2})[.\-/](\d{1,2})(?:[.\-/](\d{4}))?$");
        private static readonly Regex IsoDate = new Regex(@"^(\d{4})-(\d{1,2})-(\d{1,2})$");
        private static readonly Dictionary<string, int> Months = MonthNames();

        /// <summary>Everyone with a column in the sheet, left to right</summary>
        public List<string> Players { get; private set; }
        /// <summary>Every raid in the sheet, past ones too, in sheet order</summary>
        public List<Raid> Raids { get; private set; }

        /// <summary>
        /// The CSV export URL for a Google Sheets link (the address bar while editing, a share link, or a
        /// "Publish to the web" link), keeping the tab (gid). Null when it isn't a Google Sheets link, so no
        /// other address is ever fetched.
        /// </summary>
        public static string ToCsvUrl(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            var match = SheetLink.Match(link.Trim());
            if (!match.Success) return null;
            var tab = SheetTab.Match(link);
            var gid = tab.Success ? "&gid=" + tab.Groups[1].Value : "";
            return match.Groups[1].Success
                ? "https://docs.google.com/spreadsheets/d/e/" + match.Groups[2].Value + "/pub?output=csv" + gid
                : "https://docs.google.com/spreadsheets/d/" + match.Groups[2].Value + "/export?format=csv" + gid;
        }

        /// <param name="today">Picks the year: the sheet's dates have none</param>
        public static RaidSchedule Parse(string csv, DateTime today)
        {
            var rows = Csv.Parse(csv);

            // The date column: the column where most rows have a date
            var dateColumn = rows
                .Select(r => Array.FindIndex(r, c => ParseDate(c) != null))
                .Where(i => i >= 0)
                .GroupBy(i => i)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => (int?)g.Key)
                .FirstOrDefault();
            if (dateColumn == null) throw new InvalidDataException("The raid schedule has no dates (like \"21 Oct\") in it.");
            int dates = dateColumn.Value;
            var raidRows = rows.Where(r => ParseDate(Cell(r, dates)) != null).ToList();
            int firstRaidRow = rows.IndexOf(raidRows[0]);

            // Player columns hold 1 and 0 in the raid rows; the count and spacer columns don't
            int width = rows.Max(r => r.Length);
            var markColumns = Enumerable.Range(dates + 1, Math.Max(0, width - dates - 1)).Where(c => IsMarkColumn(raidRows, c)).ToList();
            // The names: the header row above the raids with the most text in those columns
            var header = rows.Take(firstRaidRow)
                .OrderByDescending(r => markColumns.Count(c => Cell(r, c).Trim().Length > 0))
                .FirstOrDefault();
            var players = markColumns
                .Select(c => new { Column = c, Name = header != null ? PlayerName(Cell(header, c)) : "" })
                .Where(p => p.Name.Length > 0)
                .ToList();

            var days = raidRows.Select(r => ParseDate(Cell(r, dates))).ToList();
            var starts = AssignYears(days, today);
            var raids = new List<Raid>();
            for (int i = 0; i < raidRows.Count; i++)
            {
                if (starts[i] == null) continue; // e.g. 29 Feb in a year without it
                var row = raidRows[i];
                raids.Add(new Raid
                {
                    Start = starts[i].Value + StartTime,
                    Note = string.Join(" ", row.Take(dates).Select(c => c.Trim()).Where(c => c.Length > 0)),
                    Attending = players.Where(p => Mark(Cell(row, p.Column)) == true).Select(p => p.Name).ToList(),
                    Absent = players.Where(p => Mark(Cell(row, p.Column)) == false).Select(p => p.Name).ToList(),
                });
            }
            return new RaidSchedule { Players = players.Select(p => p.Name).ToList(), Raids = raids };
        }

        private static string Cell(string[] row, int column)
        {
            return column < row.Length ? row[column] : "";
        }

        /// <summary>1/0, or a checkbox's TRUE/FALSE</summary>
        private static bool? Mark(string cell)
        {
            switch (cell.Trim().ToUpperInvariant())
            {
                case "1":
                case "TRUE": return true;
                case "0":
                case "FALSE": return false;
                default: return null;
            }
        }

        private static bool IsMarkColumn(List<string[]> raidRows, int column)
        {
            int marks = 0, other = 0;
            foreach (var row in raidRows)
            {
                var cell = Cell(row, column).Trim();
                if (Mark(cell) != null) marks++;
                else if (cell.Length > 0) other++;
            }
            return marks > 0 && other * 4 <= marks;
        }

        /// <summary>The last line of the header cell: a role label ("Tanks") may sit above the name</summary>
        private static string PlayerName(string cell)
        {
            return cell.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        }

        private sealed class SheetDate
        {
            public int Day;
            public int Month;
            public int? Year;
        }

        private static SheetDate ParseDate(string cell)
        {
            var text = cell.Trim();
            if (text.Length == 0) return null;
            var named = NamedDate.Match(text);
            if (named.Success)
            {
                int month;
                if (!Months.TryGetValue(named.Groups[2].Value, out month)) return null;
                return Valid(int.Parse(named.Groups[1].Value), month, named.Groups[3].Success ? int.Parse(named.Groups[3].Value) : (int?)null);
            }
            var numeric = NumericDate.Match(text);
            if (numeric.Success)
                return Valid(int.Parse(numeric.Groups[1].Value), int.Parse(numeric.Groups[2].Value), numeric.Groups[3].Success ? int.Parse(numeric.Groups[3].Value) : (int?)null);
            var iso = IsoDate.Match(text);
            if (iso.Success)
                return Valid(int.Parse(iso.Groups[3].Value), int.Parse(iso.Groups[2].Value), int.Parse(iso.Groups[1].Value));
            return null;
        }

        private static SheetDate Valid(int day, int month, int? year)
        {
            if (month < 1 || month > 12 || day < 1 || day > 31) return null;
            return new SheetDate { Day = day, Month = month, Year = year };
        }

        /// <summary>
        /// The rows run in date order, so the year goes up whenever the month goes back (Dec → Jan). The
        /// starting year is the one that puts a raid closest to today.
        /// </summary>
        private static List<DateTime?> AssignYears(List<SheetDate> days, DateTime today)
        {
            var offsets = new int[days.Count];
            int offset = 0, previousMonth = 0;
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i].Month < previousMonth) offset++;
                previousMonth = days[i].Month;
                offsets[i] = offset;
            }

            int bestYear = today.Year;
            double best = double.MaxValue;
            for (int year = today.Year - 1; year <= today.Year + 1; year++)
            {
                double nearest = days
                    .Select((d, i) => ToDate(d, d.Year ?? year + offsets[i]))
                    .Where(d => d != null)
                    .Select(d => Math.Abs((d.Value - today.Date).TotalDays))
                    .DefaultIfEmpty(double.MaxValue)
                    .Min();
                if (nearest < best)
                {
                    best = nearest;
                    bestYear = year;
                }
            }
            return days.Select((d, i) => ToDate(d, d.Year ?? bestYear + offsets[i])).ToList();
        }

        private static DateTime? ToDate(SheetDate date, int year)
        {
            if (year < 1 || year > 9999 || date.Day > DateTime.DaysInMonth(year, date.Month)) return null;
            return new DateTime(year, date.Month, date.Day);
        }

        private static Dictionary<string, int> MonthNames()
        {
            // English and Dutch, abbreviated and in full
            var names = new[]
            {
                "jan january januari", "feb february februari", "mar march mrt maart", "apr april", "may mei",
                "jun june juni", "jul july juli", "aug august augustus", "sep sept september", "oct october okt oktober",
                "nov november", "dec december",
            };
            var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++)
            {
                foreach (var name in names[i].Split(' ')) months[name] = i + 1;
            }
            return months;
        }
    }

    /// <summary>Comma-separated values as Google Sheets exports them (RFC 4180: quotes, "" and line breaks in quotes)</summary>
    internal static class Csv
    {
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            int i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"') field.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"') field.Append(text[++i]);
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }
    }
}
