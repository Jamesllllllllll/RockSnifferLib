using RockSnifferLib.Configuration;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Profiles;
using Xunit;

namespace RockSnifferLib.Tests;

public class ProfileTrackerTests
{
    private static readonly ProfileIdentity First = new("profile-1", "Player One");
    private static readonly ProfileIdentity Second = new("profile-2", "Player Two");
    private static ProfileTracker Create() => new(new[] { First, Second });

    [Fact]
    public void HighlightIsNotLoginAndMainUsesTheLatestSelection()
    {
        var tracker = Create();
        var first = tracker.Update(0, "panel_bib", First.Name);
        Assert.Equal("selecting", first.State);
        Assert.Equal(First, first.SelectedProfile);
        Assert.Null(first.ObservedLoadedProfile);
        tracker.Update(.25, "panel_bib", Second.Name);
        var main = tracker.Update(.5, "main", First.Name); // Residual UI text is not authoritative.
        Assert.Equal("observed", main.State);
        Assert.Equal(Second, main.ObservedLoadedProfile);
        Assert.Null(main.SelectedProfileName);
        Assert.Null(main.SelectedProfile);
        Assert.Equal(Second, tracker.Update(.75, "main", null).ObservedLoadedProfile);
        Assert.Null(first.ObservedLoadedProfile); // Earlier immutable snapshots stay unchanged.
    }

    [Theory]
    [InlineData("Player One")]
    [InlineData("Player Two")]
    public void StartupReplayRetainsLoginAfterSelectionPointerDisappears(string name)
    {
        // Synthetic identities, with the observed quarter-second polling and
        // approximately six-second selection window from the restart controls.
        var tracker = Create();
        tracker.Update(0, "Notifications", null);
        tracker.Update(.25, "panel_bib", null);
        for (int i = 2; i <= 26; i++)
            Assert.Null(tracker.Update(i * .25, "panel_bib", name).ObservedLoadedProfile);
        Assert.Equal(name, tracker.Update(6.75, "main", name).ObservedLoadedProfile?.Name);
        Assert.Equal(name, tracker.Update(7, "main", null).ObservedLoadedProfile?.Name);
    }

    [Theory]
    [InlineData("titlescreen")]
    [InlineData("Notifications")]
    [InlineData("panel_bib")]
    [InlineData("profileselect")]
    [InlineData(null)]
    [InlineData("")]
    public void LogoutSelectionAndLostReadsClearLoadedIdentity(string? stage)
    {
        var tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        tracker.Update(.25, "main", null);
        var cleared = tracker.Update(.5, stage, null);
        Assert.Null(cleared.ObservedLoadedProfile);
        Assert.Null(tracker.Update(.75, "main", null).ObservedLoadedProfile);
    }

