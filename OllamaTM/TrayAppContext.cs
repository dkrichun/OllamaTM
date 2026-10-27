using System;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using FormsTimer = System.Windows.Forms.Timer;

namespace OllamaTM
{
    internal class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly FormsTimer _statusTimer;
        private readonly Control _marshaler;
        private readonly string _iniPath;

        private ServiceController _service;
        private string _serviceName = "Ollama";
        private int _startTimeoutSec = 15;
        private int _stopTimeoutSec = 15;
        private bool _notifyOnChange = true;

        private Icon? _greenIcon;
        private Icon? _redIcon;
        private Icon? _customGreen;
        private Icon? _customRed;

        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _debounceCts;

        private readonly SemaphoreSlim _serviceLock = new(1, 1);
        private bool _lastRunningState;
        private bool _hasStateInitialized = false;

        private const int MenuStart = 0;
        private const int MenuStop = 1;
        private const int MenuRestart = 2;
        private const int MenuExit = 4;

        public TrayAppContext(string iniPath)
        {
            _iniPath = iniPath;
            _marshaler = new Control();
            _ = _marshaler.Handle;

            _service = new ServiceController(_serviceName);
            Logger.Info("Инициализация интерфейса трея.");

            _menu = new ContextMenuStrip();
            _menu.Items.Add(new ToolStripMenuItem("Запуск", null, async (s, e) => await StartServiceAsync()));
            _menu.Items.Add(new ToolStripMenuItem("Остановка", null, async (s, e) => await StopServiceAsync()));
            _menu.Items.Add(new ToolStripMenuItem("Перезапуск", null, async (s, e) => await RestartServiceAsync()));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Выход", null, ExitApplication));

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Ollama",
                ContextMenuStrip = _menu,
                Visible = true
            };

            _trayIcon.MouseClick += OnTrayClick;

            _statusTimer = new FormsTimer();
            _statusTimer.Tick += (_, _) => UpdateStatus();

