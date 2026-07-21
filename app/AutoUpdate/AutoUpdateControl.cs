using OHelper.Helpers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace OHelper.AutoUpdate
{
    public class AutoUpdateControl
    {
        const string Repository = "CoolDotty/o-helper";
        const string ReleasesUrl = "https://github.com/CoolDotty/o-helper/releases";
        const string LatestReleaseApiUrl = "https://api.github.com/repos/CoolDotty/o-helper/releases/latest";
        const string ExecutableAssetName = "OHelper.exe";
        const string ArchiveAssetName = "OHelper.zip";
        const long MaximumUpdateSize = 500L * 1024 * 1024;
        const uint WtdUiNone = 2;
        const uint WtdRevokeWholeChain = 1;
        const uint WtdChoiceFile = 1;
        const uint WtdStateActionIgnore = 0;
        static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        static readonly HttpClient HttpClient = CreateHttpClient();

        readonly SettingsForm settings;

        public string versionUrl = ReleasesUrl;
        public bool update = false;

        static long lastUpdate;

        readonly record struct ReleaseAsset(string Url, string Name, string Sha256, long Size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        static extern uint WinVerifyTrust(IntPtr hwnd, [In] ref Guid actionId, [In] ref WinTrustData data);

        public AutoUpdateControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
            var appVersion = GetCurrentVersion();
            settings.SetVersionLabel(Properties.Strings.VersionLabel + $": {FormatVersion(appVersion)}");
        }

        public void CheckForUpdates()
        {
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < 43200) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            _ = CheckForUpdatesAfterDelayAsync();
        }

        public void Update()
        {
            if (update)
                _ = CheckForUpdatesAsync(true);
            else
                LoadReleases();
        }

        static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("O-Helper", "1.0"));
            return client;
        }

        async Task CheckForUpdatesAfterDelayAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            await CheckForUpdatesAsync();
        }

        public void LoadReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        async Task CheckForUpdatesAsync(bool force = false)
        {
            if (AppConfig.Is("skip_updates")) return;

            try
            {
                var json = await HttpClient.GetStringAsync(LatestReleaseApiUrl);
                var config = JsonSerializer.Deserialize<JsonElement>(json);
                if (!IsExpectedRelease(config))
                {
                    Logger.WriteLine("Failed to check for updates: unexpected release metadata");
                    return;
                }

                var tag = NormalizeReleaseTag(config.GetProperty("tag_name").GetString() ?? string.Empty);
                if (!Version.TryParse(tag, out var gitVersion))
                {
                    Logger.WriteLine($"Failed to check for updates: invalid release tag {tag}");
                    return;
                }

                ReleaseAsset? asset = FindExpectedAsset(config.GetProperty("assets"));
                if (asset is null)
                {
                    Logger.WriteLine("Failed to check for updates: no verifiable OHelper release asset found");
                    return;
                }

                var appVersion = GetCurrentVersion();
                if (gitVersion.CompareTo(appVersion) <= 0)
                {
                    Logger.WriteLine($"Latest version {appVersion}");
                    return;
                }

                versionUrl = asset.Value.Url;
                update = true;
                settings.SetVersionLabel(Properties.Strings.DownloadUpdate + $": {FormatVersion(appVersion)} -> {FormatVersion(gitVersion)}", true);

                if (force || Environment.GetCommandLineArgs() is [_, "autoupdate", ..])
                {
                    await AutoUpdateAsync(asset.Value, gitVersion);
                    return;
                }

                if (AppConfig.GetString("skip_version") != tag)
                {
                    DialogResult dialogResult = DialogResult.No;
                    settings.Invoke((System.Windows.Forms.MethodInvoker)delegate
                    {
                        dialogResult = MessageBox.Show(settings, Properties.Strings.DownloadUpdate + ": O-Helper " + tag + "?", Properties.Strings.Updates, MessageBoxButtons.YesNo);
                    });

                    if (dialogResult == DialogResult.Yes)
                        await AutoUpdateAsync(asset.Value, gitVersion);
                    else
                        AppConfig.Set("skip_version", tag);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to check for updates:" + ex.Message);
            }
        }

        static bool IsExpectedRelease(JsonElement release)
        {
            return release.TryGetProperty("html_url", out var page)
                && string.Equals(page.GetString(), ReleasesUrl + "/tag/" + release.GetProperty("tag_name").GetString(), StringComparison.Ordinal);
        }

        static ReleaseAsset? FindExpectedAsset(JsonElement assets)
        {
            ReleaseAsset? executable = null;
            for (int i = 0; i < assets.GetArrayLength(); i++)
            {
                var asset = assets[i];
                string? name = asset.GetProperty("name").GetString();
                if (name != ArchiveAssetName && name != ExecutableAssetName)
                    continue;

                string? url = asset.GetProperty("browser_download_url").GetString();
                string? digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
                long size = asset.TryGetProperty("size", out var sizeValue) ? sizeValue.GetInt64() : 0;
                if (!IsExpectedAssetUrl(url, name) || !TryGetSha256(digest, out var sha256) || size <= 0 || size > MaximumUpdateSize)
                    continue;

                var releaseAsset = new ReleaseAsset(url!, name, sha256, size);
                if (name == ArchiveAssetName)
                    return releaseAsset;
                executable = releaseAsset;
            }
            return executable;
        }

        static bool IsExpectedAssetUrl(string? value, string assetName)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
                return false;

            string[] path = uri.AbsolutePath.Trim('/').Split('/');
            string[] repositoryPath = Repository.Split('/');
            return path.Length == 6 && path[0] == repositoryPath[0] && path[1] == repositoryPath[1] && path[2] == "releases"
                && path[3] == "download" && path[5] == assetName && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }

        static bool TryGetSha256(string? digest, out string sha256)
        {
            const string prefix = "sha256:";
            sha256 = string.Empty;
            if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string hash = digest[prefix.Length..];
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
                return false;
            sha256 = hash;
            return true;
        }

        static bool IsExpectedExecutableVersion(string path, Version expectedVersion)
        {
            try
            {
                Version? candidateVersion = Version.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty);
                return candidateVersion.Major == expectedVersion.Major
                    && candidateVersion.Minor == expectedVersion.Minor
                    && candidateVersion.Build == expectedVersion.Build;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to read update executable version: " + ex.Message);
                return false;
            }
        }

        static bool VerifyAuthenticodeIfPresent(string path)
        {
            try
            {
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                if (!IsAuthenticodeSignatureValid(path))
                {
                    Logger.WriteLine("Update Authenticode signature verification failed");
                    return false;
                }

                Logger.WriteLine("Verified signed update from " + certificate.Subject);
            }
            catch (CryptographicException)
            {
                // Unsigned releases are allowed only with the GitHub-provided SHA-256 digest checked below.
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to verify update signature: " + ex.Message);
                return false;
            }
            return true;
        }

        static bool IsAuthenticodeSignatureValid(string path)
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = path
            };
            IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var data = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeWholeChain,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = fileInfoPointer,
                    dwStateAction = WtdStateActionIgnore
                };
                Guid action = WintrustActionGenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }

        async Task AutoUpdateAsync(ReleaseAsset asset, Version version)
        {
            string? installDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(installDirectory))
            {
                Logger.WriteLine("Failed to update: can't detect executable directory");
                LoadReleases();
                return;
            }

            string stagingDirectory = Path.Combine(Path.GetTempPath(), "OHelper-update-" + Guid.NewGuid().ToString("N"));
            string downloadPath = Path.Combine(stagingDirectory, asset.Name);
            string candidatePath = Path.Combine(stagingDirectory, ExecutableAssetName);
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                using var response = await HttpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumUpdateSize)
                    throw new InvalidDataException("Downloaded update exceeds the maximum allowed size.");

                await using (Stream source = await response.Content.ReadAsStreamAsync())
                await using (FileStream destination = File.Create(downloadPath))
                    await source.CopyToAsync(destination);

                if (new FileInfo(downloadPath).Length != asset.Size || !FileHashMatches(downloadPath, asset.Sha256))
                    throw new InvalidDataException("Downloaded update does not match the release SHA-256 digest.");

                if (asset.Name == ArchiveAssetName)
                    ExtractExpectedExecutable(downloadPath, candidatePath);
                else
                    File.Copy(downloadPath, candidatePath, false);

                if (!IsExpectedExecutableVersion(candidatePath, version) || !VerifyAuthenticodeIfPresent(candidatePath))
                    throw new InvalidDataException("Downloaded update executable failed validation.");

                StartReplacement(installDirectory, candidatePath, stagingDirectory);
                Application.Exit();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to update: " + ex.Message);
                TryDeleteDirectory(stagingDirectory);
                LoadReleases();
            }
        }

        static bool FileHashMatches(string path, string expectedHash)
        {
            using var stream = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        static void ExtractExpectedExecutable(string archivePath, string candidatePath)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != ExecutableAssetName || archive.Entries[0].Length <= 0 || archive.Entries[0].Length > MaximumUpdateSize)
                throw new InvalidDataException("Update ZIP must contain exactly one OHelper.exe file.");

            archive.Entries[0].ExtractToFile(candidatePath, false);
        }

        static void StartReplacement(string installDirectory, string candidatePath, string stagingDirectory)
        {
            string executablePath = Application.ExecutablePath;
            string backupPath = Path.Combine(installDirectory, Path.GetFileName(executablePath) + ".backup-" + Guid.NewGuid().ToString("N"));
            int processId = Environment.ProcessId;
            string command =
                "$ErrorActionPreference = 'Stop'; " +
                $"$exe = '{EscapeString(executablePath)}'; $candidate = '{EscapeString(candidatePath)}'; $backup = '{EscapeString(backupPath)}'; $stage = '{EscapeString(stagingDirectory)}'; " +
                $"Wait-Process -Id {processId} -ErrorAction SilentlyContinue; " +
                "try { " +
                "Copy-Item -LiteralPath $candidate -Destination ($exe + '.new') -Force; " +
                "Move-Item -LiteralPath $exe -Destination $backup -Force; " +
                "Move-Item -LiteralPath ($exe + '.new') -Destination $exe -Force; " +
                "Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ErrorAction Stop; " +
                "Remove-Item -LiteralPath $backup -Force; Remove-Item -LiteralPath $stage -Recurse -Force " +
                "} catch { " +
                "if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue; Move-Item -LiteralPath $backup -Destination $exe -Force }; " +
                "Remove-Item -LiteralPath ($exe + '.new') -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue; throw }";

            var cmd = new Process();
            cmd.StartInfo.WorkingDirectory = installDirectory;
            cmd.StartInfo.UseShellExecute = false;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.FileName = "powershell.exe";
            cmd.StartInfo.ArgumentList.Add("-NoProfile");
            cmd.StartInfo.ArgumentList.Add("-ExecutionPolicy");
            cmd.StartInfo.ArgumentList.Add("Bypass");
            cmd.StartInfo.ArgumentList.Add("-Command");
            cmd.StartInfo.ArgumentList.Add(command);
            if (!cmd.Start())
                throw new InvalidOperationException("Could not start the update replacement process.");
        }

        static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception ex) { Logger.WriteLine("Failed to clean update staging directory: " + ex.Message); }
        }

        public static string EscapeString(string input) => input.Replace("'", "''");

        static Version GetCurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        static string FormatVersion(Version version) => $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        static string NormalizeReleaseTag(string tag)
        {
            tag = tag.Trim();
            return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
        }
    }
}
