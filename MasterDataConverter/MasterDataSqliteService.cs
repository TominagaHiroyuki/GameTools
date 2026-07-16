/**
    @file MasterDataSqliteService.cs
    @brief マスターデータを SQLite ファイルとしてエクスポートする
*/

using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using static MasterDataConverter.GoogleSpreadSheetService;

namespace MasterDataConverter;

/// <summary>
/// スキーマ定義に基づき SQLite DB を作成し、データを挿入してファイル保存する
/// </summary>
public static class MasterDataSqliteService
{
    /// <summary>
    /// スキーマと行データから SQLite DB を生成し、指定パスに保存する
    /// </summary>
    /// <param name="outputFilePath">出力する .db ファイルパス</param>
    /// <param name="schemaBySheet">シート名 → カラム定義</param>
    /// <param name="dataBySheet">シート名 → 行データ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public static async Task ExportAsync(
        string outputFilePath,
        Dictionary<string, List<MasterSchemaEntity>> schemaBySheet,
        Dictionary<string, List<Dictionary<string, object>>> dataBySheet,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrEmpty(directory) == false)
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        await using var connection = new SqliteConnection($"Data Source={outputFilePath}");
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var (sheetName, schemas) in schemaBySheet)
        {
            if (schemas.Count == 0)
            {
                continue;
            }

            var tableName = SanitizeIdentifier(sheetName);
            await CreateTableAsync(connection, transaction, tableName, schemas, cancellationToken);

            if (dataBySheet.TryGetValue(sheetName, out var rows) == false || rows.Count == 0)
            {
                continue;
            }

            await InsertRowsAsync(connection, transaction, tableName, schemas, rows, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// スキーマに基づいてテーブルを作成する
    /// </summary>
    static async Task CreateTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        List<MasterSchemaEntity> schemas,
        CancellationToken cancellationToken)
    {
        var columnDefs = schemas
            .Where(s => string.IsNullOrWhiteSpace(s.ColumnName) == false)
            .Select(s => $"\"{SanitizeIdentifier(s.ColumnName)}\" {MapSqliteType(s.ValueType)} {MapSqlitePrimaryKeyType(s.ColumnName)}")
            .ToList();

        if (columnDefs.Count == 0)
        {
            return;
        }

        var sql = $"CREATE TABLE IF NOT EXISTS \"{tableName}\" ({string.Join(", ", columnDefs)});";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 行データをテーブルに挿入する
    /// </summary>
    static async Task InsertRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        List<MasterSchemaEntity> schemas,
        List<Dictionary<string, object>> rows,
        CancellationToken cancellationToken)
    {
        var columns = schemas
            .Where(s => string.IsNullOrWhiteSpace(s.ColumnName) == false)
            .Select(s => SanitizeIdentifier(s.ColumnName))
            .ToList();

        if (columns.Count == 0)
        {
            return;
        }

        var columnList = string.Join(", ", columns.Select(c => $"\"{c}\""));
        var parameterList = string.Join(", ", columns.Select(c => $"@{c}"));
        var insertSql = $"INSERT INTO \"{tableName}\" ({columnList}) VALUES ({parameterList});";

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = insertSql;

            foreach (var schema in schemas)
            {
                if (string.IsNullOrWhiteSpace(schema.ColumnName))
                {
                    continue;
                }

                var columnName = SanitizeIdentifier(schema.ColumnName);
                row.TryGetValue(schema.ColumnName, out var rawValue);
                command.Parameters.AddWithValue($"@{columnName}", ToSqliteValue(rawValue, schema.ValueType));
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// スプレッドシートの型名を SQLite の型に変換する
    /// </summary>
    static string MapSqliteType(string valueType)
    {
        var normalized = valueType.Trim().ToLowerInvariant();

        return normalized switch
        {
            "int" or "integer" or "long" or "int32" or "int64" => "INTEGER",
            "float" or "double" or "number" or "real" => "REAL",
            "bool" or "boolean" => "INTEGER",
            _ => "TEXT",
        };
    }

    /// <summary>
    /// 主キーの型を SQLite の型に変換する
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    static string MapSqlitePrimaryKeyType(string name)
    {
        if(name == "ID")
        {
            return "PRIMARY KEY AUTOINCREMENT";
        }

        return "";
    }

    /// <summary>
    /// C# の値を SQLite パラメータ用の値に変換する
    /// </summary>
    static object? ToSqliteValue(object? value, string valueType)
    {
        if (value == null)
        {
            return DBNull.Value;
        }

        if (value is List<object> list)
        {
            return JsonConvert.SerializeObject(list);
        }

        var normalized = valueType.Trim().ToLowerInvariant();

        if (normalized is "int" or "integer" or "long" or "int32" or "int64")
        {
            if (value is int or long)
            {
                return value;
            }

            if (int.TryParse(value.ToString(), out var intValue))
            {
                return intValue;
            }
        }

        if (normalized is "float" or "double" or "number" or "real")
        {
            if (value is double or float)
            {
                return value;
            }

            if (double.TryParse(value.ToString(), out var doubleValue))
            {
                return doubleValue;
            }
        }

        if (normalized is "bool" or "boolean")
        {
            if (value is bool boolValue)
            {
                return boolValue ? 1 : 0;
            }

            if (bool.TryParse(value.ToString(), out var parsedBool))
            {
                return parsedBool ? 1 : 0;
            }
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// テーブル名・カラム名として使える識別子に整形する
    /// </summary>
    static string SanitizeIdentifier(string name)
    {
        var chars = name
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();

        var sanitized = new string(chars);

        if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }
}
