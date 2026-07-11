using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;
using BookCart.Domain.Entities.Categories.Events;
using BookCart.Domain.Entities.Categories.ValueObjects;

//! My Notes about: "Rich Domain Model & Factory Pattern":  https://app.notion.com/p/Rich-Domain-Model-Factory-Pattern-EF-constructer-with-DDD-ValueObjects-The-BookCart-Playboo-39a0535d4036801ab9b7c82baf07acb5
namespace BookCart.Domain.Entities.Categories
{
    public sealed class Category : AMasterEntity<CategoryId>, IAggregateRoot
    {
        #region Constructors & Properties

        //*     Fields & Properties: (Inherited: Id || CreatedAt || UpdatedAt || CreatedBy || UpdatedBy || IsDeleted || IsActive)
        //*     [CategoryName Name] || [CategoryDescription CategoryDescription] ||

        //! private: for EF Core and other serializers.
        private Category() { }

        private Category(CategoryId id, CategoryName name, CategoryDescription categoryDescription)
            : base(id)
        {
            Name = name;
            CategoryDescription = categoryDescription;
        }

        public CategoryName Name { get; private set; } = null!;
        public CategoryDescription CategoryDescription { get; private set; } = null!;

        //! Anemic Domain Model; Replaced it with a [ValueObject] CategoryName for better encapsulation and validation.
        //public string Name { get; private set; } = string.Empty;

        #endregion

        #region Factories

        public static Result<Category> Create(string name, string categoryDescription)
        {
            //! Validate all [[ValueObject]] => aggregate ALL [[errors]] in ONE if guard via [[Combine]] => no more per-result "if (IsFailure) errorsList.AddRange(...)" repetition for each [[ValueObject]] creation with [Create]!!
            Result<CategoryName> nameResult = CategoryName.Create(name);
            Result<CategoryDescription> descriptionResult = CategoryDescription.Create(
                categoryDescription
            );

            var validation = Result.Combine(nameResult, descriptionResult);
            if (validation.IsFailure)
            {
                //! implicit: Error[] → Result<Category> (failure)
                return validation.Errors;
            }

            //! Now EVERY [[result]] is a [[success]] => [[Value]] is safe.
            var category = new Category(
                CategoryId.New(),
                nameResult.Value,
                descriptionResult.Value
            );

            //! [[category.Id]] is [[CategoryId]]? (nullable, from ABaseEntity), but the [[event]] needs a (non-null CategoryId). It IS set by the ctor above → use [! symbol] to assert [non-null].
            category.RaiseDomainEvent(new CategoryCreatedDomainEvent(category.Id!));

            return category; //! implicit: Category → Result<Category> (success)
        }

        #endregion

        #region Methods & Behaviors

        public override Result MarkAsDeleted()
        {
            Result result = base.MarkAsDeleted();

            if (result.IsFailure)
            {
                return result.Errors;
            }

            RaiseDomainEvent(new CategoryDeletedDomainEvent(Id!));
            return result;
        }

        #endregion
    }
}
