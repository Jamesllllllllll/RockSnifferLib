using RockSnifferLib.Sniffing;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class ArrangementTuningTests
{
    [Fact]
    public void SpellsSevenStringBFlatStandardWithGFlat()
    {
        var tuning = new ArrangementTuning(0, 0, 0, 0, 0, 0, 0, 0);
        tuning.SetExtendedRangeOffsets([-1, -1, -1, -1, -1, -1, -1]);

        Assert.Equal("Bb Eb Ab Db Gb Bb Eb", tuning.TuningName);
        Assert.Equal(7, tuning.ExtendedRangeStringCount);
    }

    [Fact]
    public void SpellsSixStringArrangementWithTheSameFlatPitchCollectionConsistently()
    {
        var tuning = new ArrangementTuning(-6, -6, -6, -6, -5, -6, 0, 0);

        Assert.Equal("BbEbAbDbGbBb", tuning.TuningName);
        Assert.Null(tuning.ExtendedRangeStringCount);
    }

    [Fact]
    public void KeepsFSharpForStandardEightStringTuning()
    {
        var tuning = new ArrangementTuning(0, 0, 0, 0, 0, 0, 0, 0);
        tuning.SetExtendedRangeOffsets([0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Equal("F# B E A D G B E", tuning.TuningName);
        Assert.Equal(8, tuning.ExtendedRangeStringCount);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void PreservesExtendedRangeBassStringCount(int count)
    {
        var tuning = new ArrangementTuning(0, 0, 0, 0, 0, 0, 0, 0) { IsBass = true };
        tuning.SetExtendedRangeOffsets(new short[count]);

        Assert.Equal(count, tuning.ExtendedRangeStringCount);
    }
}
