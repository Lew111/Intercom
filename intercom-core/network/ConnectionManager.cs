using System;  // Основные системные классы
using System.Collections.Concurrent;  // Потокобезопасные коллекции
using System.Collections.Generic;  // Общие коллекции (List, Dictionary)
using System.IO;  // Работа с файловой системой
using System.Linq;  // LINQ для работы с коллекциями
using System.Net;  // Работа с сетевыми протоколами
using System.Net.Sockets;  // Работа с TCP/UDP сокетами
using System.Text;  // Кодировки строк
using System.Text.Json;
using System.Threading.Tasks;  // Асинхронное программирование
using wr;  // Пространство имен для WriterIP 

namespace network
{
    // Менеджер соединений с поддержкой множественных подключений и отслеживанием доставки сообщений и файлов
    // Явдяется ядром сетевого взаимодействия приложения - управляет всеми сетевыми подключениями
    public class ConnectionManager
    {
        // Зависимости (Dependency Injection):
        private readonly WriterIP _writer;  // Компонент для работы с файлами (запись/чтение IP-адресов)
        private readonly ChatManager _chatManager;  // Менеджер чатов (сохранение сообщений)
        private readonly NicknameManager _nicknameManager;  // Менеджер никнеймов (сопоставление IP-никнейм)
        private readonly ChatSessionManager _chatSessionManager;  // Менеджер сессий чата (управление активными чатами)
        public RelayService RelayService { private get; set; }
        private readonly RouteManager _routeManager;

        // Основные коллекции для управления соединениями и данными:
        private readonly ConcurrentDictionary<string, ConnectionInfo> _connections;  // Словарь активных подключений (потокобезопасный)
        private readonly ConcurrentQueue<PendingMessage> _pendingMessages;  // Очередь сообщений, ожидающих доставки
        private readonly ConcurrentDictionary<string, IncomingFileTransfer> _incomingFileTransfers;  // Словарь входящих передач файлов
        private System.Timers.Timer _deliveryCheckTimer;  // Таймер для периодической проверки доставки сообщений

        // События для уведомления о изменениях состояния соединений:
        public event Action<string> OnConnected;  // Событие при успешном подключении
        public event Action<string> OnDisconnected;  // Событие при отключении
        public event Action<string, string> OnMessageDelivered;  // Событие при успешной доставке сообщения
        public event Action<string, string, string> OnMessageFailed;  // Событие при неудачной доставке
        public event Action<string> OnAutoConnected;  // Событие при автоматическом подключении
        private DateTime _lastRouteUpdateSent = DateTime.MinValue;
        private readonly object _routeUpdateLock = new object();


        // В
        public void RegisterIncomingConnection(TcpClient client)
        {
            try
            {
                var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint == null)
                {
                    client.Close();
                    return;
                }

                string ip = remoteEndPoint.Address.ToString();
                int port = remoteEndPoint.Port;

                // Игнорируем подключения от localhost (кроме случаев тестирования)
                if (ip == "127.0.0.1" || ip == "::1")
                {
                    Console.WriteLine($"[CONNECT] Отклонено подключение от localhost");
                    client.Close();
                    return;
                }

                if (remoteEndPoint == null)
                {
                    client.Close();
                    return;
                }


                // Если уже есть активное соединение с этим IP, проверяем его живость
                if (_connections.TryGetValue(ip, out var existing))
                {
                    bool isAlive = existing.IsConnected && existing.TcpClient?.Client?.Poll(0, SelectMode.SelectRead) == false;
                    if (isAlive)
                    {
                        Console.WriteLine($"[CONNECT] Соединение с {ip} уже активно, входящее отклонено");
                        client.Close();
                        return;
                    }
                    else
                    {
                        // Старое соединение мертво, удаляем его
                        Console.WriteLine($"[CONNECT] Замена неактивного соединения с {ip}");
                        _connections.TryRemove(ip, out _);
                        existing.KeepAliveCancellation?.Cancel();
                        existing.NetworkStream?.Close();
                        existing.TcpClient?.Close();
                    }
                }

                var stream = client.GetStream();
                var connectionInfo = new ConnectionInfo
                {
                    TcpClient = client,
                    NetworkStream = stream,
                    IpAddress = ip,
                    Port = port,
                    IsConnected = true,
                    LastActivity = DateTime.Now
                };

                if (_connections.TryAdd(ip, connectionInfo))
                {
                    Console.WriteLine($"[CONNECT] Зарегистрировано входящее соединение от {ip}:{port}");

                    _ = Task.Run(async () => await ReceiveMessagesAsync(connectionInfo));

                    // ✅ ВЕРНУТЬ: Запускаем KeepAlive для входящих
                    var keepAliveCts = new CancellationTokenSource();
                    connectionInfo.KeepAliveCancellation = keepAliveCts;
                    _ = Task.Run(async () => await KeepAliveAsync(connectionInfo, keepAliveCts.Token));

                    OnConnected?.Invoke(ip);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка регистрации входящего соединения: {ex.Message}");
                client.Close();
            }
        }


        // Конструктор класса ConnectionManager
        // Принимает все зависимости через Dependency Injection
        public ConnectionManager(WriterIP writer, ChatManager chatManager, NicknameManager nicknameManager, ChatSessionManager chatSessionManager, RouteManager routeManager)
        {
            // Инициализация зависимостей
            _writer = writer;
            _chatManager = chatManager;
            _nicknameManager = nicknameManager;
            _chatSessionManager = chatSessionManager;
            _routeManager = routeManager;

            // Инициализация потокобезопасных коллекций
            _connections = new ConcurrentDictionary<string, ConnectionInfo>();
            _pendingMessages = new ConcurrentQueue<PendingMessage>();
            _incomingFileTransfers = new ConcurrentDictionary<string, IncomingFileTransfer>();

            // Запуск таймера для проверки доставки сообщений
            InitializeDeliveryCheckTimer();
        }

        // Метод инициализации таймера проверки доставки
        // Таймер срабатывает каждые 10 секунд для повторной отправки не доставленных сообщений
        private void InitializeDeliveryCheckTimer()
        {
            _deliveryCheckTimer = new System.Timers.Timer(10000);  // Интервал 10 секунд
            _deliveryCheckTimer.Elapsed += async (s, e) => await CheckMessageDeliveryAsync();  // Обработчик события
            _deliveryCheckTimer.AutoReset = true;  // Автоматический перезапуск
            _deliveryCheckTimer.Start();  // Запуск таймера
        }

        // ==================== ОСНОВНЫЕ МЕТОДЫ УПРАВЛЕНИЯ СОЕДИНЕНИЯМИ ====================


