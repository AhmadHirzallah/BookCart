namespace BookCart.Domain.Common.Abstractions;

public interface IDeletable
{
    bool IsDeleted { get; }
}
