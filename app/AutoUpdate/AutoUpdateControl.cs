using OHelper.Helpers;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace OHelper.AutoUpdate
{
    public class AutoUpdateControl
    {

        SettingsForm settings;

        public string versionUrl = "https://github.com/CoolDotty/o-helper/releases";
        public bool update = false;

        static long lastUpdate;

        public AutoUpdateControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
            var appVersion = GetCurrentVersion();
            settings.SetVersionLabel(Properties.Strings.VersionLabel + $": {FormatVersion(appVersion)}");
        }

        public void CheckForUpdates()
        {
            // Run update once per 12 hours
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < 43200) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                CheckForUpdatesAsync();
            });
        }

        public void Update()
        {
            if (update)
            {
                Task.Run(() =>
                {
                    CheckForUpdatesAsync(true);
                });
            } else
            {
                LoadReleases();
            }
        }

        public void LoadReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(versionUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        async void CheckForUpdatesAsync(bool force = false)
        {

            if (AppConfig.Is("skip_updates")) return;

            try
            {

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "O-Helper App");
                    var json = await httpClient.GetStringAsync("https://api.github.com/repos/CoolDotty/o-helper/releases/latest");
                    var config = JsonSerializer.Deserialize<JsonElement>(json);
                    var tag = NormalizeReleaseTag(config.GetProperty("tag_name").ToString());
                    var assets = config.GetProperty("assets");

                    string? url = null;

                    for (int i = 0; i < assets.GetArrayLength(); i++)
                    {
                        var assetUrl = assets[i].GetProperty("browser_download_url").ToString();
                        if (assetUrl.EndsWith("OHelper.zip", StringComparison.OrdinalIgnoreCase))
                        {
                            url = assetUrl;
                            break;
                        }

                        if (assetUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            url = assetUrl;
                    }

                    if (url is null)
                    {
                        for (int i = 0; i < assets.GetArrayLength(); i++)
                        {
                            var assetUrl = assets[i].GetProperty("browser_download_url").ToString();
                            if (assetUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                url = assetUrl;
                                break;
                            }
                        }
                    }

                    if (url is null)
                    {
                        Logger.WriteLine("Failed to check for updates: no supported release asset found");
                        return;
                    }

                    if (!Version.TryParse(tag, out var gitVersion))
                    {
                        Logger.WriteLine($"Failed to check for updates: invalid release tag {tag}");
                        return;
                    }

                    var appVersion = GetCurrentVersion();

                    if (gitVersion.CompareTo(appVersion) > 0)
                    {
                        versionUrl = url;
                        update = true;
                        settings.SetVersionLabel(Properties.Strings.DownloadUpdate + $": {FormatVersion(appVersion)} -> {FormatVersion(gitVersion)}", true);

                        string[] args = Environment.GetCommandLineArgs();
                        if (force || args.Length > 1 && args[1] == "autoupdate")
                        {
                            AutoUpdate(url);
                            return;
                        }

                        if (AppConfig.GetString("skip_version") != tag)
                        {
                            DialogResult dialogResult = DialogResult.No;

                            settings.Invoke((System.Windows.Forms.MethodInvoker)delegate
                            {
                                dialogResult = MessageBox.Show(settings, Properties.Strings.DownloadUpdate + ": O-Helper " + tag + "?", "Update", MessageBoxButtons.YesNo);
                            });
                            
                            if (dialogResult == DialogResult.Yes)
                                AutoUpdate(url);
                            else
                                AppConfig.Set("skip_version", tag);
                        }

                    }
                    else
                    {
                        Logger.WriteLine($"Latest version {appVersion}");
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to check for updates:" + ex.Message);
            }

        }

        public static string EscapeString(string input)
        {
            return input.Replace("'", "''");
        }

        static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        }

        static string FormatVersion(Version version)
        {
            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }

        static string NormalizeReleaseTag(string tag)
        {
            tag = tag.Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag[1..];
            return tag;
        }

        void AutoUpdate(string requestUri)
        {

            Uri uri = new Uri(requestUri);
            string zipName = Path.GetFileName(uri.LocalPath);

            string exeLocation = Application.ExecutablePath;
            string? exeDir = Path.GetDirectoryName(exeLocation);
            if (string.IsNullOrEmpty(exeDir))
            {
                Logger.WriteLine("Failed to update: can't detect executable directory");
                LoadReleases();
                return;
            }

            //exeDir = "C:\\Program Files\\\OHelper";
            string exeName = Path.GetFileName(exeLocation);
            string downloadName = zipName.Equals(exeName, StringComparison.OrdinalIgnoreCase) ? $"{zipName}.download" : zipName;
            string zipLocation = Path.Combine(exeDir, downloadName);

            using (WebClient client = new WebClient())
            {

                client.Headers.Add("User-Agent", "O-Helper App");
                Logger.WriteLine(requestUri);
                Logger.WriteLine(exeDir);
                Logger.WriteLine(zipName);
                Logger.WriteLine(downloadName);
                Logger.WriteLine(exeName);

                try
                {
                    client.DownloadFile(uri, zipLocation);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                    if (!ProcessHelper.IsUserAdministrator())
                    {
                        ProcessHelper.RunAsAdmin("autoupdate");
                        Application.Exit();
                    } else
                    {
                        LoadReleases();
                    }
                    return;
                }

                string exePath = Path.Combine(exeDir, exeName);
                int processId = Environment.ProcessId;
                string command =
                    "$ErrorActionPreference = 'Stop'; " +
                    $"Wait-Process -Id {processId} -ErrorAction SilentlyContinue; " +
                    $"Set-Location -LiteralPath '{EscapeString(exeDir)}'; " +
                    $"if ('{EscapeString(zipName)}' -like '*.zip') {{ " +
                    $"Expand-Archive -LiteralPath '{EscapeString(zipLocation)}' -DestinationPath '{EscapeString(exeDir)}' -Force; " +
                    $"Remove-Item -LiteralPath '{EscapeString(zipLocation)}' -Force; " +
                    "} else { " +
                    $"Copy-Item -LiteralPath '{EscapeString(zipLocation)}' -Destination '{EscapeString(exePath)}' -Force; " +
                    $"Remove-Item -LiteralPath '{EscapeString(zipLocation)}' -Force; " +
                    "} " +
                    $"Start-Process -FilePath '{EscapeString(exePath)}' -WorkingDirectory '{EscapeString(exeDir)}';";
                Logger.WriteLine(command);

                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.WorkingDirectory = exeDir;
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.FileName = "powershell.exe";
                    cmd.StartInfo.ArgumentList.Add("-NoProfile");
                    cmd.StartInfo.ArgumentList.Add("-ExecutionPolicy");
                    cmd.StartInfo.ArgumentList.Add("Bypass");
                    cmd.StartInfo.ArgumentList.Add("-Command");
                    cmd.StartInfo.ArgumentList.Add(command);
                    if (!cmd.Start())
                    {
                        LoadReleases();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                    LoadReleases();
                    return;
                }

                Application.Exit();
            }

        }

    }
}
