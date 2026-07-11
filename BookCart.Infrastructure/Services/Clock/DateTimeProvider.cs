using BookCart.Application.Common.Abstractions.Clock;

namespace BookCart.Infrastructure.Services.Clock;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
