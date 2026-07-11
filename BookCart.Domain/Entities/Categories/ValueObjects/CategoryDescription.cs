//! My Notes about: "Rich Domain Model & Factory Pattern":  https://app.notion.com/p/Rich-Domain-Model-Factory-Pattern-EF-constructer-with-DDD-ValueObjects-The-BookCart-Playboo-39a0535d4036801ab9b7c82baf07acb5
using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;
using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Categories.ValueObjects
{
    public sealed record CategoryDescription : ASingleValueObject<string>
    {
        #region Properties & Construction

        public const int MaxLength = 2000;

        //! For EF Core to materialize the entity from the database, it needs a parameterless constructor.
        private CategoryDescription()
            : base() { }

        private CategoryDescription(string value)
            : base(value) { }

        #endregion


        public static Result<CategoryDescription> Create(string descriptionValue)
        {
            var errorList = new List<Error>();

            if (string.IsNullOrEmpty(descriptionValue))
            {
                errorList.Add(CategoryDescriptionErrors.CategoryDescriptionIsRequired);
            }
            else if (descriptionValue.Length > MaxLength)
            {
                errorList.Add(CategoryDescriptionErrors.CategoryDescriptionIsTooLong(MaxLength));
            }

            if (errorList.Any())
            {
                return errorList;
            }

            return new CategoryDescription(descriptionValue!.Trim());
        }
    }
}
