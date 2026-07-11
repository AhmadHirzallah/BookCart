using BookCart.Application.Common.Abstractions.Data.UnitOfWork;
using BookCart.Domain.Entities.Categories;
using BookCart.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BookCart.Infrastructure.Persistence.DbContexts;

public sealed class BookCartDbContext : DbContext, IUnitOfWork
{
    #region Constructor & Fields

    //private readonly IPublisher _publisher;

    public BookCartDbContext(
        DbContextOptions<BookCartDbContext> dbContextOptions
    //, IPublisher publisher
    )
        : base(dbContextOptions)
    {
        //_publisher = publisher;
    }

    #endregion

    #region DbSets

    public DbSet<Category> Categories { get; init; } = null!;

    #endregion DbSets

    #region Overriding [OnModelCreating]

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DbContextsConfigSettings.SchemasNames.Application);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookCartDbContext).Assembly);

        //Todo: Don't forgot to map IdentityUsers from [AppUser] Class Entity

        base.OnModelCreating(modelBuilder);
    }

    #endregion Overriding [OnModelCreating]
}
