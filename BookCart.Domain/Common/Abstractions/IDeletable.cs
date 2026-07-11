using BookCart.Domain.Common.Results;

namespace BookCart.Domain.Common.Abstractions;

public interface IDeletable
{
    bool IsDeleted { get; }

    Result MarkAsDeleted();
}
