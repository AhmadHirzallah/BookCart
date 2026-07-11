using BookCart.Application.Common.Abstractions.Clock;
using BookCart.Application.Common.Abstractions.Data;
using BookCart.Application.Common.Abstractions.Emailing;
using BookCart.Infrastructure.Persistence.Configuration;
using BookCart.Infrastructure.Persistence.Connections;
using BookCart.Infrastructure.Persistence.DbContexts;
using BookCart.Infrastructure.Services.Clock;
using BookCart.Infrastructure.Services.Emailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    #endregion Persistence Services
}
