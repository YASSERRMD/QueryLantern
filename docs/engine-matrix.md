# Engine completeness matrix (Phase 7)

Every supported database engine implements the full `IDatabaseAdapter` contract: open, test,
introspect, read, and write. The matrix below records what each engine supports through QueryLantern.

| Engine | Connect | Test | Introspect | Read (parameterised) | Write (after approval) | Provider |
| --- | --- | --- | --- | --- | --- | --- |
| PostgreSQL | yes | yes | yes | yes | yes | Npgsql |
| MySQL / MariaDB | yes | yes | yes | yes | yes | MySqlConnector |
| SQL Server | yes | yes | yes | yes | yes | Microsoft.Data.SqlClient |
| Oracle (12c+) | yes | yes | yes | yes | yes | Oracle.ManagedDataAccess |
| SQLite | yes | yes | yes | yes | yes | Microsoft.Data.Sqlite |
| DuckDB | yes | yes | yes | yes | yes | DuckDB.NET.Data |
| ClickHouse | yes | yes | yes | yes | yes | ClickHouse.Client |
| ODBC (fallback) | yes | yes | best effort | yes | yes | System.Data.Odbc |

## Notes

- ODBC introspection relies on the driver exposing the standard `Tables` and `Columns` schema
  collections. Some drivers expose limited metadata, in which case introspection returns what the
  driver provides.
- ClickHouse is append and analytics oriented. Mutating statements such as `INSERT` run through the
  same approval gate as every other engine, but `UPDATE` / `DELETE` are not supported by ClickHouse
  itself.
- DuckDB runs fully in process. Point it at a file, a parquet file, or use `:memory:` for scratch
  analytics with `read_csv_auto` and `read_parquet`.
