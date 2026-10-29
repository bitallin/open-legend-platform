using System.Text.Json;
using Legend.Merge;
using Legend.Storage;
using Microsoft.Data.Sqlite;

string? sourceA = null, sourceB = null, target = null;
bool dryRun = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--source-a" && i + 1 < args.Length) sourceA = args[++i];
    else if (args[i] == "--source-b" && i + 1 < args.Length) sourceB = args[++i];
    else if (args[i] == "--target" && i + 1 < args.Length) target = args[++i];
    else if (args[i] == "--dry-run") dryRun = true;
}

if (string.IsNullOrWhiteSpace(sourceA) || string.IsNullOrWhiteSpace(sourceB) || string.IsNullOrWhiteSpace(target))
{
    Console.Error.WriteLine("用法: Legend.Merge --source-a A.db --source-b B.db --target OUT.db [--dry-run]");
    return 2;
}

var report = await RealmMerger.MergeAsync(sourceA!, sourceB!, target!, dryRun);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return report.Ok ? 0 : 1;
