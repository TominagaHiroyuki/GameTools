/*
    @file ArgsParser.cs
    @brief 引数を解析する
*/

namespace MasterDataImporter;

public static class ArgsParser
{
    public static Dictionary<ArgKind, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<ArgKind, string>();
        
        for(var i = 0; i < args.Length; i++)
        {
            Console.WriteLine(args[i]);
            switch(args[i])
            {
                case "-i":
                    if(i + 1 < args.Length)
                    {
                        result.Add(ArgKind.Input, args[i + 1]);
                    }
                    else
                    {
                        Console.WriteLine("Input file is not specified");
                        return result;
                    }
                    break;
                case "-d":
                    if(i + 1 < args.Length)
                    {
                        result.Add(ArgKind.DB, args[i + 1]);
                    }
                    else
                    {
                        Console.WriteLine("DB connection string is not specified");
                        return result;
                    }
                    break;
                case "-h":
                    Console.WriteLine("Usage: MasterDataImporter -i <input_file> -db <db_connection_string>");
                    Console.WriteLine("Example: MasterDataImporter -i data.json -db Host=localhost;Port={port};Database={dbname};Username={username};Password={password}");
                    Console.WriteLine("Options:");
                    Console.WriteLine("  -i  <input_file> Input file path (Required");
                    Console.WriteLine("  -db <db_connection_string>    DB connection string (Required)");
                    Console.WriteLine("  -h  Show help");
                    Environment.Exit(0);
                    break;
                default:
                    break;
            }
        }
        return result;
    }
}