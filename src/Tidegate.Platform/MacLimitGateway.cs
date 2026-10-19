using System.Text;
using Tidegate.Core;

namespace Tidegate.Platform;

public sealed class MacLimitGateway : ILimitGateway
{
    private readonly string _directory;
    private readonly object _gate = new();
    private string _signature = "";

    public MacLimitGateway(string directory)
    {
        _directory = directory;
    }

    public Task<LimitApplyResult> ApplyAsync(LimitPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.HasWork)
            return Task.FromResult(new LimitApplyResult(false, "沒有可寫入的規則。"));

        WritePlan(plan, force: true);
        var tokenPath = Path.Combine(_directory, "pf.token");
        var script = LimitScript.BuildApply(_directory, tokenPath);
        return RunAsync(script, cancellationToken);
    }

    public Task<LimitApplyResult> ClearAsync(CancellationToken cancellationToken)
    {
        var tokenPath = Path.Combine(_directory, "pf.token");
        return RunAsync(LimitScript.BuildClear(tokenPath), cancellationToken);
    }

    public void Stage(LimitPlan plan) => WritePlan(plan, force: false);

    public void StopTracking()
    {
        var flag = Path.Combine(_directory, "helper.on");
        if (File.Exists(flag))
            File.Delete(flag);
    }

    private void WritePlan(LimitPlan plan, bool force)
    {
        var pipes = string.Join('\n', plan.Pipes.Select(LimitScript.FormatPipeLine));
        if (pipes.Length > 0)
            pipes += "\n";
        var rules = plan.Rules.Count == 0 ? "" : string.Join('\n', plan.Rules) + "\n";
        var signature = pipes + "\n--\n" + rules;
        lock (_gate)
        {
            if (!force && signature == _signature)
                return;

            Directory.CreateDirectory(_directory);
            var encoding = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(_directory, "pipes.txt"), pipes, encoding);
            var rulesPath = Path.Combine(_directory, "rules.pf");
            var temporary = rulesPath + ".tmp";
            File.WriteAllText(temporary, rules, encoding);
            File.Move(temporary, rulesPath, overwrite: true);
            _signature = signature;
        }
    }

    private async Task<LimitApplyResult> RunAsync(string script, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        if (OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var scriptPath = Path.Combine(_directory, "tidegate.sh");
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken);
        if (OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var command = "/bin/bash " + LimitScript.BashQuote(scriptPath);
        var source = $"do shell script {AppleQuote(command)} with administrator privileges";
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.StartInfo.ArgumentList.Add("-e");
        process.StartInfo.ArgumentList.Add(source);
        if (!process.Start())
            return new LimitApplyResult(false, "無法開啟授權對話框。");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        var message = stdout.Length > 0 ? stdout : stderr;
        if (process.ExitCode == 0)
            return new LimitApplyResult(true, message.Length > 0 ? message : "已完成。");
        if (message.Contains("User canceled", StringComparison.OrdinalIgnoreCase)
            || message.Contains("-128", StringComparison.Ordinal))
            return new LimitApplyResult(false, "已取消管理者授權。");
        return new LimitApplyResult(false, message.Length > 0 ? message : "系統沒有接受這次變更。");
    }

    private static string AppleQuote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
