using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class FeedbackRepository : IFeedbackRepository
{
    private readonly ApplicationDbContext _context;

    public FeedbackRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ProductFeedback feedback, CancellationToken cancellationToken = default) =>
        await _context.ProductFeedback.AddAsync(feedback, cancellationToken);

    public async Task<PagedResult<ProductFeedbackRow>> GetPageAsync(
        string? kind, PageRequest paging, CancellationToken cancellationToken = default)
    {
        var feedback = _context.ProductFeedback.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            feedback = feedback.Where(f => f.Kind == kind);
        }

        var rows =
            from f in feedback
            join c in _context.Clinics.AsNoTracking() on f.ClinicId equals c.Id
            join u in _context.Users.AsNoTracking() on f.UserId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            orderby f.CreatedAt descending, f.Id
            select new ProductFeedbackRow(f, c.Name, u != null ? u.FullName : null, u != null ? u.Email : null);

        return await rows.ToPagedResultAsync(paging, cancellationToken);
    }

    public Task<UserFeedbackPrompt?> GetPromptAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.UserFeedbackPrompts.FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);

    public async Task AddPromptAsync(UserFeedbackPrompt prompt, CancellationToken cancellationToken = default) =>
        await _context.UserFeedbackPrompts.AddAsync(prompt, cancellationToken);
}
