namespace Evergreen.Application;

public sealed record ScheduleRequest(DateOnly StartDate, int Days, int PostsPerDay, TimeOnly[] Times, DayOfWeek[]? AllowedDays = null);
public sealed record PlannedSlot(DateTimeOffset ScheduledAt);

public interface ICampaignSchedulePlanner { IReadOnlyList<PlannedSlot> Build(ScheduleRequest request, TimeZoneInfo timeZone); }

public sealed class CampaignSchedulePlanner : ICampaignSchedulePlanner
{
    public IReadOnlyList<PlannedSlot> Build(ScheduleRequest request, TimeZoneInfo timeZone)
    {
        if (request.Days < 1 || request.Days > 365) throw new ArgumentOutOfRangeException(nameof(request.Days));
        if (request.PostsPerDay < 1 || request.PostsPerDay > 24) throw new ArgumentOutOfRangeException(nameof(request.PostsPerDay));
        if (request.Times.Length < request.PostsPerDay) throw new ArgumentException("Provide at least one time for each daily post.", nameof(request.Times));
        var result = new List<PlannedSlot>();
        for (var offset = 0; offset < request.Days; offset++)
        {
            var date = request.StartDate.AddDays(offset);
            if (request.AllowedDays is { Length: > 0 } && !request.AllowedDays.Contains(date.DayOfWeek)) continue;
            foreach (var time in request.Times.Take(request.PostsPerDay))
            {
                var local = date.ToDateTime(time, DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
                result.Add(new PlannedSlot(new DateTimeOffset(utc, TimeSpan.Zero)));
            }
        }
        return result;
    }
}
