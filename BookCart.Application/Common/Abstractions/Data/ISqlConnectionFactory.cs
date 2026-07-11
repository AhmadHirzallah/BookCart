using System.Data;

namespace BookCart.Application.Common.Abstractions.Data;

public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}
