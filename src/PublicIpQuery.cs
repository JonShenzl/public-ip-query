// Copyright (c) 2026 Jon.Shen (Jon.Shen@Foxmail.com)
// 自由复制、修改；禁止商业使用；整合或再分发需注明出处。

using System;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Public IP Query")]
[assembly: System.Reflection.AssemblyProduct("Public IP Query")]
[assembly: System.Reflection.AssemblyCompany("Jon.Shen")]
[assembly: System.Reflection.AssemblyCopyright("Copyright \u00A9 2026 Jon.Shen (Jon.Shen@Foxmail.com). Non-commercial use only; attribution required.")]
[assembly: System.Reflection.AssemblyDescription("查询本机公网 IP 地址")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace PublicIpQuery
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    internal sealed class IpResult
    {
        public string Ip;
        public string Location;
    }

    internal static class IpService
    {
        private const int TimeoutMilliseconds = 6000;

        static IpService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = TimeoutMilliseconds;
                }
                return request;
            }
        }

        // 依次尝试多个来源：ipip.net（带归属地）→ ipify → ipinfo
        public static async Task<IpResult> QueryAsync()
        {
            Func<Task<IpResult>>[] providers =
            {
                QueryIpip,
                QueryIpify,
                QueryIpInfo
            };

            foreach (Func<Task<IpResult>> provider in providers)
            {
                try
                {
                    IpResult result = await provider();
                    if (result != null && IsValidIp(result.Ip))
                    {
                        result.Ip = result.Ip.Trim();
                        return result;
                    }
                }
                catch
                {
                    // 当前来源不可用时继续尝试下一个
                }
            }

            throw new InvalidOperationException("所有查询来源均不可用");
        }

        private static async Task<IpResult> QueryIpip()
        {
            string text = await GetTextAsync("https://myip.ipip.net");
            Match ip = Regex.Match(text, @"当前\s*IP[：:]\s*([0-9a-fA-F\.:]+)");
            if (!ip.Success)
            {
                return null;
            }

            var result = new IpResult { Ip = ip.Groups[1].Value };
            Match location = Regex.Match(text, @"来自于[：:]\s*(.+)");
            if (location.Success)
            {
                result.Location = location.Groups[1].Value.Trim();
            }
            return result;
        }

        private static async Task<IpResult> QueryIpify()
        {
            string text = await GetTextAsync("https://api.ipify.org?format=json");
            Match match = Regex.Match(text, "\"ip\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? new IpResult { Ip = match.Groups[1].Value } : null;
        }

        private static async Task<IpResult> QueryIpInfo()
        {
            string text = await GetTextAsync("https://ipinfo.io/ip");
            return new IpResult { Ip = text.Trim() };
        }

        private static async Task<string> GetTextAsync(string url)
        {
            using (var client = new TimeoutWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.UserAgent] = "PublicIpQuery/1.0";
                return await client.DownloadStringTaskAsync(url);
            }
        }

        private static bool IsValidIp(string value)
        {
            IPAddress address;
            return !string.IsNullOrEmpty(value) && IPAddress.TryParse(value.Trim(), out address);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label _ipLabel;
        private readonly Label _locationLabel;
        private readonly Label _statusLabel;
        private readonly Button _copyButton;
        private readonly Button _refreshButton;
        private string _currentIp;
        private bool _busy;

        public MainForm()
        {
            Text = "公网IP查询";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 212);
            BackColor = Color.White;
            KeyPreview = true;

            _ipLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 24),
                Size = new Size(332, 46),
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = Color.FromArgb(26, 26, 26),
                Text = "—",
                TextAlign = ContentAlignment.MiddleLeft
            };

            _locationLabel = new Label
            {
                AutoSize = false,
                Location = new Point(26, 76),
                Size = new Size(328, 22),
                ForeColor = Color.FromArgb(96, 96, 96),
                Text = "正在查询…"
            };

            _copyButton = new Button
            {
                Location = new Point(24, 112),
                Size = new Size(160, 36),
                Text = "复制 IP",
                Enabled = false,
                UseVisualStyleBackColor = true
            };

            _refreshButton = new Button
            {
                Location = new Point(196, 112),
                Size = new Size(160, 36),
                Text = "刷新",
                UseVisualStyleBackColor = true
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(26, 164),
                Size = new Size(200, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(140, 140, 140),
                Text = string.Empty
            };

            var hintLabel = new Label
            {
                AutoSize = false,
                Location = new Point(226, 164),
                Size = new Size(128, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(170, 170, 170),
                Text = "F5 刷新 · Ctrl+C 复制",
                TextAlign = ContentAlignment.TopRight
            };

            _copyButton.Click += OnCopyClick;
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            Shown += async delegate { await RefreshAsync(); };
            KeyDown += OnKeyDown;
            AcceptButton = _copyButton;

            Controls.Add(_ipLabel);
            Controls.Add(_locationLabel);
            Controls.Add(_copyButton);
            Controls.Add(_refreshButton);
            Controls.Add(_statusLabel);
            Controls.Add(hintLabel);
        }

        private async Task RefreshAsync()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            _currentIp = null;
            _ipLabel.Text = "—";
            _locationLabel.Text = "正在查询…";
            _statusLabel.Text = string.Empty;
            _copyButton.Enabled = false;
            _refreshButton.Enabled = false;

            try
            {
                IpResult result = await IpService.QueryAsync();
                _currentIp = result.Ip;
                _ipLabel.Text = _currentIp;
                _locationLabel.Text = string.IsNullOrEmpty(result.Location) ? "公网 IP" : result.Location;
                _copyButton.Enabled = true;
            }
            catch (Exception)
            {
                _ipLabel.Text = "查询失败";
                _locationLabel.Text = "请检查网络连接后重试";
            }
            finally
            {
                _busy = false;
                _refreshButton.Enabled = true;
            }
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentIp))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_currentIp);
                _statusLabel.Text = "已复制到剪贴板";
            }
            catch (Exception)
            {
                _statusLabel.Text = "复制失败，请手动选择";
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C && !string.IsNullOrEmpty(_currentIp))
            {
                Clipboard.SetText(_currentIp);
                _statusLabel.Text = "已复制到剪贴板";
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5)
            {
                _refreshButton.PerformClick();
                e.Handled = true;
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
