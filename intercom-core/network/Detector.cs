using System;  // Основные системные классы
using System.Collections.Generic;  // Общие коллекции (List, Dictionary)
using System.Linq;  // LINQ для работы с коллекциями
using System.Net;  // Работа с IP-адресами
using System.Net.NetworkInformation;  // Получение информации о сетевых интерфейсах
using System.Net.Sockets;  // Работа с TCP сокетами
using System.Threading.Tasks;  // Асинхронное программирование
using wr;  // Пространство имен для WriterIP

namespace network
{
    // Детектор сетевых клиентов и интерфейсов
    // Отвечает за сканирование сети для обнаружения активных клиентов и управления сетевыми интерфейсами
    // Включает функции автосканирования, статистики и интеграции с настройками приложения
    public class Detector
    {
        // Зависимости (Dependency Injection):
        private readonly WriterIP _writer;  // Компонент для работы с файлами (запись/чтение IP-адресов)
        private readonly ConnectionManager _connectionManager;  // Менеджер подключений для автоподключения к найденным клиентам

        // Синхронизация и состояние:
        private readonly object _scanStatsLock = new object();  // Объект для потокобезопасного доступа к статистике
        private bool _isScanning = false;  // Флаг выполнения сканирования (предотвращает параллельные сканирования)

        // Статистика сканирований:
        private int _totalScans = 0;  // Общее количество выполненных сканирований
        private int _successfulScans = 0;  // Количество успешных сканирований
        private long _totalFoundIps = 0;  // Общее количество найденных IP-адресов за все сканирования
        private DateTime _lastScanTime = DateTime.MinValue;  // Время последнего сканирования
        private (bool Enabled, int Interval) _autoScanStatus = (false, 0);  // Статус автосканирования (включено/выключено, а также интервал)
        private System.Timers.Timer _autoScanTimer;  // Таймер для периодического автосканирования

        // Конструктор класса Detector
        // Принимает зависимости через Dependency Injection
        // writer - компонент для записи найденных IP-адресов в файл
        // connectionManager - менеджер подключений для интеграции с системой соединений
        public Detector(WriterIP writer, ConnectionManager connectionManager)
        {
            _writer = writer;
            _connectionManager = connectionManager;
        }

        // ==================== ОСНОВНЫЕ МЕТОДЫ СКАНИРОВАНИЯ ====================

