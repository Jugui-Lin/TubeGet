using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TubeGet
{
    internal static class LocalAi
    {
        private const int MaxOutput = 128 * 1024;

        internal static bool IsLocalProvider(string provider)
        {
            return provider == "codex" || provider == "ollama" || provider == "program";
        }

        internal static void Validate(AiRepairSettings settings)
        {
            string path = settings.ProgramPath;
            if (String.IsNullOrEmpty(path))
            {
                if (settings.Provider == "program") throw new ArgumentException("请选择本地 AI 程序的 EXE。");
            }
            else if (!Path.IsPathRooted(path) || !String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
                throw new ArgumentException("找不到指定的本地 AI 程序，请选择有效的 EXE 路径。");

            if (settings.Provider == "ollama" &&
                !Regex.IsMatch(settings.Model ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9._/:+-]{0,199}$"))
                throw new ArgumentException("请输入 Ollama 中已安装的模型名称。");
        }

        internal static async Task<string> Run(AiRepairSettings settings, string prompt, CancellationToken token)
        {
            string directory = Path.Combine(Path.GetTempPath(), "TubeGet-ai-" + Guid.NewGuid().ToString("N"));
            string schema = Path.Combine(directory, "response-schema.json");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                try
                {
                    Validate(settings);
                    string file = ResolveProgram(settings);
                    string[] arguments;
                    Dictionary<string, string> environment = null;
                    if (settings.Provider == "codex")
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(schema, "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\"}," +
                            "\"issue\":{\"type\":\"string\"}},\"required\":[\"action\",\"issue\"],\"additionalProperties\":false}");
                        arguments = new[] { "-a", "never", "exec", "--sandbox", "read-only", "--ephemeral", "--skip-git-repo-check",
                            "--ignore-user-config", "--ignore-rules", "--disable", "shell_tool", "--disable", "hooks",
                            "--disable", "plugins", "--disable", "skill_search", "-c", "web_search=\"disabled\"",
                            "--color", "never", "--output-schema", schema };
                        if (!String.IsNullOrWhiteSpace(settings.Model))
                            arguments = arguments.Concat(new[] { "-m", settings.Model }).ToArray();
                        arguments = arguments.Concat(new[] { "-" }).ToArray();
                    }
                    else if (settings.Provider == "ollama")
                    {
                        environment = new Dictionary<string, string> { { "OLLAMA_HOST", "127.0.0.1:11434" } };
                        string inventory = await ReadOutput(file, new[] { "list" }, null, directory, environment, timeout.Token).ConfigureAwait(false);
                        string model = settings.Model;
                        string name = model.Contains(":") ? model : model + ":latest";
                        bool available = inventory.Split('\n').Any(line =>
                            String.Equals(Regex.Match(line.TrimStart(), @"^\S+").Value, name, StringComparison.OrdinalIgnoreCase));
                        if (!available || Regex.IsMatch(name, @"(?:[-:]cloud)(?:$|:)", RegexOptions.IgnoreCase))
                            throw new InvalidOperationException("找不到已安装的 Ollama 本地模型，请先在 Ollama 中安装该模型。");
                        arguments = new[] { "run", "--format", "json", name };
                    }
                    else
                    {
                        arguments = SplitArguments(settings.ProgramArguments);
                    }

                    string output = await ReadOutput(file, arguments, prompt, directory, environment, timeout.Token).ConfigureAwait(false);
                    output = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Trim();
                    return Regex.Replace(output, @"^\s*<think>.*?</think>\s*", "", RegexOptions.Singleline);
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("本地 AI 程序响应超时。");
                }
                catch (ArgumentException ex) { throw new InvalidOperationException(ex.Message); }
                catch (Win32Exception) { throw new InvalidOperationException("无法启动本地 AI 程序，请检查路径和执行权限。"); }
                catch (IOException) { throw new InvalidOperationException("无法读取本地 AI 程序的输出。"); }
                catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法访问本地 AI 程序或临时目录。"); }
                finally
                {
                    Engine.TryDelete(schema);
                    try { if (Directory.Exists(directory)) Directory.Delete(directory); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static async Task<string> ReadOutput(string file, string[] args, string input, string directory,
            Dictionary<string, string> environment, CancellationToken token)
        {
            var output = new StringBuilder();
            bool exceeded = false;
            int code = await Engine.Run(file, args, token, line =>
            {
                if (line.Length >= MaxOutput - output.Length) exceeded = true;
                if (!exceeded) output.AppendLine(line);
            }, null, input, directory, environment).ConfigureAwait(false);

            if (code != 0) throw new InvalidOperationException("本地 AI 程序运行失败（退出代码 " + code + "）。请确认程序已登录或模型可用。");
            if (exceeded) throw new InvalidOperationException("本地 AI 程序输出过长。");
            return output.ToString();
        }

        private static string ResolveProgram(AiRepairSettings settings)
        {
            if (!String.IsNullOrEmpty(settings.ProgramPath)) return settings.ProgramPath;
            string executable = settings.Provider + ".exe";
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string directory = entry.Trim().Trim('"');
                if (directory.Length == 0 || !Path.IsPathRooted(directory)) continue;
                string candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate)) return candidate;
            }

            if (settings.Provider == "ollama")
            {
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Ollama", executable);
                if (File.Exists(local)) return local;
            }
            else
            {
                string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                try
                {
                    var packages = Directory.EnumerateDirectories(apps, "OpenAI.Codex_*__2p2nqsd0c76g0")
                        .OrderByDescending(path => PackageVersion(Path.GetFileName(path)));
                    foreach (string package in packages)
                    {
                        string candidate = Path.Combine(package, "app", "resources", executable);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw new InvalidOperationException("未检测到 " + executable + "，请在「设置 → AI 修复」选择程序路径。");
        }

        private static Version PackageVersion(string package)
        {
            var pieces = package.Split('_');
            Version version;
            return pieces.Length > 1 && Version.TryParse(pieces[1], out version) ? version : new Version();
        }

        private static string[] SplitArguments(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return new string[0];
            int count;
            IntPtr argv = CommandLineToArgvW("tubeget-ai.exe " + value, out count);
            if (argv == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var result = new string[count - 1];
                for (int i = 1; i < count; i++)
                    result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return result;
            }
            finally { LocalFree(argv); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
