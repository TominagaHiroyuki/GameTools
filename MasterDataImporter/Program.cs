/*
    @file Program.cs
    @brief エントリポイント
*/

using MasterDataImporter;
using System.Diagnostics;

var sw = new Stopwatch();
var datas = ArgsParser.ParseArgs(Environment.GetCommandLineArgs());

Console.WriteLine("MasterData Importer");
Console.WriteLine("Start!");
sw.Start();

if(datas.TryGetValue(ArgKind.Input, out var inputFile)
&& datas.TryGetValue(ArgKind.DB, out var dbConnectionString))
{
    var importer = new Importer(inputFile, dbConnectionString);
    if(importer.Initialize())
    {
        importer.Execute();
    }
    else
    {
        Console.WriteLine("Failed to initialize master data");
        Environment.Exit(1);
    }
}
else
{
    Console.WriteLine("Input file or DB connection string is not specified");
    Environment.Exit(1);
}

sw.Stop();

Console.WriteLine("--------------------------------");
Console.WriteLine($"Finished: {sw.Elapsed.TotalSeconds} seconds");
Console.WriteLine("--------------------------------");
Console.WriteLine();
Console.WriteLine("End");