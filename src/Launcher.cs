using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Road Rash Save Editor")]
[assembly: AssemblyDescription("暴力摩托（Road Rash 1996）存档修改器")]
[assembly: AssemblyProduct("RoadRashSaveEditor")]
[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]

namespace RRS
{
    static class Program
    {
        static string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoadRashSaveEditor");
        static string LastDirFile = Path.Combine(AppDir, "lastdir.txt");
        static DateTime LastPing = DateTime.Now;
        static DateTime Started = DateTime.Now;
        static int Port;
        static Form UiHost;

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(IntPtr h, uint msg, IntPtr a, IntPtr b);
        const string WinTitle = "暴力摩托 · 存档修改器";

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            try { Directory.CreateDirectory(AppDir); } catch { }
            // 清理上次残留的临时浏览器配置
            try
            {
                foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "RoadRashView_*"))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
            Port = FreePort();
            var t = new Thread(Serve) { IsBackground = true };
            t.Start();
            Thread.Sleep(250);

            string url = "http://127.0.0.1:" + Port + "/";
            string edge = EdgePath();
            string profile = Path.Combine(Path.GetTempPath(), "RoadRashView_" + Process.GetCurrentProcess().Id);
            try { Directory.CreateDirectory(profile); } catch { }
            if (edge != null)
            {
                int winW = 1300, winH = 940;
                var psi = new ProcessStartInfo(edge,
                    "--app=" + url + " --window-size=" + winW + "," + winH +
                    " --user-data-dir=\"" + profile + "\"" +
                    " --no-first-run --no-default-browser-check --no-service-autorun --disable-sync" +
                    " --disable-background-networking" +
                    " --disable-features=msEdgeSyncPromo,msImplicitSignin,msEdgeIdentity");
                psi.UseShellExecute = false;
                try
                {
                    var old = FindWindow(null, WinTitle);
                    if (old != IntPtr.Zero) PostMessage(old, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(300);
                    Process.Start(psi);
                    IntPtr hwnd = IntPtr.Zero;
                    for (int i = 0; i < 120 && hwnd == IntPtr.Zero; i++)
                    {
                        Thread.Sleep(100);
                        hwnd = FindWindow(null, WinTitle);
                    }
                    if (hwnd != IntPtr.Zero)
                    {
                        Thread.Sleep(600);
                        var wa = Screen.PrimaryScreen.WorkingArea;
                        int ww = Math.Min(1300, wa.Width - 24), wh = Math.Min(940, wa.Height - 24);
                        MoveWindow(hwnd, wa.Left + (wa.Width - ww) / 2,
                                   wa.Top + (wa.Height - wh) / 2, ww, wh, true);
                    }
                }
                catch { }
            }
            else
            {
                try { Process.Start(url); } catch { }
            }

            // 用一个隐藏窗体提供消息循环：文件选择框必须在带消息泵的 UI 线程上弹
            UiHost = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(-4000, -4000, 1, 1),
                ShowInTaskbar = false,
                TopMost = true
            };
            var idle = new System.Windows.Forms.Timer { Interval = 1000 };
            idle.Tick += (s, e) =>
            {
                // 窗口关掉 → 心跳停止 → 25 秒后自己退出
                if ((DateTime.Now - Started).TotalSeconds > 8 &&
                    (DateTime.Now - LastPing).TotalSeconds > 25)
                    Application.Exit();
            };
            idle.Start();
            Application.EnableVisualStyles();
            Application.Run(UiHost);
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        static string EdgePath()
        {
            string[] cands = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                             @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (var c in cands) if (File.Exists(c)) return c;
            return null;
        }

