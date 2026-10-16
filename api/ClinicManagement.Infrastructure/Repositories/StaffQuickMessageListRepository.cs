using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffQuickMessageListRepository : IStaffQuickMessageListRepository
{
    private readonly ApplicationDbContext _context;

    public StaffQuickMessageListRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StaffQuickMessageList?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffQuickMessageLists.FirstOrDefaultAsync(l => l.ClinicId == clinicId, cancellationToken);
    }

    public async Task AddAsync(StaffQuickMessageList list, CancellationToken cancellationToken = default)
    {
        await _context.StaffQuickMessageLists.AddAsync(list, cancellationToken);
    }

    public void Remove(StaffQuickMessageList list)
    {
        _context.StaffQuickMessageLists.Remove(list);
    }
}
