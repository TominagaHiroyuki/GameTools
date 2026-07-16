/**
* @file Program.cs
* @brief MasterDataConverter
*/

using YamlDotNet.Serialization;
using System.Diagnostics;
using Newtonsoft.Json;

namespace MasterDataConverter;

/// <summary>
/// Main Class
/// </summary>
class Program
{
    // 引数でもらう
    readonly static string DefaultSpreadSheetId = "xxxxx-xxxxx-xxxxx-xxxxx-xxxxx-xxxxx";

    public enum ArgsKind
    {
        SpreadSheetId,  // スプレッドシートID
        OutputDir,      // 出力ディレクトリ
        KeyPath,        // キーのパス
        ExportType,     // 出力タイプ(yaml, db, json)
        Mode,           // モード(Data, Schema, Enum, All)
        Help,           // ヘルプ
    }

    [Flags]
    public enum ModeKind
    {
        Data = 1 << 0,
        Schema = 1 << 1,
        Enum = 1 << 2,
        All = Data | Schema | Enum,
    }

    static async Task<int> Main(string[] args)
    {
        var parsedArgs = ParseArgs(args);
        var sw = new Stopwatch();
        sw.Start();

        Console.WriteLine("MasterDataConverter");
        Console.WriteLine();
        Console.WriteLine("Start");

        var mode = ModeKind.All;
        if(parsedArgs.TryGetValue(ArgsKind.Mode, out var modeValue))
        {
            if(Enum.TryParse(modeValue, out ModeKind modeKind))
            {
                mode = modeKind;
            }
        }

        Console.WriteLine($"  Mode: {mode}");

        var outputDir = "../../../output";
        if(parsedArgs.TryGetValue(ArgsKind.OutputDir, out var outputDirValue))
        {
            outputDir = outputDirValue;
        }
        if(Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, true);
        }
        Directory.CreateDirectory(outputDir);

        var keyPath = "../../../key.json";
        if(parsedArgs.TryGetValue(ArgsKind.KeyPath, out var keyPathValue))
        {
            keyPath = keyPathValue;
        }
        var spreadSheetId = DefaultSpreadSheetId;
        if(parsedArgs.TryGetValue(ArgsKind.SpreadSheetId, out var spreadSheetIdValue))
        {
            spreadSheetId = spreadSheetIdValue;
        }

        

        var credential = GoogleAuthService.GetCredential(keyPath, GoogleSpreadSheetService.Scope);
        var masterManifest = await GoogleSpreadSheetService.GetMasterManifestAsync(credential, spreadSheetId);
        var referenceData = await GoogleSpreadSheetService.GetEnumFromReferenceAsync(credential, spreadSheetId, "$Reference");

        var sheetDatas = new Dictionary<string, List<Dictionary<string, object>>>();
        var schemaDatas = new Dictionary<string, List<GoogleSpreadSheetService.MasterSchemaEntity>>();
        var schemaBySheet = new Dictionary<string, Dictionary<string, List<GoogleSpreadSheetService.MasterSchemaEntity>>>();

        foreach (var master in masterManifest)
        {
            if (master.IsEnable == false)
            {
                continue;
            }

            Console.WriteLine($"Export: {master.MasterName} ({master.SpreadSheetId})");

            var sw1 = new Stopwatch();
            sw1.Start();
            var schema = await GoogleSpreadSheetService.GetMasterSchemaAsync(credential, master.SpreadSheetId);
            sw1.Stop();
            Console.WriteLine($"  GetMasterSchemaAsync: {sw1.Elapsed.TotalMilliseconds} ms");

            var sw2 = new Stopwatch();
            sw2.Start();
            var rowDatas = await GoogleSpreadSheetService.GetSpreadSheetDataAsync(credential, master.SpreadSheetId, referenceData);
            sw2.Stop();
            Console.WriteLine($"  GetSpreadSheetDataAsync: {sw2.Elapsed.TotalMilliseconds} ms");

            foreach (var (sheetName, data) in rowDatas)
            {
                sheetDatas.Add(sheetName, data);
            }

            var schemaDict = new Dictionary<string, List<GoogleSpreadSheetService.MasterSchemaEntity>>();
            foreach (var (sheetName, schemas) in schema)
            {
                schemaDatas.Add(sheetName, schemas);
                schemaDict.Add(sheetName, schemas);
            }

            schemaBySheet.Add(master.MasterName, schemaDict);

            if(mode.HasFlag(ModeKind.Schema))
            {
                var sw3 = new Stopwatch();
                sw3.Start();
                var serializer = new SerializerBuilder().Build();
                var yamlPath = Path.Combine(outputDir, $"{master.MasterName}.yaml");
                Console.WriteLine($"  YAML File Exported: {Path.GetFullPath(yamlPath)}");
                await File.WriteAllTextAsync(yamlPath, serializer.Serialize(schema));
                sw3.Stop();
                Console.WriteLine($"  YAML File Exported: {sw3.Elapsed.TotalMilliseconds} ms");
            }
        }

