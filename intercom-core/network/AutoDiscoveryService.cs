using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wr;  // Пространство имен для WriterIP

namespace network
{
    // Сервис автоматического обнаружения клиентов в сети
    // Обеспечивает непрерывное фоновое сканирование сети для поиска активных клиентов,
    // автоматическое подключение к ним и отслеживание их состояния в реальном времени
    // Использует комбинированный подход: ping для проверки доступности и TCP-подключение для аутентификации
    // Интегрирован с системой управления подключениями, никами и чат-сессиями
    public class AutoDiscoveryService
    {
        // Зависимости (Dependency Injection):
        private readonly ConnectionManager _connectionManager;  // Менеджер для установки и управления сетевыми подключениями
        private readonly NicknameManager _nicknameManager;      // Менеджер для работы с отображением IP-адресов в никнеймы
        private readonly ChatSessionManager _chatSessionManager; // Менеджер для создания и управления чат-сессиями
        private readonly WriterIP _writer;                      // Компонент для записи найденных IP-адресов в файл
        private readonly RouteManager _routeManager;

        // Коллекции для хранения состояния обнаружения:
        private readonly ConcurrentDictionary<string, DiscoveredClient> _discoveredClients;  // Потокобезопасный словарь обнаруженных клиентов (ключ - IP)
        private readonly ConcurrentBag<string> _scanningNetworks;  // Коллекция уже просканированных подсетей для избежания дублирования

        // Управление задачами сканирования:
        private CancellationTokenSource _scanCancellationToken;  // Токен отмены для корректной остановки непрерывного сканирования
        private Task _continuousScanTask;                        // Фоновая задача непрерывного сканирования
        private bool _isRunning;                                 // Флаг активности сервиса (включен/выключен)

        // События для оповещения о изменениях в обнаруженных клиентах:
        public event Action<string, string> OnClientDiscovered;  // Срабатывает при обнаружении нового клиента (IP, никнейм)
        public event Action<string> OnClientLost;                // Срабатывает при потере клиента (IP)


        // Конструктор сервиса автоматического обнаружения
        // Принимает зависимости через Dependency Injection для интеграции с другими компонентами системы
        // connectionManager - Менеджер подключений для автоподключения к найденным клиентам
        // nicknameManager - Менеджер ников для связывания IP-адресов с именами клиентов
        // chatSessionManager - Менеджер чат-сессий для автоматического создания чатов
        // writer - Компонент для записи найденных IP-адресов в файл
        public AutoDiscoveryService(
            ConnectionManager connectionManager,
            NicknameManager nicknameManager,
            ChatSessionManager chatSessionManager,
            WriterIP writer,
            RouteManager routeManager)
        {
            _connectionManager = connectionManager;
            _nicknameManager = nicknameManager;
            _chatSessionManager = chatSessionManager;
            _writer = writer;
            _routeManager = routeManager;

            // Инициализация потокобезопасных коллекций:
            _discoveredClients = new ConcurrentDictionary<string, DiscoveredClient>();
            _scanningNetworks = new ConcurrentBag<string>();
        }

        // Запустить непрерывное сканирование сети с указанным интервалом
        // Создает фоновую задачу, которая периодически сканирует сеть и обновляет список клиентов
        // intervalSeconds - Интервал между циклами сканирования в секундах (по умолчанию 60)
        public void StartContinuousScan(int intervalSeconds = 60)
        {
            if (_isRunning) return;  // Если сервис уже работает, выходим

            _isRunning = true;  // Устанавливаем флаг активности
            _scanCancellationToken = new CancellationTokenSource();  // Создаем токен отмены

            // Запускаем фоновую задачу непрерывного сканирования
            _continuousScanTask = Task.Run(async () =>
            {
                // Основной цикл выполняется пока сервис активен и не запрошена отмена
                while (_isRunning && !_scanCancellationToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        Console.WriteLine($"[AUTO DISCOVERY] Запуск сканирования...");

                        // Выполняем полное сканирование всех сетевых интерфейсов
                        await PerformFullNetworkScanAsync();

                        // Если включено автоподключение, подключаемся к новым клиентам
                        if (AppSettings.AutoConnectDiscovered)
                        {
                            await AutoConnectToNewClientsAsync();
                        }

                        // Проверяем состояние активных подключений и отмечаем "потерянные" устройства
                        await CheckActiveConnectionsAsync();

                        // Ждем указанный интервал перед следующим сканированием
                        await Task.Delay(intervalSeconds * 1000, _scanCancellationToken.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Запрос на отмену задачи - корректно завершаем цикл
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Обработка ошибок в цикле сканирования
                        Console.WriteLine($"[AUTO DISCOVERY] Ошибка: {ex.Message}");
                        await Task.Delay(5000);  // Ждем 5 секунд при ошибке перед продолжением
                    }
                }
            });

            Console.WriteLine($"[AUTO DISCOVERY] Запущено непрерывное сканирование каждые {intervalSeconds} сек");
        }


