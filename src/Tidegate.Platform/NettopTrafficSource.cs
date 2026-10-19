using System.Runtime.CompilerServices;
using System.Text;
using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class NettopTrafficSource : ITrafficSource, IDisposable
{
    private Process? _process;

    public void Dispose()
    {
        var process = _process;
        if (process is not null)
            Stop(process);
    }

    public async IAsyncEnumerable<IReadOnlyList<ProcessTraffic>> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/script",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // nettop block-buffers when stdout is a pipe. script gives it a terminal
        // so each sample is flushed, and -q keeps script's own banner out.
        foreach (var argument in new[]
        {
            "-q", "/dev/null", "/usr/bin/nettop",
            "-P", "-L", "0", "-s", "1", "-x", "-J", "bytes_in,bytes_out", "-t", "external",
        })
            process.StartInfo.ArgumentList.Add(argument);

        _process = process;
        if (!process.Start())
            throw new IOException("無法啟動 nettop。");

        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                stderr.AppendLine(args.Data);
        };
        process.BeginErrorReadLine();

        var rows = new List<ProcessTraffic>();
        var sawHeader = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    if (rows.Count == 0 && !sawHeader)
                        throw new IOException($"nettop 已結束（{process.ExitCode}）。{stderr}");
                    yield break;
                }

                line = TerminalText.Clean(line);
                if (line.Length == 0)
                    continue;

                if (line.StartsWith(",bytes_in", StringComparison.OrdinalIgnoreCase))
                {
                    if (sawHeader)
                    {
                        yield return rows.ToArray();
                        rows.Clear();
                    }

                    sawHeader = true;
                    continue;
                }

                if (NettopCsvParser.TryParseLine(line, out var row))
                    rows.Add(row);
            }
        }
        finally
        {
            Stop(process);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
