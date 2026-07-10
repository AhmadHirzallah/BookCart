using System.Diagnostics.CodeAnalysis;
using BookCart.Domain.Common.Exceptions;
using BookCart.Domain.Common.Result.Errors;

namespace BookCart.Domain.Common.Result;

//! 📖 §15 The Result<TValue> class — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036804fb528c8d171259f40
public class Result<TValue> : Result
{
    #region Properties & Fields

    private readonly TValue? _value;

    //! 📖 §16 Value and the [NotNull] post-condition — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368062b7c6e68a0bcfb0c8
    [NotNull]
    public TValue Value =>
        IsSuccess ? _value! : throw ResultInvariantException.FalseAccessToFailureValue(FirstError);

    #endregion


    #region Constructor: {protected internal Result(...){...}}

    //! 📖 §17 The Result<TValue> constructor — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036800f85a6d54c5660fac9
    protected internal Result(TValue? value, bool isSuccess, Error[] errors)
        : base(isSuccess, errors)
    {
        _value = value;
    }

    #endregion Constructor: {protected internal Result(...){...}}


    #region Static Factory Methods (Value Carrying) - {Success, Failure, Create}


    #endregion

    #region Methods


    #region 🟢 (Methods SubRegion-1): Type-safe [[value]] inspection (تفتيش وفحص): { TryGetValue, TryGetSuccessValue}

    //! 📖 §18 TryGetValue — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036801490f8c10c8a82a187
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        if (IsSuccess)
        {
            value = _value!;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    //! 📖 §19 TryGetSuccessValue override - the boxing extractor — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368042bc01cfce3a036cf3
    public override bool TryGetSuccessValue(out object? value)
    {
        if (IsSuccess && _value is not null)
        {
            value = _value;
            return true;
        }
        else
        {
            value = null;
            return false;
        }
    }

    #endregion


    #region 🟢 (Methods SubRegion-2): {Match, MatchAsync, Map, MapAsync}

    //! 📖 - Link: Important Concept & Different (When to use? What each return? etc. && Examples of all Types Notes):
    //*     - https://app.notion.com/p/Result-Pattern-Match-Map-Bind-Methods-Dive-3990535d4036801c9480e68f5e03548d?source=copy_link#3990535d403680138cf4c3893bbe2510

    public TNext Match<TNext>(Func<TValue, TNext> onSuccess, Func<Error[], TNext> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Errors);

    public Task<TNext> MatchAsync<TNext>(
        Func<TValue, Task<TNext>> onSuccess,
        Func<Error[], Task<TNext>> onFailure
    ) => IsSuccess ? onSuccess(Value) : onFailure(Errors);

    public Result<TNext> Map<TNext>(Func<TValue, TNext> mapper) =>
        IsSuccess ? Success(mapper(Value)) : Failure<TNext>(Errors);

    public async Task<Result<TNext>> MapAsync<TNext>(Func<TValue, Task<TNext>> mapper) =>
        IsSuccess ? Success(await mapper(Value)) : Failure<TNext>(Errors);

    #endregion


    #endregion

    #region Implicit Operators

    //! - Prof. Guide & Notes I Take for Operators:
    //*     - https://app.notion.com/p/C-Operators-Master-Guide-3990535d403680518152faeb6f8ab813?source=copy_link#3990535d403680a19a02dcd8ff9f637a

    //! Both 2-Lines are same but syntax is different. I prefer the first one because it is more explicit and clear.
    //public static implicit operator Result<TValue>(TValue? value) => Create(value);       //* [1]
    public static implicit operator Result<TValue>(TValue? value) => Create<TValue>(value); //* [2]

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

    public static implicit operator Result<TValue>(List<Error> errors) => Failure<TValue>(errors);

    public static implicit operator Result<TValue>(Error[] errors) => Failure<TValue>(errors);

    #endregion
}