        // Автоматческая система подключения к сохраненным клиентам
        public async Task AutoConnectToAllAsync()
        {
            try
            {
                Console.WriteLine("[SYSTEM] Автоматическое подключение...");

                // Чтение всех сохраненных IP-адресов из файла
                var savedIps = await _writer.ReadAllIpsFromFile();
                int connectedCount = 0;  // Счетчик успешных подключений

                // Перебор всех IP-адресов
                foreach (var ip in savedIps)
                {
                    if (string.IsNullOrWhiteSpace(ip)) continue;  // Пропуск пустых строк
                    if (_connections.ContainsKey(ip)) continue;   // Пропуск уже подключенных

                    Console.WriteLine($"[CONNECT] Попытка подключения к {ip}...");

                    // Попытка подключения с автоподключением (silent=true)
                    if (await ConnectAsync(ip, AppSettings.DefaultPort, true))
                    {
                        connectedCount++;
                        OnAutoConnected?.Invoke(ip);  // Вызов события автоподключения
                        await Task.Delay(500);  // Пауза между подключениями
                    }
                }

                Console.WriteLine($"[SYSTEM] Автоматически подключено к {connectedCount} клиентам");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка автоматического подключения: {ex.Message}");
            }
        }

        // Подключиться к клиенту по IP-адресу и порту вручную
        // ipAddress - IP-адрес целевого клиента
        // port - порт подключения (по умолчанию 46000)
        // silent - Режим тишины (без вывода сообщений)
        // возвращает true если подключение успешно, false в противном случае

        public async Task<bool> ConnectAsync(string ipAddress, int port = 46000, bool silent = false)
        {
            // Если уже подключены — возвращаем успех
            if (_connections.ContainsKey(ipAddress))
            {
                if (!silent) Console.WriteLine($"[CONNECT] Уже подключен к {ipAddress}");
                return true;
            }

            try
            {
                if (!silent) Console.WriteLine($"[CONNECT] Попытка подключения к {ipAddress}:{port}");

                var tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = 5000;
                tcpClient.SendTimeout = 5000;

                var connectTask = tcpClient.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    if (!silent) Console.WriteLine($"[ERROR] Таймаут подключения к {ipAddress}");
                    return false;
                }

                await connectTask;

                var stream = tcpClient.GetStream();
                string authMessage = $"AUTH:{AppSettings.Nickname}<END>";
                byte[] authData = Encoding.UTF8.GetBytes(authMessage);
                await stream.WriteAsync(authData, 0, authData.Length);

                var connectionInfo = new ConnectionInfo
                {
                    TcpClient = tcpClient,
                    NetworkStream = stream,
                    IpAddress = ipAddress,
                    Port = port,
                    LastActivity = DateTime.Now,
                    IsConnected = true
                };

                if (_connections.TryAdd(ipAddress, connectionInfo))
                {
                    if (!silent) Console.WriteLine($"[SUCCESS] Успешное подключение к {ipAddress}");

                    // Запускаем приём сообщений
                    _ = Task.Run(async () => await ReceiveMessagesAsync(connectionInfo));

                    // ✅ ВЕРНУТЬ: KeepAlive
                    var keepAliveCts = new CancellationTokenSource();
                    connectionInfo.KeepAliveCancellation = keepAliveCts;
                    _ = Task.Run(async () => await KeepAliveAsync(connectionInfo, keepAliveCts.Token));

                    // ✅ ДОБАВИТЬ: Создаем связь с этим клиентом
                    if (!string.IsNullOrEmpty(connectionInfo.Nickname))
                    {
                        _routeManager.AddLink(AppSettings.Nickname, connectionInfo.Nickname);
                    }


                    OnConnected?.Invoke(ipAddress);
                    return true;
                }
            }
            catch (SocketException sockEx)
            {
                if (!silent) Console.WriteLine($"[ERROR] Ошибка подключения к {ipAddress}: {sockEx.Message}");
            }
            catch (Exception ex)
            {
                if (!silent) Console.WriteLine($"[ERROR] Неожиданная ошибка: {ex.Message}");
            }

            return false;
        }
        /// <summary>
        /// Подключиться к клиенту по никнейму, перебирая все его IP
        /// </summary>
        public async Task<bool> ConnectToNicknameAsync(string nickname, int port = 46000, bool silent = false)
        {
            var ips = _routeManager.GetAllIpsByNickname(nickname);

            if (ips.Count == 0)
            {
                Console.WriteLine($"[CONNECT] Нет IP адресов для {nickname}");
                return false;
            }

            Console.WriteLine($"[CONNECT] Подключаемся к {nickname}, доступно IP: {ips.Count}");

            // Перебираем все IP, ищем рабочий
            foreach (var ip in ips)
            {
                // Пропускаем localhost
                if (ip == "127.0.0.1" || ip == "::1") continue;

                // Пропускаем уже подключенные
                if (_connections.ContainsKey(ip))
                {
                    Console.WriteLine($"[CONNECT] Уже подключен к {ip}");
                    return true;
                }

                Console.WriteLine($"[CONNECT] Пробуем {ip}...");

                if (await ConnectAsync(ip, port, true)) // silent=true чтобы не спамить ошибками
                {
                    Console.WriteLine($"[CONNECT] Успешно подключились к {nickname} через {ip}");
                    return true;
                }

                Console.WriteLine($"[CONNECT] {ip} не доступен, пробуем следующий...");
            }

            Console.WriteLine($"[ERROR] Не удалось подключиться к {nickname} ни к одному из IP: {string.Join(", ", ips)}");
            return false;
        }


        // [WARN] - отправка в менеджере подключений, неправильно!!!!!!!!!!!!!!!

        // ==================== МЕТОДЫ ОТПРАВКИ СООБЩЕНИЙ ====================

        // Отправить сообщение всем подключенным клиентам (широковещательная рассылка)
        // message - Текст сообщения для рассылки
        public async Task BroadcastMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            Console.WriteLine("[SYSTEM] Рассылка сообщения...");

            // Создание списка задач для параллельной отправки
            var tasks = new List<Task<SendResult>>();
            int sentCount = 0;    // Счетчик успешных отправок
            int failedCount = 0;  // Счетчик неудачных отправок

            // Создание задачи отправки для каждого активного подключения
            foreach (var connection in _connections.Values.Where(c => c.IsConnected))
            {
                tasks.Add(SendMessageToConnectionAsync(connection, message));
            }

            // Ожидание завершения всех задач отправки
            var allResults = await Task.WhenAll(tasks);

            // Подсчет результатов
            foreach (var sendResult in allResults)
            {
                if (sendResult.Success) sentCount++;
                else failedCount++;
            }

            Console.WriteLine($"[SYSTEM] Отправлено: {sentCount}, Не удалось: {failedCount}");

