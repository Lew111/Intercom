using System;  // Основные системные классы 
using System.IO;  // Работа с файловой системой
using System.Text;  // Кодировки строк
using System.Text.Json;
using System.Threading.Tasks;  // Асинхронное программирование

namespace network
{
    // Класс Sender отвечает за отправку сообщений, файлов через TCP соединения
    public class Sender
    {
        // Приватное поле для хранения ссылки на менеджер соединений, управляющий всеми активными подключениями
        private ConnectionManager _connectionManager;
        private readonly RelayService _relayService;  
        private readonly RouteManager _routeManager;
        private readonly ChatManager _chatManager;

        // Конструктор класса Sender
        public Sender(ConnectionManager connectionManager, RelayService relayService, RouteManager routeManager, ChatManager chatManager)
        {
            _connectionManager = connectionManager;
            _relayService = relayService;
            _routeManager = routeManager;
            _chatManager = chatManager; 
        }

        // Асинхронный метод для отправки текстового сообщения подключенным клиентам
        public async Task SendMessageAsync(string message, string ipAddress = null)
        {
            // Получаем список всех активных соединений из менеджера соединений
            var activeConnections = _connectionManager.GetActiveConnections();

            // Проверка наличия активных подключений
            if (activeConnections.Count == 0)
            {
                Console.WriteLine("[ERROR] нет активных подключений.");
                return;
            }

            // Валидация входного сообщения
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("[ERROR] сообщение не может быть пустым.");
                return;
            }

            try
            {
                // Вывод заголовка операции и содержимого сообщения для красоты
                Console.WriteLine($"\n=== Отправка сообщения ===");
                Console.WriteLine($"Текст: {message}");

                // Проверяем, указан ли конкретный IP-адрес для отправки
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    // ОТПРАВКА КОНКРЕТНОМУ КЛИЕНТУ
                    var result = await _connectionManager.SendMessageAsync(ipAddress, message);

                    // Проверяем результат операции
                    if (result.Success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Сообщение отправлено на {ipAddress}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"✗ Ошибка при отправке на {ipAddress}: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // ШИРОКОВЕЩАТЕЛЬНАЯ РАССЫЛКА ВСЕМ КЛИЕНТАМ
                    await _connectionManager.BroadcastMessageAsync(message);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Сообщение отправлено всем подключенным клиентам");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка при отправке сообщения: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Отправить сообщение по никнейму (с автоматическим fallback на ретрансляцию)
        /// </summary>
        public async Task<SendResult> SendMessageByNicknameAsync(string targetNick, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return new SendResult { Success = false, ErrorMessage = "Сообщение пустое" };
            }

            SendResult result = null;

            try
            {
                Console.WriteLine($"\n=== Отправка сообщения {targetNick} ===");
                Console.WriteLine($"Текст: {message}");

                // Проверяем, есть ли прямое активное соединение с этим ником
                string directIp = _routeManager.GetIpByNickname(targetNick);
                bool hasDirectConnection = !string.IsNullOrEmpty(directIp) &&
                                           _connectionManager.GetActiveConnections().Contains(directIp);

                // Если есть прямое соединение — отправляем напрямую
                if (hasDirectConnection)
                {
                    Console.WriteLine($"[DIRECT] Отправляем через активное соединение {directIp}");
                    result = await _connectionManager.SendMessageAsync(directIp, message);

                    if (result.Success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Отправлено напрямую");
                        Console.ResetColor();

                        _routeManager.MarkChainAsVerified(targetNick, new List<string> { AppSettings.Nickname, targetNick });
                    }
                }
                else
                {
                    // Пробуем подключиться напрямую
                    Console.WriteLine($"[DIRECT] Пробуем подключиться напрямую...");
                    bool connected = await _connectionManager.ConnectToNicknameAsync(targetNick, AppSettings.DefaultPort, true);

                    if (connected)
                    {
                        var allIps = _routeManager.GetAllIpsByNickname(targetNick);
                        var activeConnections = _connectionManager.GetActiveConnections();
                        
                        foreach (var ip in allIps)
                        {
                            if (activeConnections.Contains(ip))
                            {
                                result = await _connectionManager.SendMessageAsync(ip, message);
                                if (result.Success)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"✓ Отправлено после подключения");
                                    Console.ResetColor();

                                    _routeManager.MarkChainAsVerified(targetNick, new List<string> { AppSettings.Nickname, targetNick });
                                    break;
                                }
                            }
                        }
                    }

                    if (result == null || !result.Success)
                    {
                        Console.WriteLine($"[DIRECT] Прямое соединение не сработало");

                        // Ретрансляция через цепочку
                        Console.WriteLine($"[RELAY] Пробуем ретрансляцию...");

                        var chain = _routeManager.GetBestChain(targetNick);

                        if (chain != null && chain.HopCount >= 2)
                        {
                            Console.WriteLine($"[RELAY] Отправляем через цепочку: {string.Join(" -> ", chain.Path)}");
                            result = await _relayService.SendRelayedMessageAsync(targetNick, message);

                            if (result.Success)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"✓ Отправлено через ретрансляцию ({chain.HopCount} хопов)");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.WriteLine($"[RELAY] Цепочка не сработала: {result.ErrorMessage}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[RELAY] Нет подходящей цепочки");
                        }

                        // Ручная отправка через соседа
                        if (result == null || !result.Success)
                        {
                            Console.WriteLine($"[RELAY] Пробуем отправить через любого доступного соседа...");
                            var neighbors = _routeManager.GetNeighbors(AppSettings.Nickname);

                            foreach (var neighbor in neighbors)
                            {
                                if (neighbor == targetNick) continue;

                                var neighborIps = _routeManager.GetAllIpsByNickname(neighbor);
                                var activeConnections = _connectionManager.GetActiveConnections();

                                foreach (var nip in neighborIps)
                                {
                                    if (!activeConnections.Contains(nip)) continue;

                                    // Проверяем, знает ли сосед целевого клиента
                                    var neighborNeighbors = _routeManager.GetNeighbors(neighbor);
                                    if (!neighborNeighbors.Contains(targetNick))
                                    {
                                        // Пробуем через цепочку от соседа
                                        var neighborChain = _routeManager.GetBestChain(targetNick);
                                        if (neighborChain == null || !neighborChain.Path.Contains(neighbor)) continue;
                                    }

                                    Console.WriteLine($"[RELAY] Отправляем через соседа {neighbor}...");

                                    var payload = new RelayPayload
                                    {
                                        SourceNick = AppSettings.Nickname,
                                        TargetNick = targetNick,
                                        MessageId = Guid.NewGuid().ToString(),
                                        OriginalMessage = message,
                                        Path = new List<string> { AppSettings.Nickname, neighbor, targetNick },
                                        CurrentHop = 0,
                                        MaxHops = 5,
                                        Timestamp = DateTime.UtcNow
                                    };

                                    var relayMsg = $"RELAY_MSG:{JsonSerializer.Serialize(payload)}<END>";
                                    bool sent = await _connectionManager.SendRawMessageAsync(nip, relayMsg);

                                    if (sent)
                                    {
                                        Console.WriteLine($"[RELAY] Сообщение отправлено через {neighbor}, ожидаем...");

                                        // Ждем подтверждения 15 секунд
                                        await Task.Delay(15000);

                                        result = new SendResult
                                        {
                                            Success = true,
                                            Message = $"Отправлено через {neighbor}",
                                            MessageId = payload.MessageId
                                        };
                                        break;
                                    }
                                }
                                
                                if (result != null && result.Success) break;
                            }
                        }
                    }
                }