    [Theory]
    [InlineData("titlescreen")]
    [InlineData("loading")]
    [InlineData("Notifications")]
    [InlineData(null)]
    public void CancelledOrUnobservedTransitionCannotPromoteOldSelection(string? interveningStage)
    {
        var tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        tracker.Update(.25, interveningStage, First.Name);
        Assert.Null(tracker.Update(.5, "main", First.Name).ObservedLoadedProfile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("New profile")]
    [InlineData("player one")]
    [InlineData(" Player One")]
    public void UnknownOrUnreadableSelectionInvalidatesPreviousCandidate(string? name)
    {
        var tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        var unknown = tracker.Update(.25, "panel_bib", name);
        Assert.Equal(name, unknown.SelectedProfileName);
        Assert.Null(unknown.SelectedProfile);
        Assert.Null(tracker.Update(.5, "main", null).ObservedLoadedProfile);
    }

    [Fact]
    public void DuplicateNamesOrIdsRemainAmbiguous()
    {
        var tracker = new ProfileTracker(new[]
        {
            First, new ProfileIdentity("another-id", First.Name),
            Second, new ProfileIdentity(Second.Id, "Another Name"),
        });
        Assert.Null(tracker.Update(0, "panel_bib", First.Name).SelectedProfile);
        Assert.Null(tracker.Update(.25, "panel_bib", Second.Name).SelectedProfile);
        Assert.Null(tracker.Update(.5, "main", null).ObservedLoadedProfile);
    }

    [Fact]
    public void EmptyCatalogExposesTextWithoutInferringIdentity()
    {
        var tracker = new ProfileTracker(Array.Empty<ProfileIdentity>());
        Assert.Equal(First.Name, tracker.Update(0, "panel_bib", First.Name).SelectedProfileName);
        Assert.Null(tracker.Update(.25, "main", null).ObservedLoadedProfile);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ObservationDiscontinuityClearsCandidateAndLoadedIdentity(double nextAt)
    {
        var tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        Assert.Null(tracker.Update(nextAt, "main", null).ObservedLoadedProfile);

        tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        tracker.Update(.25, "main", null);
        Assert.Null(tracker.Update(nextAt, "main", null).ObservedLoadedProfile);
    }

    [Fact]
    public void LateAttachOrNewTrackerCannotRecoverIdentityFromResidualText()
    {
        var old = Create();
        old.Update(0, "panel_bib", First.Name);
        Assert.Equal(First, old.Update(.25, "main", null).ObservedLoadedProfile);
        var nextProcess = Create();
        Assert.Null(nextProcess.Update(0, "main", First.Name).ObservedLoadedProfile);
        Assert.Null(nextProcess.Update(.25, "las_options", First.Name).ObservedLoadedProfile);
    }

    [Fact]
    public void ObservedIdentityPersistsInMenusAndGameplayWithoutAssigningPlayerSlots()
    {
        var tracker = Create();
        tracker.Update(0, "profileselect", First.Name);
        tracker.Update(.25, "main", null);
        Assert.Equal(First, tracker.Update(.5, "las_options", null).ObservedLoadedProfile);
        Assert.Equal(First, tracker.Update(.75, "multiplayer", null).ObservedLoadedProfile);
    }

    [Fact]
    public void CatalogIsCapturedAtConstructionAndInvalidEntriesAreRejected()
    {
        var catalog = new[] { First };
        var tracker = new ProfileTracker(catalog);
        catalog[0] = Second;
        Assert.Equal(First, tracker.Update(0, "panel_bib", First.Name).SelectedProfile);
        Assert.Throws<ArgumentException>(() => new ProfileTracker(new[] { new ProfileIdentity("", "Name") }));
        Assert.Throws<ArgumentException>(() => new ProfileTracker(new[] { new ProfileIdentity("id", " ") }));
    }

    [Fact]
    public void ReadoutCopyAndJsonPreserveOptionalSnapshotAndDefaultRemainsOff()
    {
        Assert.False(new SnifferSettings().enableExperimentalProfiles);
        Assert.Empty(new SnifferSettings().experimentalProfileCatalog);
        Assert.Null(new RSMemoryReadout().experimentalProfiles);
        var tracker = Create();
        tracker.Update(0, "panel_bib", First.Name);
        var readout = new RSMemoryReadout { mode = RSMode.LEARNASONG,
            experimentalProfiles = tracker.Update(.25, "main", null) };
        var clone = readout.Clone();
        readout.experimentalProfiles = null;
        Assert.Equal(First, clone.experimentalProfiles?.ObservedLoadedProfile);
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(clone);
        var serializedMode = Newtonsoft.Json.Linq.JObject.Parse(json)["mode"]!;
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, serializedMode.Type);
        Assert.Equal(1, (int)serializedMode);
        var roundtrip = Newtonsoft.Json.JsonConvert.DeserializeObject<RSMemoryReadout>(json);
        Assert.Equal(clone.experimentalProfiles, roundtrip?.experimentalProfiles);

        var settings = new SnifferSettings { enableExperimentalProfiles = true,
            experimentalProfileCatalog = new[] { First } };
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<SnifferSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(settings));
        Assert.True(config!.enableExperimentalProfiles);
        Assert.Equal(First, Assert.Single(config.experimentalProfileCatalog));
    }
}
