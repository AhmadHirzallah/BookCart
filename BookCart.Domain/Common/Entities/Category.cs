using BookCart.Domain.Common.Abstractions;

namespace BookCart.Domain.Common.Entities
{
    public class Category : ABaseEntity<Guid>
    {
        public string Name { get; private set; } = string.Empty;
    }
}
