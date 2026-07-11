using BookCart.Domain.Common.Results;

namespace BookCart.Domain.Common.Abstractions;

//! Capability contract: an entity whose active/inactive state is a DOMAIN-controlled transition.
//!   - IsActive        → read the state (infrastructure query filters use this, e.g. HasQueryFilter(e => e.IsActive)).
//!   - Activate/Deactivate → the guarded transitions; ONE shared implementation lives in AMasterEntity,
//!                            so no entity ever re-writes them. Return Result so a refused transition
//!                            (already active / already inactive) is a value, not an exception.
public interface IActivatable
{
    bool IsActive { get; }

    Result Activate();
    Result Deactivate();
}
