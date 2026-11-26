using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Products.Infrastructure.Persistence;

var options = ParseArguments(args);
var credential = new AzureCliCredential();
var token = await credential.GetTokenAsync(
    new TokenRequestContext(["https://database.windows.net/.default"]),
    CancellationToken.None);

foreach (var databaseName in new[] { "mortgages", "products" })
{
    var identity = databaseName == "mortgages" ? options.MortgagesIdentity : options.ProductsIdentity;
    await EnsureApplicationAccessAsync(options.Server, databaseName, identity, token.Token);
}

await MigrateMortgageDatabaseAsync(options.Server, token.Token);
await MigrateProductsDatabaseAsync(options.Server, token.Token);

static async Task EnsureApplicationAccessAsync(
    string server,
    string database,
    ApplicationIdentity identity,
    string accessToken)
{
    await using var connection = CreateConnection(server, database, accessToken);
    await connection.OpenAsync();
    var safeName = identity.Name.Replace("]", "]]", StringComparison.Ordinal);
    var escapedName = identity.Name.Replace("'", "''", StringComparison.Ordinal);
    var identitySid = Convert.ToHexString(identity.ObjectId.ToByteArray());
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        IF DATABASE_PRINCIPAL_ID(N'{escapedName}') IS NULL
            CREATE USER [{safeName}] WITH SID = 0x{identitySid}, TYPE = E;
        """;
    await command.ExecuteNonQueryAsync();
    await EnsureRoleMembershipAsync(connection, safeName, escapedName, "db_datareader");
    if (identity.CanWrite)
    {
        await EnsureRoleMembershipAsync(connection, safeName, escapedName, "db_datawriter");
    }
}

static async Task EnsureRoleMembershipAsync(
    SqlConnection connection,
    string quotedIdentityName,
    string escapedIdentityName,
    string role)
{
    if (role is not ("db_datareader" or "db_datawriter"))
    {
        throw new ArgumentOutOfRangeException(nameof(role));
    }

    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        IF NOT EXISTS (
            SELECT 1
            FROM sys.database_role_members AS roleMembers
            INNER JOIN sys.database_principals AS roles ON roles.principal_id = roleMembers.role_principal_id
            INNER JOIN sys.database_principals AS members ON members.principal_id = roleMembers.member_principal_id
            WHERE roles.name = N'{role}' AND members.name = N'{escapedIdentityName}')
            ALTER ROLE {role} ADD MEMBER [{quotedIdentityName}];
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task MigrateMortgageDatabaseAsync(string server, string accessToken)
{
    await using var connection = CreateConnection(server, "mortgages", accessToken);
    var options = new DbContextOptionsBuilder<MortgageDbContext>()
        .UseSqlServer(connection)
        .Options;
    await using var context = new MortgageDbContext(options);
    await context.Database.MigrateAsync();
}

static async Task MigrateProductsDatabaseAsync(string server, string accessToken)
{
    await using var connection = CreateConnection(server, "products", accessToken);
    var options = new DbContextOptionsBuilder<ProductsDbContext>()
        .UseSqlServer(connection)
        .Options;
    await using var context = new ProductsDbContext(options);
    await context.Database.MigrateAsync();
}

static SqlConnection CreateConnection(string server, string database, string accessToken)
{
    var builder = new SqlConnectionStringBuilder
    {
        DataSource = $"tcp:{server},1433",
        InitialCatalog = database,
        Encrypt = true,
        TrustServerCertificate = false,
        ConnectTimeout = 30
    };
    return new SqlConnection(builder.ConnectionString) { AccessToken = accessToken };
}

static BootstrapOptions ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected --server, --identity-name, and --identity-object-id arguments.");
        }

        values[arguments[index][2..]] = arguments[index + 1];
    }

    if (!values.TryGetValue("server", out var server) ||
        !values.TryGetValue("mortgages-identity-name", out var mortgagesIdentityName) ||
        !values.TryGetValue("mortgages-identity-object-id", out var mortgagesIdentityObjectIdText) ||
        !Guid.TryParse(mortgagesIdentityObjectIdText, out var mortgagesIdentityObjectId) ||
        !values.TryGetValue("products-identity-name", out var productsIdentityName) ||
        !values.TryGetValue("products-identity-object-id", out var productsIdentityObjectIdText) ||
        !Guid.TryParse(productsIdentityObjectIdText, out var productsIdentityObjectId))
    {
        throw new ArgumentException("Provide --server and valid identity names and object IDs for mortgages and products.");
    }

    return new BootstrapOptions(
        server,
        new ApplicationIdentity(mortgagesIdentityName, mortgagesIdentityObjectId, CanWrite: true),
        new ApplicationIdentity(productsIdentityName, productsIdentityObjectId, CanWrite: false));
}

sealed record BootstrapOptions(
    string Server,
    ApplicationIdentity MortgagesIdentity,
    ApplicationIdentity ProductsIdentity);

sealed record ApplicationIdentity(string Name, Guid ObjectId, bool CanWrite);
