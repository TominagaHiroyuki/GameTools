/*
    @file SchemaSqlGenerator.cs
    @brief スキーマをSQLに変換するクラス
*/

using System.Reflection;
using System;
using System.Text;

namespace MasterDataImporter;

public static class SchemaSqlGenerator
{
    /// <summary>
    /// テーブル作成SQLを生成する
    /// </summary>
    /// <returns></returns>
    public static List<string> GenerateCreateTableCommands()
    {
        var commands = new List<string>();
        var schemaTypes = typeof(MasterDataSchemas);

        foreach (var property in schemaTypes.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var elementType = property.PropertyType.GetElementType();
            if(elementType == null)
            {
                continue;
            }
            commands.Add(GenerateCreateTableCommand(property.Name, elementType));
        }

        return commands;
    }

    /// <summary>
    /// テーブル作成SQLを生成する
    /// </summary>
    /// <param name="tableName"></param>
    /// <param name="schemaType"></param>
    /// <returns></returns>
    public static string GenerateCreateTableCommand(string tableName, Type schemaType)
    {
        var columns = new List<string>();
        var properties = schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // IDをPrimary Keyにしておく
        foreach(var property in properties)
        {
            var sqlType = ConvertToSqlType(property.PropertyType.Name);
            var name = $"\"{property.Name}\"";
            var column = $"{name} {sqlType}";
            if(property.Name == "ID")
            {
                column = $"{column} PRIMARY KEY";
            }
            columns.Add(column);
        }

        var sb = new StringBuilder();
        sb.Append($"CREATE TABLE IF NOT EXISTS \"{tableName}\" (");
        sb.Append("    " + string.Join(", ", columns));
        sb.Append(");");

        return sb.ToString();
    }

    /// <summary>
    /// 型名をSQLの型名に変換する
    /// </summary>
    /// <param name="typeName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static string ConvertToSqlType(string typeName)
    {
        return typeName switch 
        {
            "Int32" or "int" => "INTEGER",
            "Int64" or "long" => "BIGINT",
            "String" or "string" => "TEXT",
            "Boolean" or "bool" => "BOOLEAN",
            "Float" or "float" => "REAL",
            "Double" or "double" => "DOUBLE PRECISION",
            "Decimal" or "decimal" => "DECIMAL",
            _ => throw new Exception($"Unknown type: {typeName}"),
        };
    }

    public static Dictionary<string, string> GenerateUpsertCommand()
    {
        var commands = new Dictionary<string, string>();
        var schemaTypes = typeof(MasterDataSchemas);

        foreach (var property in schemaTypes.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var elementType = property.PropertyType.GetElementType();
            if(elementType == null)
            {
                continue;
            }
            commands.Add(property.Name, GenerateUpsertCommand(property.Name, elementType));
        }

        return commands;
    }

    /// <summary>
    /// Upsert SQLを生成する
    /// </summary>
    /// <param name="tableName"></param>
    /// <param name="schemaType"></param>
    /// <returns></returns>
    public static string GenerateUpsertCommand(string tableName, Type schemaType)
    {
        var names = new List<string>();
        var unnestNames = new List<string>();
        var properties = schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // 名前を取得
        foreach(var property in properties)
        {
            names.Add($"\"{property.Name}\"");
            unnestNames.Add($"@{property.Name.ToLower()}s"); // 小文字 + 複数形s
        }

        var sb = new StringBuilder();
        sb.Append($"INSERT INTO \"{tableName}\" (");
        sb.Append(string.Join(", ", names));
        sb.Append(") ");
        sb.Append($"SELECT * FROM UNNEST({string.Join(", ", unnestNames)}) ");
        sb.Append("ON CONFLICT (\"ID\") DO UPDATE SET ");

        var updateColumns = names.Where(name => name != "\"ID\"").ToList();
        for(var i = 0; i < updateColumns.Count; i++)
        {
            if(i > 0)
            {
                sb.Append(", ");
            }
            sb.Append($"{updateColumns[i]} = EXCLUDED.{updateColumns[i]}");
        }
        sb.Append(';');

        return sb.ToString();
    }
}