        // Остановить непрерывное сканирование
        // Корректно завершает фоновую задачу и освобождает ресурсы
        public void StopContinuousScan()
        {
            _isRunning = false;  // Сбрасываем флаг активности
            _scanCancellationToken?.Cancel();  // Запрашиваем отмену через токен

            // Ожидаем завершения задачи (максимум 2 секунды)
            try
            {
                if (_continuousScanTask != null && !_continuousScanTask.IsCompleted)
                {
                    Task.WaitAny(_continuousScanTask, Task.Delay(2000));
                }
            }
            catch 
            {
                Console.WriteLine("[ERROR] возникла ошибка в завершении непрерывного сканирования");            
            }  // Игнорируем ошибки при ожидании завершения

            Console.WriteLine("[AUTO DISCOVERY] Сканирование остановлено");
        }

        // Выполнить полное сканирование всех сетевых интерфейсов
        // Собирает информацию о всех активных интерфейсах, фильтрует их по настройкам,
        // определяет подсети и запускает сканирование каждой подсети
        private async Task PerformFullNetworkScanAsync()
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                // Применяйте фильтры по типу интерфейса (как у вас было)

                foreach (var ni in networkInterfaces)
                {
                    var ipProperties = ni.GetIPProperties();
                    var unicastAddresses = ipProperties.UnicastAddresses
                        .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        .ToList();

                    foreach (var ua in unicastAddresses)
                    {
                        // Получаем маску подсети
                        IPAddress subnetMask = ua.IPv4Mask;
                        if (subnetMask == null) continue;

                        string networkKey = $"{ua.Address}/{subnetMask}";
                        if (_scanningNetworks.Contains(networkKey)) continue;

                        _scanningNetworks.Add(networkKey);
                        var ipsToScan = GetAllIpsInSubnet(ua.Address, subnetMask);
                        if (ipsToScan.Count == 0) continue;

                        Console.WriteLine($"[AUTO DISCOVERY] Сканирую подсеть {ua.Address}/{GetCidrFromMask(subnetMask)} (всего {ipsToScan.Count} IP)");
                        await ScanIpListAsync(ipsToScan);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AUTO DISCOVERY] Ошибка сканирования: {ex.Message}");
            }
        }
        private int GetCidrFromMask(IPAddress mask)
        {
            byte[] bytes = mask.GetAddressBytes();
            int cidr = 0;
            foreach (byte b in bytes)
            {
                for (int i = 7; i >= 0; i--)
                {
                    if ((b & (1 << i)) != 0)
                        cidr++;
                    else
                        return cidr;
                }
            }
            return cidr;
        }

        // Вычислить префикс сети (первые три октета) на основе IP-адреса и маски подсети
        // <param name="ipAddress">IPv4 адрес интерфейса</param>
        // <param name="subnetMask">Маска подсети интерфейса</param>
        // <returns>Строка вида "192.168.1." для подсети /24 или null при ошибке</returns>
        private List<string> GetAllIpsInSubnet(IPAddress ip, IPAddress subnetMask)
        {
            var result = new List<string>();
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();
            if (ipBytes.Length != 4 || maskBytes.Length != 4)
                return result; // только IPv4

            // Вычисляем сетевой адрес
            byte[] networkBytes = new byte[4];
            for (int i = 0; i < 4; i++)
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);

