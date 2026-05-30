using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ServiceProcess;

namespace PicasaPortable
{
    class Program
    {
        // Bumped in lockstep with the InnoSetup MyAppVersion + GitHub release tag.
        private const string CurrentVersion = "1.0.3";
        private const string ReleasesApi  = "https://api.github.com/repos/aungkokomm/picasa-portable-win11/releases/latest";
        private const string ReleasesPage = "https://github.com/aungkokomm/picasa-portable-win11/releases/latest";

        private static string portableRoot;
        private static string dataPath;
        private static string picturesPath;
        private static string sandboxiePath;
        private static string boxName;

        [DllImport("shell32.dll")]
        private static extern bool IsUserAnAdmin();

        [STAThread]
        static void Main()
        {
            try
            {
                portableRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                dataPath = Path.Combine(portableRoot, "data");
                picturesPath = Path.Combine(portableRoot, "Pictures");
                sandboxiePath = Path.Combine(portableRoot, "Sandboxie-Plus");

                // Unique sandbox box name per portable folder. Two copies on different
                // paths/drives get independent boxes instead of fighting over one shared
                // "Picasa" box in the single global C:\Windows\Sandboxie.ini.
                boxName = ComputeBoxName(portableRoot);

                bool firstRun = !Directory.Exists(sandboxiePath) ||
                                !File.Exists(Path.Combine(sandboxiePath, "Start.exe"));

                bool serviceMissing = !IsServiceInstalled("SbieSvc");

                bool picasaMissing = !File.Exists(Path.Combine(dataPath,
                    @"drive\C\Program Files\Google\Picasa3\Picasa3.exe"));

                // The box must be defined in Sandboxie's config (via the service, not a raw
                // file) with a FileRootPath matching THIS folder. If it's missing or wrong
                // (fresh copy, drive-letter change), it needs (re)registering — which goes
                // through SbieIni and requires admin. Only prompt when it actually changes.
                bool boxNeedsRegister = !BoxIsRegistered();

                if (firstRun || serviceMissing || picasaMissing || boxNeedsRegister)
                {
                    if (!IsUserAnAdmin())
                    {
                        MessageBox.Show(
                            "Picasa Portable - First-Time Setup\n\n" +
                            "Setup requires administrator permission once.\n" +
                            "Click 'Yes' on the next prompt.",
                            "Picasa Portable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        RestartAsAdmin();
                        return;
                    }

                    if (firstRun || serviceMissing || picasaMissing)
                    {
                        PerformSetup(firstRun, serviceMissing, picasaMissing); // also registers the box
                    }
                    else
                    {
                        // Everything installed; just register/refresh THIS box so it gets
                        // its own sandbox and coexists with other installs' boxes.
                        RegisterBox();
                    }
                }

                EnsureServiceRunning();

                // Always update watchedfolders.txt with current drive letter (handles USB drive-letter changes)
                PreconfigurePicasaScan();

                LaunchPicasa();

                // Picasa has been spawned via Start.exe and is loading. We can now hang
                // around for the update check without delaying the user (this process
                // exits whenever the check finishes — usually under a second; up to ~6s
                // if GitHub is slow). If a newer release exists, a small toast appears
                // bottom-right; clicking it opens the release page.
                CheckForUpdateAndMaybeToast();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message + "\n\n" + ex.StackTrace,
                    "Picasa Portable - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static bool IsServiceInstalled(string name)
        {
            try
            {
                ServiceController sc = new ServiceController(name);
                ServiceControllerStatus s = sc.Status;
                return true;
            }
            catch { return false; }
        }

        static void RestartAsAdmin()
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                UseShellExecute = true,
                Verb = "runas"
            };
            try { Process.Start(psi); }
            catch (Exception ex) { MessageBox.Show("Could not elevate: " + ex.Message); }
            Environment.Exit(0);
        }

        static void PerformSetup(bool extractSandboxie, bool installService, bool installPicasa)
        {
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(picturesPath);

            if (extractSandboxie)
            {
                ExtractSandboxie();
            }

            if (installService)
            {
                InstallService();
            }

            // Service must be running before SbieIni can talk to it.
            StartService();
            // Define THIS box via Sandboxie's API and reload (also handles drive changes).
            RegisterBox();

            if (installPicasa)
            {
                MessageBox.Show(
                    "Now Picasa will install.\n\n" +
                    "When the installer opens:\n" +
                    "1. Click through to the destination screen\n" +
                    "2. Keep destination as: C:\\Program Files\\Google\\Picasa3\n" +
                    "3. Click Install\n" +
                    "4. UNCHECK 'Run Picasa' at the end\n" +
                    "5. Click Finish\n\n" +
                    "Click OK to begin.",
                    "Picasa Portable - Install Picasa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                InstallPicasaInSandbox();

                // Verify install
                int waitCount = 0;
                while (waitCount < 60 && !File.Exists(Path.Combine(dataPath,
                    @"drive\C\Program Files\Google\Picasa3\Picasa3.exe")))
                {
                    System.Threading.Thread.Sleep(5000);
                    waitCount++;
                }
            }

            // Delete installers to save space (only after successful install)
            if (File.Exists(Path.Combine(dataPath, @"drive\C\Program Files\Google\Picasa3\Picasa3.exe")))
            {
                DeleteInstallers();
            }

            MessageBox.Show("Setup complete! Launching Picasa...",
                "Picasa Portable", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void ExtractSandboxie()
        {
            string installer = Path.Combine(portableRoot, "Sandboxie-Plus-x64-v1.17.6.exe");
            if (!File.Exists(installer))
                throw new FileNotFoundException("Sandboxie installer not found: " + installer);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/S /D=\"" + portableRoot + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(180000);
            }
            System.Threading.Thread.Sleep(2000);
        }

        static void InstallService()
        {
            string kmdUtil = Path.Combine(sandboxiePath, "KmdUtil.exe");
            string sbieSvc = Path.Combine(sandboxiePath, "SbieSvc.exe");
            string sbieDrv = Path.Combine(sandboxiePath, "SbieDrv.sys");
            string sbieMsg = Path.Combine(sandboxiePath, "SbieMsg.dll");

            RunKmdUtil("install SbieDrv \"" + sbieDrv +
                "\" type=kernel start=demand msgfile=\"" + sbieMsg + "\" altitude=86900");
            RunKmdUtil("install SbieSvc \"" + sbieSvc +
                "\" type=own start=auto \"display=Sandboxie Service\" msgfile=\"" + sbieMsg + "\"");
        }

        static void RunKmdUtil(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = Path.Combine(sandboxiePath, "KmdUtil.exe"),
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi)) { p.WaitForExit(15000); }
        }

        // Stable, Sandboxie-legal box name derived from the portable folder path.
        // "Picasa_" + first 8 hex of MD5(path) — same folder always maps to the same
        // box; different copies (other drive/folder) get different boxes.
        static string ComputeBoxName(string root)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] h = md5.ComputeHash(
                        System.Text.Encoding.UTF8.GetBytes(root.ToLowerInvariant()));
                    var sb = new System.Text.StringBuilder("Picasa_");
                    for (int i = 0; i < 4; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return "Picasa"; }
        }

