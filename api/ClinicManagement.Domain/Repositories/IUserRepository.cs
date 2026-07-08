using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<User?> GetByAuth0SubAsync(string auth0Sub, CancellationToken cancellationToken = default);
    /// <summary>Looks up a local (password-backed) account by email. Used for Local-mode login.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task AddAsync(User entity, CancellationToken cancellationToken = default);
    void Update(User entity);
    void Remove(User entity);
}

