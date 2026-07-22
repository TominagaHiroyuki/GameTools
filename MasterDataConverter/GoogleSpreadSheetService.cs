/**
    @file GoogleSpreadSheetService.cs
    @brief Google SpreadSheet Service
*/

using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Google.Apis.Auth.OAuth2;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace MasterDataConverter;

public static class GoogleSpreadSheetService
{
    /// <summary>
    /// マスター情報
    /// </summary>
    public class MasterManifestEntity
    {
        public string MasterName { get; set; } = string.Empty;
        public string SpreadSheetId {get; set;} = string.Empty;
        public bool IsEnable {get; set;} = false;
    }

    /// <summary>
    /// マスタースキーマ情報
    /// </summary>
    public class MasterSchemaEntity
    {
        public string ColumnName { get; set; } = string.Empty;
        public string ValueType {get; set;} = string.Empty;
        public string Version {get; set;} = string.Empty;
    }

    public enum RowKind
    {
        ColumnName, // 列名
        ValueType, // 値の型
        EnableVersion, // 有効バージョン

        Max,
    }

    /// <summary>
    /// 権限
    /// </summary>
    readonly public static string[] Scope = [SheetsService.Scope.SpreadsheetsReadonly];

    private static SheetsService _service = null!;

    static SheetsService GetService(ICredential credential)
    {
        _service ??= new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
        });

        return _service;
    }

    /// <summary>
    /// シート名一覧を取得する
    /// </summary>
    /// <param name="credential"></param>
    /// <param name="spreadSheetId"></param>
    /// <returns></returns>
    public static async Task<List<string>> GetSheetsNameAsync(ICredential credential, string spreadSheetId)
    {
        if (credential == null)
        {
            return [];
        }

        var service = GetService(credential);
        var res = service.Spreadsheets.Get(spreadSheetId);
        res.Fields = "sheets.properties.title";
        var response = await res.ExecuteAsync();

        var titles = response.Sheets.Select(sheet => sheet.Properties.Title).ToList();

        return titles;
    }

    /// <summary>
    /// 表示名から内部IDに変換
    /// </summary>
    /// <param name="refName"></param>
    /// <param name="target"></param>
    /// <param name="reference"></param>
    /// <returns></returns>
    static string ConvertReferenceValue(string refName, string target, Dictionary<string, object> reference)
    {
        var result = target;

        foreach (var kvp in reference)
        {
            if (kvp.Value is Dictionary<string, object> refValue)
            {
                if (refValue["display"].ToString() == target)
                {
                    result = refValue["value"].ToString(); // 表示用がさしているIDを入れる
                    break;
                }
            }
        }

        return result == null ? string.Empty : result.ToString();
    }

    /// <summary>
    /// valueの型変換を行う
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    static bool ConvertValue(string key, object value, Dictionary<string, Dictionary<string, object>> reference, out object result)
    {
        var isEnableConvert = false;

        result = value;
        // 原則文字列で入っているので、整数化などしておく
        if (value is string str)
        {
            // key名がKey#Refみたいなものになっていた場合、valueは参照用の表示値になっているので、置き換える
            if (key.Contains('#'))
            {
                var refName = key.Split('#')[1];
                foreach (var refData in reference)
                {
                    if ((refData.Value["name"] as string) == refName)
                    {
                        str = ConvertReferenceValue(refName, str, refData.Value);
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(str) == false)
            {
                if (int.TryParse(str, out var intValue))
                {
                    result = intValue;
                }
                isEnableConvert = true;
            }
        }

        return isEnableConvert;
    }

    /// <summary>
    /// シート全体のデータをJsonにSerializeできる形を取得する
    /// $Referenceによる置換あり
    /// </summary>
    /// <param name="credential"></param>
    /// <param name="spreadSheetId"></param>
    public static async Task<Dictionary<string, List<Dictionary<string, object>>>> GetSpreadSheetDataAsync(ICredential credential, string spreadSheetId, Dictionary<string, Dictionary<string, object>>? referenceData = null, Version? targetVersion = null)
    {
        if (credential == null)
        {
            return [];
        }

        var service = GetService(credential);
        var sheetNames = await GetSheetsNameAsync(credential, spreadSheetId);
        referenceData ??= await GetEnumFromReferenceAsync(credential, spreadSheetId, "$Reference");

        var result = new Dictionary<string, List<Dictionary<string, object>>>();

        var request = service.Spreadsheets.Values.BatchGet(spreadSheetId);
        request.Ranges = sheetNames.Where(x => !(x.Contains("Simulation") || x.Contains("INDEX") || x.Contains('$'))).ToList();

        var response = await request.ExecuteAsync();

        foreach (var valueRange in response.ValueRanges)
        {
            var sheetName = valueRange.Range.Split("!")[0];
            var values = valueRange.Values;
            // データがない or Key情報しかないシートは無視
            if (values == null || values.Count < (int)RowKind.Max)
            {
                continue;
            }

           

            var datas = new List<Dictionary<string, object>>();
            var keys = values[(int)RowKind.ColumnName]
                        .Where(x => !string.IsNullOrEmpty(x.ToString()))
                        .Select(x => x.ToString()).ToList();

            foreach (var row in values.Skip((int)RowKind.Max))
            {
                // 不正なデータは除外
                if (row.Count == 0 || string.IsNullOrEmpty(row[0].ToString()))
                {
                    continue;
                }

                var obj = new Dictionary<string, object>();

                for (var i = 0; i < keys.Count; i++)
                {
                    var value = i < row.Count ? row[i] : "";
                    var key = keys[i];

                    if(key != null && key.Contains('$'))
                    {
                        continue; // 対象外のキーの場合は無視
                    }

                    // 有効バージョンがある場合
                    if(values[(int)RowKind.EnableVersion].Count > i && targetVersion != null)
                    {
                        if(Version.TryParse(values[(int)RowKind.EnableVersion][i] as string ?? "1.0.0", out var columnVersion))
                        {
                            if(targetVersion != null && columnVersion > targetVersion)
                            {
                                continue;
                            }
                        }
                    }

                    if (key != null && key.Contains('#'))
                    {
                        key = key.Split('#')[0];    //Key#ReferenceName のような構造になっている
                    }

                    // 同一キーが来たらデータを配列として扱う
                    if (key != null && obj.ContainsKey(key))
                    {
                        if ((obj[key] is List<object>) == false)
                        {
                            var tmp = obj[key];
                            obj[key] = new List<object>();
                            ((List<object>)obj[key]).Add(tmp);
                        }

                        if (obj[key] is List<object> list && keys[i] != null)
                        {
                            if (ConvertValue(keys[i]!, value, referenceData, out var res))
                            {
                                list.Add(res);
                            }
                        }
                    }
                    else
                    {
                        if (ConvertValue(keys[i]!, value, referenceData, out var res))
                        {
                            if(key != null)
                            {
                                obj[key] = res;
                            }
                        }

                    }

                }

                datas.Add(obj);
            }

            result[sheetName] = datas;
        }

        return result;
    }

    /// <summary>
    /// 参照用シートからenumの情報を取得する
    /// </summary>
    /// <param name="credential"></param>
    /// <param name="spreadSheetId"></param>
    /// <param name="sheetName"></param>
    /// <returns></returns>
    public static async Task<Dictionary<string, Dictionary<string, object>>> GetEnumFromReferenceAsync(ICredential credential, string spreadSheetId, string sheetName)
    {
        if (credential == null)
        {
            return [];
        }

        var service = GetService(credential);
        var result = new Dictionary<string, Dictionary<string, object>>();

        var request = service.Spreadsheets.Values.Get(spreadSheetId, sheetName);
        var response = await request.ExecuteAsync();

        var values = response.Values;
        // データがない場合は無視
        if (values == null || values.Count < 3)
        {
            return [];
        }

        /*
        種族#Race human devil other
        ID 0 1 2 3 4
        表示名 人族 魔族 その他
        ↓のような構造にする
        {
            "Race":{
                "name": "種族",
                "human":{
                    "value":0,
                    "display":"人族",
                },
            }
        }
        */

        for (var i = 0; i < values.Count; i += 4)
        {
            // ID行や表示行がない場合は終了
            if ((i + 2) > values.Count)
            {
                break;
            }

            var name = (values[i].Count > 1 ? values[i][0] : "") as string;
            if (name == null || name.Contains('#') == false)
            {
                continue;
            }

            var displayNames = name.Split('#'); // 1番目をenum名として扱う

            // 1行目 = enum名
            // 2行目 = 数値
            // 3行目 = 表示名（コメント用）
            var obj = new Dictionary<string, object>
            {
                ["name"] = displayNames[0],
            };

            for (var j = 1; j < values[i].Count; j++)
            {
                // enum名が空のものは無視
                var key = values[i][j] as string;
                if (string.IsNullOrEmpty(key) == false)
                {
                    var nestValue = new Dictionary<string, object>
                    {
                        ["value"] = values[i + 1][j],
                        ["display"] = values[i + 2][j]
                    };

                    obj.Add(key, nestValue);
                }
            }

            result.Add(displayNames[1], obj);
        }

        return result;
    }

    /// <summary>
    /// シート全体のデータをJsonにSerializeできる形を取得する
    /// $Referenceによる置換あり
    /// </summary>
    /// <param name="credential"></param>
    /// <param name="spreadSheetId"></param>
    public static async Task<List<MasterManifestEntity>> GetMasterManifestAsync(ICredential credential, string spreadSheetId)
    {
        if (credential == null)
        {
            return [];
        }

        var service = GetService(credential);
        var result = new List<MasterManifestEntity>();

        var request = service.Spreadsheets.Values.Get(spreadSheetId, "Manifest");
        var response = await request.ExecuteAsync();

        var values = response.Values;
        // データがない場合は無視
        if (values == null || values.Count < 1)
        {
            return [];
        }

        foreach (var row in values.Skip(1))
        {
            // １列目にデータがない場合はむし
            if(string.IsNullOrEmpty(row[0] as string ?? string.Empty))
            {
                continue;
            }

            var entity = new MasterManifestEntity()
            {
                MasterName = row[0] as string ?? string.Empty,
                SpreadSheetId = row[1] as string ?? string.Empty,
                IsEnable = (row[3] as string ?? "FALSE") == "TRUE",
            };
            result.Add(entity);
        }

        return result;
    }

    public static async Task<Dictionary<string, List<MasterSchemaEntity>>> GetMasterSchemaAsync(ICredential credential, string spreadSheetId, Version? targetVersion = null)
    {
        if (credential == null)
        {
            return [];
        }

        var service = GetService(credential);
        var sheetNames = await GetSheetsNameAsync(credential, spreadSheetId);
        var request = service.Spreadsheets.Values.BatchGet(spreadSheetId);

        request.Ranges = sheetNames.Where(x => !(x.Contains("Simulation") || x.Contains("INDEX") || x.Contains('$'))).ToList();
        var response = await request.ExecuteAsync();

        var result = new Dictionary<string, List<MasterSchemaEntity>>();

        foreach (var valueRange in response.ValueRanges)
        {
            var sheetName = valueRange.Range.Split("!")[0];
            Console.WriteLine($"  SheetName: {sheetName}");
            var values = valueRange.Values;
            // データがない or Key情報しかないシートは無視
            if (values == null || values.Count < (int)RowKind.Max)
            {
                continue;
            }

            var schemas = new List<MasterSchemaEntity>();

            for(var i = 0; i < values[0].Count; i++)
            {
                if(values[(int)RowKind.ColumnName][i] != null 
                && (values[(int)RowKind.ColumnName][i] as string ?? string.Empty).Contains('$'))
                {
                    continue; // 対象外のキーの場合は無視
                }
                var schema = new MasterSchemaEntity()
                {
                    ValueType = values[(int)RowKind.ValueType][i] as string ?? string.Empty,
                };

                // 有効バージョンがある場合
                if(values[(int)RowKind.EnableVersion].Count > i)
                {
                    if(Version.TryParse(values[(int)RowKind.EnableVersion][i] as string ?? "1.0.0", out var columnVersion))
                    {
                        if(targetVersion != null && columnVersion > targetVersion)
                        {
                            continue;
                        }
                        schema.Version = columnVersion.ToString();
                    }
                }

                var name = values[(int)RowKind.ColumnName][i] as string ?? string.Empty;
                if(name.Contains('#'))
                {
                    name = name.Split('#')[0];
                }
                schema.ColumnName = name;

                schemas.Add(schema);
            }

            result[sheetName] = schemas;
        }

        return result;
    }
}