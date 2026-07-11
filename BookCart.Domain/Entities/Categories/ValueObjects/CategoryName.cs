//! My Notes about: "Rich Domain Model & Factory Pattern":  https://app.notion.com/p/Rich-Domain-Model-Factory-Pattern-EF-constructer-with-DDD-ValueObjects-The-BookCart-Playboo-39a0535d4036801ab9b7c82baf07acb5
using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;
using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Categories.ValueObjects
{
    public sealed record CategoryName : ASingleValueObject<string>
    {
        #region Properties & Construction

        public const int MaxLength = 1000;

        private CategoryName()
            : base() { }

        private CategoryName(string nameValue)
            : base(nameValue) { }

        #endregion

        public static Result<CategoryName> Create(string nameValue)
        {
            var errorsList = new List<Error>();

            if (string.IsNullOrEmpty(nameValue))
            {
                errorsList.Add(CategoryNameErrors.CategoryNameIsRequired);
            }
            else if (nameValue.Length > MaxLength)
            {
                errorsList.Add(CategoryNameErrors.CategoryNameIsTooLong(MaxLength));
            }

            if (errorsList.Any())
            {
                return errorsList;

                //! Cannot implicitly convert type 'BookCart.Domain.Common.Results.Result' to 'BookCart.Domain.Common.Results.Result<BookCart.Domain.Entities.Categories.ValueObjects.CategoryName>'. An explicit conversion exists (are you missing a cast?)
                //return Result<CategoryName>.Failure(errorsList);
            }

            return new CategoryName(nameValue!.Trim());
        }
    }
}
