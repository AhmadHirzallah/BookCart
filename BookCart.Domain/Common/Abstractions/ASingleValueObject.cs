namespace BookCart.Domain.Common.Abstractions;

public abstract record ASingleValueObject<TValue>
    where TValue : notnull
{
    #region Value + Construction

    //? The single wrapped, already-validated primitive. One canonical name for ALL VOs.
    public TValue Value { get; }

    //! [[protected]]: a value can only be set by a concrete VO's private ctor → which is only reachable from that VO's Create factory → which validated first. Invalid state = impossible.
    protected ASingleValueObject(TValue value)
    {
        Value = value;
    }

    //! [[protected]]: [[EF Core]] needs a [[parameterless constructor]] to materialize the entity from the database. It will set the Value property via reflection.
    protected ASingleValueObject() { }

    #endregion

    #region Methods (ToString)


    /*
         //!    Every '[record]' auto-generates [[ToString();]] without '[sealed]' each concrete VO would
         //!    regenerate it and print for ex.: "CategoryName { Value = Fiction }".
         //! 'sealed override' forces ONE ToString for the whole hierarchy → the raw value, clean in logs and serialisation.
    */
    public sealed override string ToString() => Value.ToString() ?? string.Empty;

    #endregion
}
