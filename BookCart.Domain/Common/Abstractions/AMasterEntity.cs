namespace BookCart.Domain.Common.Abstractions;

internal abstract class AMasterEntity<TIdKeyType>
    : ABaseEntity<TIdKeyType>,
        IAuditable,
        IActiveable,
        IDeletable
{
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public bool IsActive { get; private set; }
}
