namespace ClinicManagement.Domain.Common;

public abstract class Entity<TId>
{
    public TId Id { get; protected set; } = default!;

    /// <summary>
    /// Optimistic-concurrency token. Mapped by the DbContext to PostgreSQL's <c>xmin</c> system column — the
    /// transaction id that last wrote the row — so it needs no column of its own and no code has to remember
    /// to bump it. EF adds it to the <c>WHERE</c> of every <c>UPDATE</c>/<c>DELETE</c>; a row someone else
    /// changed in the meantime matches zero rows and raises <c>DbUpdateConcurrencyException</c>.
    ///
    /// <para>
    /// Round-tripped through the read DTO and back on the mutating command, so "the copy I edited" is what is
    /// checked — not "the copy the server just loaded", which would always match and detect nothing.
    /// </para>
    /// </summary>
    public uint Version { get; private set; }

    protected Entity() { }

    protected Entity(TId id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        return Id!.Equals(other.Id);
    }

    public override int GetHashCode()
    {
        return Id!.GetHashCode();
    }

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null && right is null)
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }
}



