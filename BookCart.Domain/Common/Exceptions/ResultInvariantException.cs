using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Common.Exceptions;

public sealed class ResultInvariantException : InvalidOperationException
{
    private ResultInvariantException(string message)
        : base(message) { }

    internal static ResultInvariantException FalseSuccessWithErrors() =>
        new("Invalid Result State: A successful Result MUST NOT carry any errors!");

    internal static ResultInvariantException FalseFailure_Without_Errors() =>
        new("Invalid Result State: A failed Result must carry at least one error!");

    internal static ResultInvariantException FalseAccessToFailureValue(Error firstError) =>
        new(
            "Invalid Result State: A failed Result's value can't be access and MUST NOT have accessability to its value!\n"
                + $"First Error: {firstError}"
        );
}