            ApplyConfig(IniConfig.Load(_iniPath));
            SetupWatcher();
        }

        private async void OnTrayClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            _service.Refresh();
            bool running = _service.Status == ServiceControllerStatus.Running
                        || _service.Status == ServiceControllerStatus.StartPending;

            if (running) await StopServiceAsync();
            else await StartServiceAsync();
        }

        private void ApplyConfig(IniConfig cfg)
        {
            string greenPath = cfg.GetString("icons", "GreenIcon", "green.ico") ?? "green.ico";
            string redPath = cfg.GetString("icons", "RedIcon", "red.ico") ?? "red.ico";

            var cg = TryLoadCustomIcon(greenPath);
            var cr = TryLoadCustomIcon(redPath);

            _customGreen?.Dispose();
            _customRed?.Dispose();
            _customGreen = cg;
            _customRed = cr;

            _greenIcon = cg ?? SystemIcons.Application;
            _redIcon = cr ?? SystemIcons.Error;

            _startTimeoutSec = Math.Max(1, cfg.GetInt("timeouts", "StartSeconds", 15));
            _stopTimeoutSec = Math.Max(1, cfg.GetInt("timeouts", "StopSeconds", 15));
            _notifyOnChange = cfg.GetInt("ui", "NotifyOnChange", 1) == 1;

            int interval = Math.Max(1000, cfg.GetInt("ui", "CheckIntervalMs", 5000));
            _statusTimer.Interval = interval;

            var newName = cfg.GetString("ollama", "ServiceName", "Ollama") ?? "Ollama";
            if (_service == null || !string.Equals(_serviceName, newName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Смена имени службы с '{_serviceName}' на '{newName}'.");
                _service?.Dispose();
                _serviceName = newName;
                _service = new ServiceController(_serviceName);
                _hasStateInitialized = false;
            }

            if (!_statusTimer.Enabled) _statusTimer.Start();
            UpdateStatus();
        }

        private static Icon? TryLoadCustomIcon(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                var full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
                if (File.Exists(full)) return new Icon(full);
                Logger.Warn($"Файл иконки не найден: {full}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка загрузки иконки из {path}", ex);
                return null;
            }
        }

        private void UpdateStatus()
        {
            try
            {
                _service.Refresh();
                bool running = _service.Status == ServiceControllerStatus.Running
                            || _service.Status == ServiceControllerStatus.StartPending;

                _trayIcon.Icon = running ? _greenIcon : _redIcon;
                _trayIcon.Text = running
                    ? $"{_serviceName}: запущена (ЛКМ — остановить)"
                    : $"{_serviceName}: остановлена (ЛКМ — запустить)";

                _menu.Items[MenuStart].Enabled = !running;
                _menu.Items[MenuStop].Enabled = running;
                _menu.Items[MenuRestart].Enabled = running;

                // Уведомление о смене статуса
                if (_hasStateInitialized && _lastRunningState != running && _notifyOnChange)
                {
                    string msg = running ? "Служба запущена" : "Служба остановлена";
                    _trayIcon.ShowBalloonTip(2000, "OllamaTM", msg, ToolTipIcon.Info);
                }

                _lastRunningState = running;
                _hasStateInitialized = true;
            }
            catch (InvalidOperationException)
            {
                _trayIcon.Icon = _redIcon;
                _trayIcon.Text = $"Служба '{_serviceName}' не найдена";
                _menu.Items[MenuStart].Enabled = false;
                _menu.Items[MenuStop].Enabled = false;
                _menu.Items[MenuRestart].Enabled = false;
            }
        }

        private async Task StartServiceAsync()
        {
            if (!await _serviceLock.WaitAsync(0)) return;
            try
            {
                SetMenuEnabled(false);
                Logger.Info("Команда: Запуск.");
                await Task.Run(() => {
                    _service.Start();
                    _service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(_startTimeoutSec));
                });
                Logger.Info($"Служба '{_serviceName}' успешно запущена.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Не удалось запустить службу '{_serviceName}'", ex);
                _marshaler.BeginInvoke((Action)(() => MessageBox.Show($"Не удалось запустить службу:\n{ex.Message}", "OllamaTM", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
            finally
            {
                SetMenuEnabled(true);
                UpdateStatus();
                _serviceLock.Release();
            }
        }

        private async Task StopServiceAsync()
        {
            if (!await _serviceLock.WaitAsync(0)) return;
            try
            {
                SetMenuEnabled(false);
                Logger.Info("Команда: Остановка.");
                await Task.Run(() => {
                    _service.Stop();
                    _service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(_stopTimeoutSec));
                });
                Logger.Info($"Служба '{_serviceName}' успешно остановлена.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Не удалось остановить службу '{_serviceName}'", ex);
                _marshaler.BeginInvoke((Action)(() => MessageBox.Show($"Не удалось остановить службу:\n{ex.Message}", "OllamaTM", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
            finally
            {
                SetMenuEnabled(true);
                UpdateStatus();
                _serviceLock.Release();
            }
        }

        private async Task RestartServiceAsync()
        {
            if (!await _serviceLock.WaitAsync(0)) return;
            try
            {
                SetMenuEnabled(false);
                Logger.Info("Команда: Перезапуск.");
                await Task.Run(() => {
                    _service.Refresh();
                    if (_service.Status != ServiceControllerStatus.Stopped)
                    {
                        _service.Stop();
                        _service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(_stopTimeoutSec));
                    }
                    _service.Start();
                    _service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(_startTimeoutSec));
                });
                Logger.Info($"Служба '{_serviceName}' успешно перезапущена.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Не удалось перезапустить службу '{_serviceName}'", ex);
                _marshaler.BeginInvoke((Action)(() => MessageBox.Show($"Не удалось перезапустить службу:\n{ex.Message}", "OllamaTM", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
            finally
            {
                SetMenuEnabled(true);
                UpdateStatus();
                _serviceLock.Release();
            }
        }

        private void SetMenuEnabled(bool enabled)
        {
            _menu.Items[MenuStart].Enabled = enabled;
            _menu.Items[MenuStop].Enabled = enabled;
            _menu.Items[MenuRestart].Enabled = enabled;
        }

        private void SetupWatcher()
        {
            try
            {
                var dir = Path.GetDirectoryName(_iniPath);
                var file = Path.GetFileName(_iniPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnIniChanged;
                _watcher.Created += OnIniChanged;
                _watcher.Renamed += (s, e) => OnIniChanged(s, e);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось запустить слежение за файлом INI", ex);
                MessageBox.Show($"Не удалось запустить наблюдение за INI:\n{ex.Message}", "OllamaTM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void OnIniChanged(object sender, FileSystemEventArgs e)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            try
            {
                await Task.Delay(500, token);
                if (!token.IsCancellationRequested)
                {
                    _marshaler.BeginInvoke((Action)ReloadConfig);
                }
            }
            catch (TaskCanceledException)
            {
                // Игнорируем отмененные таймеры
            }
        }

        private void ReloadConfig()
        {
            try
            {
                Logger.Info("Обнаружено изменение INI. Перезагрузка настроек.");
                var cfg = IniConfig.Load(_iniPath);

                if (_serviceLock.Wait(0))
                {
                    try { ApplyConfig(cfg); }
                    finally { _serviceLock.Release(); }
                }
                else
                {
                    Logger.Warn("Пропущена перезагрузка INI, так как выполняется операция со службой.");
                    return;
                }

                _trayIcon.ShowBalloonTip(2000, "OllamaTM", "Настройки перезагружены", ToolTipIcon.Info);
                Logger.Info("Настройки успешно применены.");
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка при перезагрузке INI", ex);
                _trayIcon.ShowBalloonTip(3000, "OllamaTM", $"Ошибка перезагрузки: {ex.Message}", ToolTipIcon.Error);
            }
        }

        private void ExitApplication(object? sender, EventArgs e)
        {
            Logger.Info("Команда из меню: Выход. Закрытие приложения.");
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _statusTimer.Stop(); _statusTimer.Dispose();
                _watcher?.Dispose();
                _serviceLock.Dispose();
                _service?.Dispose();
                _customGreen?.Dispose(); _customRed?.Dispose();
                _trayIcon.Visible = false; _trayIcon.Dispose();
                _menu.Dispose(); _marshaler.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}