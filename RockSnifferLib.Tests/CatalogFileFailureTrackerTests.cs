using RockSnifferLib.Sniffing;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class CatalogFileFailureTrackerTests
{
    [Fact]
    public void ReportsLocalFileNamesReasonsAndCounts()
    {
        var tracker = new CatalogFileFailureTracker();

        tracker.RecordFailure(
            Path.Combine("C:\\Rocksmith2014", "dlc", "first_p.psarc"),
            CatalogFileFailureReasons.ReadFailed
        );
        tracker.RecordFailure(
            Path.Combine("C:\\Rocksmith2014", "dlc", "second_p.psarc"),
            CatalogFileFailureReasons.HashFailed
        );

        Assert.Equal(2, tracker.Count);
        Assert.Collection(
            tracker.GetFailures(),
            failure =>
            {
                Assert.Equal("first_p.psarc", failure.fileName);
                Assert.Equal(CatalogFileFailureReasons.ReadFailed, failure.reason);
                Assert.NotEmpty(failure.message);
            },
            failure =>
            {
                Assert.Equal("second_p.psarc", failure.fileName);
                Assert.Equal(CatalogFileFailureReasons.HashFailed, failure.reason);
            }
        );
        Assert.Equal(1, tracker.GetReasonCounts()[CatalogFileFailureReasons.ReadFailed]);
        Assert.Equal(1, tracker.GetReasonCounts()[CatalogFileFailureReasons.HashFailed]);
    }

    [Fact]
    public void SuccessfulRetryRemovesTheCurrentFailure()
    {
        var tracker = new CatalogFileFailureTracker();
        const string path = "C:\\Rocksmith2014\\dlc\\retry_p.psarc";

        tracker.RecordFailure(path, CatalogFileFailureReasons.NotReady);
        tracker.RecordSuccess(path);

        Assert.Equal(0, tracker.Count);
        Assert.Empty(tracker.GetFailures());
        Assert.Empty(tracker.GetReasonCounts());
    }

    [Fact]
    public void RepeatedFailuresReplaceTheReasonInsteadOfInflatingTheCount()
    {
        var tracker = new CatalogFileFailureTracker();
        const string path = "C:\\Rocksmith2014\\dlc\\changing_p.psarc";

        tracker.RecordFailure(path, CatalogFileFailureReasons.NotReady);
        tracker.RecordFailure(path, CatalogFileFailureReasons.ChangedDuringScan);

        Assert.Equal(1, tracker.Count);
        Assert.Equal(
            CatalogFileFailureReasons.ChangedDuringScan,
            Assert.Single(tracker.GetFailures()).reason
        );
    }

    [Fact]
    public void ReportedDetailsAreBoundedButAggregateCountsRemainComplete()
    {
        var tracker = new CatalogFileFailureTracker();
        for (var index = 0; index < CatalogFileFailureTracker.MaxReportedFailures + 5; index++)
        {
            tracker.RecordFailure(
                $"C:\\Rocksmith2014\\dlc\\chart-{index:D3}_p.psarc",
                CatalogFileFailureReasons.ReadFailed
            );
        }

        Assert.Equal(CatalogFileFailureTracker.MaxReportedFailures + 5, tracker.Count);
        Assert.Equal(CatalogFileFailureTracker.MaxReportedFailures, tracker.GetFailures().Count);
        Assert.True(tracker.IsTruncated);
        Assert.Equal(
            CatalogFileFailureTracker.MaxReportedFailures + 5,
            tracker.GetReasonCounts()[CatalogFileFailureReasons.ReadFailed]
        );
    }
}