        // ---------------- HTTP ----------------
        static void Serve()
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            while (true)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    var th = new Thread(() => Handle(client)) { IsBackground = true };
                    th.Start();
                }
                catch { }
            }
        }

        static void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    var buf = new byte[1 << 20];
                    int len = 0, hdrEnd = -1;
                    while (len < buf.Length)
                    {
                        int n = ns.Read(buf, len, buf.Length - len);
                        if (n <= 0) break;
                        len += n;
                        string sofar = Encoding.ASCII.GetString(buf, 0, len);
                        hdrEnd = sofar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (hdrEnd >= 0)
                        {
                            int cl = ContentLength(sofar);
                            if (len - (hdrEnd + 4) >= cl) break;
                        }
                    }
                    if (hdrEnd < 0) return;
                    string head = Encoding.ASCII.GetString(buf, 0, hdrEnd);
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    string method = first[0], path = first.Length > 1 ? first[1] : "/";
                    int contentLen = ContentLength(head);
                    string body = "";
                    if (contentLen > 0)
                        body = Encoding.UTF8.GetString(buf, hdrEnd + 4, Math.Min(contentLen, len - hdrEnd - 4));

                    byte[] resp; string ctype;
                    if (method == "GET" && (path == "/" || path == "/index.html"))
                    {
                        resp = HtmlBytes(); ctype = "text/html; charset=utf-8";
                    }
                    else if (method == "GET" && path == "/api/ping")
                    {
                        LastPing = DateTime.Now; resp = Utf8("{\"ok\":true}"); ctype = Json;
                    }
                    else if (method == "GET" && path == "/api/bootstrap")
                    {
                        resp = Utf8(Bootstrap()); ctype = Json;
                    }
                    else if (method == "POST" && path == "/api/read")
                    {
                        resp = Utf8(ReadSave(JsonGet(body, "path"))); ctype = Json;
                    }
                    else if (method == "POST" && path == "/api/pick")
                    {
                        resp = Utf8(PickSave()); ctype = Json;
                    }
                    else if (method == "POST" && path == "/api/save")
                    {
                        resp = Utf8(Save(JsonGet(body, "path"), JsonGet(body, "b64"))); ctype = Json;
                    }
                    else if (method == "POST" && path == "/api/folder")
                    {
                        string p = JsonGet(body, "path");
                        try { Process.Start("explorer", "/select,\"" + p + "\""); } catch { }
                        resp = Utf8("{\"ok\":true}"); ctype = Json;
                    }
                    else { resp = Utf8("{\"ok\":false}"); ctype = Json; }

                    LastPing = DateTime.Now;
                    string hdr = "HTTP/1.1 200 OK\r\nContent-Type: " + ctype +
                                 "\r\nContent-Length: " + resp.Length +
                                 "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                    var hb = Encoding.ASCII.GetBytes(hdr);
                    ns.Write(hb, 0, hb.Length);
                    ns.Write(resp, 0, resp.Length);
                }
            }
            catch { }
        }

        const string Json = "application/json; charset=utf-8";
        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s); }
        static int ContentLength(string head)
        {
            foreach (var l in head.Split('\n'))
                if (l.Trim().ToLower().StartsWith("content-length:"))
                {
                    int v; int.TryParse(l.Substring(15).Trim(), out v); return v;
                }
            return 0;
        }
        static byte[] HtmlBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("rrs_editor.html"))
            {
                if (s == null) return Utf8("<h1>missing ui</h1>");
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------------- 存档逻辑 ----------------
        static string LastDir()
        {
            try { if (File.Exists(LastDirFile)) return File.ReadAllText(LastDirFile).Trim(); } catch { }
            return null;
        }

        static List<string> GameDirs()
        {
            var list = new List<string>();
            var last = LastDir();
            if (!string.IsNullOrEmpty(last)) list.Add(last);
            try
            {
                foreach (var sub in new[] { @"SOFTWARE\Electronic Arts\RoadRash 95",
                                            @"SOFTWARE\WOW6432Node\Electronic Arts\RoadRash 95" })
                {
                    foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                var p = k.GetValue("Path") as string;
                                if (!string.IsNullOrEmpty(p))
                                {
                                    var full = Path.GetFullPath(p);
                                    list.Add(full);
                                    list.Add(Path.Combine(full, "Save"));
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            foreach (var drive in new[] { @"C:\", @"D:\", @"E:\" })
            {
                foreach (var baseDir in new[] { Path.Combine(drive, "Games"), drive })
                {
                    try
                    {
                        if (!Directory.Exists(baseDir)) continue;
                        foreach (var d in Directory.GetDirectories(baseDir))
                        {
                            var nm = Path.GetFileName(d).ToLower();
                            if (nm.Contains("road") || nm.Contains("rash"))
                            {
                                list.Add(d);
                                list.Add(Path.Combine(d, "Save"));
                            }
                        }
                    }
                    catch { }
                }
            }
            return list;
        }

        class SaveInfo
        {
            public string name, path, mtime, rider, tag;
            public int level, cash;
            public bool ok;
            public DateTime t;
        }

        static readonly string[] RIDER_NAMES = { "戴维", "杰斐逊", "露西娅", "荘臣", "姆克迪", "塞利娜", "汤姆", "迈克" };

        /// <summary>从 160 字节存档里读出车手/级别/金钱，用于列表摘要。</summary>
        static void ReadSummary(SaveInfo s)
        {
            try
            {
                var b = File.ReadAllBytes(s.path);
                if (b.Length != 160) return;
                int rider = b[9];
                s.rider = (rider >= 1 && rider <= 8) ? RIDER_NAMES[rider - 1] : "未知";
                s.level = b[10] + 1;
                s.cash = BitConverter.ToInt32(b, 0x10);
                s.ok = true;
            }
            catch { }
        }

        static List<SaveInfo> FindSaves()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var res = new List<SaveInfo>();
            foreach (var d in GameDirs())
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d, "*.RRS"))
                    {
                        var full = Path.GetFullPath(f);
                        if (!seen.Add(full)) continue;
                        var fi = new FileInfo(full);
                        if (fi.Length != 160) continue;
                        var info = new SaveInfo
                        {
                            name = fi.Name, path = full,
                            mtime = fi.LastWriteTime.ToString("MM-dd HH:mm"), t = fi.LastWriteTime,
                            tag = full.IndexOf("\\Save\\", StringComparison.OrdinalIgnoreCase) >= 0 ? "存档" : "游戏目录"
                        };
                        ReadSummary(info);
                        res.Add(info);
                    }
                }
                catch { }
            }
            res.Sort((a, b) => b.t.CompareTo(a.t));
            return res;
        }

        static string Bootstrap()
        {
            var saves = FindSaves();
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"running\":").Append(GameRunning() ? "true" : "false").Append(",\"saves\":[");
            for (int i = 0; i < saves.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = saves[i];
                sb.Append("{\"name\":").Append(J(s.name))
                  .Append(",\"path\":").Append(J(s.path))
                  .Append(",\"mtime\":").Append(J(s.mtime))
                  .Append(",\"tag\":").Append(J(s.tag))
                  .Append(",\"rider\":").Append(J(s.rider ?? ""))
                  .Append(",\"level\":").Append(s.ok ? s.level : 0)
                  .Append(",\"cash\":").Append(s.ok ? s.cash : 0)
                  .Append('}');
            }
            sb.Append("]}");
            if (saves.Count > 0) SaveLastDir(Path.GetDirectoryName(saves[0].path));
            return sb.ToString();
        }

        static void SaveLastDir(string dir)
        {
            try { File.WriteAllText(LastDirFile, dir); } catch { }
        }

        static string ReadSave(string path)
        {
            try
            {
                var b = File.ReadAllBytes(path);
                if (b.Length != 160) return "{\"ok\":false,\"msg\":\"这个文件不是暴力摩托的存档\"}";
                return "{\"ok\":true,\"path\":" + J(path) + ",\"name\":" + J(Path.GetFileName(path)) +
                       ",\"b64\":\"" + Convert.ToBase64String(b) + "\"}";
            }
            catch { return "{\"ok\":false,\"msg\":\"读不了这个文件\"}"; }
        }

        static int Picking = 0;

        /// <summary>弹出系统文件选择框。必须在带消息泵的 UI 线程上执行。</summary>
        static string PickSave()
        {
            if (UiHost == null || UiHost.IsDisposed || !UiHost.IsHandleCreated)
                return "{\"ok\":false,\"msg\":\"界面还没准备好，请稍后再试\"}";
            if (Interlocked.CompareExchange(ref Picking, 1, 0) != 0)
                return "{\"ok\":false,\"msg\":\"已经有一个选择窗口打开了\"}";
            try
            {
                return (string)UiHost.Invoke(new Func<string>(delegate
                {
                    try
                    {
                        string start = LastDir();
                        using (var dlg = new OpenFileDialog())
                        {
                            dlg.Title = "选择暴力摩托存档";
                            dlg.Filter = "暴力摩托存档 (*.RRS)|*.RRS|所有文件 (*.*)|*.*";
                            if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
                                dlg.InitialDirectory = start;
                            dlg.RestoreDirectory = true;
                            if (dlg.ShowDialog(UiHost) == DialogResult.OK)
                                return ReadSave(dlg.FileName);
                        }
                    }
                    catch (Exception e)
                    {
                        return "{\"ok\":false,\"msg\":" + J("选择窗口打不开：" + e.Message) + "}";
                    }
                    return "{\"ok\":false,\"msg\":\"cancelled\"}";
                }));
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"msg\":" + J("打开选择窗口失败：" + e.Message) + "}";
            }
            finally
            {
                Interlocked.Exchange(ref Picking, 0);
            }
        }
        static string Save(string path, string b64)
        {
            if (GameRunning())
                return "{\"ok\":false,\"msg\":\"游戏正开着，先把游戏关掉再保存\"}";
            try
            {
                var data = Convert.FromBase64String(b64);
                if (data.Length != 160) return "{\"ok\":false,\"msg\":\"数据不对，没有写入\"}";
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string bak = path + ".bak_" + stamp;
                File.Copy(path, bak, true);
                var olds = new List<string>(Directory.GetFiles(Path.GetDirectoryName(path),
                                                               Path.GetFileName(path) + ".bak_*"));
                olds.Sort();
                for (int i = 0; i < olds.Count - 5; i++) { try { File.Delete(olds[i]); } catch { } }
                string tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                File.Copy(tmp, path, true);          // 覆盖写；失败时原文件仍在
                try { File.Delete(tmp); } catch { }
                if (new FileInfo(path).Length != 160) throw new IOException("写入后大小不对");
                SaveLastDir(Path.GetDirectoryName(path));
                return "{\"ok\":true,\"name\":" + J(Path.GetFileName(path)) +
                       ",\"backup\":" + J(Path.GetFileName(bak)) + "}";
            }
            catch (UnauthorizedAccessException) { return "{\"ok\":false,\"msg\":\"文件被占用或没有写入权限，关掉游戏再试\"}"; }
            catch (Exception e) { return "{\"ok\":false,\"msg\":" + J("保存失败：" + e.Message) + "}"; }
        }

        static bool GameRunning()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    if (string.Equals(p.ProcessName, "Roadrash", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        // ---------------- JSON 小工具 ----------------
        static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string JsonGet(string body, string key)
        {
            if (string.IsNullOrEmpty(body)) return "";
            int i = body.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return "";
            i = body.IndexOf(':', i);
            if (i < 0) return "";
            i++;
            while (i < body.Length && (body[i] == ' ' || body[i] == '\t')) i++;
            if (i >= body.Length || body[i] != '"') return "";
            i++;
            var sb = new StringBuilder();
            while (i < body.Length && body[i] != '"')
            {
                if (body[i] == '\\' && i + 1 < body.Length)
                {
                    i++;
                    char c = body[i];
                    if (c == 'n') sb.Append('\n');
                    else if (c == 't') sb.Append('\t');
                    else if (c == 'u' && i + 4 < body.Length)
                    {
                        sb.Append((char)Convert.ToInt32(body.Substring(i + 1, 4), 16));
                        i += 4;
                    }
                    else sb.Append(c);
                }
                else sb.Append(body[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
