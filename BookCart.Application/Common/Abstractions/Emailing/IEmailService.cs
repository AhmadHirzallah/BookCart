namespace BookCart.Application.Common.Abstractions.Emailing;

public interface IEmailService
{
    //! Replace [string to] with something like [EmailAddress to] in the future, but for now, we will keep it simple.
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
