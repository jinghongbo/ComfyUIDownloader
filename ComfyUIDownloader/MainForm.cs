using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace ComfyUIDownloader
{
    public partial class MainForm : Form
    {
        // 控件
        private Label lblWorkflowsDir;
        private TextBox txtWorkflowsDir;
        private Label lblModelsDir;
        private TextBox txtModelsDir;
        private Button btnBrowseWorkflows;
        private Button btnBrowseModels;
        private Button btnScan;
        private Button btnDownload;
        private Button btnCancel;
        private DataGridView dgvUrls;
        private RichTextBox txtLog;
        private Label lblStatus;
        private ProgressBar progressBar;

        // 数据源
        private BindingList<ModelItem> modelItems = new BindingList<ModelItem>();

        // 配置常量
        private const int MaxConcurrency = 4;
        private const int MaxRetries = 3;
        private const int ProgressUpdateIntervalMs = 500;
        private readonly Dictionary<string, string> DomainReplaceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "huggingface.co", "hf-mirror.com" }
        };

        // 下载控制
        private CancellationTokenSource _cts;
        private SemaphoreSlim _semaphore;
        private HttpClient _httpClient;
        private readonly object _logLock = new object();

        public MainForm()
        {
            InitializeCustomComponents();
            LoadDefaultPaths();
        }

        private void InitializeCustomComponents()
        {
            // 窗体设置
            this.Text = "ComfyUI 模型自动下载器";
            this.Size = new Size(1300, 750);
            this.MinimumSize = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 路径区域面板 - 用于组织路径控件
            Panel pathPanel = new Panel
            {
                Location = new Point(12, 12),
                Size = new Size(1160, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lblWorkflowsDir = new Label { Text = "工作流目录:", Location = new Point(0, 5), Size = new Size(80, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            txtWorkflowsDir = new TextBox { Location = new Point(90, 2), Size = new Size(350, 25), ReadOnly = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnBrowseWorkflows = new Button { Text = "...", Location = new Point(445, 2), Size = new Size(30, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left };

            lblModelsDir = new Label { Text = "模型根目录:", Location = new Point(0, 35), Size = new Size(80, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            txtModelsDir = new TextBox { Location = new Point(90, 32), Size = new Size(350, 25), ReadOnly = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnBrowseModels = new Button { Text = "...", Location = new Point(445, 32), Size = new Size(30, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left };

            // 按钮面板
            Panel buttonPanel = new Panel
            {
                Location = new Point(500, 0),
                Size = new Size(400, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            btnScan = new Button { Text = "扫描工作流", Location = new Point(0, 5), Size = new Size(100, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            btnDownload = new Button { Text = "开始下载", Location = new Point(110, 5), Size = new Size(100, 30), Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            btnCancel = new Button { Text = "取消下载", Location = new Point(220, 5), Size = new Size(100, 30), Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left };

            buttonPanel.Controls.AddRange(new Control[] { btnScan, btnDownload, btnCancel });

            pathPanel.Controls.AddRange(new Control[] {
                lblWorkflowsDir, txtWorkflowsDir, btnBrowseWorkflows,
                lblModelsDir, txtModelsDir, btnBrowseModels,
                buttonPanel
            });

            // 数据表格
            dgvUrls = new DataGridView
            {
                Location = new Point(12, 90),

                Size = new Size(1260, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            // 定义列
            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Url",
                HeaderText = "URL",
                DataPropertyName = "Url",
                FillWeight = 40,
                MinimumWidth = 250
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SubDir",
                HeaderText = "子目录",
                DataPropertyName = "SubDir",
                FillWeight = 6,
                MinimumWidth = 70
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "FileName",
                HeaderText = "文件名",
                DataPropertyName = "FileName",
                FillWeight = 20,
                MinimumWidth = 120
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "TotalSize",
                HeaderText = "总大小",
                DataPropertyName = "TotalSize",
                FillWeight = 4,
                MinimumWidth = 80,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "状态",
                DataPropertyName = "Status",
                FillWeight = 10,
                MinimumWidth = 80
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DownloadedSize",
                HeaderText = "已下载",
                DataPropertyName = "DownloadedSize",
                FillWeight = 4,
                MinimumWidth = 80,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            dgvUrls.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Speed",
                HeaderText = "速度",
                DataPropertyName = "Speed",
                FillWeight = 4,
                MinimumWidth = 80,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            dgvUrls.Columns.Add(new DataGridViewProgressColumn
            {
                Name = "Progress",
                HeaderText = "进度",
                DataPropertyName = "Progress",
                FillWeight = 12,
                MinimumWidth = 80
            });
            dgvUrls.DataSource = modelItems;

            // 日志框
            txtLog = new RichTextBox
            {
                Location = new Point(12, 400),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(1260, 270),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LimeGreen,
                Font = new Font("Consolas", 9)
            };

            // 状态栏面板
            Panel statusPanel = new Panel
            {
                Location = new Point(12, 680),
                Size = new Size(1260, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            lblStatus = new Label { Text = "就绪", Location = new Point(0, 5), Size = new Size(900, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            progressBar = new ProgressBar { Location = new Point(910, 5), Size = new Size(350, 20), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

            statusPanel.Controls.AddRange(new Control[] { lblStatus, progressBar });

            // 添加控件到窗体
            this.Controls.AddRange(new Control[] {
                pathPanel,
                dgvUrls,
                txtLog,
                statusPanel
            });

            // 事件绑定
            btnBrowseWorkflows.Click += BtnBrowseWorkflows_Click;
            btnBrowseModels.Click += BtnBrowseModels_Click;
            btnScan.Click += BtnScan_Click;
            btnDownload.Click += BtnDownload_Click;
            btnCancel.Click += BtnCancel_Click;
        }
        private void BtnBrowseWorkflows_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择工作流目录";
                dialog.SelectedPath = txtWorkflowsDir.Text;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtWorkflowsDir.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnBrowseModels_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择模型根目录";
                dialog.SelectedPath = txtModelsDir.Text;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtModelsDir.Text = dialog.SelectedPath;
                }
            }
        }

        private void LoadDefaultPaths()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            txtWorkflowsDir.Text = Path.Combine(userProfile, "Documents", "ComfyUI", "user", "default", "workflows");
            txtModelsDir.Text = Path.Combine(userProfile, "Documents", "ComfyUI", "models");
        }

        #region UI 辅助方法
        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(Log), message);
                return;
            }
            lock (_logLock)
            {
                // 限制日志行数避免内存问题
                if (txtLog.Lines.Length > 1000)
                {
                    var lines = txtLog.Lines.Skip(500).ToArray();
                    txtLog.Lines = lines;
                }
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                txtLog.ScrollToCaret();
            }
        }

        private void UpdateStatus(string status)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action<string>(UpdateStatus), status);
                return;
            }
            lblStatus.Text = status;
        }
        #endregion

        #region 扫描工作流
        private async void BtnScan_Click(object sender, EventArgs e)
        {
            btnScan.Enabled = false;
            btnDownload.Enabled = false;
            btnCancel.Enabled = false;
            modelItems.Clear();
            txtLog.Clear();

            string workflowsDir = txtWorkflowsDir.Text;
            if (!Directory.Exists(workflowsDir))
            {
                Log($"错误：工作流目录不存在 - {workflowsDir}");
                btnScan.Enabled = true;
                return;
            }

            Directory.CreateDirectory(txtModelsDir.Text);

            var jsonFiles = Directory.GetFiles(workflowsDir, "*.json", SearchOption.AllDirectories);
            if (jsonFiles.Length == 0)
            {
                Log("未找到任何 JSON 工作流文件。");
                btnScan.Enabled = true;
                return;
            }

            Log($"找到 {jsonFiles.Length} 个 JSON 文件，开始提取下载地址...");

            var allModelInfos = new Dictionary<string, ModelUrlInfo>();

            foreach (var file in jsonFiles)
            {
                Log($"解析：{Path.GetFileName(file)}");
                try
                {
                    string jsonContent = await File.ReadAllTextAsync(file);
                    var infos = ExtractModelInfosFromJson(jsonContent);
                    foreach (var info in infos)
                    {
                        if (!allModelInfos.ContainsKey(info.Url))
                        {
                            allModelInfos[info.Url] = info;
                        }
                        else
                        {
                            var existing = allModelInfos[info.Url];
                            if (string.IsNullOrEmpty(existing.SuggestedSubDir) && !string.IsNullOrEmpty(info.SuggestedSubDir))
                            {
                                existing.SuggestedSubDir = info.SuggestedSubDir;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"解析文件 {file} 时出错：{ex.Message}");
                }
            }

            if (allModelInfos.Count == 0)
            {
                Log("未发现任何模型下载地址。");
                btnScan.Enabled = true;
                return;
            }

            Log($"共提取到 {allModelInfos.Count} 个唯一下载地址，正在应用域名替换...");

            int existingCount = 0;
            int pendingCount = 0;

            foreach (var info in allModelInfos.Values)
            {
                string replacedUrl = ReplaceDomain(info.Url);
                string fileName = GetFileNameFromUrl(replacedUrl);

                // 构建目标路径
                string targetDir = string.IsNullOrEmpty(info.SuggestedSubDir)
                    ? txtModelsDir.Text
                    : Path.Combine(txtModelsDir.Text, info.SuggestedSubDir);
                string targetPath = Path.Combine(targetDir, fileName);

                // 检查文件是否存在
                bool fileExists = File.Exists(targetPath);
                long fileSize = fileExists ? new FileInfo(targetPath).Length : 0;

                var modelItem = new ModelItem
                {
                    Url = replacedUrl,
                    FileName = fileName,
                    SubDir = info.SuggestedSubDir ?? "",
                    Status = fileExists ? "已完成" : "等待下载",
                    Progress = fileExists ? 100 : 0,
                    DownloadedSize = fileExists ? FormatFileSize(fileSize) : "0 B",
                    Speed = "0 B/s",
                    TotalSize = fileExists ? FormatFileSize(fileSize) : "?"
                };

                modelItems.Add(modelItem);

                if (fileExists)
                {
                    existingCount++;
                    Log($"已存在：{Path.Combine(info.SuggestedSubDir ?? "", fileName)} ({FormatFileSize(fileSize)})");
                }
                else
                {
                    pendingCount++;
                }
            }

            Log($"扫描完成：总计 {modelItems.Count} 个文件，已存在 {existingCount} 个，待下载 {pendingCount} 个。");

            // 只有在有待下载文件时才启用下载按钮
            btnDownload.Enabled = pendingCount > 0;
            btnScan.Enabled = true;
        }

        private List<ModelUrlInfo> ExtractModelInfosFromJson(string jsonContent)
        {
            var infos = new List<ModelUrlInfo>();
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                TraverseElement(doc.RootElement, infos);
            }
            catch (JsonException ex)
            {
                Log($"JSON解析错误: {ex.Message}");
            }
            return infos;
        }

        private void TraverseElement(JsonElement element, List<ModelUrlInfo> infos)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    // 检查常见的模型URL字段名
                    CheckPropertyForUrl(element, "url", infos);
                    CheckPropertyForUrl(element, "download_url", infos);
                    CheckPropertyForUrl(element, "model_url", infos);

                    // 遍历所有属性
                    foreach (var prop in element.EnumerateObject())
                        TraverseElement(prop.Value, infos);
                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        TraverseElement(item, infos);
                    break;
            }
        }

        private void CheckPropertyForUrl(JsonElement element, string propertyName, List<ModelUrlInfo> infos)
        {
            if (element.TryGetProperty(propertyName, out var urlElem) &&
                urlElem.ValueKind == JsonValueKind.String)
            {
                string url = urlElem.GetString();
                if (IsValidUrl(url))
                {
                    // 尝试获取对应的目录
                    string directory = null;
                    if (element.TryGetProperty("directory", out var dirElem) &&
                        dirElem.ValueKind == JsonValueKind.String)
                    {
                        directory = dirElem.GetString();
                    }
                    else if (element.TryGetProperty("subdirectory", out dirElem) &&
                             dirElem.ValueKind == JsonValueKind.String)
                    {
                        directory = dirElem.GetString();
                    }

                    infos.Add(new ModelUrlInfo { Url = url, SuggestedSubDir = directory });
                }
            }
        }

        private bool IsValidUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private string ReplaceDomain(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return url;

            foreach (var kv in DomainReplaceMap)
            {
                if (uri.Host.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(uri) { Host = kv.Value };
                    return builder.Uri.ToString();
                }
            }
            return url;
        }

        private string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string fileName = Path.GetFileName(uri.LocalPath);

                // 如果URL没有文件名，尝试从查询参数获取
                if (string.IsNullOrEmpty(fileName) || fileName.Equals(uri.AbsolutePath.Trim('/'), StringComparison.Ordinal))
                {
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    fileName = query["filename"] ?? query["file"] ?? query["name"];
                }

                // 如果仍然没有文件名，生成一个
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = uri.Segments.LastOrDefault()?.Trim('/') ?? "model";
                    if (string.IsNullOrEmpty(fileName) || fileName.Contains('.'))
                        fileName = "model_" + uri.GetHashCode().ToString("x");
                }

                // 确保文件名有扩展名（常见模型扩展名）
                if (!Path.HasExtension(fileName))
                {
                    string[] commonExts = { ".safetensors", ".ckpt", ".bin", ".pth", ".pt", ".onnx" };
                    foreach (var ext in commonExts)
                    {
                        if (url.Contains(ext, StringComparison.OrdinalIgnoreCase))
                        {
                            fileName += ext;
                            break;
                        }
                    }
                }

                // 移除非法字符
                foreach (char c in Path.GetInvalidFileNameChars())
                    fileName = fileName.Replace(c, '_');

                return fileName;
            }
            catch
            {
                return "model_" + url.GetHashCode().ToString("x") + ".bin";
            }
        }
        #endregion

        #region 下载逻辑
        private async void BtnDownload_Click(object sender, EventArgs e)
        {
            btnDownload.Enabled = false;
            btnScan.Enabled = false;
            btnCancel.Enabled = true;
            progressBar.Visible = true;
            progressBar.Value = 0;

            _cts = new CancellationTokenSource();
            _semaphore = new SemaphoreSlim(MaxConcurrency);
            _httpClient = CreateHttpClient();

            var itemsToDownload = modelItems.Where(x => x.Status != "已完成" && x.Status != "跳过" && x.Status != "失败").ToList();
            int total = itemsToDownload.Count;
            int completed = 0;
            int failed = 0;

            progressBar.Maximum = total;
            UpdateStatus($"准备下载 {total} 个文件...");

            var tasks = itemsToDownload.Select(item => DownloadItemAsync(item, _cts.Token,
                (success) =>
                {
                    if (success)
                        Interlocked.Increment(ref completed);
                    else
                        Interlocked.Increment(ref failed);

                    int currentCompleted = Interlocked.CompareExchange(ref completed, 0, 0);
                    int currentFailed = Interlocked.CompareExchange(ref failed, 0, 0);

                    this.Invoke(() =>
                    {
                        progressBar.Value = Math.Min(currentCompleted + currentFailed, total);
                        UpdateStatus($"下载进度：成功 {currentCompleted}/{total}，失败 {currentFailed}");
                    });
                }));

            try
            {
                await Task.WhenAll(tasks);
                Log($"所有下载任务已完成。成功：{completed}，失败：{failed}");
            }
            catch (OperationCanceledException)
            {
                Log("下载已被用户取消。");
            }
            finally
            {
                _httpClient?.Dispose();
                _semaphore?.Dispose();
                _cts?.Dispose();

                btnCancel.Enabled = false;
                btnScan.Enabled = true;

                // 检查是否有未完成的项目
                bool hasPending = modelItems.Any(x =>
                    x.Status != "已完成" && x.Status != "跳过" && !x.Status.StartsWith("失败"));
                btnDownload.Enabled = hasPending;

                progressBar.Visible = false;
                UpdateStatus("就绪");
            }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };

            client.DefaultRequestHeaders.Add("User-Agent", "ComfyUI-Downloader/1.0");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

            return client;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnCancel.Enabled = false;
            Log("正在取消下载...");
        }

        private async Task DownloadItemAsync(ModelItem item, CancellationToken token, Action<bool> onCompleted)
        {
            await _semaphore.WaitAsync(token);
            bool success = false;

            try
            {
                string targetDir = string.IsNullOrEmpty(item.SubDir) ? txtModelsDir.Text : Path.Combine(txtModelsDir.Text, item.SubDir);
                Directory.CreateDirectory(targetDir);

                this.BeginInvoke(new Action(() =>
                {
                    item.Status = "准备下载";
                    item.Progress = 0;
                    item.DownloadedSize = "0 B";
                    item.Speed = "0 B/s";
                }));

                await DownloadWithRetryAsync(item, targetDir, token);
                success = true;
            }
            catch (OperationCanceledException)
            {
                item.Status = "已取消";
                Log($"下载取消 [{item.Url}]");
            }
            catch (Exception ex)
            {
                item.Status = $"失败: {ex.Message}";
                Log($"下载失败 [{item.Url}]: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
                onCompleted?.Invoke(success);
            }
        }

        private async Task DownloadWithRetryAsync(ModelItem item, string targetDir, CancellationToken token)
        {
            int retry = 0;
            while (retry <= MaxRetries)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await DownloadCoreAsync(item, targetDir, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (retry < MaxRetries && IsRetryable(ex))
                {
                    retry++;
                    int delaySeconds = (int)Math.Pow(2, retry);
                    Log($"{item.FileName} 下载失败，{delaySeconds}秒后进行第 {retry} 次重试：{ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (Exception ex)
                {
                    throw new Exception($"重试 {retry} 次后仍失败", ex);
                }
            }
        }

        private async Task DownloadCoreAsync(ModelItem item, string targetDir, CancellationToken token)
        {
            string url = item.Url;
            string fileName = item.FileName ?? GetFileNameFromUrl(url);
            string finalPath = Path.Combine(targetDir, fileName);
            string tempPath = finalPath + ".tmp";

            // 再次检查文件是否已存在（可能在扫描后被人为添加）
            if (File.Exists(finalPath))
            {
                long existingSize = new FileInfo(finalPath).Length;
                Log($"文件已存在，跳过：{Path.Combine(item.SubDir, fileName)} ({FormatFileSize(existingSize)})");
                this.BeginInvoke(new Action(() =>
                {
                    item.Status = "已完成";
                    item.Progress = 100;
                    item.DownloadedSize = FormatFileSize(existingSize);
                    item.TotalSize = FormatFileSize(existingSize);
                    item.Speed = "0 B/s";
                }));
                return;
            }

            // 清理临时文件
            if (File.Exists(tempPath))
            {
                // 检查临时文件是否可能是断点续传
                var tempInfo = new FileInfo(tempPath);
                if (tempInfo.Length > 0)
                {
                    Log($"发现临时文件：{fileName}.tmp ({FormatFileSize(tempInfo.Length)})，将尝试续传");
                }
                else
                {
                    File.Delete(tempPath);
                }
            }

            // 创建HTTP请求，支持断点续传
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (File.Exists(tempPath))
            {
                var tempInfo = new FileInfo(tempPath);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(tempInfo.Length, null);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            // 处理重定向
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && response.Headers.Location != null)
            {
                Log($"重定向到：{response.Headers.Location}");
                item.Url = response.Headers.Location.ToString();
                await DownloadCoreAsync(item, targetDir, token);
                return;
            }

            // 处理断点续传响应
            if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                Log($"文件已下载完成：{fileName}");
                File.Move(tempPath, finalPath);

                long finalSize = new FileInfo(finalPath).Length;
                this.BeginInvoke(new Action(() =>
                {
                    item.Status = "已完成";
                    item.Progress = 100;
                    item.DownloadedSize = FormatFileSize(finalSize);
                    item.TotalSize = FormatFileSize(finalSize);
                    item.Speed = "0 B/s";
                }));
                return;
            }

            response.EnsureSuccessStatusCode();

            // 尝试从Content-Disposition获取文件名
            if (response.Content.Headers.ContentDisposition?.FileNameStar != null)
            {
                var cdFileName = response.Content.Headers.ContentDisposition.FileNameStar.Trim('"');
                if (!string.IsNullOrEmpty(cdFileName))
                {
                    fileName = cdFileName;
                    item.FileName = fileName;
                    finalPath = Path.Combine(targetDir, fileName);
                    tempPath = finalPath + ".tmp";
                }
            }

            long? totalBytes = response.Content.Headers.ContentLength;
            long downloadedBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
            DateTime lastUpdate = DateTime.MinValue;
            DateTime lastSpeedCalc = DateTime.Now;
            long lastDownloadedBytes = downloadedBytes;

            var fileMode = downloadedBytes > 0 ? FileMode.Append : FileMode.Create;
            await using var fs = new FileStream(tempPath, fileMode, FileAccess.Write, FileShare.None);

            // 如果已经有下载内容，定位到末尾
            if (downloadedBytes > 0)
            {
                fs.Seek(0, SeekOrigin.End);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fs.WriteAsync(buffer, 0, bytesRead, token);
                downloadedBytes += bytesRead;

                if ((DateTime.Now - lastUpdate).TotalMilliseconds >= ProgressUpdateIntervalMs)
                {
                    lastUpdate = DateTime.Now;

                    var now = DateTime.Now;
                    double speed = 0;
                    if ((now - lastSpeedCalc).TotalSeconds > 0)
                    {
                        speed = (downloadedBytes - lastDownloadedBytes) / (now - lastSpeedCalc).TotalSeconds;
                    }
                    lastSpeedCalc = now;
                    lastDownloadedBytes = downloadedBytes;

                    int percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? (int)(downloadedBytes * 100 / (totalBytes.Value + downloadedBytes))
                        : 0;

                    this.BeginInvoke(new Action(() =>
                    {
                        item.Progress = percent;
                        item.DownloadedSize = FormatFileSize(downloadedBytes);
                        if (totalBytes.HasValue)
                            item.TotalSize = FormatFileSize(totalBytes.Value);
                        item.Speed = FormatSpeed(speed);
                        item.Status = $"下载中 {percent}%";
                    }));
                }
            }

            await fs.FlushAsync(token);
            fs.Close();

            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);

            // 验证文件大小
            var finalFileInfo = new FileInfo(finalPath);
            if (totalBytes.HasValue)
            {
                long expectedTotal = totalBytes.Value + (downloadedBytes - totalBytes.Value);
                if (finalFileInfo.Length != expectedTotal)
                {
                    throw new IOException($"文件大小不匹配：期望 {expectedTotal}，实际 {finalFileInfo.Length}");
                }
            }

            this.BeginInvoke(new Action(() =>
            {
                item.Progress = 100;
                item.DownloadedSize = FormatFileSize(finalFileInfo.Length);
                item.TotalSize = FormatFileSize(finalFileInfo.Length);
                item.Speed = "0 B/s";
                item.Status = "已完成";
            }));

            string sizeInfo = totalBytes.HasValue ? FormatFileSize(totalBytes.Value + (downloadedBytes - totalBytes.Value)) : FormatFileSize(finalFileInfo.Length);
            Log($"下载完成：{Path.Combine(item.SubDir, fileName)} ({sizeInfo})");
        }

        private bool IsRetryable(Exception ex)
        {
            return ex is HttpRequestException ||
                   ex is TaskCanceledException ||
                   ex is IOException ||
                   ex is TimeoutException;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 0) return "0 B/s";
            return FormatFileSize((long)bytesPerSecond) + "/s";
        }
        #endregion

        #region 数据模型
        public class ModelItem : INotifyPropertyChanged
        {
            private string _url;
            private string _fileName;
            private string _subDir;
            private string _status;
            private int _progress;
            private string _totalSize;
            private string _downloadedSize;
            private string _speed;

            public string Url
            {
                get => _url;
                set { _url = value; OnPropertyChanged(nameof(Url)); }
            }

            public string FileName
            {
                get => _fileName;
                set { _fileName = value; OnPropertyChanged(nameof(FileName)); }
            }

            public string SubDir
            {
                get => _subDir;
                set { _subDir = value; OnPropertyChanged(nameof(SubDir)); }
            }

            public string Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(nameof(Status)); }
            }

            public int Progress
            {
                get => _progress;
                set { _progress = value; OnPropertyChanged(nameof(Progress)); }
            }

            public string TotalSize
            {
                get => _totalSize;
                set { _totalSize = value; OnPropertyChanged(nameof(TotalSize)); }
            }

            public string DownloadedSize
            {
                get => _downloadedSize;
                set { _downloadedSize = value; OnPropertyChanged(nameof(DownloadedSize)); }
            }

            public string Speed
            {
                get => _speed;
                set { _speed = value; OnPropertyChanged(nameof(Speed)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private class ModelUrlInfo
        {
            public string Url { get; set; }
            public string SuggestedSubDir { get; set; }
        }
        #endregion

        #region 自定义进度条列
        public class DataGridViewProgressColumn : DataGridViewColumn
        {
            public DataGridViewProgressColumn()
            {
                this.CellTemplate = new DataGridViewProgressCell();
            }
        }

        public class DataGridViewProgressCell : DataGridViewTextBoxCell
        {
            protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds,
                int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue,
                string errorText, DataGridViewCellStyle cellStyle,
                DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
            {
                base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue,
                    errorText, cellStyle, advancedBorderStyle,
                    paintParts & ~DataGridViewPaintParts.ContentForeground);

                int progress = 0;
                if (value is int intVal)
                    progress = Math.Max(0, Math.Min(100, intVal));
                else if (value is string strVal && int.TryParse(strVal, out int parsed))
                    progress = Math.Max(0, Math.Min(100, parsed));

                // 绘制进度条背景
                Rectangle progressBackRect = new Rectangle(
                    cellBounds.X + 2, cellBounds.Y + 2,
                    cellBounds.Width - 4, cellBounds.Height - 4);

                using (Brush backBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
                    graphics.FillRectangle(backBrush, progressBackRect);

                if (progress > 0)
                {
                    // 绘制进度条
                    Rectangle progressRect = new Rectangle(
                        cellBounds.X + 2, cellBounds.Y + 2,
                        (int)((cellBounds.Width - 4) * (progress / 100.0)),
                        cellBounds.Height - 4);

                    // 使用渐变色
                    using (Brush progressBrush = new LinearGradientBrush(
                        progressRect, Color.FromArgb(76, 175, 80), Color.FromArgb(56, 142, 60),
                        LinearGradientMode.Horizontal))
                    {
                        graphics.FillRectangle(progressBrush, progressRect);
                    }

                    // 绘制百分比文本
                    string text = $"{progress}%";
                    using (Brush textBrush = new SolidBrush(Color.Black))
                    {
                        graphics.DrawString(text, cellStyle.Font, textBrush, cellBounds,
                            new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center
                            });
                    }
                }
            }
        }
        #endregion
    }
}