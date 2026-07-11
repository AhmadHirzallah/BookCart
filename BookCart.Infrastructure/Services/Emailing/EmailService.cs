using BookCart.Application.Common.Abstractions.Emailing;

namespace BookCart.Infrastructure.Services.Emailing;

internal sealed class EmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
