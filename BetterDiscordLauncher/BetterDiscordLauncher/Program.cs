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
    class Program
    {
        static readonly string launcherBasePath = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string bdInstallPathAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BetterDiscord");
        static readonly string bdDataPath = Path.Combine(bdInstallPathAppData, "data");
        static readonly string asarFileName = "betterdiscord.asar";
        static readonly string asarFilePath = Path.Combine(bdDataPath, asarFileName);
        static readonly string versionFileLocal = Path.Combine(launcherBasePath, "discord_version.cache");
        static readonly string logFilePath = Path.Combine(launcherBasePath, "launcher.log");
        static readonly string discordBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Discord");

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;

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

            var discordExePath = GetLatestDiscordExePath();
            if (discordExePath == null)
            {
                LogError("Discord не найден. Выход.");
                if (showConsole) WaitForExit();
                return;
            }
            LogSuccess($"Discord найден: {discordExePath}");

            var currentVersion = GetDiscordVersion(discordExePath);
            LogStep($"Текущая версия Discord: {currentVersion}");

            var savedVersion = (await File.ReadAllTextAsync(versionFileLocal)).Trim();
            LogStep($"Сохранённая версия Discord: {savedVersion}");

            if (!Directory.Exists(bdInstallPathAppData))
            {
                LogError("Папка BetterDiscord не найдена. Начинаем установку...");
                await DownloadAndLaunchInstaller(discordExePath);
                if (showConsole) WaitForExit();
                return;
            }

            if (!File.Exists(asarFilePath) || savedVersion != currentVersion)
            {
                LogStep("Обновляем betterdiscord.asar...");
                if (!await UpdateBetterDiscordAsar())
                {
                    LogError("Не удалось обновить betterdiscord.asar.");
                    if (showConsole) WaitForExit();
                    return;
                }
                LogSuccess("Файл betterdiscord.asar обновлён.");
                await File.WriteAllTextAsync(versionFileLocal, currentVersion);
            }
            else LogSuccess("BetterDiscord уже актуален.");

            LogStep("Запускаем Discord...");
            LaunchDiscord(discordExePath);
            LogSuccess("Discord запущен.");

            if (showConsole) WaitForExit();
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

        static async Task<bool> UpdateBetterDiscordAsar()
        {
            try
            {
                const string api = "https://api.github.com/repos/BetterDiscord/BetterDiscord/releases/latest";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterDiscordLauncher");
                var json = await client.GetStringAsync(api);
                using var doc = JsonDocument.Parse(json);
                var asset = doc.RootElement.GetProperty("assets")
                    .EnumerateArray()
                    .FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith(".asar") == true);
                if (asset.ValueKind == JsonValueKind.Undefined) return false;
                var url = asset.GetProperty("browser_download_url").GetString()!;
                Directory.CreateDirectory(bdDataPath);
                var data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(asarFilePath, data);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Ошибка UpdateBetterDiscordAsar: {ex.Message}");
                return false;
            }
        }

        static async Task DownloadAndLaunchInstaller(string discordExePath)
        {
            try
            {
                const string url = "https://github.com/BetterDiscord/Installer/releases/latest/download/BetterDiscord-Windows.exe";
                string installerPath = Path.Combine(launcherBasePath, "BetterDiscordInstaller.exe");

                LogStep("Начинаем скачивание установщика BetterDiscord...");
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterDiscordLauncher");

                var data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(installerPath, data);
                LogSuccess("Скачан установщик BetterDiscord.");

                LogStep("Пробуем запустить установщик...");
                var process = Process.Start(new ProcessStartInfo(installerPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = launcherBasePath
                });

                if (process == null)
                {
                    LogError("Не удалось запустить установщик BetterDiscord.");
                    return;
                }
                LogSuccess("Установщик BetterDiscord запущен.");

                await Task.Run(() => process.WaitForExit());

                LogStep("Установщик завершён. Проверяем папку BetterDiscord...");

                if (Directory.Exists(bdInstallPathAppData))
                {
                    LogSuccess("Папка BetterDiscord найдена. Запускаем Discord...");
                    LaunchDiscord(discordExePath);
                    LogSuccess("Discord запущен.");
                }
                else
                {
                    LogError("Папка BetterDiscord не найдена после установки.");
                }
            }
            catch (Exception ex)
            {
                LogError($"Ошибка при скачивании или запуске установщика: {ex.Message}");
            }
        }

        static void LaunchDiscord(string exePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                });
            }
            catch (Exception ex)
            {
                LogError($"Ошибка запуска Discord: {ex.Message}");
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

        static void WaitForExit()
        {
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}
