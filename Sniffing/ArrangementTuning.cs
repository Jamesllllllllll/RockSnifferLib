using Rocksmith2014PsarcLib.Psarc.Models.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.Sniffing
{
    [Serializable]
    public class ArrangementTuning
    {
        private static readonly Dictionary<string, ArrangementTuning> _TuningNames = new Dictionary<string, ArrangementTuning>()
        {
            ["3G-E Standard"] = new ArrangementTuning(12, 12, 12, 12, 12, 12, -999, -999),
            ["2G-Eb Standard"] = new ArrangementTuning(11, 11, 11, 11, 11, 11, -999, -999),
            ["2G-D Standard"] = new ArrangementTuning(10, 10, 10, 10, 10, 10, -999, -999),
            ["2G-C# Standard"] = new ArrangementTuning(9, 9, 9, 9, 9, 9, -999, -999),
            ["2G-C Standard"] = new ArrangementTuning(8, 8, 8, 8, 8, 8, -999, -999),
            ["2G-B Standard"] = new ArrangementTuning(7, 7, 7, 7, 7, 7, -999, -999),
            ["2G-Bb Standard"] = new ArrangementTuning(6, 6, 6, 6, 6, 6, -999, -999),
            ["2G-A Standard"] = new ArrangementTuning(5, 5, 5, 5, 5, 5, -999, -999),
            ["2G-Ab Standard"] = new ArrangementTuning(4, 4, 4, 4, 4, 4, -999, -999),
            ["2G-G Standard"] = new ArrangementTuning(3, 3, 3, 3, 3, 3, -999, -999),
            ["2G-F# Standard"] = new ArrangementTuning(2, 2, 2, 2, 2, 2, -999, -999),
            ["2G-F Standard"] = new ArrangementTuning(1, 1, 1, 1, 1, 1, -999, -999),
            ["2G-E Standard"] = new ArrangementTuning(0, 0, 0, 0, 0, 0, -999, -999),
            ["1G-Eb Standard"] = new ArrangementTuning(-1, -1, -1, -1, -1, -1, -999, -999),
            ["1G-D Standard"] = new ArrangementTuning(-2, -2, -2, -2, -2, -2, -999, -999),
            ["1G-C# Standard"] = new ArrangementTuning(-3, -3, -3, -3, -3, -3, -999, -999),
            ["1G-C Standard"] = new ArrangementTuning(-4, -4, -4, -4, -4, -4, -999, -999),
            ["1G-B Standard"] = new ArrangementTuning(-5, -5, -5, -5, -5, -5, -999, -999),
            ["1G-Bb Standard"] = new ArrangementTuning(-6, -6, -6, -6, -6, -6, -999, -999),
            ["1G-A Standard"] = new ArrangementTuning(-7, -7, -7, -7, -7, -7, -999, -999),
            ["1G-Ab Standard"] = new ArrangementTuning(-8, -8, -8, -8, -8, -8, -999, -999),
            ["1G-G Standard"] = new ArrangementTuning(-9, -9, -9, -9, -9, -9, -999, -999),
            ["1G-F# Standard"] = new ArrangementTuning(-10, -10, -10, -10, -10, -10, -999, -999),
            ["1G-F Standard"] = new ArrangementTuning(-11, -11, -11, -11, -11, -11, -999, -999),
            ["1G-E Standard"] = new ArrangementTuning(-12, -12, -12, -12, -12, -12, -999, -999),

            ["3B-E Standard"] = new ArrangementTuning(12, 12, 12, 12, 0, 0, -999, -999),
            ["2B-Eb Standard"] = new ArrangementTuning(11, 11, 11, 11, 0, 0, -999, -999),
            ["2B-D Standard"] = new ArrangementTuning(10, 10, 10, 10, 0, 0, -999, -999),
            ["2B-C# Standard"] = new ArrangementTuning(9, 9, 9, 9, 0, 0, -999, -999),
            ["2B-C Standard"] = new ArrangementTuning(8, 8, 8, 8, 0, 0, -999, -999),
            ["2B-B Standard"] = new ArrangementTuning(7, 7, 7, 7, 0, 0, -999, -999),
            ["2B-Bb Standard"] = new ArrangementTuning(6, 6, 6, 6, 0, 0, -999, -999),
            ["2B-A Standard"] = new ArrangementTuning(5, 5, 5, 5, 0, 0, -999, -999),
            ["2B-Ab Standard"] = new ArrangementTuning(4, 4, 4, 4, 0, 0, -999, -999),
            ["2B-G Standard"] = new ArrangementTuning(3, 3, 3, 3, 0, 0, -999, -999),
            ["2B-F# Standard"] = new ArrangementTuning(2, 2, 2, 2, 0, 0, -999, -999),
            ["2B-F Standard"] = new ArrangementTuning(1, 1, 1, 1, 0, 0, -999, -999),
            ["1B-Eb Standard"] = new ArrangementTuning(-1, -1, -1, -1, 0, 0, -999, -999),
            ["1B-D Standard"] = new ArrangementTuning(-2, -2, -2, -2, 0, 0, -999, -999),
            ["1B-C# Standard"] = new ArrangementTuning(-3, -3, -3, -3, 0, 0, -999, -999),
            ["1B-C Standard"] = new ArrangementTuning(-4, -4, -4, -4, 0, 0, -999, -999),
            ["1B-B Standard"] = new ArrangementTuning(-5, -5, -5, -5, 0, 0, -999, -999),
            ["1B-Bb Standard"] = new ArrangementTuning(-6, -6, -6, -6, 0, 0, -999, -999),
            ["1B-A Standard"] = new ArrangementTuning(-7, -7, -7, -7, 0, 0, -999, -999),
            ["1B-Ab Standard"] = new ArrangementTuning(-8, -8, -8, -8, 0, 0, -999, -999),
            ["1B-G Standard"] = new ArrangementTuning(-9, -9, -9, -9, 0, 0, -999, -999),
            ["1B-F# Standard"] = new ArrangementTuning(-10, -10, -10, -10, 0, 0, -999, -999),
            ["1B-F Standard"] = new ArrangementTuning(-11, -11, -11, -11, 0, 0, -999, -999),
            ["1B-E Standard"] = new ArrangementTuning(-12, -12, -12, -12, 0, 0, -999, -999),

            ["3G-F# Drop E"] = new ArrangementTuning(12, 14, 14, 14, 14, 14, -999, -999),
            ["3G-F Drop Eb"] = new ArrangementTuning(11, 13, 13, 13, 13, 13, -999, -999),
            ["3G-Drop D"] = new ArrangementTuning(10, 12, 12, 12, 12, 12, -999, -999),
            ["2G-Eb Drop Db"] = new ArrangementTuning(9, 11, 11, 11, 11, 11, -999, -999),
            ["2G-D Drop C"] = new ArrangementTuning(8, 10, 10, 10, 10, 10, -999, -999),
            ["2G-C# Drop B"] = new ArrangementTuning(7, 9, 9, 9, 9, 9, -999, -999),
            ["2G-C Drop Bb"] = new ArrangementTuning(6, 8, 8, 8, 8, 8, -999, -999),
            ["2G-B Drop A"] = new ArrangementTuning(5, 7, 7, 7, 7, 7, -999, -999),
            ["2G-Bb Drop Ab"] = new ArrangementTuning(4, 6, 6, 6, 6, 6, -999, -999),
            ["2G-A Drop G"] = new ArrangementTuning(3, 5, 5, 5, 5, 5, -999, -999),
            ["2G-Ab Drop F#"] = new ArrangementTuning(2, 4, 4, 4, 4, 4, -999, -999),
            ["2G-G Drop F"] = new ArrangementTuning(1, 3, 3, 3, 3, 3, -999, -999),
            ["2G-F# Drop E"] = new ArrangementTuning(0, 2, 2, 2, 2, 2, -999, -999),
            ["2G-F Drop Eb"] = new ArrangementTuning(-1, 1, 1, 1, 1, 1, -999, -999),
            ["2G-Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, 0, -999, -999),
            ["1G-Eb Drop Db"] = new ArrangementTuning(-3, -1, -1, -1, -1, -1, -999, -999),
            ["1G-D Drop C"] = new ArrangementTuning(-4, -2, -2, -2, -2, -2, -999, -999),
            ["1G-C# Drop B"] = new ArrangementTuning(-5, -3, -3, -3, -3, -3, -999, -999),
            ["1G-C Drop Bb"] = new ArrangementTuning(-6, -4, -4, -4, -4, -4, -999, -999),
            ["1G-B Drop A"] = new ArrangementTuning(-7, -5, -5, -5, -5, -5, -999, -999),
            ["1G-Bb Drop Ab"] = new ArrangementTuning(-8, -6, -6, -6, -6, -6, -999, -999),
            ["1G-A Drop G"] = new ArrangementTuning(-9, -7, -7, -7, -7, -7, -999, -999),
            ["1G-Ab Drop F#"] = new ArrangementTuning(-10, -8, -8, -8, -8, -8, -999, -999),
            ["1G-G Drop F"] = new ArrangementTuning(-11, -9, -9, -9, -9, -9, -999, -999),
            ["1G-F# Drop E"] = new ArrangementTuning(-12, -10, -10, -10, -10, -10, -999, -999),
            ["1G-F Drop Eb"] = new ArrangementTuning(-13, -11, -11, -11, -11, -11, -999, -999),
            ["1G-Drop D"] = new ArrangementTuning(-14, -12, -12, -12, -12, -12, -999, -999),

            ["3B-F# Drop E"] = new ArrangementTuning(12, 14, 14, 14, 0, 0, -999, -999),
            ["3B-F Drop Eb"] = new ArrangementTuning(11, 13, 13, 13, 0, 0, -999, -999),
            ["3B-Drop D"] = new ArrangementTuning(10, 12, 12, 12, 0, 0, -999, -999),
            ["2B-Eb Drop Db"] = new ArrangementTuning(9, 11, 11, 11, 0, 0, -999, -999),
            ["2B-D Drop C"] = new ArrangementTuning(8, 10, 10, 10, 0, 0, -999, -999),
            ["2B-C# Drop B"] = new ArrangementTuning(7, 9, 9, 9, 0, 0, -999, -999),
            ["2B-C Drop Bb"] = new ArrangementTuning(6, 8, 8, 8, 0, 0, -999, -999),
            ["2B-B Drop A"] = new ArrangementTuning(5, 7, 7, 7, 0, 0, -999, -999),
            ["2B-Bb Drop Ab"] = new ArrangementTuning(4, 6, 6, 6, 0, 0, -999, -999),
            ["2B-A Drop G"] = new ArrangementTuning(3, 5, 5, 5, 0, 0, -999, -999),
            ["2B-Ab Drop F#"] = new ArrangementTuning(2, 4, 4, 4, 0, 0, -999, -999),
            ["2B-G Drop F"] = new ArrangementTuning(1, 3, 3, 3, 0, 0, -999, -999),
            ["2B-F# Drop E"] = new ArrangementTuning(0, 2, 2, 2, 0, 0, -999, -999),
            ["2B-F Drop Eb"] = new ArrangementTuning(-1, 1, 1, 1, 0, 0, -999, -999),
            ["2B-Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, 0, -999, -999),
            ["1B-Eb Drop Db"] = new ArrangementTuning(-3, -1, -1, -1, 0, 0, -999, -999),
            ["1B-D Drop C"] = new ArrangementTuning(-4, -2, -2, -2, 0, 0, -999, -999),
            ["1B-C# Drop B"] = new ArrangementTuning(-5, -3, -3, -3, 0, 0, -999, -999),
            ["1B-C Drop Bb"] = new ArrangementTuning(-6, -4, -4, -4, 0, 0, -999, -999),
            ["1B-B Drop A"] = new ArrangementTuning(-7, -5, -5, -5, 0, 0, -999, -999),
            ["1B-Bb Drop Ab"] = new ArrangementTuning(-8, -6, -6, -6, 0, 0, -999, -999),
            ["1B-A Drop G"] = new ArrangementTuning(-9, -7, -7, -7, 0, 0, -999, -999),
            ["1B-Ab Drop F#"] = new ArrangementTuning(-10, -8, -8, -8, 0, 0, -999, -999),
            ["1B-G Drop F"] = new ArrangementTuning(-11, -9, -9, -9, 0, 0, -999, -999),
            ["1B-F# Drop E"] = new ArrangementTuning(-12, -10, -10, -10, 0, 0, -999, -999),
            ["1B-F Drop Eb"] = new ArrangementTuning(-13, -11, -11, -11, 0, 0, -999, -999),
            ["1B-Drop D"] = new ArrangementTuning(-14, -12, -12, -12, 0, 0, -999, -999),

            ["Open A"] = new ArrangementTuning(0, 0, 2, 2, 2, 0, -999, -999),
            ["Open B"] = new ArrangementTuning(-5, -3, -3, -1, 0, -1, -999, -999),
            ["Open C"] = new ArrangementTuning(-4, -2, -2, 0, 1, 0, -999, -999),
            ["Open D"] = new ArrangementTuning(-2, 0, 0, -1, -2, -2, -999, -999),
            ["Open E"] = new ArrangementTuning(0, 2, 2, 1, 0, 0, -999, -999),
            ["Open F"] = new ArrangementTuning(-4, -4, -2, -2, -2, 1, -999, -999),
            ["Open G"] = new ArrangementTuning(-2, -2, 0, 0, 0, -2, -999, -999),

            ["DADGAD"] = new ArrangementTuning(-2, 0, 0, 0, -2, -2, -999, -999),
            ["Minor Third"] = new ArrangementTuning(-4, -6, -8, -10, -11, -13, -999, -999),
            ["Major Third"] = new ArrangementTuning(-8, -9, -10, -11, -11, -12, -999, -999),
            ["Fourths"] = new ArrangementTuning(0, 0, 0, 0, 1, 1, -999, -999),
            ["Aug Fourths"] = new ArrangementTuning(-4, -3, -2, -1, 1, 2, -999, -999),
            ["Fifths"] = new ArrangementTuning(-4, -2, 0, 2, 5, 7, -999, -999),
            ["Double Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, -2, -999, -999),
            ["Nick Drake"] = new ArrangementTuning(-4, -2, -2, -2, 1, 0, -999, -999),
            ["C6 Modal"] = new ArrangementTuning(-4, 0, -2, 0, 1, 0, -999, -999),
        };

        // Positional MIDI pitches, thickest string first. Persist them with the cached arrangement.
        public int[]? OpenStringPitches { get; set; }

        internal void SetExtendedRangePitches(bool isBass, int stringCount, short[] offsets, bool legacy)
        {
            int[] standardPitches = (isBass, stringCount) switch
            {
                (false, 7) => [35, 40, 45, 50, 55, 59, 64],
                (false, 8) => [30, 35, 40, 45, 50, 55, 59, 64],
                (true, 5) => [23, 28, 33, 38, 43],
                (true, 6) => [23, 28, 33, 38, 43, 48],
                _ => []
            };
            if (standardPitches.Length == 0) return;

            if (legacy)
            {
                // RS7X keeps stock strings in their original slots and appends extra strings.
                offsets = (isBass, stringCount) switch
                {
                    (false, 7) => [offsets[6], .. offsets[..6]],
                    (false, 8) => [offsets[7], offsets[6], .. offsets[..6]],
                    (true, 5) => [offsets[4], .. offsets[..4]],
                    (true, 6) => [offsets[4], .. offsets[..4], offsets[5]],
                    _ => offsets
                };
            }
            OpenStringPitches = standardPitches.Select((pitch, index) => pitch + offsets[index]).ToArray();
        }

        private static readonly string[] NoteNames = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

        private static string FormatExtendedTuning(int[] pitches)
        {
            var names = pitches.Select(pitch => NoteNames[((pitch % 12) + 12) % 12]).ToArray();
            var useGFlat = names.Contains("Bb") && names.Contains("Eb") && names.Contains("Ab") && names.Contains("Db");
            return string.Join(" ", names.Select(name => useGFlat && name == "F#" ? "Gb" : name));
        }

        public string TuningName
        {
            get
            {
                string name;
                if (OpenStringPitches is { Length: > 0 } pitches)
                {
                    name = FormatExtendedTuning(pitches);
                }
                else
                {
                    var match = _TuningNames.FirstOrDefault(kvp => kvp.Value.Equals(this));
                    name = match.Key?.Replace("1G-", "").Replace("2G-", "").Replace("3G-", "")
                        .Replace("1B-", "").Replace("2B-", "").Replace("3B-", "") ?? "Custom Tuning";
                }

                //Calculate estimated hz offset from cents offset, if nonzero
                if (CentsOffset != 0)
                {
                    name = $"{name}: A{Math.Floor(440d * Math.Pow(2d, CentsOffset / 1200d))}";
                }

                //Add capo fret
                if (CapoFret != 0)
                {
                    name = $"{name} (Capo Fret {CapoFret})";
                }

                return name;
            }
        }

        /// <summary>
        /// Invalid tuning
        /// </summary>
        public ArrangementTuning()
        {
            String0 = -999;
            String1 = -999;
            String2 = -999;
            String3 = -999;
            String4 = -999;
            String5 = -999;
            CentsOffset = -999;
            CapoFret = -999;
        }

        public ArrangementTuning(int string0, int string1, int string2, int string3, int string4, int string5, int centsOffset, int capoFret)
        {
            String0 = string0;
            String1 = string1;
            String2 = string2;
            String3 = string3;
            String4 = string4;
            String5 = string5;
            CentsOffset = centsOffset;
            CapoFret = capoFret;
        }

        public ArrangementTuning(SongArrangement.ArrangementAttributes.ArrangementTuning tuning, int centsOffset, int capoFret)
        {
            String0 = tuning.String0;
            String1 = tuning.String1;
            String2 = tuning.String2;
            String3 = tuning.String3;
            String4 = tuning.String4;
            String5 = tuning.String5;
            CentsOffset = centsOffset;
            CapoFret = capoFret;
        }

        public int CentsOffset;
        public int CapoFret;
        public int String0;
        public int String1;
        public int String2;
        public int String3;
        public int String4;
        public int String5;

        /// <summary>
        /// Check if two tunings are equal, capo fret and cents offset are ignored
        /// </summary>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (obj is ArrangementTuning)
            {
                var t = obj as ArrangementTuning;
                return t.String0 == String0 &&
                    t.String1 == String1 &&
                    t.String2 == String2 &&
                    t.String3 == String3 &&
                    t.String4 == String4 &&
                    t.String5 == String5;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
