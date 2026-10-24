using TeamGames.Core.Rooms;

namespace TeamGames.Core.Tests;

public sealed class RoomRulesTests
{
    private readonly Room room = new() { Id = Guid.NewGuid(), Code = "ABC234", LastActivityTicks = 100 };
    private readonly Guid host = Guid.NewGuid();
    private Membership Host()
    {
        var actor = RoomRules.Join(room, host, "Host", 20);
        room.FacilitatorId = actor.Id;
        return actor;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a\nb")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void InvalidNamesAreRejected(string? name) => Assert.Equal("invalid_name", Assert.Throws<RoomException>(() => RoomRules.DisplayName(name)).Code);

    [Fact]
    public void NamesAreTrimmedAndUnicodeNormalized() => Assert.Equal("Alex", RoomRules.DisplayName("  Ａlex  "));

    [Theory]
    [InlineData("../../")]
    [InlineData("ABC123")]
    [InlineData("")]
    public void InvalidInvitesAreRejected(string code) => Assert.Throws<RoomException>(() => RoomRules.InviteCode(code));

    [Fact]
    public void CodesIgnoreCaseAndSurroundingWhitespace() => Assert.Equal("ABC234", RoomRules.InviteCode(" abc234 "));

    [Fact]
    public void JoiningEnforcesCapacityAndCaseInsensitiveNames()
    {
        Host();
        Assert.Equal("name_taken", Assert.Throws<RoomException>(() => RoomRules.Join(room, Guid.NewGuid(), "HOST", 2)).Code);
        RoomRules.Join(room, Guid.NewGuid(), "Player", 2);
        Assert.Equal("room_full", Assert.Throws<RoomException>(() => RoomRules.Join(room, Guid.NewGuid(), "Another", 2)).Code);
    }

    [Fact]
    public void LockedRoomsAllowExistingMembersButNotNewOrRemovedMembers()
    {
        var actor = Host();
        room.JoinLocked = true;
        Assert.Same(actor, RoomRules.Join(room, host, "Different", 20));
        Assert.Equal("room_locked", Assert.Throws<RoomException>(() => RoomRules.Join(room, Guid.NewGuid(), "New", 20)).Code);
        actor.Removed = true;
        Assert.Equal("membership_revoked", Assert.Throws<RoomException>(() => RoomRules.Join(room, host, "Host", 20)).Code);
        Assert.Throws<RoomException>(() => RoomRules.Authorize(room, host));
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("remove")]
    [InlineData("transfer")]
    [InlineData("close")]
    public void EveryControlRequiresTheCurrentFacilitator(string action)
    {
        Host();
        var player = RoomRules.Join(room, Guid.NewGuid(), "Player", 20);
        Assert.Equal("facilitator_required", Assert.Throws<RoomException>(() => RoomRules.Apply(room, player,
            new(Guid.NewGuid(), room.Version, action, room.FacilitatorId, true), 200)).Code);
        Assert.Empty(RoomRules.Actions(room, player));
    }

    [Fact]
    public void ControlsAdvanceVersionAndOldVersionsCannotRepeatTransitions()
    {
        var actor = Host();
        RoomRules.Apply(room, actor, new(Guid.NewGuid(), 1, "lock", Locked: true), 200);
        Assert.True(room.JoinLocked);
        Assert.Equal(2, room.Version);
        Assert.Equal(200, room.LastActivityTicks);
        Assert.Equal("version_changed", Assert.Throws<RoomException>(() => RoomRules.Apply(room, actor, new(Guid.NewGuid(), 1, "close"), 201)).Code);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("transfer")]
    public void TargetControlsRejectMissingAndSelfTargets(string action)
    {
        var actor = Host();
        foreach (var target in new Guid?[] { null, actor.Id, Guid.NewGuid() })
            Assert.Equal("invalid_target", Assert.Throws<RoomException>(() => RoomRules.Apply(room, actor, new(Guid.NewGuid(), 1, action, target), 200)).Code);
    }

    [Fact]
    public void TransferRevokesOldFacilitatorAndRemovalRevokesAccess()
    {
        var actor = Host();
        var guest = Guid.NewGuid();
        var player = RoomRules.Join(room, guest, "Player", 20);
        RoomRules.Apply(room, actor, new(Guid.NewGuid(), 1, "transfer", player.Id), 200);
        Assert.Equal(player.Id, room.FacilitatorId);
        Assert.Empty(RoomRules.Actions(room, actor));
        RoomRules.Apply(room, player, new(Guid.NewGuid(), 2, "remove", actor.Id), 201);
        Assert.Throws<RoomException>(() => RoomRules.Authorize(room, host));
    }

    [Fact]
    public void ClosingAndExpiryAreIndependentOfPresence()
    {
        var actor = Host();
        Assert.False(RoomRules.IsExpired(room, 100 + TimeSpan.FromHours(24).Ticks - 1, new()));
        Assert.True(RoomRules.IsExpired(room, 100 + TimeSpan.FromHours(24).Ticks, new()));
        RoomRules.Apply(room, actor, new(Guid.NewGuid(), 1, "close"), 200);
        Assert.Equal("closed", room.Status);
        Assert.Equal(200, room.ClosedTicks);
        Assert.Empty(RoomRules.Actions(room, actor));
        Assert.Equal("room_closed", Assert.Throws<RoomException>(() => RoomRules.Apply(room, actor, new(Guid.NewGuid(), 2, "lock", Locked: false), 201)).Code);
        Assert.Throws<RoomException>(() => RoomRules.Join(room, Guid.NewGuid(), "New", 20));
    }
}
