using Evergreen.Application;
using FluentAssertions;

namespace Evergreen.Tests.Application;

public sealed class CampaignSchedulePlannerTests
{
    private readonly CampaignSchedulePlanner _sut = new();
    private readonly TimeZoneInfo _utc = TimeZoneInfo.Utc;

    [Fact]
    public void Build_CreatesTwoDailySlotsForTenDays()
    {
        var result = _sut.Build(new ScheduleRequest(new DateOnly(2026, 11, 4), 10, 2, [new TimeOnly(12, 0), new TimeOnly(18, 0)]), _utc);
        result.Should().HaveCount(20);
        result[0].ScheduledAt.Should().Be(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        result[1].ScheduledAt.Should().Be(new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Build_RespectsAllowedWeekdays()
    {
        var result = _sut.Build(new ScheduleRequest(new DateOnly(2026, 11, 2), 7, 1, [new TimeOnly(12, 0)], [DayOfWeek.Monday, DayOfWeek.Wednesday]), _utc);
        result.Should().HaveCount(2);
        result.Select(x => x.ScheduledAt.DayOfWeek).Should().ContainInOrder(DayOfWeek.Monday, DayOfWeek.Wednesday);
    }

    [Fact]
    public void Build_RejectsInsufficientTimes()
    {
        var act = () => _sut.Build(new ScheduleRequest(new DateOnly(2026, 11, 4), 2, 2, [new TimeOnly(12, 0)]), _utc);
        act.Should().Throw<ArgumentException>();
    }
}
