namespace ClinicManagement.Domain.Common;

public abstract class AggregateRoot<TId> : Entity<TId>
{
    protected AggregateRoot() : base() { }

    protected AggregateRoot(TId id) : base(id) { }
}



