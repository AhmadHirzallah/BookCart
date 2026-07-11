using System.Data;
using BookCart.Application.Common.Abstractions.Data;
using Microsoft.Data.SqlClient;

namespace BookCart.Infrastructure.Persistence.Connections;

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /*
        //! - For connecting with [[PostgreSQL]]:
        //*     - NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        //?     ------------------------------------------------------------------------
        //! - For connecting with [[Microsoft SQL Server]] database
        //*     - SqlConnection connection = new SqlConnection(_connectionString);
        //?     - But it Requires: [[[  dotnet add package Microsoft.Data.SqlClient  ]]]
    */
    public IDbConnection CreateConnection()
    {
        var connection = new SqlConnection(_connectionString);

        connection.Open();

        return connection;
    }
}