            // [WARN]
            // Сохранение сообщения в историю чата как широковещательное
            await _chatManager.SaveMessageAsync(
                AppSettings.Nickname,
                "BROADCAST",  // Специальный фальш-получатель для широковещательных сообщений
                message,
                DateTime.Now
            );
            // [/WARN\]
        }


        /// <summary>
        /// Отправить сообщение конкретному клиенту по IP-адресу или никнейму
        /// Автоматически выбирает между прямой отправкой и ретрансляцией
        /// </summary>
        public async Task<SendResult> SendMessageAsync(string ipAddressOrNickname, string message)
        {
            // Проверяем, это IP или никнейм
            bool isIp = IPAddress.TryParse(ipAddressOrNickname, out _);

            if (isIp)
            {
                return await SendMessageToIpAsync(ipAddressOrNickname, message);
            }
            else
            {
                // Это никнейм — используем unified отправку
                return await SendMessageToNicknameAsync(ipAddressOrNickname, message);
            }
        }

        /// <summary>
        /// Отправить сообщение по IP-адресу (прямая отправка)
        /// </summary>
        private async Task<SendResult> SendMessageToIpAsync(string ipAddress, string message)
        {
            // Проверка наличия активного подключения
            if (!_connections.TryGetValue(ipAddress, out var connection) || !connection.IsConnected)
            {
                // Попытка переподключения в автоматическом режиме
                if (!await ConnectAsync(ipAddress, AppSettings.DefaultPort, true))
                {
                    // Пробуем найти никнейм по IP и отправить через ретрансляцию
                    string nickname = _nicknameManager.GetNicknameByIp(ipAddress);
                    if (!string.IsNullOrEmpty(nickname) && RelayService != null)
                    {
                        Console.WriteLine($"[SEND] Переподключение не удалось, пробуем ретрансляцию к {nickname}");
                        return await RelayService.SendRelayedMessageAsync(nickname, message);
                    }

                    // Создание результата с ошибкой
                    var failedResult = new SendResult
                    {
                        Success = false,
                        Message = message,
                        IpAddress = ipAddress,
                        ErrorMessage = "Клиент не подключен и переподключение не удалось"
                    };

                    await SaveFailedMessageAsync(ipAddress, message, failedResult.ErrorMessage);
                    OnMessageFailed?.Invoke(ipAddress, message, failedResult.ErrorMessage);

                    return failedResult;
                }

                _connections.TryGetValue(ipAddress, out connection);
            }

            // Отправка сообщения через подключение
            return await SendMessageToConnectionAsync(connection, message);
        }

        /// <summary>
        /// Получить IP-адрес по никнейму из активных соединений или RouteManager
        /// </summary>
        private string GetIpByNickname(string nickname)
        {
            // Сначала ищем в активных соединениях
            foreach (var conn in _connections.Values)
            {
                if (conn.Nickname?.Equals(nickname, StringComparison.OrdinalIgnoreCase) == true)
                    return conn.IpAddress;
            }

            // Иначе спрашиваем RouteManager
            return _routeManager.GetIpByNickname(nickname);
        }

        /// <summary>
        /// Unified метод отправки сообщения по никнейму
        /// Автоматически выбирает прямое соединение или ретрансляцию
        /// </summary>
        public async Task<SendResult> SendMessageToNicknameAsync(string nickname, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new SendResult { Success = false, ErrorMessage = "Пустое сообщение" };

            // Проверяем, есть ли прямое активное соединение с этим ником
            string directIp = GetIpByNickname(nickname);
            bool hasDirectConnection = !string.IsNullOrEmpty(directIp) &&
                                       _connections.ContainsKey(directIp) &&
                                       _connections[directIp].IsConnected;

            // Если есть прямое соединение — отправляем напрямую
            if (hasDirectConnection)
            {
                Console.WriteLine($"[SEND] Прямая отправка к {nickname} через {directIp}");
                var result = await SendMessageToConnectionAsync(_connections[directIp], message);

                // if (result.Success)
                // {
                //     // Сохраняем в историю при успешной прямой отправке
                //     await _chatManager.SaveMessageAsync(AppSettings.Nickname, nickname, message, DateTime.Now);
                // }

                return result;
            }

            // Нет прямого соединения — используем ретрансляцию
            if (RelayService != null)
            {
                Console.WriteLine($"[SEND] Нет прямого соединения с {nickname}, используем ретрансляцию");
                var relayResult = await RelayService.SendRelayedMessageAsync(nickname, message);

                

                return relayResult;
            }

            // Нет ни прямого соединения, ни RelayService
            return new SendResult
            {
                Success = false,
                ErrorMessage = $"Нет прямого соединения с {nickname} и ретрансляция недоступна"
            };
        }



        /// <summary>
        /// Внутренний метод для отправки сообщения через конкретное подключение (прямая отправка)
        /// </summary>
        private async Task<SendResult> SendMessageToConnectionAsync(ConnectionInfo connection, string message)
        {
            var messageId = Guid.NewGuid().ToString();

            try
            {
                // Форматирование сообщения с ID в формате MSG:{id}:{text}<END>
                string formattedMessage = $"MSG:{messageId}:{message}<END>";
                byte[] data = Encoding.UTF8.GetBytes(formattedMessage);

                // Отправка сообщения через сетевой поток
                await connection.NetworkStream.WriteAsync(data, 0, data.Length);
                connection.LastActivity = DateTime.Now;

                // Ожидание подтверждения доставки (5 секунд)
                bool confirmed = await WaitForConfirmationAsync(connection, messageId, 5000);

                if (confirmed)
                {
                    // Получение никнейма получателя по IP-адресу
                    string targetNick = _nicknameManager.GetNicknameByIp(connection.IpAddress);
                    if (string.IsNullOrEmpty(targetNick)) targetNick = connection.IpAddress;


                    OnMessageDelivered?.Invoke(connection.IpAddress, message);

                    return new SendResult
                    {
                        Success = true,
                        MessageId = messageId,
                        Message = message,
                        IpAddress = connection.IpAddress,
                        Timestamp = DateTime.Now
                    };
                }
                else
                {
                    // Не получено подтверждение — добавляем в очередь на повтор
                    var failedSendResult = new SendResult
                    {
                        Success = false,
                        MessageId = messageId,
                        Message = message,
                        IpAddress = connection.IpAddress,
                        ErrorMessage = "Не получено подтверждение доставки"
                    };

                    _pendingMessages.Enqueue(new PendingMessage
                    {
                        MessageId = messageId,
                        Message = message,
                        IpAddress = connection.IpAddress,
                        RetryCount = 0,
                        LastTry = DateTime.Now
                    });

                    await SaveFailedMessageAsync(connection.IpAddress, message, failedSendResult.ErrorMessage);
                    OnMessageFailed?.Invoke(connection.IpAddress, message, failedSendResult.ErrorMessage);

                    return failedSendResult;
                }
            }
            catch (Exception ex)
            {
                connection.IsConnected = false;

                var errorResult = new SendResult
                {
                    Success = false,
                    MessageId = messageId,
                    Message = message,
                    IpAddress = connection.IpAddress,
                    ErrorMessage = $"Ошибка отправки: {ex.Message}"
                };

                _pendingMessages.Enqueue(new PendingMessage
                {
                    MessageId = messageId,
                    Message = message,
                    IpAddress = connection.IpAddress,
                    RetryCount = 0,
                    LastTry = DateTime.Now
                });

                await SaveFailedMessageAsync(connection.IpAddress, message, errorResult.ErrorMessage);
                OnMessageFailed?.Invoke(connection.IpAddress, message, errorResult.ErrorMessage);

                return errorResult;
            }
        }



        // ==================== МЕТОДЫ ОТПРАВКИ ФАЙЛОВ ====================

        // Отправить файл клиенту по IP-адресу
        // ipAddress - IP-адрес получателя
        // filePath - Полный путь к файлу
        // Возвращает результат отправки файла
        public async Task<SendResult> SendFileAsync(string ipAddress, string filePath)
        {
            // Проверка существования файла
            if (!File.Exists(filePath))
                return new SendResult { Success = false, ErrorMessage = "Файл не найден" };

            var fileInfo = new FileInfo(filePath);

            // Проверка размера файла (ограничение 10 МБ)
            if (fileInfo.Length > 10 * 1024 * 1024)
                return new SendResult { Success = false, ErrorMessage = "Файл слишком большой (макс. 10 МБ)" };

            try
            {
                // Чтение содержимого файла
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);

                // Проверка подключения к получателю
                if (!_connections.TryGetValue(ipAddress, out var connection) || !connection.IsConnected)
                {
                    // Попытка подключения
                    if (!await ConnectAsync(ipAddress, AppSettings.DefaultPort, true))
                    {
                        return new SendResult { Success = false, ErrorMessage = "Не удалось подключиться" };
                    }
                    _connections.TryGetValue(ipAddress, out connection);
                }

                // Отправка содержимого файла
                return await SendFileContentAsync(connection, fileInfo.Name, fileContent);
            }
            catch (Exception ex)
            {
                return new SendResult { Success = false, ErrorMessage = $"Ошибка отправки файла: {ex.Message}" };
            }
        }

        // [/WARN\] - окончание позора с отправкой сообщений

        // ==================== МЕТОДЫ ПРИЕМА СООБЩЕНИЙ ====================

        // [WARN] - <тяжелый вздох>, ну а теперь начинается приём сообщений, которого здесь быть не должно

        // Асинхронный прием сообщений от подключения
        // Работает в отдельном потоке для каждого подключения
        private async Task ReceiveMessagesAsync(ConnectionInfo connection)
        {
            byte[] buffer = new byte[8192];
            var pendingData = new List<byte>();

            try
            {
                while (connection.IsConnected && connection.NetworkStream != null)
                {
                    int bytesRead = await connection.NetworkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        await HandleDisconnection(connection);
                        break;
                    }

                    // Добавляем прочитанные байты в буфер
                    pendingData.AddRange(new ArraySegment<byte>(buffer, 0, bytesRead));

                    // Ищем и обрабатываем все полные сообщения
                    while (true)
                    {
                        int endIndex = FindEndMarker(pendingData);
                        if (endIndex == -1) break;

                        byte[] messageBytes = new byte[endIndex];
                        pendingData.CopyTo(0, messageBytes, 0, endIndex);
                        pendingData.RemoveRange(0, endIndex + 5); // +5 для "<END>"

                        string receivedData = Encoding.UTF8.GetString(messageBytes);
                        await ProcessReceivedDataAsync(connection, receivedData);
                    }

                    connection.LastActivity = DateTime.Now;
                }
            }
            catch (IOException)
            {
                await HandleDisconnection(connection);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка приема от {connection.IpAddress}: {ex.Message}");
                await HandleDisconnection(connection);
            }
        }
        public async Task<bool> SendRawMessageAsync(string ipAddress, string rawMessage)
        {
            if (!_connections.TryGetValue(ipAddress, out var connection) || !connection.IsConnected)
            {
                Console.WriteLine($"[DEBUG] SendRawMessageAsync: нет активного соединения с {ipAddress}");
                return false;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(rawMessage);
                await connection.NetworkStream.WriteAsync(data, 0, data.Length);
                connection.LastActivity = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SendRawMessageAsync to {ipAddress} failed: {ex.Message}");
                connection.IsConnected = false;
                return false;
            }
        }



        private int FindEndMarker(List<byte> data)
        {
            byte[] endMarker = Encoding.UTF8.GetBytes("<END>");
            for (int i = 0; i <= data.Count - endMarker.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < endMarker.Length; j++)
                {
                    if (data[i + j] != endMarker[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private async Task KeepAliveAsync(ConnectionInfo connection, CancellationToken token)
        {
            while (!token.IsCancellationRequested && connection.IsConnected)
            {
                try
                {
                    await Task.Delay(30000, token); // 30 секунд

                    if (!connection.IsConnected) break;

                    byte[] pingData = Encoding.UTF8.GetBytes("PING<END>");
                    await connection.NetworkStream.WriteAsync(pingData, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KEEPALIVE] Ошибка для {connection.IpAddress}: {ex.Message}");
                    connection.IsConnected = false;
                    break;
                }
            }
        }





        // private int FindEndMarker(List<byte> data)
        // {
        //     byte[] endMarker = Encoding.UTF8.GetBytes("<END>");

        //     // Ищем последовательность байт маркера
        //     for (int i = 0; i <= data.Count - endMarker.Length; i++)
        //     {
        //         bool found = true;
        //         for (int j = 0; j < endMarker.Length; j++)
        //         {
        //             if (data[i + j] != endMarker[j])
        //             {
        //                 found = false;
        //                 break;
        //             }
        //         }
        //         if (found) return i; // Возвращаем индекс начала маркера
        //     }
        //     return -1;
        // }

        // [WARN] - многострочные сообщения могут ломаться тут [/WARN\]

        // Обработка полученных данных (могут содержать несколько сообщений)
        private async Task ProcessReceivedDataAsync(ConnectionInfo connection, string data)
        {
            // Разделение данных на отдельные сообщения по разделителю <END>
            if (data.Contains("<END>"))
            {
                string[] messages = data.Split(new[] { "<END>" }, StringSplitOptions.RemoveEmptyEntries);

                // Обработка каждого сообщения отдельно
                foreach (var message in messages)
                {
                    await ProcessSingleMessageAsync(connection, message + "<END>");
                }
            }
            else
            {
                // Обработка одного сообщения
                await ProcessSingleMessageAsync(connection, data);
            }
        }

        private bool IsSystemMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return true;

            return message.StartsWith("ROUTE_UPDATE:") ||
                   message.StartsWith("RELAY:") ||
                   message.StartsWith("RELAY_MSG:") ||
                   message.StartsWith("RELAY_CONFIRM:") ||
                   message.StartsWith("PING") ||
                   message.StartsWith("PONG") ||
                   message.StartsWith("AUTH:") ||
                   message.StartsWith("AUTH_OK:") ||
                   message.StartsWith("CONFIRM:") ||
                   message.StartsWith("FILE_") ||
                   message.StartsWith("[SYSTEM]") ||
                   message.StartsWith("[RELAY]") ||
                   message.StartsWith("[CONNECT]") ||
                   message.StartsWith("[AUTO]") ||
                   message.StartsWith("[DEBUG]");
        }






        // Обработка одного сообщения с определением его типа
        private async Task ProcessSingleMessageAsync(ConnectionInfo connection, string message)
        {
            try
            {
                // ОБРАБОТКА ОБЫЧНЫХ СООБЩЕНИЙ (префикс MSG:)
                if (message.StartsWith("MSG:"))
                {
                    string[] parts = message.Substring(4).Split(':', 2);
                    if (parts.Length >= 2)
                    {
                        string messageId = parts[0];
                        string text = parts[1].Replace("<END>", "");
                        await SendConfirmationAsync(connection, messageId);

                        // Системные сообщения внутри MSG: игнорируем — они придут отдельно
                        // или обрабатываем как обычный текст (не рекурсия!)
                        if (IsSystemMessage(text))
                        {
                            Console.WriteLine($"[WARN] Получено системное сообщение внутри MSG:, игнорируем: {text.Substring(0, Math.Min(30, text.Length))}...");
                            return;
                        }

                        // Обычное пользовательское сообщение
                        string senderNickname = _nicknameManager.GetNicknameByIp(connection.IpAddress);
                        if (string.IsNullOrEmpty(senderNickname))
                            senderNickname = connection.IpAddress;
                        await _chatManager.SaveMessageAsync(senderNickname, senderNickname, text, DateTime.Now);
                        _chatSessionManager.HandleNewMessage(senderNickname, text);
                    }
                    return;
                }
                // ОБРАБОТКА ПОДТВЕРЖДЕНИЙ ДОСТАВКИ (префикс CONFIRM:)
                else if (message.StartsWith("CONFIRM:"))
                {
                    string messageId = message.Substring(8).Replace("<END>", "");
                    // Вызов события подтверждения доставки
                    connection.RaiseConfirmationReceived(messageId);
                    return;
                }
                // ОБРАБОТКА АУТЕНТИФИКАЦИИ (префикс AUTH:)
                else if (message.StartsWith("AUTH:"))
                {
                    // Извлечение никнейма из сообщения аутентификации
                    string nickname = message.Substring(5).Replace("<END>", "");
                    connection.Nickname = nickname;
                    _routeManager.AddDynamicClient(nickname, connection.IpAddress, null);
                    _routeManager.AddLink(AppSettings.Nickname, nickname);

                    // Обновление сопоставления IP-никнейм
                    _nicknameManager.UpdateMapping(connection.IpAddress, nickname);
                    // Создание или открытие чата с этим пользователем
                    _chatSessionManager.CreateOrOpenChat(nickname);
                    // Создание чата в ChatManager
                    _chatManager.CreateChat(nickname);

                    // Отправка подтверждения аутентификации
                    string response = $"AUTH_OK:{AppSettings.Nickname}<END>";
                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    await connection.NetworkStream.WriteAsync(responseData, 0, responseData.Length);
                    await SendRouteUpdateAsync(connection);

                    Console.WriteLine($"[CONNECT] {nickname} ({connection.IpAddress}) - аутентифицирован");
                    return;
                }
                if (message.StartsWith("PING"))
                {
                    await connection.NetworkStream.WriteAsync(Encoding.UTF8.GetBytes("PONG<END>"));
                    return;
                }
                if (message.StartsWith("PONG"))
                {
                    connection.LastActivity = DateTime.Now;
                    return;
                }
                // ОБРАБОТКА ПОДТВЕРЖДЕНИЯ АУТЕНТИФИКАЦИИ (префикс AUTH_OK:)
                else if (message.StartsWith("AUTH_OK:"))
                {
                    string nickname = message.Substring(8).Replace("<END>", "");
                    connection.Nickname = nickname;

                    // ✅ ДОБАВИТЬ: сохраняем IP сразу!
                    _routeManager.AddDynamicClient(nickname, connection.IpAddress, null);
                    _routeManager.AddLink(AppSettings.Nickname, nickname);

                    _nicknameManager.UpdateMapping(connection.IpAddress, nickname);
                    _chatManager.CreateChat(nickname);

                    // Отправляем маршруты
                    await SendRouteUpdateAsync(connection);

                    Console.WriteLine($"[CONNECT] Подтверждена аутентификация {nickname} ({connection.IpAddress})");
                    return;
                }

                // ... внутри ProcessSingleMessageAsync, после обработки CONFIRM:

                else if (message.StartsWith("ROUTE_UPDATE:"))
                {
                    string routesData = message.Substring(13).Replace("<END>", "");
                    Console.WriteLine($"[ROUTE] 📥 Получен ROUTE_UPDATE от {connection.Nickname}");

                    try
                    {
                        var routes = JsonSerializer.Deserialize<List<ClientInfo>>(routesData);
                        int addedCount = 0;
                        string sourceNick = _nicknameManager.GetNicknameByIp(connection.IpAddress) ?? connection.Nickname;

                        foreach (var route in routes)
                        {
                            if (route.Nickname.Equals(AppSettings.Nickname, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Добавляем все IP из списка
                            if (route.Ips != null)
                            {
                                foreach (var ip in route.Ips)
                                {
                                    if (ip == "127.0.0.1" || ip == "::1")
                                        continue;

                                    var existing = _routeManager.GetAllIpsByNickname(route.Nickname);
                                    if (!existing.Contains(ip))
                                    {
                                        _routeManager.AddDynamicClient(route.Nickname, ip, sourceNick);
                                        addedCount++;
                                        Console.WriteLine($"[ROUTE] Добавлен из ROUTE_UPDATE: {route.Nickname} -> {ip}");
                                    }
                                }
                            }

                            // ✅ ДОБАВИТЬ: Создаем связь между отправителем и каждым клиентом в списке
                            if (route.Nickname != sourceNick)
                            {
                                _routeManager.AddLink(sourceNick, route.Nickname);
                                Console.WriteLine($"[ROUTE] Добавлена связь: {sourceNick} <-> {route.Nickname}");
                            }
                        }

                        if (addedCount > 0 || routes.Count > 0)
                        {
                            Console.WriteLine($"[ROUTE] Добавлено {addedCount} маршрутов из ROUTE_UPDATE, перестраиваем цепочки");
                            _routeManager.RebuildAllChains();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Ошибка обработки ROUTE_UPDATE: {ex.Message}");
                    }
                    return;
                }

                else if (message.StartsWith("ROUTE_CHAINS:"))
                {
                    string chainsData = message.Substring(13).Replace("<END>", "");
                    Console.WriteLine($"[ROUTE] 📥 Получены цепочки от {connection.Nickname}");

                    try
                    {
                        var chains = JsonSerializer.Deserialize<List<RouteChainInfo>>(chainsData);
                        string sourceNick = _nicknameManager.GetNicknameByIp(connection.IpAddress) ?? connection.Nickname;

                        if (chains != null && !string.IsNullOrEmpty(sourceNick))
                        {
                            _routeManager.ProcessReceivedChains(sourceNick, chains);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Ошибка обработки ROUTE_CHAINS: {ex.Message}");
                    }
                    return;
                }

                // ... дальше идёт обработка AUTH и т.д.
                else if (message.StartsWith("RELAY:"))
                {
                    if (RelayService != null)
                    {
                        string relayData = message.Substring(6).Replace("<END>", "");
                        await RelayService.ProcessRelayMessageAsync(connection.IpAddress, relayData);
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] Получено ретранслируемое сообщение, но RelayService не инициализирован");
                    }
                    return;
                }
                //[WARN] - возможно не работающая система отправки файлов
                // ОБРАБОТКА НАЧАЛА ПЕРЕДАЧИ ФАЙЛА (префикс FILE_START:)
                else if (message.StartsWith("FILE_START:"))
                {
                    // Извлечение информации о файле
                    string fileData = message.Replace("FILE_START:", "").Replace("<END>", "");
                    string[] parts = fileData.Split(':');

                    if (parts.Length >= 4)
                    {
                        string fileId = parts[0];
                        string fileName = parts[1];
                        long fileSize = long.Parse(parts[2]);
                        int totalChunks = int.Parse(parts[3]);

                        // Создание объекта для отслеживания передачи файла
                        var fileTransfer = new IncomingFileTransfer
                        {
                            FileId = fileId,
                            FileName = fileName,
                            FileSize = fileSize,
                            TotalChunks = totalChunks,
                            Data = new byte[fileSize],
                            CurrentPosition = 0
                        };

                        // Добавление в словарь входящих передач файлов
                        _incomingFileTransfers[fileId] = fileTransfer;

                        Console.WriteLine($"[FILE] Начинаем прием файла: {fileName} ({fileSize} байт)");
                    }
                    return;
                }
                // ConnectionManager.cs - внутри ProcessSingleMessageAsync
                else if (message.StartsWith("RELAY_MSG:"))
                {
                    if (RelayService != null)
                    {
                        string relayData = message.Substring(10).Replace("<END>", "");
                        await RelayService.ProcessRelayMessageAsync(connection.IpAddress, relayData);
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] Получено RELAY_MSG, но RelayService не инициализирован");
                    }
                    return;
                }

                else if (message.StartsWith("RELAY_CONFIRM:"))
                {
                    if (RelayService != null)
                    {
                        string confirmData = message.Substring(14).Replace("<END>", "");
                        RelayService.ProcessConfirmation(confirmData);
                    }
                    return;
                }

                // ОБРАБОТКА ЧАНКА ФАЙЛА (префикс FILE_CHUNK:)
                else if (message.StartsWith("FILE_CHUNK:"))
                {
                    // Извлечение информации о чанке
                    string chunkData = message.Replace("FILE_CHUNK:", "").Replace("<END>", "");
                    string[] parts = chunkData.Split(':');

                    if (parts.Length >= 3)
                    {
                        string fileId = parts[0];
                        int chunkIndex = int.Parse(parts[1]);
                        int chunkSize = int.Parse(parts[2]);

                        // Отправка подтверждения получения чанка
                        string ack = $"FILE_ACK:{fileId}:{chunkIndex}<END>";
                        byte[] ackData = Encoding.UTF8.GetBytes(ack);
                        await connection.NetworkStream.WriteAsync(ackData, 0, ackData.Length);

                        // Установка флагов приема файла
                        connection.IsReceivingFile = true;
                        connection.CurrentFileTransferId = fileId;
                        connection.ExpectedFileChunkSize = chunkSize;
                    }
                    return;
                }
                // ОБРАБОТКА ЗАВЕРШЕНИЯ ПЕРЕДАЧИ ФАЙЛА (префикс FILE_END:)
                else if (message.StartsWith("FILE_END:"))
                {
                    string fileId = message.Replace("FILE_END:", "").Replace("<END>", "");

                    // Получение информации о передаче файла
                    if (_incomingFileTransfers.TryGetValue(fileId, out var transfer))
                    {
                        // Сохранение полученного файла
                        await SaveIncomingFile(transfer, connection.IpAddress);

                        // Удаление из словаря входящих передач
                        _incomingFileTransfers.TryRemove(fileId, out _);

                        Console.WriteLine($"[FILE] Файл {transfer.FileName} успешно получен");
                    }
                    return;
                }

                //[/WARN\]
                // ОБРАБОТКА ПРОСТЫХ СООБЩЕНИЙ (без префикса)
                else if (!string.IsNullOrWhiteSpace(message) && message != "<END>")
                {
                    string text = message.Replace("<END>", "");

                    // Игнорирование системных сообщений
                    if (text.StartsWith("AUTH_OK:") || text.StartsWith("CONFIRM:") ||
                        text.StartsWith("FILE_") || string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    // Получение никнейма отправителя
                    string senderNickname = _nicknameManager.GetNicknameByIp(connection.IpAddress);
                    if (string.IsNullOrEmpty(senderNickname))
                        senderNickname = connection.IpAddress;

                    // Сохранение сообщения в историю чата
                    await _chatManager.SaveMessageAsync(
                        senderNickname,
                        AppSettings.Nickname,
                        text,
                        DateTime.Now
                    );

                    // Передача сообщения в ChatSessionManager
                    _chatSessionManager.HandleNewMessage(senderNickname, text);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка обработки сообщения: {ex.Message}");
            }
        }


        private async Task SendRouteUpdateAsync(ConnectionInfo connection)
        {
            // lock (_routeUpdateLock)
            // {
            //     // Отправляем не чаще раза в 5 секунд
            //     if ((DateTime.Now - _lastRouteUpdateSent).TotalSeconds < 5)
            //         return;
            //     _lastRouteUpdateSent = DateTime.Now;
            // }

            var allRoutes = _routeManager.GetAllRoutes();
            string routesJson = JsonSerializer.Serialize(allRoutes);
            string routeUpdate = $"ROUTE_UPDATE:{routesJson}<END>";
            byte[] data = Encoding.UTF8.GetBytes(routeUpdate);
            await connection.NetworkStream.WriteAsync(data, 0, data.Length);
        }



        // [/WARN\] - приём сообщений кончился, это писец

        // ==================== СЛУЖЕБНЫЕ МЕТОДЫ ====================

        // [WARN] - о господи, тут и сохранение файлов


        // Сохранить полученный файл на диск
        private async Task SaveIncomingFile(IncomingFileTransfer transfer, string senderIp)
        {
            try
            {
                // Проверка наличия данных
                if (transfer.Data == null || transfer.Data.Length == 0)
                {
                    Console.WriteLine($"[FILE] Ошибка: данные файла пусты");
                    return;
                }

                // Получение никнейма отправителя
                string senderNickname = _nicknameManager.GetNicknameByIp(senderIp);
                if (string.IsNullOrEmpty(senderNickname))
                    senderNickname = senderIp;

                // Сохранение файла через ChatManager
                var saveResult = await _chatManager.SaveFileAsync(
                    senderNickname,
                    AppSettings.Nickname,
                    transfer.FileName,
                    transfer.Data,
                    DateTime.Now
                );

                if (saveResult.success)
                {
                    Console.WriteLine($"[FILE] Файл {transfer.FileName} успешно сохранен от {senderNickname}");

                    // Уведомление пользователя о получении файла
                    string notification = $"[ФАЙЛ] Получен файл: {transfer.FileName}";
                    _chatSessionManager.HandleNewMessage(senderNickname, notification);
                }
                else
                {
                    Console.WriteLine($"[FILE] Ошибка сохранения файла {transfer.FileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения файла: {ex.Message}");
            }
        }


        // Отправить подтверждение получения сообщения
        private async Task SendConfirmationAsync(ConnectionInfo connection, string messageId)
        {
            try
            {
                // Формирование сообщения подтверждения
                string confirmation = $"CONFIRM:{messageId}<END>";
                byte[] data = Encoding.UTF8.GetBytes(confirmation);
                // Отправка подтверждения
                await connection.NetworkStream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка отправки подтверждения: {ex.Message}");
            }
        }

        // Сохранить информацию о неудачной доставке сообщения

        private async Task SaveFailedMessageAsync(string ipAddress, string message, string error)
        {
            // Форматирование сообщения о неудачной доставке
            string failedMessage = $"[НЕ ДОСТАВЛЕНО] {message}\nПричина: {error}\nВремя: {DateTime.Now:HH:mm:ss}";

            // Получение никнейма получателя
            string nickname = _nicknameManager.GetNicknameByIp(ipAddress);
            if (string.IsNullOrEmpty(nickname))
                nickname = ipAddress;

            // Сохранение в историю чата
            await _chatManager.SaveMessageAsync(
                AppSettings.Nickname,
                nickname + "_failed",  // Специальный суффикс для неудачных сообщений
                failedMessage,
                DateTime.Now
            );

            // Логирование в файл
            string logPath = Path.Combine(AppSettings.GetUserDataPath(), "delivery_failures.log");
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {ipAddress} | {message} | {error}\n";
            await File.AppendAllTextAsync(logPath, logEntry, Encoding.UTF8);
        }


        // Ожидание подтверждения доставки сообщения

        private async Task<bool> WaitForConfirmationAsync(ConnectionInfo connection, string messageId, int timeoutMs)
        {
            // Создание источника задачи для ожидания подтверждения
            var confirmationSource = new TaskCompletionSource<bool>();
            var cts = new System.Threading.CancellationTokenSource(timeoutMs);

            // Обработчик события подтверждения
            EventHandler<ConfirmationReceivedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                if (args.MessageId == messageId)
                {
                    confirmationSource.TrySetResult(true);  // Подтверждение получено
                }
            };

            // Подписка на событие подтверждения
            connection.ConfirmationReceived += handler;

            try
            {
                // Ожидание подтверждения или таймаута
                var completedTask = await Task.WhenAny(
                    confirmationSource.Task,
                    Task.Delay(timeoutMs, cts.Token)
                );

                // Проверка результата
                var isConfirmed = completedTask == confirmationSource.Task && confirmationSource.Task.Result;
                return isConfirmed;
            }
            finally
            {
                // Отписка от события
                connection.ConfirmationReceived -= handler;
                cts.Dispose();
            }
        }

        // Проверка доставки сообщений и выполнение повторных попыток

        private async Task CheckMessageDeliveryAsync()
        {
            if (_pendingMessages.IsEmpty) return;  // Нет сообщений для проверки

            var now = DateTime.Now;
            var retryMessages = new List<PendingMessage>();  // Список сообщений для повторной отправки

            // Извлечение всех сообщений из очереди
            while (_pendingMessages.TryDequeue(out var pending))
            {
                // Проверка условий для повторной отправки
                if (pending.RetryCount < 3 && (now - pending.LastTry).TotalMinutes > 1)
                {
                    retryMessages.Add(pending);  // Добавление в список для повторной отправки
                }
                else if (pending.RetryCount >= 3)
                {
                    // Превышено количество попыток
                    Console.WriteLine($"[ERROR] Сообщение не доставлено после 3 попыток: {pending.Message}");
                    await SaveFailedMessageAsync(pending.IpAddress, pending.Message, "Превышено количество попыток отправки");
                }
            }

            // Повторная отправка сообщений
            foreach (var pending in retryMessages)
            {
                Console.WriteLine($"[RETRY] Повторная отправка на {pending.IpAddress}...");

                var result = await SendMessageAsync(pending.IpAddress, pending.Message);

                // Если отправка не удалась, увеличиваем счетчик попыток и возвращаем в очередь
                if (!result.Success)
                {
                    pending.RetryCount++;
                    pending.LastTry = now;
                    _pendingMessages.Enqueue(pending);
                }
            }
        }

        // Обработка разрыва соединения

        private async Task HandleDisconnection(ConnectionInfo connection)
        {
            connection.IsConnected = false;

            // Удаление подключения из словаря
            if (_connections.TryRemove(connection.IpAddress, out _))
            {
                try
                {
                    // Закрытие сетевого потока и TCP-клиента
                    connection.NetworkStream?.Close();
                    connection.TcpClient?.Close();
                    connection.KeepAliveCancellation?.Cancel();
                }
                catch { }  // Игнорирование ошибок при закрытии

                Console.WriteLine($"[CONNECT] Отключено от {connection.IpAddress}");
                // Вызов события отключения
                OnDisconnected?.Invoke(connection.IpAddress);

                // Автоматическое переподключение (если включено в настройках)
                if (AppSettings.AutoConnectDiscovered)
                {
                    await Task.Delay(5000);  // Пауза 5 секунд
                    // Попытка переподключения в фоновом режиме
                    _ = Task.Run(async () => await ConnectAsync(connection.IpAddress, connection.Port, true));
                }
            }
        }

        // [WARN] - о неееееееет, и отправка файлов

        // Асинхронная отправка содержимого файла с таймаутом
        private async Task<SendResult> SendFileContentAsync(ConnectionInfo connection, string fileName, byte[] fileContent)
        {
            var fileId = Guid.NewGuid().ToString();  // Уникальный идентификатор файла
            var timeoutTask = Task.Delay(30000);  // Таймаут 30 секунд

            // Задача отправки файла
            var sendTask = Task.Run(async () =>
            {
                try
                {
                    var stream = connection.NetworkStream;

                    // Отправка заголовка файла
                    string header = $"FILE_START:{fileId}:{fileName}:{fileContent.Length}:1<END>";
                    byte[] headerData = Encoding.UTF8.GetBytes(header);
                    await stream.WriteAsync(headerData, 0, headerData.Length);
                    await stream.FlushAsync();

                    await Task.Delay(100);  // Короткая пауза

                    // Отправка заголовка чанка
                    string chunkHeader = $"FILE_CHUNK:{fileId}:0:{fileContent.Length}<END>";
                    byte[] chunkHeaderData = Encoding.UTF8.GetBytes(chunkHeader);
                    await stream.WriteAsync(chunkHeaderData, 0, chunkHeaderData.Length);
                    await stream.FlushAsync();

                    // Отправка содержимого файла
                    await stream.WriteAsync(fileContent, 0, fileContent.Length);
                    await stream.FlushAsync();

                    // Ожидание подтверждения получения чанка
                    byte[] ackBuffer = new byte[1024];
                    int ackBytes = await stream.ReadAsync(ackBuffer, 0, ackBuffer.Length);
                    string ackResponse = Encoding.UTF8.GetString(ackBuffer, 0, ackBytes);

                    // Проверка подтверждения
                    if (!ackResponse.Contains($"FILE_ACK:{fileId}:0"))
                    {
                        return new SendResult { Success = false, ErrorMessage = "Не получено подтверждение файла" };
                    }

                    // Отправка маркера завершения передачи
                    string endMarker = $"FILE_END:{fileId}<END>";
                    byte[] endData = Encoding.UTF8.GetBytes(endMarker);
                    await stream.WriteAsync(endData, 0, endData.Length);
                    await stream.FlushAsync();

                    // Обновление времени последней активности
                    connection.LastActivity = DateTime.Now;

                    Console.WriteLine($"[FILE] Файл {fileName} успешно отправлен на {connection.IpAddress}");

                    return new SendResult
                    {
                        Success = true,
                        MessageId = fileId,
                        Message = $"Файл: {fileName}",
                        IpAddress = connection.IpAddress,
                        Timestamp = DateTime.Now
                    };
                }
                catch (Exception ex)
                {
                    return new SendResult { Success = false, ErrorMessage = $"Ошибка при отправке: {ex.Message}" };
                }
            });

            // Ожидание завершения отправки или таймаута
            var completedTask = await Task.WhenAny(sendTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                return new SendResult { Success = false, ErrorMessage = "Таймаут при отправке файла" };
            }

            return await sendTask;
        }

        // ==================== ПУБЛИЧНЫЕ МЕТОДЫ ДЛЯ ПОЛУЧЕНИЯ ИНФОРМАЦИИ ====================

        // Получить список активных подключений

        public List<string> GetActiveConnections()
        {
            return _connections.Values
                .Where(c => c.IsConnected)  // Фильтрация активных подключений
                .Select(c => c.IpAddress)   // Выбор IP-адресов
                .ToList();
        }


        // Получить статистику подключений

        public ConnectionStats GetConnectionStats()
        {
            var active = _connections.Values.Count(c => c.IsConnected);  // Количество активных подключений
            var total = _connections.Count;  // Общее количество подключений

            return new ConnectionStats
            {
                TotalConnections = total,
                ActiveConnections = active,
                PendingMessages = _pendingMessages.Count  // Количество сообщений в очереди
            };
        }


        // Получить информацию о соединении по IP-адресу

        public string GetConnectionInfo(string ipAddress)
        {
            if (_connections.TryGetValue(ipAddress, out var connection))
            {
                var status = connection.IsConnected ? "✓" : "✗";  // Символ статуса
                string nickname = _nicknameManager.GetNicknameByIp(ipAddress);
                string nicknameStr = !string.IsNullOrEmpty(nickname) ? $" ({nickname})" : "";

                return $"{ipAddress}{nicknameStr} - {status}";
            }

            return $"{ipAddress} - не подключен";
        }


        // Отключиться от всех клиентов

        public async Task DisconnectAllAsync()
        {
            Console.WriteLine("[SYSTEM] Отключение от всех клиентов...");

            var disconnectTasks = new List<Task>();

            // Создание задач для отключения от каждого клиента
            foreach (var connection in _connections.Values)
            {
                disconnectTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        connection.NetworkStream?.Close();
                        connection.TcpClient?.Close();
                    }
                    catch { }
                }));
            }

            // Ожидание завершения всех задач отключения
            await Task.WhenAll(disconnectTasks);
            _connections.Clear();  // Очистка словаря подключений

            Console.WriteLine("[SYSTEM] Отключено от всех клиентов");
        }
    }

    // ==================== ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ ====================

    // Информация о подключении
    // Хранит состояние одного сетевого подключения

    public class ConnectionInfo
    {
        public TcpClient TcpClient { get; set; }  // TCP-клиент для обмена данными
        public NetworkStream NetworkStream { get; set; }  // Сетевой поток для чтения/записи
        public string IpAddress { get; set; }  // IP-адрес удаленного клиента
        public int Port { get; set; }  // Порт подключения
        public string Nickname { get; set; }  // Никнейм удаленного клиента
        public DateTime LastActivity { get; set; }  // Время последней активности
        public bool IsConnected { get; set; }  // Флаг активности подключения

        // Флаги для приема файлов
        public bool IsReceivingFile { get; set; }  // Флаг приема файла
        public string CurrentFileTransferId { get; set; }  // ID текущей передачи файла
        public int ExpectedFileChunkSize { get; set; }  // Ожидаемый размер чанка файла
        public CancellationTokenSource KeepAliveCancellation { get; set; }

        // Событие подтверждения доставки сообщения
        public event EventHandler<ConfirmationReceivedEventArgs> ConfirmationReceived;

        // Метод для вызова события подтверждения
        public void RaiseConfirmationReceived(string messageId)
        {
            ConfirmationReceived?.Invoke(this, new ConfirmationReceivedEventArgs { MessageId = messageId });
        }
    }

    // Результат отправки сообщения
    // Содержит информацию об успешности отправки и детали ошибки

    public class SendResult
    {
        public bool Success { get; set; }  // Успешность отправки
        public string MessageId { get; set; }  // Уникальный идентификатор сообщения
        public string Message { get; set; }  // Текст сообщения
        public string IpAddress { get; set; }  // IP-адрес получателя
        public DateTime Timestamp { get; set; }  // Время отправки
        public string ErrorMessage { get; set; }  // Сообщение об ошибке (если есть)
    }


    // Ожидающее сообщение
    // Сообщение, которое еще не было доставлено и ожидает повторной отправки

    public class PendingMessage
    {
        public string MessageId { get; set; }  // ID сообщения
        public string Message { get; set; }  // Текст сообщения
        public string IpAddress { get; set; }  // IP-адрес получателя
        public int RetryCount { get; set; }  // Количество попыток отправки
        public DateTime LastTry { get; set; }  // Время последней попытки
    }


    // Аргументы события подтверждения получения сообщения

    public class ConfirmationReceivedEventArgs : EventArgs
    {
        public string MessageId { get; set; }  // ID подтвержденного сообщения
    }


    // Входящая передача файла
    // Информация о файле, который принимается от удаленного клиента

    public class IncomingFileTransfer
    {
        public string FileId { get; set; }  // Уникальный ID файла
        public string FileName { get; set; }  // Имя файла
        public long FileSize { get; set; }  // Размер файла в байтах
        public int TotalChunks { get; set; }  // Общее количество чанков
        public byte[] Data { get; set; }  // Данные файла
        public int CurrentPosition { get; set; }  // Текущая позиция записи
    }


    // Статистика подключений
    // Сводная информация о состоянии всех подключений

    public class ConnectionStats
    {
        public int TotalConnections { get; set; }  // Общее количество подключений
        public int ActiveConnections { get; set; }  // Количество активных подключений
        public int PendingMessages { get; set; }  // Количество сообщений в очереди
    }
}

//конец файла, юху!!!