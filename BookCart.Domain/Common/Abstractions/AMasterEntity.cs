using BookCart.Domain.Common.Abstractions.Errors;
using BookCart.Domain.Common.Results;

namespace BookCart.Domain.Common.Abstractions;

public abstract class AMasterEntity<TIdKeyType>
    : ABaseEntity<TIdKeyType>,
        IAuditable,
        IActivatable,
        IDeletable
{
    //! Parameterless ctor: EF Core materialisation ONLY — it must NOT force IsActive, because EF
    //! restores the value stored in the database (an inactive row must stay inactive on load).
    protected AMasterEntity() { }

    //! Id-ctor: called by concrete entity factories for a NEW entity → it starts active.
    protected AMasterEntity(TIdKeyType idKeyType)
    {
        Id = idKeyType;
        IsActive = true;
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public string? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public bool IsActive { get; private set; }

    #region State transitions — ONE shared implementation for every entity

    /*
        //>  virtual on purpose → a specific [[aggregate]] can [[OVERRIDE]] to also raise ITS OWN [[domain event]]
        //>  (the base can't, because the event type differs per aggregate — mechanic here, policy there):
        //>      public override Result Deactivate()
        //>      {
        //>          Result result = base.Deactivate();
        //>          if (result.IsSuccess) RaiseDomainEvent(new CategoryDeactivatedDomainEvent(Id!));
        //>          return result;
        //>      }
    */
    public virtual Result Activate()
    {
        if (IsActive)
        {
            return EntityStateErrors.AlreadyActive;
        }

        IsActive = true;
        return Result.Success();
    }

    public virtual Result Deactivate()
    {
        if (!IsActive)
        {
            return EntityStateErrors.AlreadyInactive;
        }

        IsActive = false;
        return Result.Success();
    }

    public virtual Result MarkAsDeleted()
    {
        if (IsDeleted)
        {
            return EntityStateErrors.AlreadyDeleted;
        }

        IsDeleted = true;
        return Result.Success();
    }

    #endregion State transitions — ONE shared implementation for every entity
}
