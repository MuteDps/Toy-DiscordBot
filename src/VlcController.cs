// VlcController.cs
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace YoutubeTogether
{
    public class VlcController
    {
        private Process _vlcProcess;
        private readonly string _vlcPath;
        private readonly string _vlcArgsBase;
        private readonly string _vlcHttpUrl;
        private readonly string _vlcHttpPassword;

        public VlcController(string vlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe",
            string vlcArgsBase = "--extraintf http --http-password=ytqueue --fullscreen",
            string vlcHttpUrl = "http://localhost:8080",
            string vlcHttpPassword = "ytqueue")
        {
            _vlcPath = vlcPath;
            _vlcArgsBase = vlcArgsBase;
            _vlcHttpUrl = vlcHttpUrl.TrimEnd('/');
            _vlcHttpPassword = vlcHttpPassword;
        }

        public bool IsRunning => Process.GetProcessesByName("vlc").Length > 0 || (_vlcProcess != null && !_vlcProcess.HasExited);

        public void StartProcess(string initialUrl = null, string inputSlave = null)
        {
            string argsForUrl = string.Empty;
            if (!string.IsNullOrEmpty(initialUrl))
            {
                argsForUrl = !string.IsNullOrWhiteSpace(inputSlave)
                    ? $"\"{initialUrl}\" --input-slave={inputSlave}"
                    : $"\"{initialUrl}\"";
            }

            string args = (argsForUrl + " " + _vlcArgsBase).Trim();

            var psi = new ProcessStartInfo
            {
                FileName = _vlcPath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(_vlcPath)
            };
            try
            {
                _vlcProcess = Process.Start(psi);
                if (_vlcProcess != null)
                {
                    _vlcProcess.EnableRaisingEvents = true;
                    _vlcProcess.Exited += (s, e) =>
                    {
                        try { Console.WriteLine("VLC 프로세스가 종료되었습니다."); } catch { }
                        try { _vlcProcess = null; } catch { }
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VLC 시작 실패: {ex.Message}");
            }
        }

        private HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(_vlcHttpPassword))
                handler.Credentials = new System.Net.NetworkCredential("", _vlcHttpPassword);
            return new HttpClient(handler, disposeHandler: true);
        }

        public async Task<string> SendRequestAsync(string relativeOrFullUrl)
        {
            try
            {
                var url = relativeOrFullUrl.StartsWith("http") ? relativeOrFullUrl : (_vlcHttpUrl + relativeOrFullUrl);
                using (var client = CreateClient())
                {
                    return await client.GetStringAsync(url);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VLC HTTP 요청 실패(비동기): {ex.Message}");
                // if process isn't running, clear handle
                try { if (_vlcProcess != null && _vlcProcess.HasExited) _vlcProcess = null; } catch { }
                return string.Empty;
            }
        }

        public string SendRequest(string relativeOrFullUrl)
        {
            return SendRequestAsync(relativeOrFullUrl).GetAwaiter().GetResult();
        }

        public async Task<bool> EnsureRunningAsync(int timeoutMs = 8000)
        {
            if (IsRunning) return true;
            StartProcess();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var s = await SendRequestAsync("/requests/status.xml");
                    if (!string.IsNullOrEmpty(s)) return true;
                }
                catch { }
                await Task.Delay(300);
            }
            return false;
        }

        public async Task<string> EnqueueAsync(string url, string inputSlave = null)
        {
            try
            {
                var encoded = Uri.EscapeDataString(url);
                string commandUrl = $"/requests/status.xml?command=in_enqueue&input={encoded}";
                if (!string.IsNullOrWhiteSpace(inputSlave))
                {
                    var encodedSlave = Uri.EscapeDataString(inputSlave);
                    commandUrl += $"&option={Uri.EscapeDataString("input-slave=" + inputSlave)}";
                }
                await SendRequestAsync(commandUrl);
                // return last id
                var ids = await GetPlaylistIdsAsync();
                if (ids.Count > 0) return ids[ids.Count - 1];
            }
            catch { }
            return null;
        }

        public async Task PlayByIdAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            await SendRequestAsync($"/requests/status.xml?command=pl_play&id={id}");
        }

        public async Task DeleteByIdAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            await SendRequestAsync($"/requests/status.xml?command=pl_delete&id={id}");
        }

        public async Task<string> GetPlaylistXmlAsync()
        {
            return await SendRequestAsync("/requests/playlist.xml");
        }

        public async Task<string> GetCurrentPlayingIdAsync()
        {
            try
            {
                var statusXml = await SendRequestAsync("/requests/status.xml");
                if (!string.IsNullOrEmpty(statusXml))
                {
                    try
                    {
                        var doc = new System.Xml.XmlDocument();
                        doc.LoadXml(statusXml);
                        var node = doc.SelectSingleNode("//currentplid");
                        if (node != null && !string.IsNullOrEmpty(node.InnerText))
                            return node.InnerText.Trim();
                    }
                    catch { }
                }

                // fallback: try to find a leaf node in playlist marked as current
                var plist = await GetPlaylistXmlAsync();
                if (!string.IsNullOrEmpty(plist))
                {
                    try
                    {
                        var pdoc = new System.Xml.XmlDocument();
                        pdoc.LoadXml(plist);
                        var curLeaf = pdoc.SelectSingleNode("//leaf[@current]");
                        if (curLeaf != null)
                        {
                            var id = curLeaf.Attributes?["id"]?.Value;
                            if (!string.IsNullOrEmpty(id)) return id;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public async Task<List<string>> GetPlaylistIdsAsync()
        {
            var result = new List<string>();
            var xml = await GetPlaylistXmlAsync();
            if (string.IsNullOrEmpty(xml)) return result;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                var nodes = doc.SelectNodes("//leaf");
                foreach (System.Xml.XmlNode node in nodes)
                {
                    var id = node.Attributes["id"]?.Value;
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
                }
            }
            catch { }
            return result;
        }

        public void Stop()
        {
            try
            {
                if (_vlcProcess != null && !_vlcProcess.HasExited)
                {
                    try { _vlcProcess.Kill(); } catch { }
                    _vlcProcess = null;
                }
            }
            catch { }
            try
            {
                foreach (var p in Process.GetProcessesByName("vlc"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }
    }
}
