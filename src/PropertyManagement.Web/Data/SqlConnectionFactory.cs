using Microsoft.Data.SqlClient;

namespace PropertyManagement.Web.Data;

public sealed class SqlConnectionFactory(string connectionString)
{
    public SqlConnection Create() => new(connectionString);
}