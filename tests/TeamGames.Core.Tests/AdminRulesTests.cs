using TeamGames.Core.Administration;
using TeamGames.Core.Rooms;
using TeamGames.Core.Games;

namespace TeamGames.Core.Tests;

public sealed class AdminRulesTests
{
    [Fact]
    public void ConfigurationFailsClosedAndCredentialComparisonIsExact()
    {
        var settings = new AdminSettings();
        Assert.False(settings.Enabled); Assert.False(settings.Accepts("anything")); settings.Validate();
        settings.Credential = "a-long-administrator-token";
        Assert.True(settings.Accepts(settings.Credential)); Assert.False(settings.Accepts(settings.Credential.ToUpperInvariant())); Assert.False(settings.Accepts(null));
        settings.Credential = "short"; Assert.Throws<InvalidOperationException>(settings.Validate);
        settings.Credential = null; settings.SessionMinutes = 0; Assert.Throws<InvalidOperationException>(settings.Validate);
    }
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1000001, 20)]
    public void PaginationIsBounded(int page, int size) => Assert.Throws<RoomException>(() => AdminRules.Pagination(page, size));
    [Fact]
    public void SharedManagementRulesKeepAFacilitatorAndRequireVersionAndDeletionConfirmation()
    {
        var room = new Room { Code = "ABC234" };
        var host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20); room.FacilitatorId = host.Id;
        var player = RoomRules.Join(room, Guid.NewGuid(), "Player", 20);
        Assert.Throws<RoomException>(() => RoomRules.ApplyManagement(room, new(Guid.NewGuid(), room.Version, "remove", host.Id), 1));
        RoomRules.ApplyManagement(room, new(Guid.NewGuid(), room.Version, "transfer", player.Id), 2);
        RoomRules.ApplyManagement(room, new(Guid.NewGuid(), room.Version, "remove", host.Id), 3);
        Assert.True(host.Removed); Assert.Equal(player.Id, room.FacilitatorId);
        Assert.Throws<RoomException>(() => RoomRules.ApplyManagement(room, new(Guid.NewGuid(), 1, "lock", Locked: true), 4));
        Assert.Throws<RoomException>(() => AdminRules.ConfirmDeletion(room, new(Guid.NewGuid(), room.Version, "delete", Confirmation: "wrong")));
        AdminRules.ConfirmDeletion(room, new(Guid.NewGuid(), room.Version, "delete", Confirmation: room.Code));
    }
    [Theory]
    [InlineData("this-or-that")]
    [InlineData("would-you-rather")]
    [InlineData("never-have-i-ever")]
    [InlineData("most-likely-to")]
    [InlineData("two-truths-and-a-lie")]
    public void EveryModuleHasStatusOnlyInspectionAndAdministrativeFinish(string id)
    {
        var random = new GameRandom();
        var modules = new GameModules([new ThisOrThat(random), new WouldYouRather(random), new NeverHaveIEver(random), new MostLikelyTo(random), new TwoTruthsAndALie()]);
        var room = new Room { Id = Guid.NewGuid() }; var host = RoomRules.Join(room, Guid.NewGuid(), "Host", 20); room.FacilitatorId = host.Id;
        room.PromptPool = TeamGames.Core.Prompts.PromptLibrary.All;
        modules.Apply(room, host, new(Guid.NewGuid(), "start", GameId: id, ExpectedVersion: room.Version), 1);
        Assert.Equal(id, Assert.Single(modules.Describe(room)).GameId);
        modules.FinishAdministrative(room, 2);
        Assert.Equal("finished", Assert.Single(modules.Describe(room)).Stage);
        Assert.All(modules.Project(room, host)!.Results, r => { Assert.True(r.Skipped); Assert.Null(r.LieIndex); Assert.Empty(r.Choices); });
        Assert.Throws<RoomException>(() => modules.FinishAdministrative(room, 3));
    }
}
