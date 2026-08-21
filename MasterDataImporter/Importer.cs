/* 
    @file Importer.cs
    @brief マスターデータインポーター本体
*/


using Newtonsoft.Json;
using Npgsql;
using System.Diagnostics;

namespace MasterDataImporter;

public class Importer
{
    private MasterDataSchemas? _masterData = null;
    private readonly string _inputFile = string.Empty;
    private readonly string _dbConnectionString = string.Empty;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="dbConnectionString"></param>
    public Importer(string inputFile, string dbConnectionString)
    {
        if(string.IsNullOrEmpty(inputFile))
        {
            throw new Exception("Input file is not specified");
        }
        if(string.IsNullOrEmpty(dbConnectionString))
        {
            throw new Exception("DB connection string is not specified");
        }
        
        _inputFile = inputFile;
        _dbConnectionString = dbConnectionString;
    }

    /// <summary>
    /// 初期化
    /// </summary>
    /// <exception cref="Exception"></exception>
    public bool Initialize()
    {
        var json = File.ReadAllText(_inputFile);
        _masterData = JsonConvert.DeserializeObject<MasterDataSchemas>(json);
        if (_masterData is null)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// インポート実行
    /// </summary>
    public void Execute()
    {
        var sw = new Stopwatch();

        if(_masterData is null)
        {
            throw new Exception("Master data is not initialized");
        }

        using var connection = new NpgsqlConnection(_dbConnectionString);
        using var command = connection.CreateCommand();

        connection.Open();

        var transaction = connection.BeginTransaction();
        {
            Console.WriteLine("--------------------------------");
            Console.WriteLine("Execute: Create Tables");
            sw.Start();
            // テーブル作成
            var createTableCommands = SchemaSqlGenerator.GenerateCreateTableCommands();
            foreach(var createTableCommand in createTableCommands)
            {
                command.CommandText = createTableCommand;
                command.ExecuteNonQuery();
            }
            sw.Stop();
            Console.WriteLine($"Finished: Create Tables {sw.Elapsed.TotalSeconds} seconds");

            Console.WriteLine("--------------------------------");
            Console.WriteLine("Execute: Upsert");
            sw.Restart();
            // パラメータをUpsert
            var upsertCommands = SchemaSqlGenerator.GenerateUpsertCommand();
            foreach(var kvp in upsertCommands)
            {
                command.Parameters.Clear();
                command.CommandText = kvp.Value;
                CommandParameterGenerator.GenerateParameters(command, kvp.Key, _masterData);
                command.ExecuteNonQuery();
            }
            sw.Stop();
            Console.WriteLine($"Finished: Upsert {sw.Elapsed.TotalSeconds} seconds");
        }
        transaction.Commit();

        connection.Close();
    }
}