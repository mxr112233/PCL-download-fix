using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pcl299DownloadFix
{
    internal static class PclLocator
    {
        private const int SearchTimeoutMs = 12000;
        private static readonly HashSet<string> SkipDirectoryNames = new HashSet<string>(
            new string[]
            {
                "$Recycle.Bin", "System Volume Information", "Windows", "Windows.old",
                "ProgramData", "Recovery", "Boot", "PerfLogs", "AppData",
                "node_modules", ".git"
            },
            StringComparer.OrdinalIgnoreCase);

        internal static string Find(out string detail, Action<string> report)
        {
            Report(report, "正在检查工具目录、当前目录和上层目录...");
            List<string> matches = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stopwatch watch = Stopwatch.StartNew();
            string selected;

            AddCandidate(AppDomain.CurrentDomain.BaseDirectory, matches, seen);
            AddCandidate(Environment.CurrentDirectory, matches, seen);

            string directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 4; i++)
            {
                DirectoryInfo parent = Directory.GetParent(directory);
                if (parent == null)
                {
                    break;
                }
                directory = parent.FullName;
                AddCandidate(directory, matches, seen);
            }
            if (TryUseExpected(matches, out selected, out detail))
            {
                return selected;
            }

            Report(report, "检查正在运行的 PCL 进程...");
            AddRunningProcessPaths(matches, seen);
            if (TryUseExpected(matches, out selected, out detail))
            {
                return selected;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<string> commonDirectories = new List<string>();
            commonDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            commonDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            commonDirectories.Add(Path.Combine(userProfile, "Downloads"));
            commonDirectories.Add(Path.Combine(userProfile, "OneDrive", "Desktop"));
            commonDirectories.Add(Path.Combine(userProfile, "OneDrive", "Downloads"));

            Report(report, "搜索桌面、文档和下载目录...");
            for (int i = 0; i < commonDirectories.Count && watch.ElapsedMilliseconds < SearchTimeoutMs; i++)
            {
                string common = commonDirectories[i];
                if (!String.IsNullOrWhiteSpace(common) && Directory.Exists(common))
                {
                    SearchTree(common, 3, matches, seen, watch);
                    if (TryUseExpected(matches, out selected, out detail))
                    {
                        return selected;
                    }
                }
            }

            List<DriveInfo> drives = GetFixedDrives();
            Report(report, "搜索固定磁盘中的 PCL（最多等待 12 秒）...");
            for (int i = 0; i < drives.Count && watch.ElapsedMilliseconds < SearchTimeoutMs; i++)
            {
                SearchTree(drives[i].RootDirectory.FullName, 3, matches, seen, watch);
                if (TryUseExpected(matches, out selected, out detail))
                {
                    return selected;
                }
            }

            if (matches.Count == 0)
            {
                detail = watch.ElapsedMilliseconds >= SearchTimeoutMs
                    ? "自动查找超时，未找到 PCL。请手动选择文件。"
                    : "未在常用位置找到 PCL。请手动选择文件。";
                return null;
            }

            selected = matches[0];
            detail = "找到 " + matches.Count + " 个 PCL，已选择：" + selected;
            Report(report, detail);
            return selected;
        }

        private static void Report(Action<string> report, string message)
        {
            if (report != null)
            {
                report(message);
            }
        }

        private static bool TryUseExpected(List<string> matches, out string selected, out string detail)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (IsExpectedVersion(matches[i]))
                {
                    selected = matches[i];
                    detail = matches.Count == 1
                        ? "自动定位到 PCL：" + selected
                        : "找到 " + matches.Count + " 个 PCL，已优先选择 2.99 版本：" + selected;
                    return true;
                }
            }
            selected = null;
            detail = null;
            return false;
        }
        private static void AddRunningProcessPaths(List<string> matches, HashSet<string> seen)
        {
            Process[] processes = Process.GetProcesses();
            try
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        if (processes[i].HasExited)
                        {
                            continue;
                        }
                        string path = processes[i].MainModule.FileName;
                        if (String.Equals(Path.GetFileName(path), Program.DefaultExeName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            AddPath(path, matches, seen);
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
        }

        private static List<DriveInfo> GetFixedDrives()
        {
            List<DriveInfo> preferred = new List<DriveInfo>();
            List<DriveInfo> systemDrive = new List<DriveInfo>();
            DriveInfo[] drives = DriveInfo.GetDrives();
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            for (int i = 0; i < drives.Length; i++)
            {
                try
                {
                    if (drives[i].DriveType != DriveType.Fixed || !drives[i].IsReady)
                    {
                        continue;
                    }
                    if (String.Equals(drives[i].RootDirectory.FullName, systemRoot,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        systemDrive.Add(drives[i]);
                    }
                    else
                    {
                        preferred.Add(drives[i]);
                    }
                }
                catch
                {
                }
            }
            preferred.AddRange(systemDrive);
            return preferred;
        }

        private static void SearchTree(string root, int maxDepth, List<string> matches,
            HashSet<string> seen, Stopwatch watch)
        {
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            while (queue.Count > 0 && watch.ElapsedMilliseconds < SearchTimeoutMs)
            {
                KeyValuePair<string, int> item = queue.Dequeue();
                string directory = item.Key;
                int depth = item.Value;
                try
                {
                    string direct = Path.Combine(directory, Program.DefaultExeName);
                    AddPath(direct, matches, seen);
                    if (depth >= maxDepth)
                    {
                        continue;
                    }

                    string[] children = Directory.GetDirectories(directory);
                    for (int i = 0; i < children.Length; i++)
                    {
                        try
                        {
                            DirectoryInfo info = new DirectoryInfo(children[i]);
                            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                                SkipDirectoryNames.Contains(info.Name))
                            {
                                continue;
                            }
                            queue.Enqueue(new KeyValuePair<string, int>(children[i], depth + 1));
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static void AddCandidate(string directory, List<string> matches, HashSet<string> seen)
        {
            if (!String.IsNullOrWhiteSpace(directory))
            {
                AddPath(Path.Combine(directory, Program.DefaultExeName), matches, seen);
            }
        }

        private static void AddPath(string path, List<string> matches, HashSet<string> seen)
        {
            try
            {
                if (!File.Exists(path) || !String.Equals(Path.GetFileName(path),
                    Program.DefaultExeName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                string fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                {
                    matches.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static bool IsExpectedVersion(string path)
        {
            try
            {
                return String.Equals(FileVersionInfo.GetVersionInfo(path).FileVersion,
                    "2.99.0.0", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class FixerForm : Form
    {
        private static readonly Color PageColor = Color.FromArgb(244, 247, 251);
        private static readonly Color AccentColor = Color.FromArgb(61, 142, 234);
        private static readonly Color TextColor = Color.FromArgb(45, 52, 61);
        private static readonly Color MutedColor = Color.FromArgb(112, 122, 134);
        private static readonly Color SuccessColor = Color.FromArgb(47, 163, 107);
        private static readonly Color ErrorColor = Color.FromArgb(211, 79, 79);

        private readonly Label statusLabel;
        private readonly Label pathLabel;
        private readonly Label progressLabel;
        private readonly TextBox logBox;
        private readonly Panel progressTrack;
        private readonly Panel progressFill;
        private readonly Button closeButton;
        private readonly StringBuilder logBuilder = new StringBuilder();
        private bool working;
        private bool allowClose;
        private string targetPath;
        private int exitCode;

        internal int ExitCode
        {
            get { return exitCode; }
        }

        internal FixerForm()
        {
            Text = "PCL 2.99 下载与整合包修复工具";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 500);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = PageColor;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 94;
            header.BackColor = Color.White;
            Controls.Add(header);

            Panel accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 6;
            accent.BackColor = AccentColor;
            header.Controls.Add(accent);

            Panel logo = new Panel();
            logo.Location = new Point(24, 21);
            logo.Size = new Size(50, 50);
            logo.BackColor = AccentColor;
            header.Controls.Add(logo);

            Label logoText = new Label();
            logoText.Dock = DockStyle.Fill;
            logoText.Text = "P";
            logoText.TextAlign = ContentAlignment.MiddleCenter;
            logoText.ForeColor = Color.White;
            logoText.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold, GraphicsUnit.Point, 134);
            logo.Controls.Add(logoText);

            Label title = new Label();
            title.AutoSize = false;
            title.Location = new Point(90, 17);
            title.Size = new Size(600, 31);
            title.Text = "下载与整合包导入修复";
            title.ForeColor = TextColor;
            title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Point, 134);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = false;
            subtitle.Location = new Point(92, 51);
            subtitle.Size = new Size(590, 23);
            subtitle.Text = "非官方第三方修复  ·  下载入口  ·  整合包导入  ·  安全回滚";
            subtitle.ForeColor = MutedColor;
            header.Controls.Add(subtitle);

            Panel statusCard = new Panel();
            statusCard.Location = new Point(24, 112);
            statusCard.Size = new Size(672, 92);
            statusCard.BackColor = Color.White;
            Controls.Add(statusCard);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Location = new Point(18, 13);
            statusLabel.Size = new Size(548, 25);
            statusLabel.Text = "准备启动...";
            statusLabel.ForeColor = AccentColor;
            statusLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 134);
            statusCard.Controls.Add(statusLabel);

            progressLabel = new Label();
            progressLabel.AutoSize = false;
            progressLabel.Location = new Point(574, 13);
            progressLabel.Size = new Size(44, 25);
            progressLabel.Text = "0%";
            progressLabel.TextAlign = ContentAlignment.MiddleRight;
            progressLabel.ForeColor = MutedColor;
            progressLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 134);
            statusCard.Controls.Add(progressLabel);

            pathLabel = new Label();
            pathLabel.AutoSize = false;
            pathLabel.Location = new Point(18, 40);
            pathLabel.Size = new Size(636, 22);
            pathLabel.Text = "正在自动查找 Plain Craft Launcher 2.exe";
            pathLabel.ForeColor = MutedColor;
            pathLabel.AutoEllipsis = true;
            statusCard.Controls.Add(pathLabel);

            progressTrack = new Panel();
            progressTrack.Location = new Point(18, 70);
            progressTrack.Size = new Size(636, 8);
            progressTrack.BackColor = Color.FromArgb(229, 235, 243);
            statusCard.Controls.Add(progressTrack);

            progressFill = new Panel();
            progressFill.Location = new Point(0, 0);
            progressFill.Size = new Size(0, 8);
            progressFill.BackColor = AccentColor;
            progressTrack.Controls.Add(progressFill);

            Label logTitle = new Label();
            logTitle.AutoSize = true;
            logTitle.Location = new Point(24, 220);
            logTitle.Text = "修复过程";
            logTitle.ForeColor = TextColor;
            logTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 134);
            Controls.Add(logTitle);

            logBox = new TextBox();
            logBox.Location = new Point(24, 246);
            logBox.Size = new Size(672, 174);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.ForeColor = TextColor;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            Controls.Add(logBox);

            Label footer = new Label();
            footer.AutoSize = false;
            footer.Location = new Point(24, 434);
            footer.Size = new Size(460, 26);
            footer.Text = "仅支持 PCL 2.99.0.0 (413) 的原版或已知修复版，其他文件不会被修改。";
            footer.ForeColor = MutedColor;
            Controls.Add(footer);

            closeButton = new Button();
            closeButton.Location = new Point(596, 430);
            closeButton.Size = new Size(100, 34);
            closeButton.Text = "关闭";
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.BackColor = AccentColor;
            closeButton.ForeColor = Color.White;
            closeButton.Enabled = false;
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            FormClosing += FixerForm_FormClosing;
            Shown += delegate { StartWork(); };
        }

        private void FixerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (working && !allowClose)
            {
                e.Cancel = true;
                SetStatus("正在处理，请稍候...", AccentColor);
            }
        }

        private void StartWork()
        {
            if (working)
            {
                return;
            }
            working = true;
            allowClose = false;
            exitCode = 0;
            targetPath = null;
            logBuilder.Length = 0;
            logBox.Clear();
            closeButton.Enabled = false;
            SetProgress(6);
            SetStatus("正在自动定位 PCL...", AccentColor);
            pathLabel.Text = "请稍候，正在检查同目录、当前目录、进程和常用位置";
            Program.SetInteractiveReporter(AppendLog);

            Thread thread = new Thread(Work);
            thread.IsBackground = true;
            thread.Name = "PCL download fix";
            thread.Start();
        }

        private void Work()
        {
            string summary = null;
            try
            {
                string detail;
                string found = LocatePcl(out detail);
                if (!String.IsNullOrEmpty(found))
                {
                    AppendLog(detail);
                }
                if (String.IsNullOrEmpty(found))
                {
                    string chosen = (string)Invoke(new Func<string>(PromptForPcl));
                    if (String.IsNullOrEmpty(chosen))
                    {
                        ShowFailure(3, "未找到 PCL，操作已取消。" + Environment.NewLine + detail);
                        return;
                    }
                    found = chosen;
                    detail = "已手动选择 PCL：" + found;
                }

                targetPath = found;
                OnUi(delegate
                {
                    pathLabel.Text = targetPath;
                    SetProgress(16);
                    SetStatus("已定位 PCL，正在校验...", AccentColor);
                });
                Options options = new Options();
                options.PclPath = targetPath;
#if PCL299_TEST_HOOKS
                options.NoLaunch = String.Equals(
                    Environment.GetEnvironmentVariable("PCL299_TEST_GUI_NO_LAUNCH"), "1",
                    StringComparison.Ordinal);
#endif
                int code = Program.Execute(options);
                exitCode = code;
                if (code == 0)
                {
                    summary = "修复流程已完成。现在可以进入 PCL 的“下载 → 原版游戏”页面，也可以把整合包 ZIP 拖入 PCL 窗口导入。";
                    ShowSuccess(summary);
                }
                else
                {
                    ShowFailure(code, "修复流程返回了非零状态。");
                }
            }
            catch (ToolException ex)
            {
                exitCode = ex.ExitCode;
                ShowFailure(ex.ExitCode, ex.Message, ex);
            }
            catch (Exception ex)
            {
                exitCode = 3;
                ShowFailure(3, "发生未处理错误：" + ex.Message, ex);
            }
        }

        private string LocatePcl(out string detail)
        {
#if PCL299_TEST_HOOKS
            string forced = Environment.GetEnvironmentVariable("PCL299_TEST_GUI_PCL_PATH");
            if (!String.IsNullOrWhiteSpace(forced))
            {
                detail = "测试指定 PCL：" + forced;
                return Path.GetFullPath(forced);
            }
#endif
            string found = PclLocator.Find(out detail, AppendLog);
            if (!String.IsNullOrEmpty(found))
            {
                AppendLog(detail);
            }
            return found;
        }

        private string PromptForPcl()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 Plain Craft Launcher 2.exe";
                dialog.Filter = "Plain Craft Launcher 2|Plain Craft Launcher 2.exe|可执行文件 (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private void ShowSuccess(string summary)
        {
            OnUi(delegate
            {
                working = false;
                closeButton.Enabled = true;
                SetProgress(100);
                SetStatus("修复完成", SuccessColor);
                string backup = targetPath + Program.BackupSuffix;
                string backupLine = File.Exists(backup)
                    ? "原版备份：" + backup
                    : "原版备份：未找到（本次仍已完成修复，但无法通过 --restore 恢复）";
                string details =
                    "PCL 路径：" + targetPath + Environment.NewLine +
                    backupLine + Environment.NewLine + Environment.NewLine +
                    "请进入 PCL 的“下载 → 原版游戏”页面，或将整合包压缩包拖入 PCL 窗口。" + Environment.NewLine +
                    "如果 PCL 原本已打开且已是修复版，本次不会重复启动。";
                using (ResultDialog dialog = new ResultDialog(true, 0, summary, details, false, null))
                {
                    dialog.ShowDialog(this);
                }
                exitCode = 0;
                allowClose = true;
                Close();
            });
        }

        private void ShowFailure(int code, string error, Exception exception = null)
        {
            OnUi(delegate
            {
                working = false;
                closeButton.Enabled = true;
                SetStatus("修复失败，请查看错误信息", ErrorColor);
                progressFill.BackColor = ErrorColor;
                string details = BuildDiagnostics(code, error, exception);
                string diagnosticPath = WriteDiagnosticLog(details);
                if (!String.IsNullOrEmpty(diagnosticPath))
                {
                    details += Environment.NewLine + Environment.NewLine + "诊断日志：" + diagnosticPath;
                }
                using (ResultDialog dialog = new ResultDialog(false, code, error, details, true, diagnosticPath))
                {
                    dialog.ShowDialog(this);
                    if (dialog.RetryRequested)
                    {
                        BeginInvoke(new Action(StartWork));
                        return;
                    }
                }
                allowClose = true;
                Close();
            });
        }

        private string BuildDiagnostics(int code, string error, Exception exception)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("PCL 2.99 下载与整合包修复工具 - 诊断信息");
            builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("工具版本：" + Assembly.GetExecutingAssembly().GetName().Version);
            builder.AppendLine("系统：" + Environment.OSVersion);
            builder.AppendLine(".NET：" + Environment.Version);
            builder.AppendLine("进程架构：" + (Environment.Is64BitProcess ? "x64" : "x86"));
            builder.AppendLine("返回码：" + code + "（" + GetErrorMeaning(code) + "）");
            builder.AppendLine("PCL 路径：" + (targetPath ?? "(未定位)"));
            builder.AppendLine("错误信息：" + error);
            if (exception != null && !(exception is ToolException))
            {
                builder.AppendLine("异常详情：" + exception);
            }
            builder.AppendLine();
            builder.AppendLine("修复过程：");
            builder.Append(logBuilder.ToString());
            return builder.ToString();
        }

        private static string GetErrorMeaning(int code)
        {
            switch (code)
            {
                case 0: return "成功";
                case 2: return "PCL 版本或哈希不受支持";
                case 3: return "文件、备份或运行状态错误";
                case 4: return "PCL 启动失败，已自动回滚";
                case 5: return "恢复或回滚失败";
                default: return "未知错误";
            }
        }

        private static string WriteDiagnosticLog(string details)
        {
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "PCL-2.99-Download-Fix-last.log");
                File.WriteAllText(logPath, details, new UTF8Encoding(true));
                return logPath;
            }
            catch
            {
                return null;
            }
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || Disposing || String.IsNullOrWhiteSpace(message))
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(AppendLog), message); } catch { }
                return;
            }

            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message.Trim();
            logBuilder.AppendLine(line);
            logBox.AppendText(line + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
            UpdateProgress(message);
        }

        private void UpdateProgress(string message)
        {
            if (message.IndexOf("PCL 路径", StringComparison.Ordinal) >= 0)
            {
                SetProgress(20);
            }
            else if (message.IndexOf("检测结果", StringComparison.Ordinal) >= 0)
            {
                SetProgress(36);
                SetStatus("已识别 PCL，正在准备修复...", AccentColor);
            }
            else if (message.IndexOf("创建原版备份", StringComparison.Ordinal) >= 0)
            {
                SetProgress(50);
                SetStatus("正在创建并校验原版备份...", AccentColor);
            }
            else if (message.IndexOf("写入下载入口", StringComparison.Ordinal) >= 0 ||
                     message.IndexOf("追加整合包修复", StringComparison.Ordinal) >= 0)
            {
                SetProgress(68);
                SetStatus("正在写入下载与整合包补丁并校验文件...", AccentColor);
            }
            else if (message.IndexOf("复核完整补丁", StringComparison.Ordinal) >= 0)
            {
                SetProgress(62);
                SetStatus("正在复核已有修复状态...", AccentColor);
            }
            else if (message.IndexOf("修复完成", StringComparison.Ordinal) >= 0)
            {
                SetProgress(84);
                SetStatus("补丁写入完成，正在启动 PCL...", SuccessColor);
            }
            else if (message.IndexOf("正在启动 PCL", StringComparison.Ordinal) >= 0 ||
                     message.IndexOf("检测到 PCL 已在运行", StringComparison.Ordinal) >= 0)
            {
                SetProgress(92);
                SetStatus("正在启动或连接 PCL...", AccentColor);
            }
            else if (message.IndexOf("成功启动", StringComparison.Ordinal) >= 0)
            {
                SetProgress(100);
                SetStatus("修复完成", SuccessColor);
            }
        }

        private void SetStatus(string text, Color color)
        {
            if (IsDisposed)
            {
                return;
            }
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private void SetProgress(int value)
        {
            if (IsDisposed)
            {
                return;
            }
            int bounded = Math.Max(0, Math.Min(100, value));
            progressFill.Width = progressTrack.Width * bounded / 100;
            progressFill.BackColor = AccentColor;
            progressLabel.Text = bounded + "%";
        }

        private void OnUi(Action action)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { Invoke(action); } catch { }
            }
            else
            {
                action();
            }
        }
    }

    internal sealed class ResultDialog : Form
    {
        private static readonly Color AccentColor = Color.FromArgb(61, 142, 234);
        private static readonly Color TextColor = Color.FromArgb(45, 52, 61);
        private static readonly Color MutedColor = Color.FromArgb(112, 122, 134);
        private readonly string detailsText;
        private readonly string diagnosticPath;
#if PCL299_TEST_HOOKS
        private readonly System.Windows.Forms.Timer autoCloseTimer;
#endif

        internal bool RetryRequested { get; private set; }

        internal ResultDialog(bool success, int exitCode, string summary, string details, bool canRetry,
            string diagnosticPath)
        {
            detailsText = details;
            this.diagnosticPath = diagnosticPath;
            Color stateColor = success
                ? Color.FromArgb(47, 163, 107)
                : Color.FromArgb(211, 79, 79);

            Text = success ? "修复完成" : "修复失败";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            AutoScaleMode = AutoScaleMode.Dpi;

            Panel accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 6;
            accent.BackColor = stateColor;
            Controls.Add(accent);

            Panel iconPanel = new Panel();
            iconPanel.Location = new Point(24, 28);
            iconPanel.Size = new Size(46, 46);
            iconPanel.BackColor = stateColor;
            Controls.Add(iconPanel);

            Label icon = new Label();
            icon.Dock = DockStyle.Fill;
            icon.Text = success ? "✓" : "!";
            icon.TextAlign = ContentAlignment.MiddleCenter;
            icon.ForeColor = Color.White;
            icon.Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold, GraphicsUnit.Point, 134);
            iconPanel.Controls.Add(icon);

            Label title = new Label();
            title.AutoSize = false;
            title.Location = new Point(88, 25);
            title.Size = new Size(520, 31);
            title.Text = success ? "修复完成" : "修复失败";
            title.ForeColor = TextColor;
            title.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point, 134);
            Controls.Add(title);

            Label summaryLabel = new Label();
            summaryLabel.AutoSize = false;
            summaryLabel.Location = new Point(90, 58);
            summaryLabel.Size = new Size(520, 55);
            summaryLabel.Text = success
                ? summary
                : "返回码 " + exitCode + "：" + FirstLine(summary);
            summaryLabel.ForeColor = success ? MutedColor : stateColor;
            Controls.Add(summaryLabel);

            Label detailsTitle = new Label();
            detailsTitle.AutoSize = true;
            detailsTitle.Location = new Point(24, 112);
            detailsTitle.Text = success ? "处理结果" : "错误信息（可复制反馈）";
            detailsTitle.ForeColor = TextColor;
            detailsTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 134);
            Controls.Add(detailsTitle);

            TextBox detailsBox = new TextBox();
            detailsBox.Location = new Point(24, 138);
            detailsBox.Size = new Size(592, 232);
            detailsBox.Multiline = true;
            detailsBox.ReadOnly = true;
            detailsBox.ScrollBars = ScrollBars.Both;
            detailsBox.WordWrap = false;
            detailsBox.BackColor = Color.FromArgb(247, 249, 252);
            detailsBox.ForeColor = TextColor;
            detailsBox.BorderStyle = BorderStyle.FixedSingle;
            detailsBox.Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            detailsBox.Text = details;
            Controls.Add(detailsBox);

            Button copyButton = CreateButton("复制信息", Color.FromArgb(230, 236, 244), TextColor);
            copyButton.Location = new Point(424, 386);
            copyButton.Size = new Size(92, 34);
            copyButton.Click += delegate
            {
                try
                {
                    Clipboard.SetText(detailsText);
                    copyButton.Text = "已复制";
                }
                catch
                {
                    copyButton.Text = "复制失败";
                }
            };
            Controls.Add(copyButton);

            Button closeButton = CreateButton("关闭", AccentColor, Color.White);
            closeButton.Location = new Point(524, 386);
            closeButton.Size = new Size(92, 34);
            closeButton.DialogResult = DialogResult.Cancel;
            Controls.Add(closeButton);

            if (canRetry)
            {
                Button retryButton = CreateButton("重新尝试", stateColor, Color.White);
                retryButton.Location = new Point(328, 386);
                retryButton.Size = new Size(88, 34);
                retryButton.Click += delegate
                {
                    RetryRequested = true;
                    DialogResult = DialogResult.Retry;
                };
                Controls.Add(retryButton);
            }

            if (!String.IsNullOrEmpty(this.diagnosticPath))
            {
                Button openLogButton = CreateButton("打开日志", Color.FromArgb(230, 236, 244), TextColor);
                openLogButton.Location = new Point(24, 386);
                openLogButton.Size = new Size(92, 34);
                openLogButton.Click += delegate
                {
                    try
                    {
                        ProcessStartInfo info = new ProcessStartInfo(this.diagnosticPath);
                        info.UseShellExecute = true;
                        Process.Start(info);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "无法打开诊断日志：" + ex.Message, "打开失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                Controls.Add(openLogButton);
            }

            AcceptButton = closeButton;
            CancelButton = closeButton;
#if PCL299_TEST_HOOKS
            autoCloseTimer = CreateAutoCloseTimer();
            if (autoCloseTimer != null)
            {
                autoCloseTimer.Start();
            }
#endif
        }

#if PCL299_TEST_HOOKS
        private System.Windows.Forms.Timer CreateAutoCloseTimer()
        {
            int delay;
            if (!Int32.TryParse(Environment.GetEnvironmentVariable("PCL299_TEST_GUI_AUTOCLOSE_MS"),
                out delay) || delay <= 0)
            {
                return null;
            }
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = delay;
            timer.Tick += delegate
            {
                timer.Stop();
                Close();
            };
            return timer;
        }
#endif

        private static string FirstLine(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "未知错误";
            }
            int index = text.IndexOfAny(new char[] { '\r', '\n' });
            return index < 0 ? text : text.Substring(0, index);
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}