        if(mode.HasFlag(ModeKind.Data))
        {
            if(parsedArgs.TryGetValue(ArgsKind.ExportType, out var exportType))
            {
                Console.WriteLine($"  Export Type: {exportType}");
                await ExportMasterData(outputDir, sheetDatas, schemaDatas, exportType);
            }  
        }
        
        if(mode.HasFlag(ModeKind.Schema))
        {
            foreach(var (masterName, schemas) in schemaBySheet)
            {
                MasterDataCodeGenerator.GenerateCode(outputDir, schemas, masterName);
                Console.WriteLine($"  Code File Exported: {Path.GetFullPath(Path.Combine(outputDir, $"{masterName}.cs"))}");    
            }
        }
        

        // enum string export
        if(mode.HasFlag(ModeKind.Enum))
        {
            var enumData = await GoogleSpreadSheetService.GetEnumFromReferenceAsync(credential, spreadSheetId, "$Reference");
            MasterDataCodeGenerator.CreateEnumString(outputDir, "MasterDataDefine", enumData);
            Console.WriteLine($"  Enum File Exported: {Path.GetFullPath(Path.Combine(outputDir, "MasterDataDefine.cs"))}");
        }

        sw.Stop();

        Console.WriteLine("--------------------------------");
        Console.WriteLine($"Success: {sw.Elapsed.TotalSeconds} seconds");
        Console.WriteLine("--------------------------------");
        Console.WriteLine();
        Console.WriteLine("End");

        return 0;
    }

    /// <summary>
    /// 引数を解析する
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public static Dictionary<ArgsKind, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<ArgsKind, string>();

        // デフォルトはdb出力
        result[ArgsKind.ExportType] = "db";
        result[ArgsKind.Mode] = "All";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch(arg)
            {
                case "-s":
                    if(i + 1 < args.Length)
                    {
                        result[ArgsKind.SpreadSheetId] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: -s is required");
                        Environment.Exit(1);
                    }
                    break;
                case "-o":
                    if(i + 1 < args.Length)
                    {
                        result[ArgsKind.OutputDir] = args[i + 1];
                    }
                    break;
                case "-k":
                    if(i + 1 < args.Length)
                    {
                        result[ArgsKind.KeyPath] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: -k is required");
                        Environment.Exit(1);
                    }
                    break;
                case "-t":
                    if(i + 1 < args.Length)
                    {
                        result[ArgsKind.ExportType] = args[i + 1];
                        i++;
                    }
                    break;
                case "-m":
                    if(i + 1 < args.Length)
                    {
                        result[ArgsKind.Mode] = args[i + 1];
                        i++;
                    }
                    break;
                case "-h":
                    Console.WriteLine("Usage: MasterDataConverter -s <spreadsheet_id> -o <output_dir> -m <mode> -k <key_path>");
                    Console.WriteLine("  -s: Spreadsheet ID");
                    Console.WriteLine("  -o: Output Directory");
                    Console.WriteLine("  -k: Key Path");
                    Console.WriteLine("  -t: Export Type (yaml, db, json)");
                    Console.WriteLine("  -m: Mode (Data, Schema, Enum, All)");
                    Console.WriteLine("  -h: Help");
                    Environment.Exit(0);
                    break;
                default:
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// マスターデータを出力する
    /// </summary>
    /// <param name="outputDir"></param>
    /// <param name="sheetDatas"></param>
    /// <param name="schemaDatas"></param>
    /// <param name="exportType"></param>
    /// <returns></returns>
    public static async Task ExportMasterData(string outputDir, Dictionary<string, List<Dictionary<string, object>>> sheetDatas, Dictionary<string, List<GoogleSpreadSheetService.MasterSchemaEntity>> schemaDatas, string exportType)
    {
        switch(exportType)
        {
            case "yaml":
                var serializer = new SerializerBuilder().Build();
                var yamlPath = Path.Combine(outputDir, "master.yaml");
                await File.WriteAllTextAsync(yamlPath, serializer.Serialize(sheetDatas));
                Console.WriteLine($"  YAML File Exported: {Path.GetFullPath(yamlPath)}");
                break;
            case "db":
                var dbPath = Path.Combine(outputDir, "master.db");
                await MasterDataSqliteService.ExportAsync(dbPath, schemaDatas, sheetDatas);
                File.Copy(dbPath, Path.Combine(outputDir, "master.bytes"), true);
                Console.WriteLine($"  DB File Exported: {Path.GetFullPath(dbPath)}");
                break;
            case "json":
                var jsonPath = Path.Combine(outputDir, "master.json");
                await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(sheetDatas));
                Console.WriteLine($"  JSON File Exported: {Path.GetFullPath(jsonPath)}");
                break;
            default:
                Console.WriteLine("Error: Invalid export type");
                Environment.Exit(1);
                break;
        }
    }
    
}