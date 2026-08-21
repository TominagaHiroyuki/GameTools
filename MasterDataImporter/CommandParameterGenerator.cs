/*
    @file CommandParameterGenerator.cs
    @brief コマンドパラメータを生成する
*/

using System.Reflection;
using Npgsql;

namespace MasterDataImporter;

public static class CommandParameterGenerator
{
    /// <summary>
    /// Upsertコマンドのパラメータを生成する
    /// </summary>
    /// <param name="command"></param>
    /// <param name="tableName"></param>
    /// <param name="masterData"></param>
    public static void GenerateParameters(NpgsqlCommand command, string tableName, MasterDataSchemas masterData)
    {
        var properties = masterData.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var property = properties.FirstOrDefault(p => p.Name == tableName);

        if(property is null)
        {
            return;
        }

        var elementType = property.PropertyType.GetElementType();
        if(elementType is null)
        {
            return;
        }

        var rows = property.GetValue(masterData) as Array ?? Array.Empty<object>();
        if(rows is null || rows.Length == 0)
        {
            return;
        }

        var typeProperties = elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // 名前を取得
        foreach(var typeProperty in typeProperties)
        {
            var parameterName = $"@{typeProperty.Name.ToLower()}s"; // 小文字 + 複数形s
            
            var values = Array.CreateInstance(typeProperty.PropertyType, rows.Length);
            for(var i = 0; i < rows.Length; i++)
            {
                var row = rows.GetValue(i);
                values.SetValue(typeProperty.GetValue(row), i);
            }
            command.Parameters.AddWithValue(parameterName, values);
        }
    }
}