            // Вычисляем количество хостов в подсети
            int hostBits = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((maskBytes[i / 8] & (1 << (7 - (i % 8)))) == 0)
                    hostBits++;
            }
            long hosts = (1L << hostBits) - 2; // минус сетевой и широковещательный
            if (hosts <= 0 || hosts > 65536) // ограничим разумным числом
                return result;

            // Генерируем все IP
            for (long i = 1; i <= hosts; i++)
            {
                byte[] ipBytesCopy = (byte[])networkBytes.Clone();
                long remaining = i;
                for (int pos = 3; pos >= 0 && remaining > 0; pos--)
                {
                    int bits = 8;
                    if (remaining < 256)
                        bits = (int)remaining;
                    ipBytesCopy[pos] += (byte)remaining;
                    remaining >>= 8;
                }
                // Пропускаем собственный IP
                if (ipBytesCopy.SequenceEqual(ipBytes))
                    continue;
                result.Add(new IPAddress(ipBytesCopy).ToString());
            }
            return result;
        }

        // Сканировать диапазон адресов в подсети
        // networkPrefix - Префикс подсети (например "192.168.1.")
        // Сканирует все 254 адреса в подсети с ограничением одновременных проверок (maxConcurrent = 20)
        // Использует механизм очереди задач для эффективного параллельного сканирования
        private async Task ScanIpListAsync(List<string> ips)
        {
            const int maxConcurrent = 20;
            var scanTasks = new List<Task<DiscoveredClient>>();

            foreach (var ip in ips)
            {
                if (scanTasks.Count >= maxConcurrent)
                {
                    var completedTask = await Task.WhenAny(scanTasks);
                    scanTasks.Remove(completedTask);
                    var discoveredClient = await completedTask;
                    if (discoveredClient != null)
                        _ = RegisterDiscoveredClient(discoveredClient);
                }
                scanTasks.Add(ScanSingleIpAsync(ip));
                await Task.Delay(10);
            }

            var remainingResults = await Task.WhenAll(scanTasks);
            foreach (var discoveredClient in remainingResults.Where(dc => dc != null))
                _ = RegisterDiscoveredClient(discoveredClient);
        }

        // Сканировать один IP-адрес для обнаружения клиента
        // ipAddress - IP-адрес для проверки
        // Объект DiscoveredClient если клиент обнаружен, иначе null
        // Выполняет двухэтапную проверку:
        // 1. Ping для проверки доступности узла
        // 2. TCP-подключение к порту сервиса для аутентификации и получения никнейма
        private async Task<DiscoveredClient> ScanSingleIpAsync(string ipAddress)
        {
            try
            {
                // 1. Проверяем доступность через ping
                using var ping = new Ping();
                var pingReply = await ping.SendPingAsync(ipAddress, 500);  // Таймаут 500 мс
                if (pingReply.Status != IPStatus.Success)
                    return null;  // IP не отвечает на ping

                // 2. Пытаемся подключиться по TCP к порту сервиса
                using var tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = 2000;  // Таймаут приема данных
                tcpClient.SendTimeout = 2000;     // Таймаут отправки данных

                var connectTask = tcpClient.ConnectAsync(ipAddress, AppSettings.DefaultPort);
                var timeoutTask = Task.Delay(2000);  // Общий таймаут 2 секунды

                // Ожидаем либо подключения, либо таймаута
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                    return null;  // Таймаут подключения

                await connectTask;  // Убеждаемся что подключение завершено

                // Получаем сетевой поток для обмена данными
                var stream = tcpClient.GetStream();

                // Отправляем сообщение аутентификации с нашим никнеймом
                string authMessage = $"AUTH:{AppSettings.Nickname}<END>";
                byte[] authData = Encoding.UTF8.GetBytes(authMessage);
                await stream.WriteAsync(authData, 0, authData.Length);

                // Читаем ответ от сервиса
                byte[] buffer = new byte[1024];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Проверяем успешность аутентификации
                if (response.Contains("AUTH_OK"))
                {
                    string nickname = "Unknown";
                    // Извлекаем никнейм из ответа (формат: AUTH_OK:никнейм<END>)
                    if (response.Contains(":"))
                    {
                        nickname = response.Split(':')[1].Replace("<END>", "").Trim();
                    }

                    // Возвращаем информацию о найденном клиенте
                    return new DiscoveredClient
                    {
                        IpAddress = ipAddress,
                        Nickname = nickname,
                        LastSeen = DateTime.Now,           // Время последнего обнаружения
                        ResponseTime = pingReply.RoundtripTime  // Время отклика ping в миллисекундах
                    };
                }
            }
            catch
            {
                // Игнорируем все ошибки при сканировании отдельного IP
                // (недоступность, таймауты, сетевые проблемы)
            }

            return null;  // Клиент не найден или произошла ошибка
        }

        // Зарегистрировать обнаруженного клиента в системе
        // client - Информация о найденном клиенте
        // Обновляет внутренние коллекции, вызывает события, сохраняет IP в файл
        // и обновляет отображение IP->никнейм в менеджере ников
        private async Task RegisterDiscoveredClient(DiscoveredClient client)
        {
            bool isNew = !_discoveredClients.ContainsKey(client.IpAddress);

            _discoveredClients.AddOrUpdate(client.IpAddress,
                key => client,
                (key, existing) =>
                {
                    existing.LastSeen = DateTime.Now;
                    existing.ResponseTime = client.ResponseTime;
                    return existing;
                });

            _nicknameManager.UpdateMapping(client.IpAddress, client.Nickname);

            // ✅ ДОБАВИТЬ: Добавляем динамического клиента и создаем связь
            _routeManager.AddDynamicClient(client.Nickname, client.IpAddress, AppSettings.Nickname);
            _routeManager.AddLink(AppSettings.Nickname, client.Nickname); // Создаем связь при обнаружении

            await _writer.WriteIpInFile(client.IpAddress);

            if (isNew)
            {
                OnClientDiscovered?.Invoke(client.IpAddress, client.Nickname);
                Console.WriteLine($"[AUTO DISCOVERY] Найден клиент: {client.Nickname} ({client.IpAddress})");

                // ✅ Перестраиваем цепочки при обнаружении нового клиента
                _routeManager.RebuildAllChains();
            }
        }


        // Автоматически подключиться к новым клиентам
        // Проверяет настройку AutoConnectDiscovered и подключается к клиентам,
        // с которыми еще нет активного подключения. Подключается только к клиентам,
        // обнаруженным в последние 5 минут.
        private async Task AutoConnectToNewClientsAsync()
        {
            if (!AppSettings.AutoConnectDiscovered)
                return;

            // Проходим по всем обнаруженным клиентам
            foreach (var client in _discoveredClients.Values)
            {
                var activeConnections = _connectionManager.GetActiveConnections();
                // Пропускаем клиентов, к которым уже есть подключение
                if (activeConnections.Contains(client.IpAddress))
                    continue;

                // Подключаемся только к клиентам, обнаруженным в последние 5 минут
                if ((DateTime.Now - client.LastSeen).TotalMinutes < 5)
                {
                    try
                    {
                        // Пытаемся установить подключение (автоматический режим)
                        bool connected = await _connectionManager.ConnectAsync(
                            client.IpAddress,
                            AppSettings.DefaultPort,
                            true);

                        if (connected)
                        {
                            // Создаем чат-сессию с новым клиентом
                            _chatSessionManager.CreateOrOpenChat(client.Nickname);
                            Console.WriteLine($"[AUTO DISCOVERY] Подключено к {client.Nickname} ({client.IpAddress})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AUTO DISCOVERY] Ошибка подключения к {client.IpAddress}: {ex.Message}");
                    }
                }
            }
        }

        // Проверить активные подключения и пометить "потерянных" клиентов
        // Сравнивает список обнаруженных клиентов с активными подключениями,
        // отмечает клиентов, которых не видели более 10 минут как "потерянные"
        // и удаляет их из коллекции обнаруженных клиентов
        private async Task CheckActiveConnectionsAsync()
        {
            var activeConnections = _connectionManager.GetActiveConnections();
            var lostClients = new List<string>();  // Список клиентов для удаления

            // Ищем клиентов, которых нет в активных подключениях
            foreach (var ip in _discoveredClients.Keys)
            {
                if (!activeConnections.Contains(ip))
                {
                    // Если клиента не видели более 10 минут, добавляем в список на удаление
                    if (_discoveredClients.TryGetValue(ip, out var client) &&
                        (DateTime.Now - client.LastSeen).TotalMinutes > 10)
                    {
                        lostClients.Add(ip);
                    }
                }
            }

            // Удаляем "потерянных" клиентов и вызываем события
            foreach (var ip in lostClients)
            {
                if (_discoveredClients.TryRemove(ip, out var client))
                {
                    OnClientLost?.Invoke(ip);
                    Console.WriteLine($"[AUTO DISCOVERY] Клиент потерян: {client.Nickname} ({ip})");
                }
            }
        }

        // Получить список всех обнаруженных клиентов
        // возвращает копию списка клиентов для безопасного чтения
        public List<DiscoveredClient> GetAllDiscoveredClients()
        {
            return _discoveredClients.Values.ToList();
        }

        // Получить статистику обнаружения
        // возвращает объект DiscoveryStats с агрегированными данными
        public DiscoveryStats GetDiscoveryStats()
        {
            return new DiscoveryStats
            {
                TotalDiscovered = _discoveredClients.Count,  // Всего обнаружено клиентов
                ActiveInLastHour = _discoveredClients.Values
                    .Count(c => (DateTime.Now - c.LastSeen).TotalHours < 1),  // Активны в последний час
                LastScanTime = DateTime.Now  // Время последнего сканирования
            };
        }
        // Проверка, запущен ли сервис
        // возвращает True если непрерывное сканирование активно
        public bool IsRunning()
        {
            return _isRunning;
        }
    }

    // ==================== ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ ====================

    // Обнаруженный клиент
    // Содержит информацию о клиенте, найденном в процессе сканирования сети
    public class DiscoveredClient
    {
        public string IpAddress { get; set; }      // IP-адрес клиента
        public string Nickname { get; set; }       // Имя/никнейм клиента (полученный при аутентификации)
        public DateTime LastSeen { get; set; }     // Время последнего обнаружения клиента
        public long ResponseTime { get; set; }     // Время отклика ping (в миллисекундах)
    }

    // Статистика обнаружения
    // Содержит агрегированные данные о процессе обнаружения клиентов
    public class DiscoveryStats
    {
        public int TotalDiscovered { get; set; }     // Общее количество обнаруженных клиентов за все время
        public int ActiveInLastHour { get; set; }    // Количество клиентов, активных в последний час
        public DateTime LastScanTime { get; set; }   // Время последнего сканирования
    }
}