using System.Diagnostics;
using System.Text;

namespace Legend.Launcher.Core;

public sealed class ClientProcessLauncher
{
    public string BuildArguments(string template, RealmEntry realm)
    {
        string t = template ?? "";
        return t
            .Replace("{host}", realm.Host, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", realm.Port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{realmId}", realm.RealmId, StringComparison.OrdinalIgnoreCase)
            .Replace("{realmName}", realm.RealmName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Start the configured client. If <paramref name="waitForExit"/> is true, waits and returns exit code.
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(
        string executable,
        string arguments,
        bool waitForExit = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new LaunchResult
            {
                Ok = false,
                Message = "未配置 clientExecutable"
            };
        }

        string exe = executable;
        // Allow "dotnet path/to.dll" style via shebang scripts — resolve relative paths
        if (!exe.Contains(' ') && !Path.IsPathRooted(exe) && !File.Exists(exe))
        {
            // keep as-is; Process will search PATH for bare commands like bash
        }

        try
        {
            // Support "bash script.sh" / "dotnet dll" by splitting first token when file doesn't exist as whole
            string fileName;
            string args;
            if (File.Exists(exe) || exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "dotnet";
                    args = $"\"{exe}\" {arguments}".Trim();
                }
                else if (exe.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "/bin/bash";
                    args = $"\"{exe}\" {arguments}".Trim();
                }
                else
                {
                    fileName = exe;
                    args = arguments;
                }
            }
            else
            {
                // e.g. "dotnet" with args containing dll — treat whole executable as fileName
                fileName = exe;
                args = arguments;
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
            {
                return new LaunchResult
                {
                    Ok = false,
                    Executable = fileName,
                    Arguments = args,
                    Message = "进程启动失败"
                };
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!waitForExit)
            {
                return new LaunchResult
                {
                    Ok = true,
                    Executable = fileName,
                    Arguments = args,
                    Pid = proc.Id,
                    Message = $"已启动 PID={proc.Id}"
                };
            }

            await proc.WaitForExitAsync(ct);
            string combined = (stdout.ToString() + stderr).Trim();
            bool ok = proc.ExitCode == 0;
            return new LaunchResult
            {
                Ok = ok,
                ExitCode = proc.ExitCode,
                Executable = fileName,
                Arguments = args,
                Pid = proc.Id,
                Message = string.IsNullOrEmpty(combined)
                    ? (ok ? "客户端已退出 (0)" : $"客户端退出码 {proc.ExitCode}")
                    : combined
            };
        }
        catch (Exception ex)
        {
            return new LaunchResult
            {
                Ok = false,
                Executable = executable,
                Arguments = arguments,
                Message = $"启动异常：{ex.Message}"
            };
        }
    }
}
