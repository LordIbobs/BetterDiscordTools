using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterDiscordLauncher
{
    internal class Program
    {
        static readonly string launcherBasePath = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string bdInstallPathAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterDiscord");
        static readonly string bdDataPath = Path.Combine(bdInstallPathAppData, "data");
        static readonly string asarFileName = "betterdiscord.asar";
        static readonly string asarFilePath = Path.Combine(bdDataPath, asarFileName);
        static readonly string versionFileLocal = Path.Combine(launcherBasePath, "discord_version.cache");
        static readonly string logFilePath = Path.Combine(launcherBasePath, "launcher.log");
        static readonly string discordBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int SW_HIDE = 0;

        static bool showConsole = false;

        static async Task Main(string[] args)
        {
            showConsole = args.Contains("--console", StringComparer.OrdinalIgnoreCase);
            if (!showConsole) ShowWindow(GetConsoleWindow(), SW_HIDE);
            else LogStep("Запуск в режиме консоли");

            LogStep("=== BetterDiscord Launcher started ===");
            Directory.CreateDirectory(launcherBasePath);

            if (!File.Exists(versionFileLocal))
                await File.WriteAllTextAsync(versionFileLocal, "");

            if (!Directory.Exists(bdInstallPathAppData))
            {
                LogStep("[*] Папка BetterDiscord не найдена. Загружаем установщик...");
                const string installerUrl = "https://github.com/BetterDiscord/Installer/releases/latest/download/BetterDiscord-Windows.exe";
                string installerPath = Path.Combine(launcherBasePath, "BetterDiscord-Installer.exe");

                try
                {
                    using var httpClient = new HttpClient();
                    var installerData = await httpClient.GetByteArrayAsync(installerUrl);
                    await File.WriteAllBytesAsync(installerPath, installerData);

                    LogSuccess("[+] Установщик BetterDiscord скачан. Запускаем...");
                    var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                    process?.WaitForExit();

                    LogStep("[*] Установка завершена, удаляем установщик...");
                    File.Delete(installerPath);

                    LogStep("[*] Запускаем Discord после установки BD...");
                    string? discordAfterInstall = GetLatestDiscordExePath();
                    if (discordAfterInstall != null) LaunchDiscord(discordAfterInstall);
                    return;
                }
                catch (Exception ex)
                {
                    LogError("[-] Ошибка загрузки или запуска установщика: " + ex.Message);
                    return;
                }
            }

            string? discordExePath = GetLatestDiscordExePath();
            if (discordExePath == null)
            {
                LogError("Discord не найден.");
                return;
            }

            LogSuccess($"Discord найден: {discordExePath}");

            string currentVersion = GetDiscordVersion(discordExePath);
            string savedVersion = (await File.ReadAllTextAsync(versionFileLocal)).Trim();

            LogStep("Запускаем Discord...");
            LaunchDiscord(discordExePath);

            LogStep("Ожидаем запуска Discord...");
            await Task.Delay(10000);

            LogStep("Ожидаем закрытия Discord...");
            KillDiscord();

            LogStep("Discord закрыт, начинаем проверку патча и repair.");

            LogStep("[Check] Проверка наличия патча в index.js...");
            bool indexJsPatched = IsIndexJsPatched();
            if (currentVersion != savedVersion || !indexJsPatched)
            {
                LogStep("[Check] Патч BetterDiscord не обнаружен или версия обновлена, запускаем repair...");
                await RepairAsync(discordExePath);
                await File.WriteAllTextAsync(versionFileLocal, currentVersion);
            }
            else
            {
                LogStep("[Check] Патч BetterDiscord уже актуален, повторный repair не требуется.");
            }

            LogStep("Запускаем Discord с исправлениями...");
            LaunchDiscord(discordExePath);
            await Task.Delay(5000);

            if (!Process.GetProcessesByName("Discord").Any())
            {
                LogError("Discord не запустился. Возможна ошибка патча. Пытаемся восстановить index.js из .bak...");
                RestoreIndexJsBackup();
                LaunchDiscord(discordExePath);
            }
        }

        static async Task RepairAsync(string discordExePath)
        {
            try
            {
                if (!File.Exists(asarFilePath))
                {
                    LogStep("betterdiscord.asar не найден, скачиваем...");
                    await UpdateBetterDiscordAsar();
                }

                var destAsar = Path.Combine(bdDataPath, asarFileName);
                var tempAsar = destAsar + ".tmp";
                const int maxTries = 20;
                bool copied = false;

                for (int i = 0; i < maxTries; i++)
                {
                    LogStep($"Процессы Discord перед копированием: {Process.GetProcessesByName("Discord").Length}");
                    if (!File.Exists(asarFilePath))
                    {
                        LogError("Файл betterdiscord.asar исчез до попытки копирования.");
                        return;
                    }

                    try
                    {
                        await Task.Delay(1000);
                        File.Copy(asarFilePath, tempAsar, true);
                        File.Replace(tempAsar, destAsar, null);
                        LogSuccess("betterdiscord.asar обновлён через File.Replace.");
                        copied = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogStep("Ошибка копирования betterdiscord.asar: " + ex.Message);
                    }
                }

                if (!copied)
                {
                    LogError("Не удалось обновить betterdiscord.asar — файл всё ещё занят.");
                    return;
                }

                var indexJsPath = GetDiscordIndexJsPath();
                if (indexJsPath == null)
                {
                    LogError("index.js не найден для патча.");
                    return;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string escapedPath = appData.Replace(@"\", @"\\");
                string patchContent =
                    $"require(\"{escapedPath}\\\\BetterDiscord\\\\data\\\\betterdiscord.asar\");\r\n" +
                    "module.exports = require(\"./core.asar\");";

                if (!File.Exists(indexJsPath))
                {
                    LogStep("index.js не найден, создаём с нуля...");
                    File.WriteAllText(indexJsPath, patchContent);
                    LogSuccess("index.js создан: " + indexJsPath);
                }

                string backupPath = Path.Combine(launcherBasePath, "index.js.bak");
                if (!File.Exists(backupPath))
                {
                    File.Copy(indexJsPath, backupPath);
                    LogSuccess("Резервная копия создана: " + backupPath);
                }

                File.WriteAllText(indexJsPath, patchContent);
                LogSuccess("index.js пропатчен: " + indexJsPath);
            }
            catch (Exception ex)
            {
                LogError("Repair failed: " + ex.Message);
            }
        }

        static string? GetLatestDiscordExePath()
        {
            if (!Directory.Exists(discordBasePath)) return null;
            var dirs = Directory.GetDirectories(discordBasePath, "app-*");
            if (!dirs.Any()) return null;
            var latest = dirs.OrderBy(d => d).Last();
            var exe = Path.Combine(latest, "Discord.exe");
            return File.Exists(exe) ? exe : null;
        }

        static string GetDiscordVersion(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown"; }
            catch { return "unknown"; }
        }

        static void KillDiscord()
        {
            foreach (var proc in Process.GetProcessesByName("Discord"))
            {
                try { proc.Kill(); proc.WaitForExit(); } catch { }
            }
        }

        static string? GetDiscordIndexJsPath()
        {
            try
            {
                if (!Directory.Exists(discordBasePath)) return null;
                var appDirs = Directory.GetDirectories(discordBasePath, "app-*");
                var latestApp = appDirs.OrderBy(d => d).LastOrDefault();
                if (latestApp == null) return null;

                var modulesPath = Path.Combine(latestApp, "modules");
                if (!Directory.Exists(modulesPath)) return null;

                var coreCandidates = Directory.GetDirectories(modulesPath, "discord_desktop_core*");
                foreach (var coreCandidate in coreCandidates.OrderByDescending(d => d))
                {
                    var innerCorePath = Path.Combine(coreCandidate, "discord_desktop_core");
                    if (Directory.Exists(innerCorePath))
                        return Path.Combine(innerCorePath, "index.js");

                    return Path.Combine(coreCandidate, "index.js");
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError("Ошибка поиска index.js: " + ex.Message);
                return null;
            }
        }

        static bool IsIndexJsPatched()
        {
            var indexJsPath = GetDiscordIndexJsPath();
            if (indexJsPath == null || !File.Exists(indexJsPath))
            {
                LogError("index.js не найден для проверки патча.");
                return false;
            }

            string content = File.ReadAllText(indexJsPath);
            bool contains = content.Contains("betterdiscord.asar");
            LogStep(contains ? "[Check] Патч в index.js найден." : "[Check] Патч в index.js не найден.");
            return contains;
        }

        static void RestoreIndexJsBackup()
        {
            try
            {
                string backupPath = Path.Combine(launcherBasePath, "index.js.bak");
                if (!File.Exists(backupPath))
                {
                    LogError("Файл index.js.bak не найден, восстановление невозможно.");
                    return;
                }

                var indexJsPath = GetDiscordIndexJsPath();
                if (indexJsPath == null)
                {
                    LogError("index.js не найден для восстановления.");
                    return;
                }

                File.Copy(backupPath, indexJsPath, true);
                LogSuccess("index.js восстановлен из .bak");
            }
            catch (Exception ex)
            {
                LogError("Ошибка при восстановлении index.js: " + ex.Message);
            }
        }

        static async Task UpdateBetterDiscordAsar()
        {
            try
            {
                const string api = "https://api.github.com/repos/BetterDiscord/BetterDiscord/releases/latest";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterDiscordLauncher");
                var json = await client.GetStringAsync(api);
                using var doc = JsonDocument.Parse(json);
                var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith(".asar") == true);
                var url = asset.GetProperty("browser_download_url").GetString();
                var data = await client.GetByteArrayAsync(url);
                Directory.CreateDirectory(bdDataPath);
                await File.WriteAllBytesAsync(asarFilePath, data);
            }
            catch (Exception ex)
            {
                LogError("Ошибка обновления asar: " + ex.Message);
            }
        }

        static void LaunchDiscord(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path)
                });
            }
            catch (Exception ex)
            {
                LogError("Ошибка запуска Discord: " + ex.Message);
            }
        }

        static void Log(string pfx, string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {pfx} {msg}";
            File.AppendAllText(logFilePath, line + Environment.NewLine);
            if (showConsole) Console.WriteLine(line);
        }
        static void LogStep(string m) => Log("[*]", m);
        static void LogSuccess(string m) => Log("[+]", m);
        static void LogError(string m) => Log("[-]", m);
    }
}
