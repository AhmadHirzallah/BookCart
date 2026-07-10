namespace BookCart.Domain.Common.Abstractions;

public abstract class ABaseEntity<TIdKeyType> : IEntity, IEquatable<ABaseEntity<TIdKeyType>>
{
    #region Class Members

    public TIdKeyType? Id { get; init; }

    public override int GetHashCode() => Id is not null ? Id.GetHashCode() * 41 : 0;

    /*
        //!     - [RaiseDomainEvent] is [protected] on purpose
        //!         - only the [[entity]] itself (from inside its own methods) should decide when a [domain event] occurs.
        //*     - For example, [Booking.Confirm()] internally calls [RaiseDomainEvent(new BookingConfirmedDomainEvent(Id))] which is a [[ domain event ]].
        //!     No external code should ever be able to call [booking.RaiseDomainEvent(...)] directly.
        //!     🚨 This is a way to encapsulate the domain events and only allow the entity itself to raise domain events.
     */
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    #endregion Class Members


    #region Constructors

    //! For EF ( [[ Domain Entities ]] will inherit from this abstract class ) => Then we need to make protected for inheritance!
    protected ABaseEntity() { }

    protected ABaseEntity(TIdKeyType id)
    {
        Id = id;
    }

    #endregion Constructors


    #region Equality - IEquatable Implementation

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (obj is not ABaseEntity<TIdKeyType> other)
        {
            return false;
        }

        return Equals(other);
    }

    public bool Equals(ABaseEntity<TIdKeyType>? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other?.GetType())
        {
            return false;
        }

        /*
           //!      - If either entity is transient (i.e., has not been assigned a valid Id), they are not considered equal.
           //!      - This is standard in DDD entity equality comparisons.
           //!          - Example:
           //!              - if (EqualityComparer<TIdType>.Default.Equals(Id, default)) return false;
           //!              - if (EqualityComparer<TIdType>.Default.Equals(other.Id, default)) return false;
        */
        if (EqualityComparer<TIdKeyType>.Default.Equals(Id, default))
        {
            return false;
        }

        /*
           //!      -   If either entity is transient (i.e., has not been assigned a valid Id), they are not considered equal.
           //!      -   This is standard in DDD entity equality comparisons.
           //!      -   Example:
           //!              -  if (EqualityComparer<TIdType>.Default.Equals(Id, default)) return false;
           //!              -  if (EqualityComparer<TIdType>.Default.Equals(other.Id, default)) return false;
        */
        if (EqualityComparer<TIdKeyType>.Default.Equals(other.Id, default))
        {
            return false;
        }

        return EqualityComparer<TIdKeyType>.Default.Equals(Id, other.Id);
    }

    #endregion Equality - IEquatable Implementation


    #region Operators [[ Equals: (==) ]] and [[ Not Equals: (!=) ]]

    public static bool operator ==(ABaseEntity<TIdKeyType>? left, ABaseEntity<TIdKeyType>? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(ABaseEntity<TIdKeyType>? left, ABaseEntity<TIdKeyType>? right) =>
        !(left == right);

    #endregion Operators [[ Equals: (==) ]] and [[ Not Equals: (!=) ]]


    #region Implementations of Contracts (Interfaces)

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        return [.. _domainEvents];
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    #endregion Implementations of Contracts (Interfaces)
}
