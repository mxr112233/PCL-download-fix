using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("原版游戏下载恢复工具")]
[assembly: AssemblyDescription("为 Plain Craft Launcher 2.99.0.0 (413) 恢复被禁用的原版游戏下载入口")]
[assembly: AssemblyCompany("mxr112233")]
[assembly: AssemblyProduct("原版游戏下载恢复工具")]
[assembly: AssemblyCopyright("Copyright (c) 2026 mxr112233. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Pcl299DownloadFix
{
    internal enum PclKind
    {
        Unsupported,
        Original,
        Patched
    }

    internal sealed class ToolException : Exception
    {
        public readonly int ExitCode;

        public ToolException(int exitCode, string message) : base(message)
        {
            ExitCode = exitCode;
        }
    }

    internal sealed class Options
    {
        public string PclPath;
        public bool Verify;
        public bool Restore;
        public bool NoLaunch;
        public bool Help;
    }

    internal static class Program
    {
        internal const string DefaultExeName = "Plain Craft Launcher 2.exe";
        internal const string BackupSuffix = ".pcl-download-fix.bak";
        private const string OriginalSha256 = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
        private const string PatchedSha256 = "34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7";
        private const string ExpectedVersion = "2.99.0.0";
        private const long ExpectedLength = 5980160L;
        private const int MethodHeaderOffset = 0x2C0B4;
        private const int CodeSizeOffset = 0x2C0B8;
        private const int MethodBodyOffset = 0x2C0C0;
        private const int OriginalCodeSize = 94;
        private const int PatchedCodeSize = 51;
        private const int StartupObservationMs = 12000;
        private const int StartupPollIntervalMs = 200;
        private const int StartupLogReadBytes = 524288;
        private const string LoaderEvidence = "loader";
        private const string GracefulExitEvidence = "graceful-exit";

        private static readonly byte[] FatHeaderPrefix = new byte[] { 0x13, 0x30, 0x09, 0x00 };
        private static readonly byte[] PatchBytes = Hex(
            "02-28-12-05-00-06-02-02-6F-07-06-00-06-02-6F-05-06-00-06" +
            "02-6F-51-05-00-06-14-7E-0F-04-00-04-02-FE-06-1C-06-00-06" +
            "73-8D-05-00-0A-14-17-28-CA-00-00-2B-2A");

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private const uint MoveFileReplaceExisting = 0x1;
        private const uint MoveFileWriteThrough = 0x8;
        private const uint AttachParentProcess = 0xFFFFFFFF;

        private static Action<string> interactiveReporter;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                return RunInteractive();
            }

            OpenParentConsole();
            try
            {
                Options options = Parse(args);
                if (options.Help)
                {
                    ShowHelp(false);
                    return 0;
                }
                return Execute(options);
            }
            catch (ToolException ex)
            {
                Report("错误：" + ex.Message);
                return ex.ExitCode;
            }
            catch (Exception ex)
            {
                Report("未处理错误：" + ex.Message);
                return 3;
            }
        }

        private static int RunInteractive()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (FixerForm form = new FixerForm())
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }

        internal static void SetInteractiveReporter(Action<string> reporter)
        {
            interactiveReporter = reporter;
        }
        private static Options Parse(string[] args)
        {
            Options options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (String.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    options.Help = true;
                }
                else if (String.Equals(arg, "--verify", StringComparison.OrdinalIgnoreCase))
                {
                    options.Verify = true;
                }
                else if (String.Equals(arg, "--restore", StringComparison.OrdinalIgnoreCase))
                {
                    options.Restore = true;
                }
                else if (String.Equals(arg, "--no-launch", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoLaunch = true;
                }
                else if (String.Equals(arg, "--pcl", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || String.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        throw new ToolException(2, "--pcl 后必须提供 PCL 可执行文件路径。");
                    }
                    options.PclPath = args[++i];
                }
                else
                {
                    throw new ToolException(2, "不支持的参数：" + arg);
                }
            }

            if (options.Verify && options.Restore)
            {
                throw new ToolException(2, "--verify 与 --restore 不能同时使用。");
            }
            return options;
        }

        internal static int Execute(Options options)
        {
            string target = ResolveTarget(options.PclPath);
            if (!File.Exists(target))
            {
                throw new ToolException(3, "未找到 PCL 文件：" + target);
            }

            Report("PCL 路径：" + target);
            Report("正在校验 PCL 版本和文件哈希...");

            PclKind kind;
            try
            {
                kind = IdentifyKind(target);
            }
            catch (Exception ex)
            {
                if (options.Restore)
                {
                    throw new ToolException(5, "恢复失败：无法读取目标 PCL。" + ex.Message);
                }
                throw;
            }
            if (kind == PclKind.Unsupported)
            {
                string actualHash = HashFile(target);
                string actualVersion = null;
                try { actualVersion = FileVersionInfo.GetVersionInfo(target).FileVersion; } catch { }
                throw new ToolException(2,
                    "不支持的文件：SHA256 不属于受支持的 PCL 2.99.0.0 (413) 原版或本工具补丁版。" +
                    Environment.NewLine + "文件版本：" + (String.IsNullOrEmpty(actualVersion) ? "(未知)" : actualVersion) +
                    Environment.NewLine + "实际 SHA256：" + actualHash +
                    Environment.NewLine + "文件：" + target);
            }

            EnsureExpectedVersion(target);
            Report("文件版本：" + ExpectedVersion + " (413)，SHA256 " +
                (kind == PclKind.Original ? OriginalSha256 : PatchedSha256) + "。");
            Report(kind == PclKind.Original
                ? "检测结果：受支持的原版 PCL 2.99.0.0 (413)。"
                : "检测结果：PCL 已是本工具的修复版。");

            if (options.Verify)
            {
                if (kind == PclKind.Original)
                {
                    VerifyOriginalLayout(target);
                    Report("校验通过：原版 PCL 2.99.0.0 (413)，SHA256 " + OriginalSha256 + "。");
                }
                else
                {
                    VerifyPatchedLayout(target);
                    Report("校验通过：已修复的 PCL 2.99.0.0 (413)，SHA256 " + PatchedSha256 + "。");
                    ValidateOptionalBackup(target);
                }
                return 0;
            }

            if (options.Restore)
            {
                Report("正在从固定备份恢复原版 PCL...");
                return RestoreTarget(target, kind);
            }

            if (kind == PclKind.Original)
            {
                if (IsPclRunning(target))
                {
                    throw new ToolException(3, "PCL 正在运行，暂时无法修改。请完全退出 PCL 后点击“重新尝试”。");
                }

                Report("正在创建原版备份并校验...");
                EnsureBackupFromOriginal(target);
                Report("正在写入补丁并执行完整文件校验...");
                ApplyPatch(target);
                Report("修复完成：已备份原版并写入补丁，补丁版 SHA256 " + PatchedSha256 + "。");
            }
            else
            {
                Report("正在复核补丁与备份状态...");
                VerifyPatchedLayout(target);
                ValidateOptionalBackup(target);
                Report("PCL 已是修复版，无需再次修改。");
            }

            if (options.NoLaunch)
            {
                Report("已按参数要求完成修复，不启动 PCL。");
                return 0;
            }

            if (IsPclRunning(target))
            {
                Report("检测到 PCL 已在运行；本次不重复启动。");
                return 0;
            }

            Report("正在启动 PCL...");
            string launchError;
            if (TryLaunchAndObserve(target, out launchError))
            {
                Report("PCL 已成功启动，下载入口现在可以正常使用。");
                return 0;
            }

            try
            {
                RestoreFromBackup(target);
            }
            catch (Exception rollbackError)
            {
                throw new ToolException(5,
                    "PCL 启动失败，且自动回滚也没有完成。" + Environment.NewLine +
                    "启动错误：" + launchError + Environment.NewLine +
                    "回滚错误：" + rollbackError.Message);
            }

            throw new ToolException(4,
                "PCL 启动失败，已自动从备份恢复原版。" + Environment.NewLine +
                "启动错误：" + launchError);
        }
        private static string ResolveTarget(string requested)
        {
            string path;
            if (String.IsNullOrWhiteSpace(requested))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultExeName);
            }
            else
            {
                path = requested;
                if (Directory.Exists(path))
                {
                    path = Path.Combine(path, DefaultExeName);
                }
            }
            return Path.GetFullPath(path);
        }

        private static PclKind IdentifyKind(string path)
        {
            string hash = HashFile(path);
            if (String.Equals(hash, OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return PclKind.Original;
            }
            if (String.Equals(hash, PatchedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return PclKind.Patched;
            }
            return PclKind.Unsupported;
        }

        private static void EnsureExpectedVersion(string path)
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if (!String.Equals(info.FileVersion, ExpectedVersion, StringComparison.Ordinal))
            {
                throw new ToolException(2, "文件版本不是受支持的 " + ExpectedVersion + "，实际为 " +
                    (info.FileVersion ?? "(未知)") + "。");
            }
        }

        private static void VerifyOriginalLayout(string path)
        {
            VerifyCommonHeader(path, OriginalCodeSize);
            if (HashFile(path) != OriginalSha256)
            {
                throw new ToolException(3, "原版文件哈希与预期不一致。");
            }
        }

        private static void VerifyPatchedLayout(string path)
        {
            VerifyCommonHeader(path, PatchedCodeSize);
            byte[] actual = new byte[PatchBytes.Length];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.Position = MethodBodyOffset;
                ReadExactly(stream, actual, 0, actual.Length);
                if (!BytesEqual(actual, PatchBytes))
                {
                    throw new ToolException(3, "补丁方法的 IL 字节不正确。");
                }
                byte[] tail = new byte[OriginalCodeSize - PatchedCodeSize];
                ReadExactly(stream, tail, 0, tail.Length);
                for (int i = 0; i < tail.Length; i++)
                {
                    if (tail[i] != 0)
                    {
                        throw new ToolException(3, "补丁方法尾部清零校验失败。");
                    }
                }
            }
            if (HashFile(path) != PatchedSha256)
            {
                throw new ToolException(3, "补丁版文件哈希与预期不一致。");
            }
        }

        private static void VerifyCommonHeader(string path, int expectedCodeSize)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length != ExpectedLength)
            {
                throw new ToolException(3, "文件大小与 PCL 2.99.0.0 (413) 不符。");
            }
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] header = new byte[8];
                stream.Position = MethodHeaderOffset;
                ReadExactly(stream, header, 0, header.Length);
                for (int i = 0; i < FatHeaderPrefix.Length; i++)
                {
                    if (header[i] != FatHeaderPrefix[i])
                    {
                        throw new ToolException(3, "目标方法头不匹配，拒绝继续。");
                    }
                }
                int codeSize = BitConverter.ToInt32(header, 4);
                if (codeSize != expectedCodeSize)
                {
                    throw new ToolException(3, "目标方法代码长度不是预期的 " + expectedCodeSize + " 字节。");
                }
            }
        }

        private static void EnsureBackupFromOriginal(string target)
        {
            string backup = GetBackupPath(target);
            if (File.Exists(backup))
            {
                ValidateBackup(backup);
                Report("检测到已有原版备份，保留且不覆盖：" + backup);
                return;
            }
            if (Directory.Exists(backup))
            {
                throw new ToolException(3, "备份路径已被目录占用：" + backup);
            }

            string temporary = backup + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(target, temporary, false);
                if (IdentifyKind(temporary) != PclKind.Original)
                {
                    throw new ToolException(3, "创建备份时原版哈希校验失败。");
                }
                VerifyOriginalLayout(temporary);
                try
                {
                    File.Move(temporary, backup);
                }
                catch (IOException)
                {
                    if (!File.Exists(backup))
                    {
                        throw;
                    }
                    ValidateBackup(backup);
                }
                ValidateBackup(backup);
                Report("已创建原版备份：" + backup);
            }
            catch (ToolException)
            {
                TryDelete(temporary);
                throw;
            }
            catch (Exception ex)
            {
                TryDelete(temporary);
                throw new ToolException(3, "创建备份失败：" + ex.Message);
            }
        }

        private static void ValidateOptionalBackup(string target)
        {
            string backup = GetBackupPath(target);
            if (!File.Exists(backup))
            {
                return;
            }
            ValidateBackup(backup);
        }

        private static void ValidateBackup(string backup)
        {
            if (IdentifyKind(backup) != PclKind.Original)
            {
                throw new ToolException(3, "备份不是受支持的原版 PCL，拒绝使用：" + backup);
            }
            EnsureExpectedVersion(backup);
            VerifyOriginalLayout(backup);
        }

        private static void ApplyPatch(string target)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(target),
                Path.GetFileName(target) + ".download-fix.tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.Copy(target, temporary, false);
                if (IdentifyKind(temporary) != PclKind.Original)
                {
                    throw new ToolException(3, "临时副本原版校验失败。");
                }
                VerifyOriginalLayout(temporary);

                using (FileStream stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] size = BitConverter.GetBytes(PatchedCodeSize);
                    stream.Position = CodeSizeOffset;
                    stream.Write(size, 0, size.Length);
                    stream.Position = MethodBodyOffset;
                    stream.Write(PatchBytes, 0, PatchBytes.Length);
                    stream.Position = MethodBodyOffset + PatchBytes.Length;
                    stream.Write(new byte[OriginalCodeSize - PatchedCodeSize], 0, OriginalCodeSize - PatchedCodeSize);
                    stream.Flush(true);
                }

                VerifyPatchedLayout(temporary);
                if (IsPclRunning(target))
                {
                    throw new ToolException(3, "PCL 在修复过程中启动，已停止覆盖。");
                }
                AtomicReplace(temporary, target);
                if (IdentifyKind(target) != PclKind.Patched)
                {
                    throw new ToolException(3, "覆盖后的 PCL 哈希校验失败。");
                }
                VerifyPatchedLayout(target);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static int RestoreTarget(string target, PclKind kind)
        {
            if (IsPclRunning(target))
            {
                throw new ToolException(3, "PCL 正在运行，已拒绝恢复。请完全退出 PCL 后重试。");
            }

            string backup = GetBackupPath(target);
            if (!File.Exists(backup))
            {
                throw new ToolException(3, "未找到备份文件，无法恢复：" + backup);
            }
            ValidateBackup(backup);

            if (kind == PclKind.Original)
            {
                Report("当前文件已经是原版，无需恢复。");
                return 0;
            }

            try
            {
                RestoreFromBackup(target);
                Report("恢复完成：已从备份写回原版 PCL。");
                return 0;
            }
            catch (Exception ex)
            {
                throw new ToolException(5, "恢复失败：" + ex.Message);
            }
        }

        private static void RestoreFromBackup(string target)
        {
            string backup = GetBackupPath(target);
            ValidateBackup(backup);

            string directory = Path.GetDirectoryName(target);
            string temporary = Path.Combine(directory,
                Path.GetFileName(target) + ".restore.tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.Copy(backup, temporary, false);
                VerifyOriginalLayout(temporary);
                AtomicReplace(temporary, target);
                VerifyOriginalLayout(target);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static bool TryLaunchAndObserve(string target, out string error)
        {
            error = null;
#if PCL299_TEST_HOOKS
            if (String.Equals(Environment.GetEnvironmentVariable("PCL299_TEST_STARTUP_FAILURE"), "1",
                StringComparison.Ordinal))
            {
                error = "测试注入：模拟启动失败。";
                return false;
            }

            string simulatedEvidence = Environment.GetEnvironmentVariable("PCL299_TEST_LAUNCH_EVIDENCE");
            if (!String.IsNullOrEmpty(simulatedEvidence))
            {
                Dictionary<string, long> simulatedLogLengths = CapturePclLogLengths(target);
                DateTime simulatedLaunchUtc = DateTime.UtcNow;
                if (String.Equals(simulatedEvidence, "loader", StringComparison.Ordinal))
                {
                    string logDirectory = Path.Combine(Path.GetDirectoryName(target), "PCL");
                    Directory.CreateDirectory(logDirectory);
                    string logPath = Path.Combine(logDirectory, "Log1.txt");
                    File.WriteAllText(logPath,
                        "20:00:00 I [ModLoader] 加载器 LoaderTask`2 DlClientList Mojang 状态改变：Loading" +
                        Environment.NewLine,
                        new UTF8Encoding(false));
                }
                Thread.Sleep(100);
                string simulatedFound;
                bool simulatedEvidenceFound = TryFindFreshStartupEvidence(target,
                    simulatedLaunchUtc, simulatedLogLengths, out simulatedFound);
                string simulatedExitText = Environment.GetEnvironmentVariable(
                    "PCL299_TEST_LAUNCH_EXIT_CODE");
                if (!String.IsNullOrEmpty(simulatedExitText))
                {
                    int simulatedExitCode;
                    if (!Int32.TryParse(simulatedExitText, out simulatedExitCode))
                    {
                        error = "测试注入：无效的退出码。";
                        return false;
                    }
                    ProcessStartInfo exitInfo = new ProcessStartInfo(
                        Environment.GetEnvironmentVariable("ComSpec"),
                        "/c exit " + simulatedExitCode.ToString());
                    exitInfo.UseShellExecute = false;
                    exitInfo.CreateNoWindow = true;
                    using (Process exitProcess = Process.Start(exitInfo))
                    {
                        exitProcess.WaitForExit();
                        if (exitProcess.ExitCode == 0 || simulatedEvidenceFound)
                        {
                            return true;
                        }
                    }
                }
                else if (simulatedEvidenceFound)
                {
                    return true;
                }
                error = "测试注入：未检测到新的 PCL 原版下载列表加载记录。";
                return false;
            }
#endif
            Dictionary<string, long> logLengths = CapturePclLogLengths(target);
            DateTime launchedAtUtc = DateTime.UtcNow;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = target;
                startInfo.WorkingDirectory = Path.GetDirectoryName(target);
                startInfo.UseShellExecute = true;
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "Process.Start 未返回进程对象。";
                    return false;
                }

                bool windowSeen = false;
                string evidence = null;
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    while (watch.ElapsedMilliseconds < StartupObservationMs)
                    {
                        bool exited;
                        try
                        {
                            exited = process.HasExited;
                        }
                        catch (Exception ex)
                        {
                            error = "无法读取 PCL 进程状态：" + ex.Message;
                            return false;
                        }

                        if (!exited)
                        {
                            try
                            {
                                process.Refresh();
                                if (process.MainWindowHandle != IntPtr.Zero ||
                                    !String.IsNullOrWhiteSpace(process.MainWindowTitle))
                                {
                                    windowSeen = true;
                                }
                            }
                            catch
                            {
                            }
                        }

                        bool hasEvidence = TryFindFreshStartupEvidence(target, launchedAtUtc,
                            logLengths, out evidence);
                        if (hasEvidence &&
                            (windowSeen || String.Equals(evidence, LoaderEvidence, StringComparison.Ordinal)))
                        {
                            return true;
                        }

                        if (exited)
                        {
                            int exitCode;
                            try
                            {
                                exitCode = process.ExitCode;
                            }
                            catch (Exception ex)
                            {
                                error = "PCL 已退出，但无法读取退出码：" + ex.Message;
                                return false;
                            }

                            if (exitCode == 0)
                            {
                                return true;
                            }

                            // PCL 2.99 在正常关闭时可能返回 -1。日志刚写入时文件句柄
                            // 也可能尚未刷新，因此退出后短暂等待并再次检查。
                            Thread.Sleep(300);
                            hasEvidence = TryFindFreshStartupEvidence(target, launchedAtUtc,
                                logLengths, out evidence);
                            if (hasEvidence &&
                                (String.Equals(evidence, LoaderEvidence, StringComparison.Ordinal) ||
                                 (windowSeen && String.Equals(evidence, GracefulExitEvidence,
                                     StringComparison.Ordinal))))
                            {
                                return true;
                            }

                            error = "PCL 在启动阶段退出，退出码 " + exitCode +
                                "；未检测到原版下载列表加载记录。" +
                                (windowSeen ? "主窗口曾出现。" : "主窗口未出现。");
                            return false;
                        }

                        Thread.Sleep(StartupPollIntervalMs);
                    }
                }
                finally
                {
                    process.Dispose();
                }

                bool finalEvidence = TryFindFreshStartupEvidence(target, launchedAtUtc,
                    logLengths, out evidence);
                if (finalEvidence &&
                    (windowSeen || String.Equals(evidence, LoaderEvidence, StringComparison.Ordinal)))
                {
                    return true;
                }
                if (windowSeen)
                {
                    error = "PCL 主窗口已出现，但 " + (StartupObservationMs / 1000) +
                        " 秒内没有检测到原版下载列表加载记录。";
                }
                else
                {
                    error = "PCL 在 " + (StartupObservationMs / 1000) +
                        " 秒内没有显示主窗口，也没有产生可识别的启动日志。";
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static Dictionary<string, long> CapturePclLogLengths(string target)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);
            string[] files = GetPclLogFiles(target);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    result[files[i]] = new FileInfo(files[i]).Length;
                }
                catch
                {
                }
            }
            return result;
        }

        private static string[] GetPclLogFiles(string target)
        {
            List<string> files = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string executableDirectory = Path.GetDirectoryName(target);
            List<string> directories = new List<string>();
            directories.Add(executableDirectory);
            directories.Add(Path.Combine(executableDirectory, "PCL"));

            for (int i = 0; i < directories.Count; i++)
            {
                try
                {
                    if (!Directory.Exists(directories[i]))
                    {
                        continue;
                    }
                    string[] found = Directory.GetFiles(directories[i], "Log*.txt",
                        SearchOption.TopDirectoryOnly);
                    for (int j = 0; j < found.Length; j++)
                    {
                        string fullPath = Path.GetFullPath(found[j]);
                        if (seen.Add(fullPath))
                        {
                            files.Add(fullPath);
                        }
                    }
                }
                catch
                {
                }
            }
            return files.ToArray();
        }

        private static bool TryFindFreshStartupEvidence(string target, DateTime launchedAtUtc,
            Dictionary<string, long> previousLengths, out string evidence)
        {
            evidence = null;
            bool gracefulExitSeen = false;
            string[] files = GetPclLogFiles(target);
            DateTime freshnessCutoff = launchedAtUtc.AddSeconds(-2);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    FileInfo info = new FileInfo(files[i]);
                    long previousLength;
                    bool wasKnown = previousLengths.TryGetValue(files[i], out previousLength);
                    bool changed = !wasKnown || info.Length != previousLength ||
                        info.LastWriteTimeUtc >= freshnessCutoff;
                    if (!changed)
                    {
                        continue;
                    }

                    long offset = wasKnown && info.Length > previousLength ? previousLength : 0;
                    string text = ReadLogFragment(files[i], offset, StartupLogReadBytes);
                    if (text.IndexOf("DlClientList Mojang", StringComparison.Ordinal) >= 0 &&
                        text.IndexOf("状态改变：Loading", StringComparison.Ordinal) >= 0)
                    {
                        evidence = LoaderEvidence;
                        return true;
                    }
                    if (text.IndexOf("程序已退出，返回值：Success",
                        StringComparison.Ordinal) >= 0)
                    {
                        gracefulExitSeen = true;
                    }
                }
                catch
                {
                }
            }

            if (gracefulExitSeen)
            {
                evidence = GracefulExitEvidence;
                return true;
            }
            return false;
        }

        private static string ReadLogFragment(string path, long offset, int maxBytes)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (offset < 0 || offset > stream.Length)
                    {
                        offset = 0;
                    }
                    long available = stream.Length - offset;
                    int count = (int)Math.Min((long)maxBytes, available);
                    if (count <= 0)
                    {
                        return String.Empty;
                    }
                    stream.Position = offset;
                    byte[] bytes = new byte[count];
                    int total = 0;
                    while (total < count)
                    {
                        int read = stream.Read(bytes, total, count - total);
                        if (read <= 0)
                        {
                            break;
                        }
                        total += read;
                    }
                    string utf8 = Encoding.UTF8.GetString(bytes, 0, total);
                    string localized = Encoding.Default.GetString(bytes, 0, total);
                    return utf8 + Environment.NewLine + localized;
                }
            }
            catch
            {
                return String.Empty;
            }
        }
        private static bool IsPclRunning(string target)
        {
            string fullTarget;
            try
            {
                fullTarget = Path.GetFullPath(target);
            }
            catch
            {
                fullTarget = target;
            }

            Process[] processes = Process.GetProcesses();
            try
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }
                        string processPath = null;
                        try
                        {
                            processPath = process.MainModule.FileName;
                        }
                        catch
                        {
                        }
                        if (!String.IsNullOrEmpty(processPath) &&
                            String.Equals(Path.GetFullPath(processPath), fullTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        if (!String.IsNullOrEmpty(processPath) && String.Equals(
                            Path.GetFileName(processPath), DefaultExeName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try { processes[i].Dispose(); } catch { }
                }
            }
            return false;
        }

        private static void AtomicReplace(string source, string destination)
        {
            if (!MoveFileEx(source, destination, MoveFileReplaceExisting | MoveFileWriteThrough))
            {
                int code = Marshal.GetLastWin32Error();
                throw new IOException("无法原子替换 PCL 文件（Win32 " + code + "）。");
            }
        }

        private static string GetBackupPath(string target)
        {
            return target + BackupSuffix;
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("X2"));
                }
                return builder.ToString();
            }
        }

        private static byte[] Hex(string value)
        {
            string compact = value.Replace("-", String.Empty);
            byte[] result = new byte[compact.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(compact.Substring(i * 2, 2), 16);
            }
            return result;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    throw new EndOfStreamException("读取 PCL 文件时意外到达文件末尾。");
                }
                total += read;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void OpenParentConsole()
        {
            try
            {
                AttachConsole(AttachParentProcess);
                Stream standardOutput = Console.OpenStandardOutput();
                StreamWriter writer = new StreamWriter(standardOutput, new UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
            }
            catch
            {
            }
        }

        private static void Report(string message)
        {
            Console.WriteLine(message);
            Action<string> reporter = interactiveReporter;
            if (reporter != null)
            {
                try
                {
                    reporter(message);
                }
                catch
                {
                }
            }
        }

        private static void ShowHelp(bool interactive)
        {
            string help =
                "原版游戏下载恢复工具" + Environment.NewLine +
                "仅支持 PCL 2.99.0.0 (413) 原版及其对应补丁版。" + Environment.NewLine + Environment.NewLine +
                "用法：" + Environment.NewLine +
                "  PCL-2.99-Download-Fix.exe [选项]" + Environment.NewLine + Environment.NewLine +
                "选项：" + Environment.NewLine +
                "  --pcl <路径>   指定 PCL EXE 或所在目录" + Environment.NewLine +
                "  --verify       只校验原版/补丁版状态" + Environment.NewLine +
                "  --restore      从固定备份恢复原版" + Environment.NewLine +
                "  --no-launch    修复或校验后不启动 PCL" + Environment.NewLine +
                "  --help         显示本帮助" + Environment.NewLine + Environment.NewLine +
                "返回码：0 成功；2 不支持的文件；3 文件或备份错误；4 启动失败并已回滚；5 恢复失败。";
            if (interactive)
            {
                MessageBox.Show(help, "原版游戏下载恢复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Console.WriteLine(help);
            }
        }
    }
}