                if (result == null || !result.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ Не удалось отправить: все способы исчерпаны");
                    Console.ResetColor();

                    result = new SendResult { Success = false, ErrorMessage = "Все способы отправки исчерпаны" };
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка: {ex.Message}");
                Console.ResetColor();
                result = new SendResult { Success = false, ErrorMessage = ex.Message };
            }

            // ✅ ЕДИНСТВЕННОЕ МЕСТО СОХРАНЕНИЯ — ЗДЕСЬ
            if (result.Success)
            {
                await _chatManager.SaveMessageAsync(
                    AppSettings.Nickname,
                    targetNick,
                    message,
                    DateTime.Now
                );

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[{DateTime.Now:HH:mm}] Я -> {targetNick}: {message}");
                Console.ResetColor();
            }

            return result;
        }

        public async Task SendFileAsync(string filePath, string ipAddress = null)
        {
            // Получаем список всех активных соединений
            var activeConnections = _connectionManager.GetActiveConnections();

            // Проверка наличия активных подключений
            if (activeConnections.Count == 0)
            {
                Console.WriteLine("Ошибка: нет активных подключений.");
                return;
            }

            try
            {
                // Проверяем существование файла по указанному пути
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Ошибка: файл не найден: {filePath}");
                    return;
                }

                // Получаем информацию о файле
                FileInfo fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                long fileSize = fileInfo.Length;

                // Вывод информации о файле
                Console.WriteLine($"\n=== Отправка файла ===");
                Console.WriteLine($"Имя файла: {fileName}");
                Console.WriteLine($"Размер: {fileSize} байт");
                Console.WriteLine($"Путь: {filePath}");

                // Проверяем, указан ли конкретный IP-адрес для отправки
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    // ОТПРАВКА ФАЙЛА КОНКРЕТНОМУ КЛИЕНТУ
                    var result = await _connectionManager.SendFileAsync(ipAddress, filePath);

                    if (result.Success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Файл '{fileName}' отправлен на {ipAddress}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"✗ Ошибка при отправке файла: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // ШИРОКОВЕЩАТЕЛЬНАЯ РАССЫЛКА ФАЙЛА ВСЕМ КЛИЕНТАМ
                    int sentCount = 0;
                    int failedCount = 0;

                    foreach (var ip in activeConnections)
                    {
                        try
                        {
                            var result = await _connectionManager.SendFileAsync(ip, filePath);
                            if (result.Success)
                                sentCount++;
                            else
                                failedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка отправки на {ip}: {ex.Message}");
                            failedCount++;
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Файл отправлен. Успешно: {sentCount}, Не удалось: {failedCount}");
                    Console.ResetColor();
                }
            }
            catch (IOException ioEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка ввода-вывода: {ioEx.Message}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка при отправке файла: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Асинхронный метод для отправки команд подключенным клиентам
        public async Task SendCommandAsync(string command, string ipAddress = null)
        {
            // Получаем список всех активных соединений
            var activeConnections = _connectionManager.GetActiveConnections();

            // Проверка наличия активных подключений
            if (activeConnections.Count == 0)
            {
                Console.WriteLine("Ошибка: нет активных подключений.");
                return;
            }

            try
            {
                // Вывод информации о команде
                Console.WriteLine($"\n=== Отправка команды ===");
                Console.WriteLine($"Команда: {command}");

                // Форматируем команду, добавляя префикс "CMD:" для идентификации
                string formattedCommand = $"CMD:{command}";

                // Проверяем, указан ли конкретный IP-адрес для отправки
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    // ОТПРАВКА КОМАНДЫ КОНКРЕТНОМУ КЛИЕНТУ
                    var result = await _connectionManager.SendMessageAsync(ipAddress, formattedCommand);

                    if (result.Success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Команда отправлена на {ipAddress}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"✗ Ошибка при отправке команды: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // ШИРОКОВЕЩАТЕЛЬНАЯ РАССЫЛКА КОМАНДЫ ВСЕМ КЛИЕНТАМ
                    await _connectionManager.BroadcastMessageAsync(formattedCommand);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Команда отправлена всем подключенным клиентам");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка при отправке команды: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Метод для отображения интерактивного меню отправки
        public async Task ShowSenderMenuAsync()
        {
            // Бесконечный цикл для отображения меню до выбора опции "Назад"
            while (true)
            {
                // Получаем текущий список активных соединений
                var activeConnections = _connectionManager.GetActiveConnections();

                // Вывод заголовка меню и статистики подключений
                Console.WriteLine("\n=== Меню отправки ===");
                Console.WriteLine($"Активных подключений: {activeConnections.Count}");

                // Если есть активные подключения, выводим их список
                if (activeConnections.Count > 0)
                {
                    Console.WriteLine("Подключенные клиенты:");
                    foreach (var ip in activeConnections)
                    {
                        Console.WriteLine($"  {ip}");
                    }
                }

                // Вывод доступных опций меню
                Console.WriteLine("\n1. Отправить текстовое сообщение");
                Console.WriteLine("2. Отправить файл");
                Console.WriteLine("3. Отправить команду");
                Console.WriteLine("4. Проверить соединения");
                Console.WriteLine("5. Назад в главное меню");
                Console.Write("Выберите действие: ");

                // Чтение выбора пользователя
                string choice = Console.ReadLine();

                // Обработка выбора пользователя с помощью оператора switch
                switch (choice)
                {
                    case "1":  // Отправка текстового сообщения
                        Console.Write("Введите текст сообщения: ");
                        string message = Console.ReadLine();

                        Console.Write("Введите IP адрес (или оставьте пустым для рассылки всем): ");
                        string messageIp = Console.ReadLine();

                        await SendMessageAsync(
                            message,
                            string.IsNullOrWhiteSpace(messageIp) ? null : messageIp
                        );
                        break;

                    case "2":  // Отправка файла
                        Console.Write("Введите полный путь к файлу: ");
                        string filePath = Console.ReadLine();

                        Console.Write("Введите IP адрес (или оставьте пустым для рассылки всем): ");
                        string fileIp = Console.ReadLine();

                        await SendFileAsync(
                            filePath,
                            string.IsNullOrWhiteSpace(fileIp) ? null : fileIp
                        );
                        break;

                    case "3":  // Отправка команды
                        Console.Write("Введите команду: ");
                        string command = Console.ReadLine();

                        Console.Write("Введите IP адрес (или оставьте пустым для рассылки всем): ");
                        string commandIp = Console.ReadLine();

                        await SendCommandAsync(
                            command,
                            string.IsNullOrWhiteSpace(commandIp) ? null : commandIp
                        );
                        break;

                    case "4":  // Проверка соединений
                        // Получение статистики соединений из менеджера соединений
                        var stats = _connectionManager.GetConnectionStats();

                        // Вывод статистики соединений
                        Console.WriteLine($"=== СТАТУС СОЕДИНЕНИЙ ===");
                        Console.WriteLine($"Всего подключений: {stats.TotalConnections}");
                        Console.WriteLine($"Активных: {stats.ActiveConnections}");
                        Console.WriteLine($"Ожидающих сообщений: {stats.PendingMessages}");

                        // Если есть активные подключения, выводим детальную информацию
                        if (activeConnections.Count > 0)
                        {
                            Console.WriteLine("\nДетали подключений:");
                            foreach (var ip in activeConnections)
                            {
                                Console.WriteLine(_connectionManager.GetConnectionInfo(ip));
                            }
                        }
                        break;

                    case "5":  // Возврат в главное меню
                        return;

                    default:  // Обработка неверного выбора
                        Console.WriteLine("Неверный выбор.");
                        break;
                }
            }
        }
    }
}