namespace BookCart.Domain.Common.Abstractions;

//! - Contract used by infrastructure — EF Core interceptors and MediatR dispatchers need to: [Read events], [Clear events]
//! - RaiseDomainEvent is NOT on [[ IEntity ]] — interfaces force all members to be public.
//!     - RaiseDomainEvent is [protected] on AEntity<T> so only the entity itself decides when a domain event occurs. No external code can call booking.RaiseDomainEvent(...) directly.
public interface IEntity
{
    IReadOnlyList<IDomainEvent> GetDomainEvents();

    void ClearDomainEvents();
}
