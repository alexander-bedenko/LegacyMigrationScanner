using System.Text;
using LegacyMigrationScanner.Cli;

Console.OutputEncoding = Encoding.UTF8;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C: cancel gracefully (cache is still saved); second: default behavior (terminate).
    if (!cancellation.IsCancellationRequested)
    {
        e.Cancel = true;
        cancellation.Cancel();
    }
};

return await CliApplication.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
