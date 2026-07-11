namespace BookCart.Infrastructure.Persistence.Configuration;

internal static class DbContextsConfigSettings
{
    internal static class SchemasNames
    {
        internal const string Application = "BookCart";
        internal const string Identity = "Identity";
    }

    internal static class TablesNames
    {
        internal const string Categories = "Categories";
        internal const string Books = "Books"; //! Products table is named as Books in the database.
        internal const string Orders = "Orders";
        internal const string Users = "Users";
        internal const string Roles = "Roles";
        internal const string Permissions = "Permissions";
        internal const string UserRoles = "UserRoles";
        internal const string RolePermissions = "RolePermissions";
    }
}
