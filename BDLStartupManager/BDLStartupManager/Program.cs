using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace BetterDiscordLauncherStartupManager
{
    class Program
    {
        static readonly string startupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static readonly string bdlStartupManagerDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        static readonly string launcherPath = Path.Combine(bdlStartupManagerDir, "BetterDiscordLauncher.exe");
        static readonly string launcherName = "BetterDiscordLauncher";

        static void Main(string[] args)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== BetterDiscord Launcher Autostart Manager ===");
                Console.WriteLine($"Путь к лаунчеру:\n{launcherPath}\n");
                Console.WriteLine("1. Добавить в автозагрузку");
                Console.WriteLine("2. Убрать из автозагрузки");
                Console.WriteLine("3. Проверить состояние автозагрузки");
                Console.WriteLine("4. Выйти");
                Console.Write("Выберите пункт (1-4): ");

                var key = Console.ReadKey(intercept: true).KeyChar;
                Console.WriteLine();

                switch (key)
                {
                    case '1':
                        AddToStartup();
                        break;
                    case '2':
                        RemoveFromStartup();
                        break;
                    case '3':
                        CheckStartup();
                        break;
                    case '4':
                        return;
                    default:
                        Console.WriteLine("Неверный ввод. Попробуйте снова.");
                        break;
                }

                Console.WriteLine("Нажмите любую клавишу, чтобы продолжить...");
                Console.ReadKey();
            }
        }

        static void AddToStartup()
        {
            if (!File.Exists(launcherPath))
            {
                Console.WriteLine($"Ошибка: не найден файл лаунчера по пути:\n{launcherPath}");
                return;
            }

            using (var key = Registry.CurrentUser.OpenSubKey(startupRegistryKey, writable: true))
            {
                if (key == null)
                {
                    Console.WriteLine("Не удалось открыть раздел реестра для записи.");
                    return;
                }

                key.SetValue(launcherName, $"\"{launcherPath}\"");
                Console.WriteLine("Лаунчер добавлен в автозагрузку.");
            }
        }

        static void RemoveFromStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(startupRegistryKey, writable: true))
            {
                if (key == null)
                {
                    Console.WriteLine("Не удалось открыть раздел реестра для удаления.");
                    return;
                }

                if (key.GetValue(launcherName) != null)
                {
                    key.DeleteValue(launcherName);
                    Console.WriteLine("Лаунчер удалён из автозагрузки.");
                }
                else
                {
                    Console.WriteLine("Лаунчер не найден в автозагрузке.");
                }
            }
        }

        static void CheckStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(startupRegistryKey, writable: false))
            {
                if (key == null)
                {
                    Console.WriteLine("Не удалось открыть раздел реестра для чтения.");
                    return;
                }

                var value = key.GetValue(launcherName) as string;
                if (string.IsNullOrEmpty(value))
                {
                    Console.WriteLine("Лаунчер не находится в автозагрузке.");
                }
                else
                {
                    Console.WriteLine($"Лаунчер находится в автозагрузке. Путь:\n{value}");
                }
            }
        }
    }
}