        // Сканировать сеть для обнаружения активных клиентов (с учетом настроек интерфейса)
        // quiet - режим тишины (без подробного вывода в консоль)
        // Возвращает объект ScanResult с результатами сканирования
        public async Task<ScanResult> ScanNetworkAsync(bool quiet = false)
        {
            var startTime = DateTime.Now;  // Запоминаем время начала сканирования

            try
            {
                Console.WriteLine($"[SCAN] Запуск сканирования...");
                // Console.WriteLine($"[SCAN] Режим интерфейса: {AppSettings.InterfaceMode}");
                // Console.WriteLine($"[SCAN] Только WiFi: {AppSettings.ForceWirelessOnly}");
                Console.WriteLine($"[SCAN] Предпочитать WiFi: {AppSettings.PreferWireless}");

                // Получаем активный интерфейс с учетом настроек
                var interfaceInfo = DetectActiveInterface();

                if (interfaceInfo.Address == null)
                {
                    // Возвращаем результат с ошибкой если не удалось определить интерфейс
                    return new ScanResult
                    {
                        Success = false,
                        Error = "Не удалось определить активный интерфейс",
                        StartTime = startTime,
                        EndTime = DateTime.Now
                    };
                }

                // Проверяем, что интерфейс соответствует настройкам
                // if (AppSettings.ForceWirelessOnly && interfaceInfo.InterfaceType != NetworkInterfaceType.Wireless80211)
                // {
                //     return new ScanResult
                //     {
                //         Success = false,
                //         Error = "Требуется WiFi интерфейс (принудительный режим), но активный интерфейс не является беспроводным",
                //         InterfaceName = interfaceInfo.InterfaceName,
                //         InterfaceType = interfaceInfo.InterfaceType,
                //         StartTime = startTime,
                //         EndTime = DateTime.Now
                //     };
                // }

                // if (AppSettings.InterfaceMode.ToLower() == "wifi" &&
                //     interfaceInfo.InterfaceType != NetworkInterfaceType.Wireless80211)
                // {
                //     return new ScanResult
                //     {
                //         Success = false,
                //         Error = "Требуется WiFi интерфейс, но активный интерфейс не является беспроводным",
                //         InterfaceName = interfaceInfo.InterfaceName,
                //         InterfaceType = interfaceInfo.InterfaceType,
                //         StartTime = startTime,
                //         EndTime = DateTime.Now
                //     };
                // }

                // Получаем базовый IP (первые три октета) для сканирования подсети
                string baseIp = interfaceInfo.BaseIp;
                if (string.IsNullOrEmpty(baseIp))
                {
                    return new ScanResult
                    {
                        Success = false,
                        Error = "Не удалось определить базовый IP",
                        InterfaceName = interfaceInfo.InterfaceName,
                        InterfaceType = interfaceInfo.InterfaceType,
                        StartTime = startTime,
                        EndTime = DateTime.Now
                    };
                }

                Console.WriteLine($"[SCAN] Интерфейс: {interfaceInfo.InterfaceName}");
                Console.WriteLine($"[SCAN] Тип: {interfaceInfo.InterfaceType}");
                Console.WriteLine($"[SCAN] IP: {interfaceInfo.Address}");
                Console.WriteLine($"[SCAN] Базовый IP: {baseIp}");
                Console.WriteLine($"[SCAN] Сканируем подсеть: {baseIp}0/24");

                var foundIps = new List<string>();  // Список найденных IP-адресов
                int foundCount = 0;  // Счетчик найденных адресов

                // Для WiFi сканирования ограничиваем диапазон
                int maxIps = interfaceInfo.InterfaceType == NetworkInterfaceType.Wireless80211
                    ? AppSettings.MaxWirelessClients  // Максимальное количество клиентов для WiFi из настроек
                    : 254;  // Для Ethernet полный диапазон

                // Если в настройках указано 0 или отрицательное значение, используем полный диапазон
                if (maxIps <= 0 || maxIps > 254) maxIps = 254;

                Console.WriteLine($"[SCAN] Максимальное количество IP для сканирования: {maxIps}");

                // Используем задачи для параллельного сканирования
                var tasks = new List<Task>();
                var ipList = new List<string>();  // Список IP-адресов для сканирования

                // Генерируем список IP для сканирования
                for (int i = 1; i <= maxIps; i++)
                {
                    string ip = $"{baseIp}{i}";

                    // Пропускаем свой IP (не сканируем себя)
                    if (ip == interfaceInfo.Address.ToString())
                        continue;

                    ipList.Add(ip);
                }

                Console.WriteLine($"[SCAN] Всего IP для сканирования: {ipList.Count}");

                // Параллельное сканирование с ограничением одновременных задач
                int maxConcurrentTasks = interfaceInfo.InterfaceType == NetworkInterfaceType.Wireless80211 ? 10 : 20;
                var taskQueue = new Queue<string>(ipList);

                while (taskQueue.Count > 0)
                {
                    var batchTasks = new List<Task>();  // Задачи текущей партии

                    // Создаем задачи для текущей партии IP-адресов
                    for (int i = 0; i < maxConcurrentTasks && taskQueue.Count > 0; i++)
                    {
                        string ip = taskQueue.Dequeue();
                        batchTasks.Add(ScanSingleIpAsync(ip, foundIps, quiet, interfaceInfo.InterfaceType));
                    }

                    // Ожидаем завершения всех задач текущей партии
                    await Task.WhenAll(batchTasks);

                    // Выводим прогресс сканирования (если не в тихом режиме)
                    if (!quiet && taskQueue.Count > 0)
                    {
                        int scanned = ipList.Count - taskQueue.Count;
                        int total = ipList.Count;
                        Console.WriteLine($"[SCAN] Прогресс: {scanned}/{total} ({scanned * 100 / total}%)");
                    }
                }

                foundCount = foundIps.Count;  // Получаем общее количество найденных адресов

                // Обновляем статистику сканирований
                lock (_scanStatsLock)
                {
                    _totalScans++;
                    _successfulScans++;
                    _totalFoundIps += foundCount;
                    _lastScanTime = DateTime.Now;
                }

                // Возвращаем успешный результат сканирования
                return new ScanResult
                {
                    Success = true,
                    FoundIps = foundIps,
                    InterfaceName = interfaceInfo.InterfaceName,
                    InterfaceType = interfaceInfo.InterfaceType,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                // Обновляем статистику при ошибке
                lock (_scanStatsLock)
                {
                    _totalScans++;
                }

                // Возвращаем результат с ошибкой
                return new ScanResult
                {
                    Success = false,
                    Error = ex.Message,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
            }
        }

        // Сканировать один IP адрес с учетом типа интерфейса
        // ipAddress - IP-адрес для сканирования
        // foundIps - список для добавления найденных адресов (потокобезопасный доступ)
        // quiet - режим тишины (без вывода в консоль)
        // interfaceType - тип интерфейса (влияет на таймауты)
        private async Task ScanSingleIpAsync(string ipAddress, List<string> foundIps, bool quiet, NetworkInterfaceType? interfaceType)
        {
            try
            {
                // Используем разные таймауты для разных типов интерфейсов
                int timeout = interfaceType == NetworkInterfaceType.Wireless80211
                    ? AppSettings.WirelessScanTimeout  // Таймаут для WiFi из настроек
                    : 500;  // Стандартный таймаут для Ethernet

                // Проверяем доступность IP через ping
                if (await PingIpAsync(ipAddress, timeout))
                {
                    // Добавляем найденный адрес в список (с синхронизацией)
                    lock (foundIps)
                    {
                        foundIps.Add(ipAddress);
                    }

                    // Выводим информацию о найденном адресе (если не в тихом режиме)
                    if (!quiet)
                    {
                        Console.WriteLine($"  • {ipAddress} - доступен");
                    }

                    // Сохраняем найденный IP в файл
                    await _writer.WriteIpInFile(ipAddress);

                    // Проверяем наличие сервиса на стандартном порту
                    if (await CheckPortAsync(ipAddress, AppSettings.DefaultPort))
                    {
                        if (!quiet)
                        {
                            Console.WriteLine($"  • {ipAddress} - сервер активен на порту {AppSettings.DefaultPort}");
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки при сканировании отдельных IP
            }
        }

        // Проверить доступность IP с помощью ping
        // ipAddress - IP-адрес для проверки
        // timeout - время ожидания ответа в миллисекундах
        // Возвращает true если IP отвечает на ping
        private async Task<bool> PingIpAsync(string ipAddress, int timeout)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ipAddress, timeout);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        // Проверить доступность порта на указанном IP
        // ipAddress - IP-адрес для проверки
        // port - порт для проверки
        // Возвращает true если порт открыт и доступен
        private async Task<bool> CheckPortAsync(string ipAddress, int port)
        {
            try
            {
                using var tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = 1000;  // Таймаут приема данных
                tcpClient.SendTimeout = 1000;     // Таймаут отправки данных

                // Попытка подключения с таймаутом
                var connectTask = tcpClient.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(1000);  // Общий таймаут 1 секунда

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                // Возвращаем true если подключение успешно и клиент подключен
                return completedTask == connectTask && tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        // ==================== МЕТОДЫ РАБОТЫ С ИНТЕРФЕЙСАМИ ====================

        // Определить активный сетевой интерфейс с учетом настроек
        // Возвращает объект InterfaceInfo с информацией о выбранном интерфейсе
        // Использует метод из класса Interface_list для получения основного интерфейса
        public InterfaceInfo DetectActiveInterface()
        {
            try
            {
                // Используем обновленный метод из Interface_list
                var primaryInterface = Interface_list.GetPrimaryInterface();

                if (!primaryInterface.HasValue)
                {
                    Console.WriteLine("[ERROR] Не удалось определить активный интерфейс");
                    return new InterfaceInfo();  // Возвращаем пустой объект
                }

                var interfaceInfo = primaryInterface.Value;

                // Получаем базовый IP (первые три октета) для сканирования подсети
                string baseIp = GetBaseIp(interfaceInfo.Address);

                Console.WriteLine($"[SCAN] Выбран интерфейс: {interfaceInfo.InterfaceName}");
                Console.WriteLine($"[SCAN] Тип: {interfaceInfo.Type}");
                Console.WriteLine($"[SCAN] IP адрес: {interfaceInfo.Address}");
                Console.WriteLine($"[SCAN] Базовый IP: {baseIp}");

                // Возвращаем информацию об интерфейсе
                return new InterfaceInfo
                {
                    Address = interfaceInfo.Address,
                    InterfaceName = interfaceInfo.InterfaceName,
                    InterfaceType = interfaceInfo.Type,
                    BaseIp = baseIp
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка определения интерфейса: {ex.Message}");
                return new InterfaceInfo();  // Возвращаем пустой объект при ошибке
            }
        }

        // Получить базовый IP (первые три октета) из полного IP-адреса
        // ipAddress - полный IP-адрес (например, 192.168.1.10)
        // Возвращает базовый IP (например, "192.168.1.")
        private string GetBaseIp(IPAddress ipAddress)
        {
            try
            {
                byte[] bytes = ipAddress.GetAddressBytes();
                if (bytes.Length == 4)  // Проверяем что адрес IPv4
                {
                    return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";  // Возвращаем первые три октета
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения базового IP: {ex.Message}");
            }
            return null;  // Возвращаем null при ошибке
        }

        // ==================== СТАТИСТИКА И МОНИТОРИНГ ====================

        // Получить статистику сканирований
        // Возвращает объект ScanStats с агрегированной статистикой всех сканирований
        public ScanStats GetScanStats()
        {
            lock (_scanStatsLock)  // Синхронизация доступа к статистике
            {
                return new ScanStats
                {
                    TotalScans = _totalScans,
                    SuccessfulScans = _successfulScans,
                    AverageFoundIps = _totalScans > 0 ? (int)(_totalFoundIps / _totalScans) : 0,  // Среднее количество найденных IP
                    LastScanTime = _lastScanTime
                };
            }
        }

        // ==================== АВТОМАТИЧЕСКОЕ СКАНИРОВАНИЕ ====================

        // Запустить автосканирование с указанным интервалом
        // intervalSeconds - интервал между сканированиями в секундах
        // Создает и запускает таймер для периодического сканирования сети
        public void StartAutoScan(int intervalSeconds)
        {
            _autoScanStatus = (true, intervalSeconds);  // Обновляем статус автосканирования

            // Останавливаем и освобождаем существующий таймер
            if (_autoScanTimer != null)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Dispose();
            }

            // Создаем новый таймер с указанным интервалом
            _autoScanTimer = new System.Timers.Timer(intervalSeconds * 1000);
            _autoScanTimer.Elapsed += async (s, e) =>
            {
                if (_isScanning)  // Проверяем не выполняется ли уже сканирование
                {
                    Console.WriteLine("[AUTO SCAN] Пропускаем, сканирование уже выполняется");
                    return;
                }

                _isScanning = true;  // Устанавливаем флаг выполнения сканирования
                try
                {
                    Console.WriteLine($"[AUTO SCAN] Автоматическое сканирование...");
                    // Выполняем сканирование в тихом режиме
                    var result = await ScanNetworkAsync(true);

                    // Выводим информацию о результатах сканирования
                    if (result.Success && result.FoundIps?.Count > 0)
                    {
                        Console.WriteLine($"[AUTO SCAN] Найдено {result.FoundIps.Count} клиентов");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AUTO SCAN] Ошибка: {ex.Message}");
                }
                finally
                {
                    _isScanning = false;  // Сбрасываем флаг выполнения сканирования
                }
            };
            _autoScanTimer.AutoReset = true;  // Таймер перезапускается автоматически
            _autoScanTimer.Start();  // Запускаем таймер

            Console.WriteLine($"[AUTO SCAN] Автосканирование запущено (интервал: {intervalSeconds} сек)");
        }

        // Остановить автосканирование
        // Останавливает таймер и сбрасывает статус автосканирования
        public void StopAutoScan()
        {
            _autoScanStatus = (false, 0);  // Сбрасываем статус автосканирования

            // Останавливаем и освобождаем таймер
            if (_autoScanTimer != null)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Dispose();
                _autoScanTimer = null;
            }

            Console.WriteLine("[AUTO SCAN] Автосканирование остановлено");
        }

        // Получить статус автосканирования
        // Возвращает кортеж (Enabled, Interval) с информацией о состоянии автосканирования
        public (bool Enabled, int Interval) GetAutoScanStatus()
        {
            return _autoScanStatus;
        }

        // ==================== МЕТОДЫ РАБОТЫ С WIFI ИНТЕРФЕЙСАМИ ====================

        // Проверить доступность беспроводного интерфейса
        // Возвращает true если в системе есть хотя бы один активный WiFi адаптер
        public bool HasWirelessInterface()
        {
            return Interface_list.HasWirelessInterface();  // Используем метод из Interface_list
        }

        // Получить список беспроводных интерфейсов
        // Возвращает список кортежей (InterfaceName, Address) для всех активных WiFi адаптеров
        public List<(string InterfaceName, IPAddress Address)> GetWirelessInterfaces()
        {
            return Interface_list.GetWirelessInterfaces()
                .Select(i => (i.InterfaceName, i.Address))
                .ToList();
        }

        // Получить информацию о WiFi адаптерах в форматированном виде
        // Возвращает строку с детальной информацией о всех WiFi интерфейсах
        public string GetWirelessInfo()
        {
            var wifiInterfaces = GetWirelessInterfaces();

            if (wifiInterfaces.Count == 0)
                return "[WIFI] Беспроводные интерфейсы не найдены";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[WIFI] Беспроводные интерфейсы:");

            var activeInterface = Interface_list.GetPrimaryInterface();  // Получаем активный интерфейс

            // Формируем информацию о каждом WiFi адаптере
            foreach (var iface in wifiInterfaces)
            {
                // Помечаем активный интерфейс
                string isActive = activeInterface.HasValue && iface.InterfaceName == activeInterface.Value.InterfaceName ? "[АКТИВНЫЙ]" : "";
                sb.AppendLine($"  • {iface.InterfaceName} - {iface.Address} {isActive}");
            }

            return sb.ToString();
        }
    }

    // ==================== ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ ====================

    // Результат сканирования сети
    // Содержит информацию об успешности сканирования, найденных IP-адресах и метаданных
    public class ScanResult
    {
        public bool Success { get; set; }  // Успешность сканирования
        public List<string> FoundIps { get; set; }  // Список найденных IP-адресов
        public string InterfaceName { get; set; }  // Имя интерфейса, использованного для сканирования
        public NetworkInterfaceType? InterfaceType { get; set; }  // Тип интерфейса (WiFi, Ethernet и т.д.)
        public string Error { get; set; }  // Сообщение об ошибке (если сканирование не удалось)
        public DateTime StartTime { get; set; }  // Время начала сканирования
        public DateTime EndTime { get; set; }  // Время окончания сканирования
    }

    // Информация о сетевом интерфейсе
    // Содержит детальную информацию об одном сетевом интерфейсе для использования в сканировании
    public class InterfaceInfo
    {
        public IPAddress Address { get; set; }  // IP-адрес интерфейса
        public string InterfaceName { get; set; }  // Имя интерфейса в системе
        public NetworkInterfaceType? InterfaceType { get; set; }  // Тип интерфейса
        public string BaseIp { get; set; }  // Базовый IP (первые три октета) для сканирования подсети
    }

    // Статистика сканирований
    // Содержит агрегированные данные о всех выполненных сканированиях
    public class ScanStats
    {
        public int TotalScans { get; set; }  // Общее количество выполненных сканирований
        public int SuccessfulScans { get; set; }  // Количество успешных сканирований
        public int AverageFoundIps { get; set; }  // Среднее количество найденных IP за сканирование
        public DateTime LastScanTime { get; set; }  // Время последнего сканирования
    }
}