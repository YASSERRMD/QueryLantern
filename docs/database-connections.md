# Database connection shapes (Phase 5)

QueryLantern connects to each engine through a matching `IDatabaseAdapter`. The connection form
captures the same fields for every engine: host, port, database, username, and a password that is
encrypted at rest. The engine specific connection string is built internally. This note records the
default ports and the connection string shapes each adapter produces.

| Engine | Default port | Provider | Connection string shape |
| --- | --- | --- | --- |
| PostgreSQL | 5432 | Npgsql | `Host=..;Port=5432;Database=..;Username=..;Password=..` |
| MySQL or MariaDB | 3306 | MySqlConnector | `Server=..;Port=3306;Database=..;User ID=..;Password=..;AllowUserVariables=true` |
| SQL Server | 1433 | Microsoft.Data.SqlClient | `Server=..,1433;Database=..;User Id=..;Password=..` |
| Oracle | 1521 | Oracle.ManagedDataAccess | `Data Source=host:1521/service;User Id=..;Password=..` |
| SQLite | n/a | Microsoft.Data.Sqlite | `Data Source=/path/to/file.db` or `:memory:` |
| DuckDB | n/a | DuckDB.NET | file path or `:memory:` |
| ClickHouse | 8443 | ClickHouse.Client | `Host=..;Port=8443;Database=..;User=..;Password=..` |
| ODBC | varies | System.Data.Odbc | `DSN=..` or a full ODBC connection string |

The `Options` field on a connection profile is appended verbatim to the generated connection string,
so you can add engine specific flags such as `sslmode=require` for PostgreSQL.

## Oracle: service name vs SID and TNS

For Oracle the `Database` field holds the service name (the `host:port/service` form). If you need
to connect by SID instead, put the SID in `Database` and the `Options` field will be used verbatim as
the `Data Source`, for example `SID=orcl`. You can also paste a full TNS alias into `Options` and
leave `Database` empty; the adapter prefers `Options` for the data source when it is present.

All reads and writes go through the adapter's parameterised execution path. The adapter never
string concatenates user values into SQL.
