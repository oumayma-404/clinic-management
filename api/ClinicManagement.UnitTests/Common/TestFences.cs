using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <see cref="IClinicWriteFence"/> for tests: <see cref="None"/> fences nothing (every deployment with no PC de secours),
/// <see cref="Of"/> fences the cabinets named and records which ones were asked about.
/// </summary>
public sealed class TestFence : IClinicWriteFence
{
    private readonly HashSet<Guid> _fenced;

    private TestFence(IEnumerable<Guid> fenced)
    {
        _fenced = fenced.ToHashSet();
    }

    public static TestFence None => new(Array.Empty<Guid>());

    public static TestFence Of(params Guid[] fenced) => new(fenced);

    public List<Guid> Asked { get; } = new();

    public Task<bool> RefusesAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        Asked.Add(clinicId);
        return Task.FromResult(_fenced.Contains(clinicId));
    }
}