        // Ask the Sandboxie service (via SbieIni) what FileRootPath THIS box is configured
        // with. Returns "" if the box doesn't exist. Works without admin (read-only query).
        static string QueryBoxFileRoot()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(sandboxiePath, "SbieIni.exe"),
                    Arguments = "query " + boxName + " FileRootPath",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return (o ?? "").Trim();
                }
            }
            catch { return ""; }
        }

        // True only if Sandboxie already has THIS box defined with a FileRootPath matching
        // this folder. Lets normal launches skip the (admin-only) registration step.
        static bool BoxIsRegistered()
        {
            return string.Equals(QueryBoxFileRoot(), dataPath, StringComparison.OrdinalIgnoreCase);
        }

        // Run one SbieIni command (set/append/delete/reload). Writing config requires admin,
        // so callers do this from elevated paths only.
        static void RunSbieIni(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(sandboxiePath, "SbieIni.exe"),
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi)) { p.WaitForExit(10000); }
            }
            catch (Exception ex) { Debug.WriteLine("SbieIni failed: " + args + " -> " + ex.Message); }
        }

        // Define THIS box through Sandboxie's API. This is the ONLY reliable way to make the
        // service+driver actually know the box — raw edits to C:\Windows\Sandboxie.ini or the
        // install-folder ini are NOT picked up (Start.exe then says "Invalid box name").
        // Box name is unique per folder and FileRootPath points at this folder's data, so each
        // copy is an independent library that coexists with the others.
        static void RegisterBox()
        {
            EnsureServiceRunning(); // SbieIni needs the service up
            string u = Environment.UserName;

            RunSbieIni("delete " + boxName); // clear any stale/duplicate definition first
            RunSbieIni("set " + boxName + " Enabled y");
            RunSbieIni("set " + boxName + " ConfigLevel 9");
            RunSbieIni("set " + boxName + " FileRootPath \"" + dataPath + "\"");
            RunSbieIni("set " + boxName + " BlockNetworkFiles y");
            RunSbieIni("set " + boxName + " ReadFilePath \"" + picturesPath + "\"");
            // WriteFilePath = "box only": hides host Google data, box keeps its own copy
            RunSbieIni("append " + boxName + " WriteFilePath \"C:\\Users\\" + u + "\\AppData\\Local\\Google\"");
            RunSbieIni("append " + boxName + " WriteFilePath \"C:\\Users\\" + u + "\\AppData\\Roaming\\Google\"");
            // ClosedFilePath = blocks read AND write: keep the box out of the real user's media
            foreach (string p in new[] { "Pictures", "Documents", "Desktop", "Downloads", "Music", "Videos", "OneDrive" })
                RunSbieIni("append " + boxName + " ClosedFilePath \"C:\\Users\\" + u + "\\" + p + "\"");
            RunSbieIni("append " + boxName + " ClosedFilePath \"C:\\Users\\Public\"");

            RunSbieIni("reload");
            // Belt-and-suspenders: force the driver to load it (registration is rare, so the
            // extra few seconds is worth never showing "Invalid box name parameter").
            RestartSbieSvc();
        }

        // Reliably push ini changes into the driver. "SbieIni.exe reload" is unreliable
        // (the service/driver keeps the OLD box list -> Start.exe says "Invalid box name
        // parameter"). A full service stop+start forces the driver to re-read the ini.
        // Requires admin, so this is only called from elevated code paths.
        static void RestartSbieSvc()
        {
            try
            {
                ServiceController sc = new ServiceController("SbieSvc");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
                System.Threading.Thread.Sleep(1000);
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                System.Threading.Thread.Sleep(1500);
            }
            catch (Exception ex)
            {
                // Fall back to the (best-effort) reload if a restart isn't possible.
                Debug.WriteLine("Service restart failed: " + ex.Message);
                try
                {
                    string sbieIni = Path.Combine(sandboxiePath, "SbieIni.exe");
                    if (File.Exists(sbieIni))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = sbieIni, Arguments = "reload",
                            UseShellExecute = false, CreateNoWindow = true
                        };
                        using (Process p = Process.Start(psi)) { p.WaitForExit(8000); }
                    }
                }
                catch { }
            }
        }

        static void StartService()
        {
            try
            {
                ServiceController sc = new ServiceController("SbieSvc");
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            }
            catch { /* will be retried by EnsureServiceRunning */ }
            System.Threading.Thread.Sleep(2000);
        }

        static void EnsureServiceRunning()
        {
            try
            {
                ServiceController sc = new ServiceController("SbieSvc");
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start Sandboxie service: " + ex.Message,
                    "Service Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static void InstallPicasaInSandbox()
        {
            string installer = Path.Combine(portableRoot, "picasa39-setup.exe");
            string startExe = Path.Combine(sandboxiePath, "Start.exe");

            if (!File.Exists(installer) || !File.Exists(startExe))
                return;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = startExe,
                Arguments = "/box:" + boxName + " \"" + installer + "\"",
                UseShellExecute = false
            };
            Process.Start(psi);
        }

        static void LaunchPicasa()
        {
            string startExe = Path.Combine(sandboxiePath, "Start.exe");
            if (!File.Exists(startExe))
                throw new FileNotFoundException("Start.exe not found at: " + startExe);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = startExe,
                Arguments = "/box:" + boxName + " \"C:\\Program Files\\Google\\Picasa3\\Picasa3.exe\"",
                UseShellExecute = false
            };
            Process.Start(psi);
        }

        static void DeleteInstallers()
        {
            try { File.Delete(Path.Combine(portableRoot, "picasa39-setup.exe")); } catch { }
            try { File.Delete(Path.Combine(portableRoot, "Sandboxie-Plus-x64-v1.17.6.exe")); } catch { }
        }

        static void PreconfigurePicasaScan()
        {
            try
            {
                // Write watchedfolders.txt with the CURRENT portable Pictures path
                // (drive letter may change when USB plugs into different PC)
                string albumsDir = Path.Combine(dataPath,
                    @"user\current\AppData\Local\Google\Picasa2Albums");
                Directory.CreateDirectory(albumsDir);

                string watchedFile = Path.Combine(albumsDir, "watchedfolders.txt");
                string content = picturesPath + "\\\r\n";

                // Force watchedfolders to ONLY this folder's Pictures on every launch.
                // Picasa appends folders as you browse (host Pictures/Desktop/etc. have
                // leaked in before) — overwriting each launch keeps the scan scoped.
                File.WriteAllText(watchedFile, content);

                // Also create an empty excludedfolders.txt to be safe
                string excludedFile = Path.Combine(albumsDir, "excludedfolders.txt");
                if (!File.Exists(excludedFile))
                {
                    File.WriteAllText(excludedFile, "");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not pre-configure Picasa scan: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------------------
        // Auto-update check (NetMon-style)
        //
        // - Runs AFTER LaunchPicasa(), so Picasa is already starting in its sandbox
        //   while we hit GitHub — no perceived launch delay.
        // - Throttled to once per 6 h via <data>\update-last-checked.txt.
        // - Opt-out by creating <data>\update-config.txt containing "enabled=false".
        // - Silent on any failure (offline, rate-limit, parse error, etc.).
        // - If a newer tag is found, a small dark toast pops up bottom-right;
        //   click anywhere on it to open the release page, or wait 12 s for it to
        //   self-dismiss. Process exits after the toast closes (or immediately if
        //   there's no toast).
        // ---------------------------------------------------------------------------
        static void CheckForUpdateAndMaybeToast()
        {
            try
            {
                string cfgPath  = Path.Combine(dataPath, "update-config.txt");
                string lastPath = Path.Combine(dataPath, "update-last-checked.txt");

                // Opt-out
                if (File.Exists(cfgPath))
                {
                    string cfg = File.ReadAllText(cfgPath);
                    if (cfg.IndexOf("enabled=false", StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }

                // Throttle: skip if checked within last 6 hours
                if (File.Exists(lastPath))
                {
                    long ticks;
                    if (long.TryParse(File.ReadAllText(lastPath).Trim(), out ticks))
                    {
                        try
                        {
                            DateTime last = new DateTime(ticks, DateTimeKind.Utc);
                            if ((DateTime.UtcNow - last).TotalHours < 6.0) return;
                        }
                        catch { /* corrupt marker — just re-check */ }
                    }
                }

                string latestTag = QueryLatestTag();

                // Whatever the result, record that we tried (avoids hammering the API
                // if GitHub is down — we'll retry after 6 h).
                try { Directory.CreateDirectory(dataPath); File.WriteAllText(lastPath, DateTime.UtcNow.Ticks.ToString()); }
                catch { }

                if (string.IsNullOrEmpty(latestTag)) return;

                string latest = latestTag.TrimStart('v', 'V').Trim();
                if (!IsNewerVersion(latest, CurrentVersion)) return;

                // Block until the toast closes (or 12 s pass). We're outside the user's
                // critical path — Picasa is already up.
                using (var toast = new UpdateToast(latest, ReleasesPage))
                {
                    Application.Run(toast);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update check failed: " + ex.Message);
            }
        }

        // Returns the "tag_name" of the latest release, or "" on any failure.
        static string QueryLatestTag()
        {
            try
            {
                // Win 11's .NET 4.8 supports TLS 1.2 fine; csc.exe at 4.0 doesn't expose
                // the Tls12 enum value, so we OR in the numeric value (0xC00 = 3072).
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ReleasesApi);
                req.UserAgent = "PicasaPortable/" + CurrentVersion;
                req.Accept    = "application/vnd.github+json";
                req.Timeout   = 6000;
                req.ReadWriteTimeout = 6000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    string body = sr.ReadToEnd();
                    return ExtractJsonString(body, "tag_name");
                }
            }
            catch { return ""; }
        }

        // Crude but sufficient extractor for `"key":"value"` in a flat JSON object.
        // Avoids pulling in a JSON dependency (System.Web.Extensions / Newtonsoft).
        static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return "";
            i = json.IndexOf('"', i + 1);
            if (i < 0) return "";
            int j = i + 1;
            while (j < json.Length && json[j] != '"')
            {
                if (json[j] == '\\' && j + 1 < json.Length) j += 2;
                else j++;
            }
            if (j >= json.Length) return "";
            return json.Substring(i + 1, j - i - 1);
        }

        // True iff `remote` is strictly greater than `local` by dotted-numeric compare.
        // "1.0.10" > "1.0.2"; "1.0.2" == "1.0.2" returns false.
        static bool IsNewerVersion(string remote, string local)
        {
            try
            {
                if (string.IsNullOrEmpty(remote)) return false;
                string[] r = remote.Split('.');
                string[] l = local.Split('.');
                int n = Math.Max(r.Length, l.Length);
                for (int i = 0; i < n; i++)
                {
                    int rv = 0, lv = 0;
                    if (i < r.Length) int.TryParse(r[i], out rv);
                    if (i < l.Length) int.TryParse(l[i], out lv);
                    if (rv > lv) return true;
                    if (rv < lv) return false;
                }
                return false;
            }
            catch { return false; }
        }

        // Small dark popup, bottom-right. Click anywhere to open the release page;
        // wait 12 s for it to self-dismiss. Borderless, no taskbar entry, topmost.
        sealed class UpdateToast : Form
        {
            private readonly string _url;
            private readonly System.Windows.Forms.Timer _autoClose;

            public UpdateToast(string newVersion, string url)
            {
                _url = url;

                FormBorderStyle  = FormBorderStyle.None;
                StartPosition    = FormStartPosition.Manual;
                ShowInTaskbar    = false;
                TopMost          = true;
                BackColor        = Color.FromArgb(28, 32, 48);
                Width            = 360;
                Height           = 96;

                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 20, wa.Bottom - Height - 20);

                Label title = new Label
                {
                    Text      = "Picasa Portable v" + newVersion + " available",
                    ForeColor = Color.White,
                    Font      = new Font("Segoe UI Semibold", 10F, FontStyle.Regular),
                    Location  = new Point(16, 14),
                    AutoSize  = true,
                    BackColor = Color.Transparent
                };
                Label body = new Label
                {
                    Text      = "Click to open the release page  ·  closes in 12s",
                    ForeColor = Color.FromArgb(190, 200, 220),
                    Font      = new Font("Segoe UI", 9F, FontStyle.Regular),
                    Location  = new Point(16, 42),
                    AutoSize  = true,
                    BackColor = Color.Transparent
                };
                Label close = new Label
                {
                    Text          = "×",
                    ForeColor     = Color.FromArgb(160, 170, 190),
                    Font          = new Font("Segoe UI", 12F, FontStyle.Bold),
                    Location      = new Point(Width - 28, 6),
                    Size          = new Size(22, 22),
                    Cursor        = Cursors.Hand,
                    TextAlign     = ContentAlignment.MiddleCenter,
                    BackColor     = Color.Transparent
                };
                close.Click += (s, e) => SafeClose();

                Click       += (s, e) => OpenAndClose();
                title.Click += (s, e) => OpenAndClose();
                body.Click  += (s, e) => OpenAndClose();

                Controls.Add(title);
                Controls.Add(body);
                Controls.Add(close);

                _autoClose = new System.Windows.Forms.Timer { Interval = 12000 };
                _autoClose.Tick += (s, e) => SafeClose();
                _autoClose.Start();
            }

            private void OpenAndClose()
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
                }
                catch { /* shell can refuse; we still close */ }
                SafeClose();
            }

            private void SafeClose()
            {
                try { if (_autoClose != null) { _autoClose.Stop(); _autoClose.Dispose(); } } catch { }
                try { Close(); } catch { }
            }
        }
    }
}
