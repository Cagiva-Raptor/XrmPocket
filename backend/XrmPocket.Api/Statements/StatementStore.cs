using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace XrmPocket.Api.Statements;

/// <summary>
/// Eigen opgeslagen SQL-statements. Opslag: SQLite in ~/.config/xrmpocket/statements.db.
/// De inhoud (SQL-tekst) is versleuteld met AES-GCM; de sleutel staat in
/// ~/.config/xrmpocket/statements.key en wordt bij de eerste keer automatisch gemaakt.
/// Namen en tijdstippen blijven leesbaar voor het overzicht.
/// </summary>
public sealed class StatementStore
{
    private readonly string _dbPath;
    private readonly string _keyPath;
    private readonly byte[] _key;
    private readonly object _gate = new();

    public StatementStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "xrmpocket");
        Directory.CreateDirectory(folder);

        _dbPath = Path.Combine(folder, "statements.db");
        _keyPath = Path.Combine(folder, "statements.key");
        _key = LoadOrCreateKey();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS statements (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                body BLOB NOT NULL,
                created_on TEXT NOT NULL,
                kind TEXT NOT NULL DEFAULT 'sql'
            );
            """;
        command.ExecuteNonQuery();

        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('statements') WHERE name = 'kind'";
        if (Convert.ToInt32(columns.ExecuteScalar()) == 0)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE statements ADD COLUMN kind TEXT NOT NULL DEFAULT 'sql'";
            migrate.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredStatement> List(string kind)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, body, created_on FROM statements WHERE kind = $kind ORDER BY name COLLATE NOCASE";
            command.Parameters.AddWithValue("$kind", kind);

            var result = new List<StoredStatement>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                result.Add(new StoredStatement(
                    reader.GetString(0),
                    reader.GetString(1),
                    Decrypt((byte[])reader["body"]),
                    reader.GetString(3)));
            }

            return result;
        }
    }

    public StoredStatement Save(string name, string sql, string kind = "sql")
    {
        lock (_gate)
        {
            var statement = new StoredStatement(
                Guid.NewGuid().ToString(),
                name.Trim(),
                sql,
                DateTimeOffset.UtcNow.ToString("o"));

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO statements (id, name, body, created_on, kind) VALUES ($id, $name, $body, $created, $kind)";
            command.Parameters.AddWithValue("$id", statement.Id);
            command.Parameters.AddWithValue("$name", statement.Name);
            command.Parameters.AddWithValue("$body", Encrypt(sql));
            command.Parameters.AddWithValue("$created", statement.CreatedOn);
            command.Parameters.AddWithValue("$kind", kind);
            command.ExecuteNonQuery();

            return statement;
        }
    }

    public StoredStatement? Update(string id, string sql)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE statements SET body = $body WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$body", Encrypt(sql));
            if (command.ExecuteNonQuery() == 0)
                return null;

            using var read = connection.CreateCommand();
            read.CommandText = "SELECT id, name, body, created_on FROM statements WHERE id = $id";
            read.Parameters.AddWithValue("$id", id);
            using var reader = read.ExecuteReader();
            if (!reader.Read()) return null;
            return new StoredStatement(reader.GetString(0), reader.GetString(1), Decrypt((byte[])reader["body"]), reader.GetString(3));
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM statements WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
            return Convert.FromBase64String(File.ReadAllText(_keyPath).Trim());

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_keyPath, Convert.ToBase64String(key));
        return key;
    }

    /// <summary>Formaat: nonce (12) + tag (16) + cipher-tekst.</summary>
    private byte[] Encrypt(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return nonce.Concat(tag).Concat(cipher).ToArray();
    }

    private string Decrypt(byte[] payload)
    {
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = payload[..nonceSize];
        var tag = payload[nonceSize..(nonceSize + tagSize)];
        var cipher = payload[(nonceSize + tagSize)..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, tagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}

public sealed record StoredStatement(string Id, string Name, string Sql, string CreatedOn);

public sealed record SaveStatementRequest(string? Name, string? Sql, string? Kind = null);
