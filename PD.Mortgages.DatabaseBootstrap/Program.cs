using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.CustomerSessions;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Products.Infrastructure.Persistence;
using PD.Notifications.Worker;
using PD.Mortgages.DatabaseBootstrap;

var options = ParseArguments(args);
var bootstrapClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
    ?? throw new InvalidOperationException("AZURE_CLIENT_ID is required for the database bootstrap managed identity.");
var credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(bootstrapClientId));
var token = await credential.GetTokenAsync(
    new TokenRequestContext(["https://database.windows.net/.default"]),
    CancellationToken.None);

foreach (var (databaseName, identity) in new[]
{
    ("mortgages", options.MortgagesIdentity),
    ("products", options.ProductsIdentity),
    ("customer-sessions", options.CustomerSessionsIdentity),
    ("notifications", options.NotificationsIdentity)
})
{
    Console.WriteLine($"Ensuring application access in SQL database '{databaseName}'.");
    await EnsureApplicationAccessAsync(options.Server, databaseName, identity, token.Token);
}

await MigrateMortgageDatabaseAsync(options.Server, token.Token);
await MigrateProductsDatabaseAsync(options.Server, token.Token);
await MigrateCustomerSessionsDatabaseAsync(options.Server, token.Token);
await MigrateNotificationsDatabaseAsync(options.Server, token.Token);
await EnsureOutboxPublisherAccessAsync(options.Server, options.OutboxPublisherIdentity, token.Token);
await using (var source = CreateConnection(options.Server, "mortgages", token.Token))
await using (var target = CreateConnection(options.Server, "notifications", token.Token))
    await HistoricalNotificationCopy.CopyAsync(source, target);

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
    var identitySid = Convert.ToHexString(identity.ClientId.ToByteArray());
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        IF DATABASE_PRINCIPAL_ID(N'{escapedName}') IS NULL
            CREATE USER [{safeName}] WITH SID = 0x{identitySid}, TYPE = E;
        ELSE IF NOT EXISTS (
            SELECT 1 FROM sys.database_principals
            WHERE name = N'{escapedName}' AND type = 'E' AND sid = 0x{identitySid})
            THROW 50000, 'Existing database principal does not match the managed identity client ID.', 1;
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

static async Task EnsureOutboxPublisherAccessAsync(
    string server,
    ApplicationIdentity identity,
    string accessToken)
{
    await using var connection = CreateConnection(server, "mortgages", accessToken);
    await connection.OpenAsync();
    var safeName = identity.Name.Replace("]", "]]", StringComparison.Ordinal);
    var escapedName = identity.Name.Replace("'", "''", StringComparison.Ordinal);
    var identitySid = Convert.ToHexString(identity.ClientId.ToByteArray());
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        IF DATABASE_PRINCIPAL_ID(N'{escapedName}') IS NULL
            CREATE USER [{safeName}] WITH SID = 0x{identitySid}, TYPE = E;
        ELSE IF NOT EXISTS (
            SELECT 1 FROM sys.database_principals
            WHERE name = N'{escapedName}' AND type = 'E' AND sid = 0x{identitySid})
            THROW 50000, 'Existing outbox publisher principal does not match the managed identity client ID.', 1;
        GRANT SELECT, UPDATE ON OBJECT::dbo.SubmissionOutbox TO [{safeName}];
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

static async Task MigrateCustomerSessionsDatabaseAsync(string server, string accessToken)
{
    await using var connection = CreateConnection(server, "customer-sessions", accessToken);
    var options = new DbContextOptionsBuilder<CustomerSessionDbContext>()
        .UseSqlServer(connection)
        .Options;
    await using var context = new CustomerSessionDbContext(options);
    await context.Database.MigrateAsync();
}

static async Task MigrateNotificationsDatabaseAsync(string server, string accessToken)
{
    await using var connection = CreateConnection(server, "notifications", accessToken);
    var options = new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseSqlServer(connection)
        .Options;
    await using var context = new NotificationsDbContext(options);
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
        ConnectTimeout = 120
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
            throw new ArgumentException("Expected --server, --identity-name, and --identity-client-id arguments.");
        }

        values[arguments[index][2..]] = arguments[index + 1];
    }

    if (!values.TryGetValue("server", out var server) ||
        !values.TryGetValue("mortgages-identity-name", out var mortgagesIdentityName) ||
        !values.TryGetValue("mortgages-identity-client-id", out var mortgagesIdentityClientIdText) ||
        !Guid.TryParse(mortgagesIdentityClientIdText, out var mortgagesIdentityClientId) ||
        !values.TryGetValue("products-identity-name", out var productsIdentityName) ||
        !values.TryGetValue("products-identity-client-id", out var productsIdentityClientIdText) ||
        !Guid.TryParse(productsIdentityClientIdText, out var productsIdentityClientId) ||
        !values.TryGetValue("customer-sessions-identity-name", out var customerSessionsIdentityName) ||
        !values.TryGetValue("customer-sessions-identity-client-id", out var customerSessionsIdentityClientIdText) ||
        !Guid.TryParse(customerSessionsIdentityClientIdText, out var customerSessionsIdentityClientId) ||
        !values.TryGetValue("notifications-identity-name", out var notificationsIdentityName) ||
        !values.TryGetValue("notifications-identity-client-id", out var notificationsIdentityClientIdText) ||
        !Guid.TryParse(notificationsIdentityClientIdText, out var notificationsIdentityClientId) ||
        !values.TryGetValue("outbox-publisher-identity-name", out var outboxPublisherIdentityName) ||
        !values.TryGetValue("outbox-publisher-identity-client-id", out var outboxPublisherIdentityClientIdText) ||
        !Guid.TryParse(outboxPublisherIdentityClientIdText, out var outboxPublisherIdentityClientId))
    {
        throw new ArgumentException("Provide --server and valid identity names and client IDs for mortgages, products, customer sessions, notifications, and the outbox publisher.");
    }

    return new BootstrapOptions(
        server,
        new ApplicationIdentity(mortgagesIdentityName, mortgagesIdentityClientId, CanWrite: true),
        new ApplicationIdentity(productsIdentityName, productsIdentityClientId, CanWrite: false),
        new ApplicationIdentity(customerSessionsIdentityName, customerSessionsIdentityClientId, CanWrite: true),
        new ApplicationIdentity(notificationsIdentityName, notificationsIdentityClientId, CanWrite: true),
        new ApplicationIdentity(outboxPublisherIdentityName, outboxPublisherIdentityClientId, CanWrite: false));
}

sealed record BootstrapOptions(
    string Server,
    ApplicationIdentity MortgagesIdentity,
    ApplicationIdentity ProductsIdentity,
    ApplicationIdentity CustomerSessionsIdentity,
    ApplicationIdentity NotificationsIdentity,
    ApplicationIdentity OutboxPublisherIdentity);

sealed record ApplicationIdentity(string Name, Guid ClientId, bool CanWrite);
