using BookCart.Domain.Common.Exceptions;
using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Common.Results;

//! 📖 §4 The Result class — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680829b97fe3b28f0e281
public class Result
{
    #region Properties { IsSuccess, IsFailure, Errors, FirstError }

    /// <summary><c>true</c> when the operation succeeded. The single source of truth.</summary>
    public bool IsSuccess { get; }

    /// <summary>The inverse of <see cref="IsSuccess"/>, so guard clauses read as English.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Errors of a failed operation. Never <c>null</c>, always empty on success.</summary>
    public Error[] Errors { get; }

    //! 📖 §6 FirstError — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680dca864e12b3131db1f
    public Error FirstError => Errors.Length > 0 ? Errors[0] : Error.None;

    #endregion Properties


    #region Constructor {protected internal Result(...){...}}

    //! 📖 §3 Invariants & the sealed constructor — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368023902dc32aa4cb212d
    protected internal Result(bool isSuccess, Error[] errors)
    {
        if (isSuccess && errors.Length > 0)
        {
            throw ResultInvariantException.FalseSuccessWithErrors();
        }

        if (!isSuccess && errors.Length == 0)
        {
            throw ResultInvariantException.FalseFailure_Without_Errors();
        }

        IsSuccess = isSuccess;
        Errors = errors;
    }

    #endregion Constructor {protected internal Result(...){...}}


    #region Methods: {Match, MatchAsync, OnSuccess, OnFailure, [virtual] TryGetSuccessValue}

    //! 📖 §7 Match — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368047a76ed0fd5ddb1262
    public TNext Match<TNext>(Func<TNext> onSuccess, Func<Error[], TNext> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Errors);

    //! 📖 §8 MatchAsync — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680118ba9f69561f3f2ab
    public Task<TNext> MatchAsync<TNext>(
        Func<Task<TNext>> onSuccess,
        Func<Error[], Task<TNext>> onFailure
    ) => IsSuccess ? onSuccess() : onFailure(Errors);

    //! 📖 §9 OnSuccess — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036807db989f43514505f8b
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    //! 📖 §10 OnFailure — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036800bb553daa56fa6a0fc
    public Result OnFailure(Action<Error[]> action)
    {
        if (IsFailure)
        {
            action(Errors);
        }

        return this;
    }

    //! 📖 §11 TryGetSuccessValue (virtual) — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036805a926fdf3bdd5c3d4a
    public virtual bool TryGetSuccessValue(out object? value)
    {
        value = null;
        return false;
    }

    #endregion Methods: {Match, MatchAsync, OnSuccess, OnFailure, [virtual] TryGetSuccessValue}


    #region Static factory methods


    #region (SubRegion-1). Non Generic (void / command) results by [[Static Factory Methods]]

    //! 📖 Links of Notes: Success(), Failure(Error error), Result Failure(IReadOnlyList<Error> errors
    //*     - https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036804fb4cdf481be82be4e

    public static Result Success() => new(true, []);

    public static Result Failure(Error error) => new Result(false, [error]);

    public static Result Failure(IReadOnlyList<Error> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            throw ResultInvariantException.FalseFailure_Without_Errors();
        }

        return new(false, [.. errors]);
    }

    #endregion


    #region (SubRegion-2). Generic (value-carrying) results by [[Static Factory Methods]] =>

    public static Result<TValue> Success<TValue>(TValue value) =>
        new Result<TValue>(value, true, []);

    public static Result<TValue> Failure<TValue>(Error oneError) => new(default, false, [oneError]);

    public static Result<TValue> Failure<TValue>(IReadOnlyList<Error> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            throw ResultInvariantException.FalseFailure_Without_Errors();
        }

        return new Result<TValue>(default, false, [.. errors]);
        //return new Result<TValue>(default, false, errors.ToArray());
    }

    public static Result<TValue> Create<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue(nameof(value)));

    #endregion


    #endregion Static factory methods


    #region Combine (applicative validation — aggregate many results into one)

    /*
     //! - THE shortcut for factories. Replaces the repeated "check each [[IsFailure]] after [[Create]], AddRange its Errors"
     //!   dance with a SINGLE guard that collects ALL errors from every failed result at once.
     //?    - Returns Success() only when EVERY result succeeded; otherwise one Failure carrying every error.
     //?    - Because Result<TValue> derives from Result, you pass Result<CategoryName>, Result<Price>, ...
     //?      straight in — each upcasts to Result with its errors intact. One method covers all value types.
     //>    - After a successful Combine, read each result's .Value safely (they are all guaranteed success).
    */
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        var errors = new List<Error>();

        foreach (Result result in results)
        {
            if (result.IsFailure)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count > 0 ? Failure(errors) : Success();
    }

    #endregion Combine (applicative validation — aggregate many results into one)


    #region Implicit Operators

    //! - Prof. Guide & Notes I made for Operators:
    //*     - https://app.notion.com/p/C-Operators-Master-Guide-3990535d403680518152faeb6f8ab813?source=copy_link#3990535d403680a19a02dcd8ff9f637a

    public static implicit operator Result(Error error) => Failure(error);

    public static implicit operator Result(List<Error> errors) => Failure(errors);

    public static implicit operator Result(Error[] errors) => Failure(errors);

    #endregion Implicit Operators
}
