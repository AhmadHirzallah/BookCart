using BookCart.Application.Common.Abstractions.Clock;
using BookCart.Application.Common.Abstractions.Data;
using BookCart.Application.Common.Abstractions.Data.Repositories;
using BookCart.Application.Common.Abstractions.Data.UnitOfWork;
using BookCart.Application.Common.Abstractions.Emailing;
using BookCart.Infrastructure.Persistence.Configuration;
using BookCart.Infrastructure.Persistence.Connections;
using BookCart.Infrastructure.Persistence.DbContexts;
using BookCart.Infrastructure.Persistence.Repositories;
using BookCart.Infrastructure.Services.Clock;
using BookCart.Infrastructure.Services.Emailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookCart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddClockService().AddEmailService().AddPersistenceServices(configuration);

        return services;
    }

    #region Clock & Email Services

    private static IServiceCollection AddClockService(this IServiceCollection services)
    {
        services.AddTransient<IDateTimeProvider, DateTimeProvider>();
        services.AddTransient<IEmailService, EmailService>();

        return services;
    }

    private static IServiceCollection AddEmailService(this IServiceCollection services)
    {
        services.AddTransient<IEmailService, EmailService>();

        return services;
    }

    #endregion Clock & Email Services


    #region Persistence Services

    private static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        string connectionString = GetConnectionStringFromConfig(configuration);

        services
            .AddBookCartDbContextService(connectionString)
            .AddRepositories()
            .AddUnitOfWork()
            .AddConnectionFactoryForDapper(connectionString);

        return services;
    }

    private static string GetConnectionStringFromConfig(IConfiguration configuration)
    {
        return configuration.GetConnectionString("Database")
            ?? throw new ArgumentNullException(
                paramName: nameof(configuration),
                message: "Database connection string is not configured."
            );
    }

    private static IServiceCollection AddBookCartDbContextService(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.AddDbContext<BookCartDbContext>(dbContextOptionsBuilder =>
        {
            dbContextOptionsBuilder.UseSqlServer(
                connectionString: connectionString,
                sqlServerDbContextOptionsBuilder =>
                {
                    sqlServerDbContextOptionsBuilder.MigrationsHistoryTable(
                        tableName: HistoryRepository.DefaultTableName,
                        schema: DbContextsConfigSettings.SchemasNames.Application
                    );
                }
            );

#if DEBUG
            dbContextOptionsBuilder
                .LogTo(Console.WriteLine, LogLevel.Information)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();

#endif
        });
        ////.UseSnakeCaseNamingConvention(); //! This is example I can add if I want to use for example [PostgreSQL] to Align Naming of {Table, Cols} with Snake Case Naming Convention which is default for [PostgreSQL]

        return services;
    }

    //private static IServiceCollection AddRepositoriesAndUOF(this IServiceCollection services) { }

    private static IServiceCollection AddConnectionFactoryForDapper(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.AddSingleton<ISqlConnectionFactory>(_ =>
        {
            return new SqlConnectionFactory(connectionString);
        });

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IBookRepository, BookRepository>();
        //services.AddScoped<IOrderRepository, OrderRepository>();

        return services;
    }

    private static IServiceCollection AddUnitOfWork(this IServiceCollection services)
    {
        /*
            //!     ❌ LOOKS RIGHT. IS A DISASTER => That registers a [SECOND service descriptor] for [[BookCartDbContext]]. The [[DI container]] has NO idea it should reuse the one from [[AddDbContext<>()]] — so it CONSTRUCTS A SECOND INSTANCE.
            //! 🚨🚨❌❌    Result:      your repositories stage Add/Update/Delete on [[DbContext **A**]], and then SaveChangesAsync() commits on [[DbContext **B**]] — which is empty (tracking has no changes).
            //>      EVERY WRITE IN YOUR APPLICATION SILENTLY DOES NOTHING. No exception. No error. No log.
            //  ❌❌❌ =>  services.AddScoped<IUnitOfWork, BookCartDbContext>(); // ❌❌❌❌
            //*      ✅ THE FIX: resolve the SAME instance the DbContext registration already created.
         */
        services.AddScoped<IUnitOfWork>(serviceProvider =>
        {
            return serviceProvider.GetRequiredService<BookCartDbContext>();
        });

        return services;
    }
    #endregion Persistence Services
}
