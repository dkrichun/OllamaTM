using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OllamaTM
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Глобальный перехват ошибок
            Application.ThreadException += (s, e) => Logger.Error("Необработанная ошибка интерфейса", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Error("Критическая ошибка", e.ExceptionObject as Exception);

            const string mutexName = @"Global\OllamaTM_SingleInstance_Mutex";

            using (var mutex = new Mutex(true, mutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    Logger.Warn("Попытка запуска второго экземпляра программы.");
                    MessageBox.Show("Программа OllamaTM уже запущена и находится в системном трее.",
                                    "OllamaTM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Logger.Info("=== Запуск OllamaTM ===");

                try
                {
                    ApplicationConfiguration.Initialize();
                    var iniPath = Path.Combine(AppContext.BaseDirectory, "OllamaTM.ini");
                    Application.Run(new TrayAppContext(iniPath));
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка при работе главного цикла приложения", ex);
                }
                finally
                {
                    Logger.Info("=== Завершение работы OllamaTM ===");
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}