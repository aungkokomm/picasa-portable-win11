using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ServiceProcess;

namespace PicasaPortable
{
    class Program
    {
        private static string portableRoot;
        private static string dataPath;
        private static string picturesPath;
        private static string sandboxiePath;

        [DllImport("shell32.dll")]
        private static extern bool IsUserAnAdmin();

        static void Main()
        {
            try
            {
                portableRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                dataPath = Path.Combine(portableRoot, "data");
                picturesPath = Path.Combine(portableRoot, "Pictures");
                sandboxiePath = Path.Combine(portableRoot, "Sandboxie-Plus");

                bool firstRun = !Directory.Exists(sandboxiePath) ||
                                !File.Exists(Path.Combine(sandboxiePath, "Start.exe"));

                bool serviceMissing = !IsServiceInstalled("SbieSvc");

                bool picasaMissing = !File.Exists(Path.Combine(dataPath,
                    @"drive\C\Program Files\Google\Picasa3\Picasa3.exe"));

                if (firstRun || serviceMissing || picasaMissing)
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

                    PerformSetup(firstRun, serviceMissing, picasaMissing);
                }

                CopyIniToWindows();
                EnsureServiceRunning();

                // Always update watchedfolders.txt with current drive letter (handles USB drive-letter changes)
                PreconfigurePicasaScan();

                LaunchPicasa();
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

            // Always write fresh config in case drive letter changed
            WriteSandboxieIni();
            CopyIniToWindows();
            StartService();

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

        static void WriteSandboxieIni()
        {
            string userName = Environment.UserName;
            // WriteFilePath = "box only" - hides host data, allows sandbox to create its own version
            // ClosedFilePath = blocks both read and write completely
            string ini =
                "[GlobalSettings]\r\n\r\n" +
                "[Picasa]\r\n" +
                "Enabled=y\r\n" +
                "FileRootPath=" + dataPath + "\r\n" +
                "ConfigLevel=9\r\n" +
                "BlockNetworkFiles=y\r\n" +
                "ReadFilePath=" + picturesPath + "\r\n" +
                // Hide host's Google data but allow sandbox to create its own (no leak, no errors)
                "WriteFilePath=C:\\Users\\" + userName + "\\AppData\\Local\\Google\r\n" +
                "WriteFilePath=C:\\Users\\" + userName + "\\AppData\\Roaming\\Google\r\n" +
                // Hard block host photo folders entirely (read+write)
                "ClosedFilePath=C:\\Users\\" + userName + "\\Pictures\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\Documents\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\Desktop\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\Downloads\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\Music\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\Videos\r\n" +
                "ClosedFilePath=C:\\Users\\" + userName + "\\OneDrive\r\n" +
                "ClosedFilePath=C:\\Users\\Public\r\n";

            string iniPath = Path.Combine(sandboxiePath, "Sandboxie.ini");
            File.WriteAllText(iniPath, ini, new System.Text.UnicodeEncoding(false, true));
        }

        static void CopyIniToWindows()
        {
            string src = Path.Combine(sandboxiePath, "Sandboxie.ini");
            string dst = @"C:\Windows\Sandboxie.ini";
            if (File.Exists(src))
            {
                try { File.Copy(src, dst, true); }
                catch (Exception ex) { Debug.WriteLine("Could not copy ini: " + ex.Message); }
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
                Arguments = "/box:Picasa \"" + installer + "\"",
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
                Arguments = "/box:Picasa \"C:\\Program Files\\Google\\Picasa3\\Picasa3.exe\"",
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

                // Only rewrite if path differs (avoid unnecessary writes)
                bool needsWrite = true;
                if (File.Exists(watchedFile))
                {
                    string existing = File.ReadAllText(watchedFile);
                    if (existing.Trim() == content.Trim()) needsWrite = false;
                }
                if (needsWrite)
                {
                    File.WriteAllText(watchedFile, content);
                }

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
    }
}
