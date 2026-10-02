using System.Linq.Expressions;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Services;

public static class JobAvailabilityPolicy
{
    public static Expression<Func<Job, bool>> OpenPredicate(DateTime onDate)
    {
        var today = onDate.Date;
        var tomorrow = today.AddDays(1);
        return job => !job.IsDeletion && job.IsOpen && job.PostedAt < tomorrow &&
            (job.ExpiresAt == null || job.ExpiresAt >= today);
    }

    public static IQueryable<Job> Open(this IQueryable<Job> query, DateTime onDate) =>
        query.Where(OpenPredicate(onDate));

    public static Expression<Func<Job, bool>> CandidateVisiblePredicate(DateTime onDate)
    {
        var today = onDate.Date;
        var tomorrow = today.AddDays(1);
        return job => !job.IsDeletion && job.IsPublished && job.IsOpen && job.PostedAt < tomorrow &&
            (job.ExpiresAt == null || job.ExpiresAt >= today);
    }

    public static IQueryable<Job> CandidateVisible(this IQueryable<Job> query, DateTime onDate) =>
        query.Where(CandidateVisiblePredicate(onDate));

    public static bool IsCandidateVisible(Job job, DateTime onDate)
    {
        var today = onDate.Date;
        return !job.IsDeletion && job.IsPublished && job.IsOpen && job.PostedAt.Date <= today &&
            (job.ExpiresAt is null || job.ExpiresAt.Value.Date >= today);
    }
}
