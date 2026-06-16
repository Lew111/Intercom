using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using network;
using wr;  // Пространство имен для WriterIP

namespace network
{
    // Обработчик команд пользовательского интерфейса
    // Центральный класс для обработки всех команд, вводимых пользователем в интерактивном режиме
    // Интегрирует все компоненты системы (сканирование, подключения, чаты и т.д.) в единый интерфейс командной строки
    // Обеспечивает полное управление системой через текстовые команды
    public class CommandHandler
    {
        // Зависимости (Dependency Injection) - все основные компоненты системы:
        private readonly WriterIP _writer;  // Компонент для работы с файлами (запись/чтение IP-адресов)
        private readonly ChatManager _chatManager;  // Менеджер чатов для работы с историей сообщений
        private readonly ConnectionManager _connectionManager;  // Менеджер сетевых подключений
        private readonly Detector _detector;  // Детектор для сканирования сети и работы с интерфейсами
        private readonly BackgroundService _backgroundService;  // Фоновые службы (автосканирование и т.д.)
        private readonly NicknameManager _nicknameManager;  // Менеджер для работы с никами (отображение IP -> никнейм)
        private readonly ChatSessionManager _chatSessionManager;  // Менеджер активных чат-сессий
        private readonly AutoDiscoveryService _autoDiscoveryService;  // Сервис автоматического обнаружения клиентов
        private readonly RelayService _relayService;
        private readonly RouteManager _routeManager;
        private readonly Sender _sender;

        // Конструктор класса CommandHandler
        // Принимает все зависимости через Dependency Injection для полного доступа к функциональности системы
        // writer - компонент для записи найденных IP-адресов в файл
        // chatManager - менеджер для работы с историей чатов и локальными сообщениями
        // connectionManager - менеджер для установки и управления сетевыми подключениями
        // detector - детектор для сканирования сети и работы с сетевыми интерфейсами
        // backgroundService - сервис фоновых задач (автосканирование)
        // nicknameManager - менеджер для работы с никами пользователей
        // chatSessionManager - менеджер для управления активными чат-сессиями
        // autoDiscoveryService - сервис автоматического обнаружения клиентов в сети
        public CommandHandler(
            WriterIP writer,
            ChatManager chatManager,
            ConnectionManager connectionManager,
            Detector detector,
            BackgroundService backgroundService,
            NicknameManager nicknameManager,
            ChatSessionManager chatSessionManager,
            AutoDiscoveryService autoDiscoveryService,
            RelayService relayService,
            RouteManager routeManager,
            Sender sender)
        {
            _writer = writer;
            _chatManager = chatManager;
            _connectionManager = connectionManager;
            _detector = detector;
            _backgroundService = backgroundService;
            _nicknameManager = nicknameManager;
            _chatSessionManager = chatSessionManager;
            _autoDiscoveryService = autoDiscoveryService;
            _relayService = relayService;
            _routeManager = routeManager;
            _sender = sender;
            
        }

        // ==================== ОСНОВНОЙ МЕТОД ОБРАБОТКИ КОМАНД ====================

        // Обработать команду пользователя
        // command - строка с командой и параметрами (например: "scan", "connect 192.168.1.10")
        // Возвращает строку с результатом выполнения команды для вывода пользователю
        // Маршрутизирует команду к соответствующему обработчику и обрабатывает ошибки
        public async Task<string> HandleCommand(string command)
        {
            // Проверка на пустую команду
            if (string.IsNullOrWhiteSpace(command))
                return "[ERROR] Пустая команда"; // [LOG]

            // Разбиваем команду на части: команда и аргументы
            string[] parts = command.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLower();  // Основная команда (в нижнем регистре)
            string args = parts.Length > 1 ? parts[1] : "";  // Аргументы команды

            try
            {
                // Маршрутизация по командам
                switch (cmd)
                {
                    // Команды для работы с WiFi:
                    case "wifiscan":  // Сканирование WiFi сети
                        return await HandleWifiScan();
                    case "wifilist":  // Список WiFi интерфейсов
                        return HandleWifiList();
                    case "wifisettings":  // Настройки WiFi
                        return await HandleWifiSettings(args);
                    case "interfaceinfo":  // Информация об интерфейсах
                        return HandleInterfaceInfo();
                    //case "forcewifi":  // Принудительный режим WiFi
                    //    return await HandleForceWifi(args);

                    // Команды для локальных сообщений и заметок:
                    case "note":  // Сохранение заметок
                        return await HandleNote(args);
                    case "save":  // Сохранение локальных сообщений
                        return await HandleSave(args);
                    case "local":  // Управление локальными данными
                        return await HandleLocal(args);
                    case "connectroutes":
                        await _backgroundService.TryConnectToRoutesAsync();
                        return "[ROUTE] Попытка подключения ко всем маршрутам";

                    // Основные команды системы:
                    case "help":  // Справка по командам
                        return GetHelpText();
                    case "scan":  // Сканирование сети
                        return await HandleScan(args);
                    case "autoscan":  // Автосканирование
                        return await HandleAutoScan(args);
                    case "autodiscovery":  // Автообнаружение клиентов
                        return await HandleAutoDiscovery(args);
                    case "connect":  // Подключение к клиенту
                        return await HandleConnect(args);
                    case "autoconnect":  // Автоподключение ко всем
                        return await HandleAutoConnect();
                    case "disconnectall":  // Отключение от всех
                        return await HandleDisconnectAll();
                    case "map":
                        return await HandleMap(args);

                    // Команды для работы с сообщениями:
                    case "send":  // Отправка сообщения
                        return await HandleSendByNickname(args);
                    case "broadcast":  // Широковещательная рассылка
                        return await HandleBroadcast(args);
                    case "sendfile":  // Отправка файла
                        return await HandleSendFile(args);
                    case "broadcastroutes":
                        await _backgroundService.BroadcastRoutesToNeighborsAsync();
                        return "[ROUTE] Маршруты разосланы соседям";
    

                    // Команды для работы с чатами:
                    case "chats":  // Список чатов
                        return await HandleChats();
                    case "history":  // История чата
                        return await HandleHistory(args);
                    case "chat":  // Управление чатами
                        return await HandleChat(args);
                    case "chatdates":  // Даты с сообщениями в чате
                    case "dates":
                        return await HandleChatDates(args);

                    // Информационные команды:
                    case "connections":  // Активные подключения
                        return await HandleConnections();
                    case "stats":  // Статистика системы
                        return HandleStats();
                    case "failed":  // Неудавшиеся сообщения
                        return await HandleFailedMessages();
                    case "status":  // Статус системы
                        return HandleStatus();
                    case "interface":  // Активный интерфейс
                        return HandleInterface();
                    case "testinterface":  // Тест интерфейса
                        return HandleTestInterface();
                    case "route":
                        return await HandleRoute(args);
                    case "routeclear":
                        _routeManager.ClearDynamicRoutes();
                        return "[ROUTE] Все динамические маршруты удалены";
                    case "chains":
                        return await HandleChains(args);
                    case "verifychain":
                        return await HandleVerifyChain(args);

                    // Команды управления системой:
                    case "settings":  // Настройки приложения
                        return await HandleSettings(args);
                    case "startservices":  // Запуск служб
                        return HandleStartServices();
                    case "stopservices":  // Остановка служб
                        return HandleStopServices();
                    case "nickname":  // Управление никнеймом
                        return await HandleNickname(args);

                    // Команды обслуживания:
                    case "clear":  // Очистка данных
                        return await HandleClear(args);
                    case "exit":  // Выход из приложения
                        return await HandleExit();

                    default:  // Неизвестная команда
                        return $"[ERROR] Неизвестная команда: {cmd}\nВведите 'help' для списка команд"; // [LOG]
                }
            }
            catch (Exception ex)
            {
                // Обработка исключений при выполнении команд
                return $"[ERROR] {ex.Message}"; // [LOG]
            }
        }

        // ==================== СПРАВОЧНАЯ ИНФОРМАЦИЯ ====================

        // Получить текст справки по всем командам
        // Возвращает форматированную строку с описанием всех доступных команд и примеров использования
        private string GetHelpText()
        {
            return @"
[HELP] === КОМАНДЫ ===

ОСНОВНЫЕ:
  help                    - Эта справка
  exit                    - Выход
  status                  - Статус системы
  settings [ключ] [знач] - Настройки

ЛОКАЛЬНЫЕ СООБЩЕНИЯ (без отправки):
  note [текст]           - Сохранить заметку (без отправки)
  note [текст+категория] - Сохранить заметку в категорию
  note [текст] [категория] - То же самое
  save [текст]           - Сохранить сообщение в локальный диалог
  save [диалог] [текст]  - Сохранить в указанный диалог
  local                  - Управление локальными данными
  local list             - Список всех локальных данных
  local dialogs          - Список локальных диалогов
  local categories       - Список категорий заметок
  local history [диалог] - История локального диалога
  local notes [категория] - Заметки категории

ЧАТЫ:
  chat switch [ник]      - Переключиться на чат
  chat create [ник]      - Создать новый чат
  chat list              - Список всех чатов
  chat current           - Текущий активный чат
  chat close [ник]       - Закрыть чат
  chat dates [ник]       - Показать даты с сообщениями
  send [ник] [текст]     - Отправить сообщение (сеть)
  send [текст]           - Отправить в активный чат (сеть)
  broadcast [текст]      - Отправить всем (сеть)
  sendfile [ник] [путь]  - Отправить файл (сеть)

ИСТОРИЯ:
  history [ник]          - История за сегодня/последний день
  history [ник] [дата]   - История за конкретную дату (YYYY-MM-DD)
  history [ник] range [начало] [конец] - История за диапазон дат

СЕТЬ:
  [HELP]
  === КОМАНДЫ ===
ДИАГНОСТИКА:
  testinterface          - Проверить определение интерфейса...
  scan [quiet]           - Сканировать сеть
  autoscan [сек/off]     - Автосканирование
  autodiscovery [cmd]    - Управление автообнаружением
  connect [IP/ник]       - Подключиться
  autoconnect            - Подключиться ко всем
  connections            - Активные подключения
  interface              - Активный интерфейсmap
  map [set/list]          - Управление маппингом IP->ник

АВТООБНАРУЖЕНИЕ:
  autodiscovery start    - Запустить автообнаружение
  autodiscovery stop     - Остановить автообнаружение
  autodiscovery status   - Статус автообнаружения
  autodiscovery list     - Список обнаруженных клиентов

ПОЛЬЗОВАТЕЛЬ:
  nickname [set/list]    - Управление ником
  chats                  - Список чатов
  history [ник]          - История чата

СЛУЖБЫ:
  startservices          - Запустить службы
  stopservices           - Остановить службы

УПРАВЛЕИЕ МАРШРУТИЗАЦИЕЙ:
  route add <никнейм_сервера> <IP_сервера> - Добавить статический маршрут
  route remove <никнейм>                   - Удалить статический маршрут
  routeclear                               - Очитить динамические маршруты

Примеры:
  chat switch bob
  send Привет!
  send alice Как дела?
  autoscan 120
  autodiscovery start
  connect 192.168.1.100
  nickname set myuser
";
        }

        // ==================== КОМАНДЫ ДИАГНОСТИКИ И ТЕСТИРОВАНИЯ ====================

        // Протестировать определение активного интерфейса
        // Возвращает детальную информацию о текущем активном интерфейсе и всех доступных интерфейсах
        // Полезно для диагностики проблем с сетевыми интерфейсами
        private async Task<string> HandleMap(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[MAP] Используйте: map set <ник> <IP> или map list";

            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string sub = parts[0].ToLower();

            switch (sub)
            {
                case "set":
                    if (parts.Length < 3)
                        return "[ERROR] Укажите ник и IP: map set <ник> <IP>";
                    string nick = parts[1];
                    string ip = parts[2];
                    _nicknameManager.UpdateMapping(ip, nick);
                    return $"[MAP] Установлено: {nick} -> {ip}";

                case "list":
                    return _nicknameManager.GetAllMappings();

                default:
                    return "[ERROR] Неизвестная подкоманда. Используйте set или list";
            }
        }

        private string HandleTestInterface()
        {
            var interfaceInfo = _detector.DetectActiveInterface();

            // Проверка успешности определения интерфейса
            if (interfaceInfo.Address == null)
            {
                return "[ERROR] Не удалось определить активный интерфейс"; // [LOG]
            }

            var sb = new StringBuilder();
            sb.AppendLine("[INTERFACE] Информация об интерфейсе:");
            sb.AppendLine($"[INTERFACE] Имя: {interfaceInfo.InterfaceName}");
            sb.AppendLine($"[INTERFACE] Тип: {interfaceInfo.InterfaceType}");
            sb.AppendLine($"[INTERFACE] IP: {interfaceInfo.Address}");
            sb.AppendLine($"[INTERFACE] Базовый IP: {interfaceInfo.BaseIp}");

            // Получаем список всех сетевых интерфейсов для дополнительной информации
            try
            {
                var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                sb.AppendLine("\n[INTERFACE] Все интерфейсы:");

                foreach (var ni in allInterfaces)
                {
                    // Отображаем только активные интерфейсы
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        var ipProps = ni.GetIPProperties();
                        var ipv4 = ipProps.UnicastAddresses
                            .FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork);

                        if (ipv4 != null)
                        {
                            sb.AppendLine($"  • {ni.Name} ({ni.NetworkInterfaceType}): {ipv4.Address}");
                        }
                    }
                }
            }
            catch { }  // Игнорируем ошибки при получении списка интерфейсов

            return sb.ToString();
        }

        // ==================== КОМАНДЫ АВТООБНАРУЖЕНИЯ КЛИЕНТОВ ====================

        // Обработать команды автообнаружения клиентов
        // args - аргументы команды (start, stop, list, status)
        // Управляет сервисом автоматического обнаружения клиентов в сети
        private async Task<string> HandleAutoDiscovery(string args)
        {
            // Если аргументы не указаны - показываем текущий статус
            if (string.IsNullOrEmpty(args))
            {
                var stats = _autoDiscoveryService.GetDiscoveryStats();
                return $"[AUTO DISCOVERY] Статус: {(_autoDiscoveryService.IsRunning() ? "Запущено" : "Остановлено")}\n" +
                       $"[AUTO DISCOVERY] Обнаружено клиентов: {stats.TotalDiscovered}\n" +
                       $"[AUTO DISCOVERY] Активных за час: {stats.ActiveInLastHour}\n" +
                       $"[AUTO DISCOVERY] Последнее сканирование: {stats.LastScanTime:HH:mm:ss}";
            }

            // Разбираем аргументы команды
            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string subCommand = parts[0].ToLower();

            // Обработка подкоманд автообнаружения
            switch (subCommand)
            {
                case "start":  // Запуск автообнаружения
                    int interval = 30;  // Интервал по умолчанию
                    if (parts.Length > 1 && int.TryParse(parts[1], out int customInterval))
                    {
                        interval = customInterval;  // Использовать указанный интервал
                    }
                    _autoDiscoveryService.StartContinuousScan(interval);
                    return $"[AUTO DISCOVERY] Автообнаружение запущено (интервал: {interval} сек)";

                case "stop":  // Остановка автообнаружения
                    _autoDiscoveryService.StopContinuousScan();
                    return "[AUTO DISCOVERY] Автообнаружение остановлено";

                case "list":  // Список обнаруженных клиентов
                    var clients = _autoDiscoveryService.GetAllDiscoveredClients();
                    if (clients.Count == 0)
                        return "[AUTO DISCOVERY] Нет обнаруженных клиентов";

                    var sb = new StringBuilder();
                    sb.AppendLine("[AUTO DISCOVERY] Обнаруженные клиенты:");
                    foreach (var client in clients.OrderBy(c => c.Nickname))
                    {
                        string timeAgo = GetTimeAgo(client.LastSeen);
                        sb.AppendLine($"  • {client.Nickname} ({client.IpAddress}) - {timeAgo} (пинг: {client.ResponseTime}ms)");
                    }
                    return sb.ToString();

                case "status":  // Статус автообнаружения
                    var statusStats = _autoDiscoveryService.GetDiscoveryStats();
                    return $"[AUTO DISCOVERY] Статус: {(_autoDiscoveryService.IsRunning() ? "Запущено" : "Остановлено")}\n" +
                           $"[AUTO DISCOVERY] Обнаружено: {statusStats.TotalDiscovered} клиентов\n" +
                           $"[AUTO DISCOVERY] Активных за час: {statusStats.ActiveInLastHour}";

                default:  // Неизвестная подкоманда
                    return "[ERROR] Неизвестная подкоманда. Используйте: start, stop, list, status";
            }
        }
        private async Task<string> HandleChains(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                // Показываем все цепочки
                var allChains = _routeManager.GetAllChains();
                var verifiedChains = _routeManager.GetAllVerifiedChains();

                var sb = new StringBuilder();
                sb.AppendLine("[CHAINS] === ВСЕ ЦЕПОЧКИ ===");

                foreach (var kv in allChains.OrderBy(c => c.Key))
                {
                    sb.AppendLine($"\nДо {kv.Key}:");
                    foreach (var chain in kv.Value.Take(3))
                    {
                        string verified = chain.IsVerified ? "✅" : "⏳";
                        string nat = chain.ChainType == ChainType.NatRestricted ? "⚠️NAT" : "";
                        sb.AppendLine($"  {verified} {string.Join(" -> ", chain.Path)} " +
                                    $"(успехов: {chain.SuccessCount}, {nat})");
                    }
                }

                sb.AppendLine("\n[CHAINS] === ПРОВЕРЕННЫЕ ЦЕПОЧКИ ===");
                foreach (var kv in verifiedChains.OrderBy(c => c.Key))
                {
                    var best = kv.Value.OrderByDescending(c => c.SuccessCount).First();
                    sb.AppendLine($"  {kv.Key}: {string.Join(" -> ", best.Path)} " +
                                $"(успехов: {best.SuccessCount})");
                }

                return sb.ToString();
            }

            // Подкоманды: rebuild, clear
            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string sub = parts[0].ToLower();

            switch (sub)
            {
                case "rebuild":
                    _routeManager.RebuildAllChains();
                    return "[CHAINS] Цепочки перестроены";

                case "clear":
                    // Очищаем неверифицированные цепочки
                    return "[CHAINS] Очистка выполнена";

                default:
                    return "[ERROR] Используйте: chains [rebuild|clear]";
            }
        }

        private async Task<string> HandleVerifyChain(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите целевой ник";

            // Проверяем лучшую цепочку до указанного клиента
            var chain = _routeManager.GetBestChain(args);

            if (chain == null)
                return $"[ERROR] Нет цепочки до {args}";

            // Отправляем тестовое сообщение
            string testMsg = $"[SYSTEM] Проверка цепочки: {string.Join(" -> ", chain.Path)}";

            var result = await _relayService.SendRelayedMessageAsync(args, testMsg);

            if (result.Success)
            {
                return $"[SUCCESS] Цепочка до {args} работает: {string.Join(" -> ", chain.Path)}";
            }
            else
            {
                return $"[ERROR] Цепочка не работает: {result.ErrorMessage}";
            }
        }

        // Получить читаемое время, прошедшее с указанной даты
        // lastSeen - дата последнего обнаружения
        // Возвращает строку в формате "только что", "5 мин назад", "2 ч назад" и т.д.
        private string GetTimeAgo(DateTime lastSeen)
        {
            var timeSpan = DateTime.Now - lastSeen;

            if (timeSpan.TotalMinutes < 1)
                return "только что";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} мин назад";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} ч назад";

            return $"{(int)timeSpan.TotalDays} д назад";
        }

        // ==================== КОМАНДЫ СКАНИРОВАНИЯ СЕТИ ====================

        // Обработать команду сканирования сети
        // args - аргументы команды (может содержать "quiet" для тихого режима)
        // Запускает сканирование сети через детектор и возвращает результаты
        private async Task<string> HandleScan(string args)
        {
            bool quiet = args.ToLower().Contains("quiet");  // Определяем режим сканирования

            Console.WriteLine($"[SCAN] Запуск сканирования ({(quiet ? "тихий режим" : "обычный режим")})...");

            var result = await _detector.ScanNetworkAsync(quiet);  // Выполняем сканирование

            if (!result.Success)  // Проверяем успешность сканирования
                return $"[ERROR] Сканирование не удалось: {result.Error}";

            var sb = new StringBuilder();
            sb.AppendLine($"[SCAN] СКАНИРОВАНИЕ ЗАВЕРШЕНО");
            sb.AppendLine($"[SCAN] Интерфейс: {result.InterfaceName}");
            sb.AppendLine($"[SCAN] Найдено IP: {result.FoundIps?.Count ?? 0}");
            sb.AppendLine($"[SCAN] Длительность: {(result.EndTime - result.StartTime).TotalSeconds:F2} сек");

            // В не-тихом режиме выводим список найденных IP
            if (!quiet && result.FoundIps?.Count > 0)
            {
                sb.AppendLine("[SCAN] Найденные клиенты:");
                foreach (var ip in result.FoundIps)
                {
                    sb.AppendLine($"  • {ip}");
                }
            }

            return sb.ToString();
        }

        // Обработать команду автосканирования
        // args - аргументы команды (интервал в секундах или "off")
        // Управляет автоматическим периодическим сканированием сети
        private async Task<string> HandleAutoScan(string args)
        {
            // Если аргументы не указаны - показываем текущий статус
            if (string.IsNullOrEmpty(args))
            {
                var autoScanStatus = _backgroundService.GetAutoScanStatus();
                string status = autoScanStatus.Enabled ?
                    $"Включено, интервал: {autoScanStatus.Interval} секунд" :
                    "Выключено";
                return $"[SYSTEM] Автосканирование: {status}";
            }

            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string param = parts[0].ToLower();

            // Обработка команды отключения автосканирования
            if (param == "off" || param == "stop" || param == "disable")
            {
                _backgroundService.StopAutoScanning();
                AppSettings.AutoScanInterval = 0;
                return "[SYSTEM] Автосканирование отключено";
            }

            // Обработка установки интервала автосканирования
            if (int.TryParse(param, out int seconds) && seconds > 0)
            {
                if (seconds < 10 && seconds > 0)
                {
                    return "[ERROR] Минимальный интервал: 10 секунд";  // Защита от слишком частого сканирования
                }

                AppSettings.AutoScanInterval = seconds;  // Сохраняем настройку
                _backgroundService.UpdateAutoScanInterval(seconds);  // Обновляем сервис
                return $"[SYSTEM] Автосканирование: каждые {seconds} секунд";
            }

            return "[ERROR] Укажите интервал в секундах (мин. 10) или 'off' для отключения";
        }

        // ==================== КОМАНДЫ ПОДКЛЮЧЕНИЯ К КЛИЕНТАМ ====================

        // Обработать команду подключения к клиенту
        // args - аргументы команды (IP или никнейм клиента, опционально порт)
        // Устанавливает TCP-подключение к указанному клиенту
        private async Task<string> HandleConnect(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите IP или ник для подключения";

            // Разбираем аргументы: цель подключения и порт
            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string target = parts[0];  // Цель подключения (IP или ник)
            int port = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : AppSettings.DefaultPort;

            string ip;  // IP-адрес для подключения

            // Определяем, указан ли IP или никнейм
            if (System.Net.IPAddress.TryParse(target, out _))
            {
                ip = target;  // Указан IP-адрес
            }
            else
            {
                // Указан никнейм - ищем соответствующий IP
                ip = _nicknameManager.GetIpByNickname(target);
                if (string.IsNullOrEmpty(ip))
                {
                    return $"[ERROR] Не найден IP для {target}. Используйте scan для поиска";
                }
            }

            Console.WriteLine($"[CONNECT] Подключение к {target} ({ip}:{port})...");

            // Пытаемся установить подключение
            bool success = await _connectionManager.ConnectAsync(ip, port, false);

            if (success)
            {
                // При успешном подключении создаем/открываем чат
                _chatSessionManager.CreateOrOpenChat(target);
                _chatSessionManager.SwitchToChat(target);

                // Обновляем отображение никнейм -> IP
                _nicknameManager.UpdateMapping(ip, target);

                return $"[SUCCESS] Подключено к {target} ({ip}:{port})";
            }

            return $"[ERROR] Не удалось подключиться к {target} ({ip}:{port})";
        }

        // Обработать команду автоподключения ко всем известным клиентам
        // Подключается ко всем IP-адресам, сохраненным в файле
        private async Task<string> HandleAutoConnect()
        {
            Console.WriteLine("[SYSTEM] Автоматическое подключение...");

            await _connectionManager.AutoConnectToAllAsync();  // Запускаем автоподключение

            var stats = _connectionManager.GetConnectionStats();  // Получаем статистику
            return $"[SYSTEM] Автоподключение завершено. Активных подключений: {stats.ActiveConnections}";
        }

        // Обработать команду отключения от всех клиентов
        // Закрывает все активные сетевые подключения
        private async Task<string> HandleDisconnectAll()
        {
            await _connectionManager.DisconnectAllAsync();
            return "[SYSTEM] Отключено от всех клиентов";
        }

        // ==================== КОМАНДЫ ОТПРАВКИ СООБЩЕНИЙ ====================

        // Обработать команду отправки сообщения
        // args - аргументы команды (получатель и текст сообщения, или только текст для активного чата)
        // Отправляет текстовое сообщение указанному пользователю или в активный чат
        private async Task<string> HandleSendByNickname(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                string activeChat = _chatSessionManager.GetActiveChat();
                if (string.IsNullOrEmpty(activeChat))
                    return "[ERROR] Нет активного чата. Укажите получателя или используйте chat switch";

                return "[ERROR] Укажите сообщение для отправки";
            }

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            // Если указан только текст - отправляем в активный чат
            if (parts.Length == 1)
            {
                string activeChat = _chatSessionManager.GetActiveChat();
                if (string.IsNullOrEmpty(activeChat))
                    return "[ERROR] Нет активного чата. Укажите получателя";

                string message = parts[0];
                return await SendMessageToUser(activeChat, message);
            }

            // Если указаны получатель и сообщение
            string target = parts[0];
            string messageFull = parts[1];

            return await SendMessageToUser(target, messageFull);
        }

        // Отправить сообщение пользователю
        // target - никнейм или IP получателя
        // message - текст сообщения
        // Возвращает результат отправки или сообщение об ошибке
        private async Task<string> SendMessageToUser(string target, string message)
        {
            // Активируем чат сразу
            _chatSessionManager.CreateOrOpenChat(target);
            _chatSessionManager.SwitchToChat(target);
            _chatSessionManager.SendMessage(target, message);

            // Запускаем отправку в фоне, не блокируя ввод
            _ = Task.Run(async () =>
            {
                try
                {
                    SendResult result = await _sender.SendMessageByNicknameAsync(target, message);

                    if (!result.Success)
                    {
                        Console.WriteLine($"[ERROR] Не удалось доставить {target}: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Ошибка отправки {target}: {ex.Message}");
                }
            });

            return ""; // Возвращаемся сразу, не ждём отправку
        }


        // Обработать команду широковещательной рассылки
        // args - текст сообщения для рассылки
        // Отправляет сообщение всем подключенным клиентам
        private async Task<string> HandleBroadcast(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите сообщение";

            Console.WriteLine("[SYSTEM] Широковещательная рассылка...");

            await _connectionManager.BroadcastMessageAsync(args);  // Выполняем рассылку

            return "[SYSTEM] Рассылка запущена";
        }

        // Обработать команду отправки файла
        // args - аргументы команды (получатель и путь к файлу)
        // Отправляет файл указанному пользователю
        private async Task<string> HandleSendFile(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите получателя и путь к файлу";

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return "[ERROR] Укажите получателя и путь к файлу";

            string target = parts[0];
            string filePath = parts[1];

            // Проверяем существование файла
            if (!File.Exists(filePath))
                return $"[ERROR] Файл не найден: {filePath}";

            // Определяем IP-адрес получателя
            string ip = _nicknameManager.GetIpByNickname(target);

            if (string.IsNullOrEmpty(ip))
            {
                if (System.Net.IPAddress.TryParse(target, out _))
                {
                    ip = target;
                }
                else
                {
                    return $"[ERROR] Пользователь '{target}' не найден";
                }
            }

            Console.WriteLine($"[SYSTEM] Отправка файла {target}...");

            // Активируем чат с получателем
            _chatSessionManager.CreateOrOpenChat(target);
            _chatSessionManager.SwitchToChat(target);

            // Отправляем файл
            var result = await _connectionManager.SendFileAsync(ip, filePath);

            if (result.Success)
            {
                _nicknameManager.UpdateMapping(ip, target);
                return $"[SUCCESS] Файл отправлен {target}";
            }
            else
            {
                return $"[ERROR] Не удалось отправить файл: {result.ErrorMessage}";
            }
        }

        // ==================== КОМАНДЫ РАБОТЫ С ЧАТАМИ ====================
        // Реализация обработки команд маршрутов
        private async Task<string> HandleRoute(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ROUTE] Используйте: route add <ник> <ip>  или  route list [или route remove <ник>]";

            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string subCmd = parts[0].ToLower();

            switch (subCmd)
            {
                case "add":
                    if (parts.Length < 3)
                        return "[ERROR] Укажите ник и IP: route add <ник> <ip>";
                    string nickname = parts[1];
                    string ip = parts[2];
                    try
                    {
                        _routeManager.AddStaticRoute(nickname, ip);
                        var activeConnections = _connectionManager.GetActiveConnections();
                        var allRoutes = _routeManager.GetAllRoutes();
                        string routesJson = JsonSerializer.Serialize(allRoutes);
                        string routeUpdate = $"ROUTE_UPDATE:{routesJson}<END>";
                        foreach (var connIp in activeConnections)
                        {
                            await _connectionManager.SendMessageAsync(connIp, routeUpdate);
                        }
                        // Немедленная попытка подключения (если ещё нет соединения)
                        if (!_connectionManager.GetActiveConnections().Contains(ip))
                        {
                            _ = Task.Run(async () =>
                            {
                                bool connected = await _connectionManager.ConnectAsync(ip, AppSettings.DefaultPort, true);
                                if (connected) _chatSessionManager.CreateOrOpenChat(nickname);
                            });
                        }
                        return $"[ROUTE] Маршрут добавлен: {nickname} ({ip})";
                    }
                    catch (Exception ex)
                    {
                        return $"[ERROR] {ex.Message}";
                    }

                case "remove":
                    if (parts.Length < 2)
                        return "[ERROR] Укажите ник для удаления: route remove <ник>";
                    string nickToRemove = parts[1];
                    _routeManager.RemoveStaticRoute(nickToRemove);
                    return $"[ROUTE] Маршрут {nickToRemove} удалён";
                case "chains":
                    var chains = _routeManager.GetAllChains();
                    if (chains.Count == 0)
                        return "[ROUTE] Нет сохранённых цепочек";

                    var sb2 = new StringBuilder();
                    sb2.AppendLine("[ROUTE] === ЦЕПОЧКИ МАРШРУТОВ ===");
                    foreach (var kv in chains)
                    {
                        sb2.AppendLine($"\nДо {kv.Key}:");
                        foreach (var chain in kv.Value.Take(3))
                        {
                            string verified = chain.IsVerified ? "✅" : "⏳";
                            sb2.AppendLine($"  {verified} {string.Join(" -> ", chain.Path)} (подтверждена: {chain.IsVerified})");
                        }
                    }
                    return sb2.ToString();
                case "list":
                    var staticRoutes = _routeManager.GetAllStaticRoutes();
                    var dynamicRoutes = _routeManager.GetAllDynamicRoutes();
                    var links = _routeManager.GetAllLinks();

                    if (staticRoutes.Count == 0 && dynamicRoutes.Count == 0)
                        return "[ROUTE] Нет маршрутов";

                    var sb = new StringBuilder();
                    sb.AppendLine("[ROUTE] === СТАТИЧЕСКИЕ МАРШРУТЫ ===");
                    foreach (var r in staticRoutes)
                    {
                        string ipsStr = r.Ips != null && r.Ips.Count > 0 ? string.Join(", ", r.Ips) : "(нет IP)";
                        sb.AppendLine($"  {r.Nickname} -> {ipsStr}");
                    }

                    sb.AppendLine("\n[ROUTE] ДИНАМИЧЕСКИЕ МАРШРУТЫ:");
                    foreach (var r in dynamicRoutes)
                    {
                        string ipsStr = r.Ips != null && r.Ips.Count > 0 ? string.Join(", ", r.Ips) : "(нет IP)";
                        sb.AppendLine($"  {r.Nickname} -> {ipsStr}");
                    }

                    if (links.Count > 0)
                    {
                        sb.AppendLine("\n[ROUTE] СВЯЗИ:");
                        foreach (var link in links)
                            sb.AppendLine($"  {link.From} <-> {link.To}");
                    }
                    return sb.ToString();

                default:
                    return "[ERROR] Неизвестная подкоманда. Используйте: add, remove, list";
            }
        }



        // Обработать команду вывода списка чатов
        // Возвращает список всех чатов с количеством файлов в каждом
        private async Task<string> HandleChats()
        {
            var chats = _chatManager.GetChatList();

            if (chats.Count == 0)
                return "[INFO] Чатов нет";

            var sb = new StringBuilder();
            sb.AppendLine("[CHATS] Ваши чаты:");

            foreach (var chat in chats)
            {
                var files = _chatManager.GetChatFiles(chat);
                sb.AppendLine($"  • {chat} (файлов: {files.Count})");
            }

            return sb.ToString();
        }

        // Обработать команду вывода истории чата
        // args - аргументы команды (никнейм и опционально дата или диапазон дат)
        // Возвращает историю сообщений с указанным пользователем
        private async Task<string> HandleHistory(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите ник [и дату в формате YYYY-MM-DD]";

            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string nickname = parts[0];

            DateTime? specificDate = null;

            // Проверяем, указана ли конкретная дата
            if (parts.Length > 1)
            {
                if (DateTime.TryParse(parts[1], out DateTime parsedDate))
                {
                    specificDate = parsedDate;
                }
                else if (parts[1].ToLower() == "range" && parts.Length >= 4)
                {
                    // Обработка диапазона дат: history ник range 2023-01-01 2023-01-31
                    if (DateTime.TryParse(parts[2], out DateTime startDate) &&
                        DateTime.TryParse(parts[3], out DateTime endDate))
                    {
                        string history = await _chatManager.GetChatHistoryRangeAsync(nickname, startDate, endDate);
                        return $"[HISTORY] === ИСТОРИЯ ЧАТА С {nickname} ({startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}) ===\n{history}";
                    }
                }
            }

            // Получаем историю чата
            string historySingle = await _chatManager.GetChatHistoryAsync(nickname, specificDate);

            string dateStr = specificDate?.ToString("yyyy-MM-dd") ?? "последний день";
            if (historySingle.StartsWith("Чат с") && historySingle.Contains("не найден"))
                return $"[INFO] {historySingle}";

            return $"[HISTORY] === ИСТОРИЯ ЧАТА С {nickname} ({dateStr}) ===\n{historySingle}";
        }

        // Обработка команды вывода активных подключений
        // Возвращает список всех активных сетевых подключений
        private async Task<string> HandleConnections()
        {
            var connections = _connectionManager.GetActiveConnections();
            var stats = _connectionManager.GetConnectionStats();

            var sb = new StringBuilder();
            sb.AppendLine("[CONNECTIONS] Активные подключения:");
            sb.AppendLine($"[CONNECTIONS] Всего: {stats.TotalConnections}, Активных: {stats.ActiveConnections}");

            if (connections.Count > 0)
            {
                sb.AppendLine("[CONNECTIONS] Список:");
                foreach (var ip in connections)
                {
                    sb.AppendLine($"  • {_connectionManager.GetConnectionInfo(ip)}");
                }
            }
            else
            {
                sb.AppendLine("[CONNECTIONS] Нет активных подключений");
            }

            return sb.ToString();
        }

        // ==================== ИНФОРМАЦИОННЫЕ КОМАНДЫ ====================

        // Обработать команду вывода статистики системы
        // Возвращает агрегированную статистику по всем компонентам системы
        private string HandleStats()
        {
            // Собираем статистику со всех компонентов
            var connStats = _connectionManager.GetConnectionStats();
            var scanStats = _detector.GetScanStats();
            var autoScanStatus = _backgroundService.GetAutoScanStatus();
            var discoveryStats = _autoDiscoveryService.GetDiscoveryStats();

            var sb = new StringBuilder();
            sb.AppendLine("[STATS] === СТАТИСТИКА СИСТЕМЫ ===");
            sb.AppendLine("[STATS] ПОДКЛЮЧЕНИЯ:");
            sb.AppendLine($"[STATS]   Всего соединений: {connStats.TotalConnections}");
            sb.AppendLine($"[STATS]   Активных: {connStats.ActiveConnections}");
            sb.AppendLine($"[STATS]   Ожидающих сообщений: {connStats.PendingMessages}");

            sb.AppendLine("[STATS] СКАНИРОВАНИЯ:");
            sb.AppendLine($"[STATS]   Всего сканирований: {scanStats.TotalScans}");
            sb.AppendLine($"[STATS]   Успешных: {scanStats.SuccessfulScans}");
            sb.AppendLine($"[STATS]   Среднее найденных IP: {scanStats.AverageFoundIps}");
            sb.AppendLine($"[STATS]   Последнее: {scanStats.LastScanTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"[STATS]   Автосканирование: {(autoScanStatus.Enabled ? $"ВКЛ ({autoScanStatus.Interval} сек)" : "ВЫКЛ")}");

            sb.AppendLine("[STATS] АВТООБНАРУЖЕНИЕ:");
            sb.AppendLine($"[STATS]   Обнаружено клиентов: {discoveryStats.TotalDiscovered}");
            sb.AppendLine($"[STATS]   Активных за час: {discoveryStats.ActiveInLastHour}");
            sb.AppendLine($"[STATS]   Статус: {(_autoDiscoveryService.IsRunning() ? "Запущено" : "Остановлено")}");

            // Дополнительная информация о файлах статистики
            try
            {
                string statsPath = Path.Combine(AppSettings.GetUserDataPath(), "system_stats.csv");
                if (File.Exists(statsPath))
                {
                    var lines = File.ReadAllLines(statsPath);
                    if (lines.Length > 1)
                    {
                        sb.AppendLine($"[STATS] СОБРАНО ЗАПИСЕЙ: {lines.Length - 1}");
                    }
                }
            }
            catch { }  // Игнорируем ошибки при чтении файла статистики

            return sb.ToString();
        }

        // Обработать команду вывода неудавшихся сообщений
        // Возвращает список сообщений, которые не удалось доставить
        private async Task<string> HandleFailedMessages()
        {
            string failedPath = Path.Combine(AppSettings.GetUserDataPath(), "delivery_failures.log");

            if (!File.Exists(failedPath))
                return "[INFO] Нет неудавшихся сообщений";

            var lines = await File.ReadAllLinesAsync(failedPath);

            if (lines.Length == 0)
                return "[INFO] Нет неудавшихся сообщений";

            var sb = new StringBuilder();
            sb.AppendLine("[FAILED] Неудавшиеся сообщения:");
            sb.AppendLine($"[FAILED] Всего: {lines.Length}\n");

            int count = 0;
            foreach (var line in lines.Take(10))  // Показываем только первые 10 сообщений
            {
                count++;
                sb.AppendLine($"[FAILED] {count}. {line}");
            }

            if (lines.Length > 10)
                sb.AppendLine($"[FAILED] ... и еще {lines.Length - 10} сообщений");

            return sb.ToString();
        }

        // Обработать команду управления настройками
        // args - аргументы команды (ключ настройки и новое значение)
        // Позволяет изменять настройки приложения в runtime
        private async Task<string> HandleSettings(string args)
        {
            // Если аргументы не указаны - показываем текущие настройки
            if (string.IsNullOrEmpty(args))
                return AppSettings.GetSettingsSummary();

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return "[ERROR] Укажите ключ и значение";

            string key = parts[0].ToLower();
            string value = parts[1];

            string oldValue = "";  // Для хранения старого значения настройки

            // Обработка различных настроек
            switch (key)
            {
                case "nickname":  // Никнейм пользователя
                    oldValue = AppSettings.Nickname;
                    AppSettings.Nickname = value;
                    break;

                case "port":  // Порт по умолчанию
                    if (int.TryParse(value, out int port) && port > 0 && port < 65536)
                    {
                        oldValue = AppSettings.DefaultPort.ToString();
                        AppSettings.DefaultPort = port;
                    }
                    else
                    {
                        return "[ERROR] Неверный порт (1-65535)";
                    }
                    break;

                case "autosave":  // Автосохранение сообщений
                    if (bool.TryParse(value, out bool autoSave))
                    {
                        oldValue = AppSettings.AutoSaveMessages.ToString();
                        AppSettings.AutoSaveMessages = autoSave;
                    }
                    else
                    {
                        return "[ERROR] Укажите true или false";
                    }
                    break;

                case "autoconnect":  // Автоподключение к обнаруженным клиентам
                    if (bool.TryParse(value, out bool autoConnect))
                    {
                        oldValue = AppSettings.AutoConnectDiscovered.ToString();
                        AppSettings.AutoConnectDiscovered = autoConnect;

                        // Если включили автоподключение, автоматически запускаем автообнаружение
                        if (autoConnect && !_autoDiscoveryService.IsRunning())
                        {
                            _autoDiscoveryService.StartContinuousScan(30);
                        }
                    }
                    else
                    {
                        return "[ERROR] Укажите true или false";
                    }
                    break;

                case "autoscaninterval":  // Интервал автосканирования
                    if (int.TryParse(value, out int interval) && interval >= 0)
                    {
                        oldValue = AppSettings.AutoScanInterval.ToString();
                        AppSettings.AutoScanInterval = interval;
                        _backgroundService.UpdateAutoScanInterval(interval);
                    }
                    else
                    {
                        return "[ERROR] Укажите интервал в секундах (0 для отключения)";
                    }
                    break;

                default:
                    return $"[ERROR] Неизвестная настройка: {key}";
            }

            AppSettings.SaveAllSettings();  // Сохраняем настройки на диск

            return $"[SYSTEM] Настройка '{key}' изменена: {oldValue} → {value}";
        }

        // Обработать команду вывода статуса системы
        // Возвращает текущее состояние всех компонентов системы
        private string HandleStatus()
        {
            var connStats = _connectionManager.GetConnectionStats();
            var interfaceInfo = _detector.DetectActiveInterface();
            var autoScanStatus = _backgroundService.GetAutoScanStatus();
            var discoveryStats = _autoDiscoveryService.GetDiscoveryStats();

            var sb = new StringBuilder();
            sb.AppendLine("[STATUS] === СТАТУС СИСТЕМЫ ===");
            sb.AppendLine($"[STATUS] Пользователь: {AppSettings.Nickname}");

            if (interfaceInfo.Address == null)
                return "[ERROR] Не удалось определить интерфейс";

            sb.AppendLine($"[STATUS] Подключения: {connStats.ActiveConnections} активных из {connStats.TotalConnections}");
            sb.AppendLine($"[STATUS] Ожидающие сообщения: {connStats.PendingMessages}");
            sb.AppendLine($"[STATUS] Время: {DateTime.Now:HH:mm:ss}");
            sb.AppendLine($"[STATUS] Автосохранение: {(AppSettings.AutoSaveMessages ? "ВКЛ" : "ВЫКЛ")}");
            sb.AppendLine($"[STATUS] Автоподключение: {(AppSettings.AutoConnectDiscovered ? "ВКЛ" : "ВЫКЛ")}");
            sb.AppendLine($"[STATUS] Автосканирование: {(autoScanStatus.Enabled ? $"ВКЛ ({autoScanStatus.Interval} сек)" : "ВЫКЛ")}");
            sb.AppendLine($"[STATUS] Автообнаружение: {(_autoDiscoveryService.IsRunning() ? "ВКЛ" : "ВЫКЛ")}");
            sb.AppendLine($"[STATUS] Обнаружено клиентов: {discoveryStats.TotalDiscovered}");

            string activeChat = _chatSessionManager.GetActiveChat();
            if (!string.IsNullOrEmpty(activeChat))
                sb.AppendLine($"[STATUS] Активный чат: {activeChat}");

            return sb.ToString();
        }

        // Преобразовать тип интерфейса в читаемую строку
        // interfaceType - тип интерфейса из System.Net.NetworkInformation
        // Возвращает понятное строковое представление типа интерфейса
        private string GetInterfaceTypeString(object interfaceType)
        {
            if (interfaceType is System.Net.NetworkInformation.NetworkInterfaceType type)
            {
                return type switch
                {
                    System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 => "WiFi",
                    System.Net.NetworkInformation.NetworkInterfaceType.Ethernet => "Ethernet",
                    System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet => "Gigabit Ethernet",
                    System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT => "Fast Ethernet",
                    _ => type.ToString()
                };
            }
            return interfaceType?.ToString() ?? "Unknown";
        }

        // Обработать команду вывода информации об активном интерфейсе
        // Возвращает информацию о текущем активном сетевом интерфейсе
        private string HandleInterface()
        {
            var interfaceInfo = _detector.DetectActiveInterface();

            if (interfaceInfo.Address == null)
                return "[ERROR] Не удалось определить интерфейс";

            string interfaceType = GetInterfaceTypeString(interfaceInfo.InterfaceType);

            return $"[INTERFACE] АКТИВНЫЙ ИНТЕРФЕЙС:\n" +
                   $"[INTERFACE] Имя: {interfaceInfo.InterfaceName}\n" +
                   $"[INTERFACE] Тип: {interfaceType}\n" +
                   $"[INTERFACE] IP: {interfaceInfo.Address}\n" +
                   $"[INTERFACE] Базовый IP: {interfaceInfo.BaseIp}";
        }

        // Обработать команду запуска всех служб
        // Запускает все фоновые службы системы
        private string HandleStartServices()
        {
            _backgroundService.StartAllServices();  // Запуск фоновых служб
            if (AppSettings.AutoConnectDiscovered)
            {
                // Если включено автоподключение, запускаем автообнаружение
                _autoDiscoveryService.StartContinuousScan(30);
            }
            return "[SYSTEM] Все фоновые службы запущены";
        }

        // Обработать команду остановки всех служб
        // Останавливает все фоновые службы системы
        private string HandleStopServices()
        {
            _backgroundService.StopAllServices();  // Остановка фоновых служб
            _autoDiscoveryService.StopContinuousScan();  // Остановка автообнаружения
            return "[SYSTEM] Все фоновые службы остановлены";
        }

        // ==================== КОМАНДЫ УПРАВЛЕНИЯ ПОЛЬЗОВАТЕЛЕМ ====================

        // Обработать команду управления никнеймом
        // args - аргументы команды (set, list, search)
        // Управляет никнеймом текущего пользователя и отображением IP->ник
        private async Task<string> HandleNickname(string args)
        {
            // Если аргументы не указаны - показываем текущий ник
            if (string.IsNullOrEmpty(args))
                return $"[SYSTEM] Текущий ник: {AppSettings.Nickname}";

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string subCommand = parts[0].ToLower();

            switch (subCommand)
            {
                case "set":  // Установка нового ника
                    if (parts.Length < 2)
                        return "[ERROR] Укажите новый ник";

                    string oldNickname = AppSettings.Nickname;
                    string newNickname = parts[1];

                    AppSettings.Nickname = newNickname;
                    return $"[SYSTEM] Ник изменен: {oldNickname} → {newNickname}";

                case "list":  // Список всех сопоставлений IP->ник
                    return _nicknameManager.GetAllMappings();

                case "search":  // Поиск по никнеймам
                    if (parts.Length < 2)
                        return "[ERROR] Укажите запрос для поиска";

                    string searchResults = _nicknameManager.Search(parts[1]);
                    return $"[MAPPINGS] Результаты поиска: {searchResults}";

                default:  // Неизвестная подкоманда
                    return "[ERROR] Неизвестная подкоманда. Используйте: set, list, search";
            }
        }

        // Обработать команду управления чатами
        // args - аргументы команды (switch, create, list, current, close)
        // Управляет активными чат-сессиями
        private async Task<string> HandleChat(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Используйте: chat switch/create/list/current/close";

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string subCommand = parts[0].ToLower();

            switch (subCommand)
            {
                case "switch":  // Переключение на чат с указанным пользователем
                    if (parts.Length < 2)
                        return "[ERROR] Укажите ник для переключения";

                    string chatToSwitch = parts[1];
                    bool switched = _chatSessionManager.SwitchToChat(chatToSwitch);

                    if (switched)
                    {
                        // При переключении показываем историю чата
                        string history = await _chatManager.GetChatHistoryAsync(chatToSwitch);
                        if (!history.Contains("не найден"))
                        {
                            Console.WriteLine($"[HISTORY] === ИСТОРИЯ ЧАТА С {chatToSwitch} ===");
                            Console.WriteLine(history);
                        }
                        return $"[CHAT] Переключен на чат с {chatToSwitch}";
                    }
                    return $"[ERROR] Не удалось переключиться на чат с {chatToSwitch}";

                case "create":  // Создание нового чата
                    if (parts.Length < 2)
                        return "[ERROR] Укажите ник для создания чата";

                    string chatToCreate = parts[1];
                    bool created = _chatSessionManager.CreateOrOpenChat(chatToCreate);

                    if (created)
                    {
                        _chatSessionManager.SwitchToChat(chatToCreate);
                        return $"[CHAT] Создан новый чат с {chatToCreate}";
                    }
                    return $"[ERROR] Не удалось создать чат с {chatToCreate}";

                case "list":  // Список всех открытых чатов
                    var sessions = _chatSessionManager.GetAllSessions();
                    var activeChat = _chatSessionManager.GetActiveChat();

                    if (sessions.Count == 0)
                        return "[CHAT] Нет открытых чатов";

                    var sb = new StringBuilder();
                    sb.AppendLine("[CHAT] Открытые чаты:");

                    foreach (var session in sessions)
                    {
                        string marker = string.Equals(session, activeChat, StringComparison.OrdinalIgnoreCase) ? "✓" : " ";
                        sb.AppendLine($"  [{marker}] {session}");
                    }

                    return sb.ToString();

                case "current":  // Текущий активный чат
                    string current = _chatSessionManager.GetActiveChat();
                    if (string.IsNullOrEmpty(current))
                        return "[CHAT] Нет активного чатa";
                    return $"[CHAT] Текущий активный чат: {current}";

                case "close":  // Закрытие чата
                    if (parts.Length < 2)
                        return "[ERROR] Укажите ник чата для закрытия";

                    string chatToClose = parts[1];
                    bool closed = _chatSessionManager.CloseChat(chatToClose);

                    if (closed)
                        return $"[CHAT] Чат с {chatToClose} закрыт";
                    return $"[ERROR] Не удалось закрыть чат с {chatToClose}";

                default:  // Неизвестная подкоманда
                    return "[ERROR] Неизвестная подкоманда. Используйте: switch/create/list/current/close";
            }
        }

        // ==================== КОМАНДЫ ОБСЛУЖИВАНИЯ ====================

        // Обработать команду очистки данных
        // args - аргументы команды (history, failed, stats)
        // Очищает различные типы данных системы
        private async Task<string> HandleClear(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите что очистить (history, failed, stats)";

            string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string what = parts[0].ToLower();

            switch (what)
            {
                case "history":  // Очистка истории чата
                    if (parts.Length < 2)
                        return "[ERROR] Укажите IP или ник [и дату в формате YYYY-MM-DD] для очистки истории";

                    if (parts.Length >= 3 && DateTime.TryParse(parts[2], out DateTime dateToClear))
                    {
                        // Очистка истории за конкретную дату
                        _chatManager.ClearChatHistory(parts[1], dateToClear);
                        return $"[SYSTEM] История чата с {parts[1]} за {dateToClear:yyyy-MM-dd} очищена";
                    }
                    else
                    {
                        // Очистка всей истории
                        _chatManager.ClearChatHistory(parts[1]);
                        return $"[SYSTEM] Вся история чата с {parts[1]} очищена";
                    }

                case "failed":  // Очистка лога неудавшихся сообщений
                    string failedPath = Path.Combine(AppSettings.GetUserDataPath(), "delivery_failures.log");
                    if (File.Exists(failedPath))
                    {
                        File.Delete(failedPath);
                        return "[SYSTEM] Лог неудавшихся сообщений очищен";
                    }
                    return "[INFO] Лог неудавшихся сообщений уже пуст";

                case "stats":  // Очистка статистики
                    string statsPath = Path.Combine(AppSettings.GetUserDataPath(), "system_stats.csv");
                    if (File.Exists(statsPath))
                    {
                        File.WriteAllText(statsPath,
                            "Timestamp,ActiveConnections,TotalConnections,PendingMessages," +
                            "TotalScans,SuccessfulScans,AverageFoundIps\n");
                        return "[SYSTEM] Статистика очищена";
                    }
                    return "[INFO] Статистика уже пуста";

                default:  // Неизвестный тип данных для очистки
                    return $"[ERROR] Неизвестный объект для очистки: {what}";
            }
        }

        // Обработать команду выхода из приложения
        // Выполняет корректное завершение работы всех компонентов системы
        private async Task<string> HandleExit()
        {
            Console.WriteLine("[SYSTEM] Сохранение настроек...");
            AppSettings.SaveAllSettings();  // Сохраняем настройки на диск

            Console.WriteLine("[SYSTEM] Остановка фоновых служб...");
            _backgroundService.StopAllServices();  // Останавливаем фоновые службы
            _autoDiscoveryService.StopContinuousScan();  // Останавливаем автообнаружение

            Console.WriteLine("[SYSTEM] Отключение от всех клиентов...");
            await _connectionManager.DisconnectAllAsync();  // Закрываем все соединения

            return "[SYSTEM] Система завершает работу";
        }

        // ==================== ИНТЕРАКТИВНЫЙ РЕЖИМ ====================

        // Запустить интерактивный режим командной строки
        // Основной цикл приложения - принимает команды пользователя и выполняет их
        public async Task RunInteractiveMode()
        {
            Console.WriteLine("[SYSTEM] === КОМАНДНЫЙ РЕЖИМ ===");
            Console.WriteLine("[SYSTEM] Введите 'help' для списка команд");
            Console.WriteLine("[SYSTEM] Введите 'exit' для выхода\n");

            _backgroundService.StartAllServices();  // Автоматический запуск служб при старте

            // Если включено автоподключение, запускаем его
            if (AppSettings.AutoConnectDiscovered)
            {
                _autoDiscoveryService.StartContinuousScan(30);
                Console.WriteLine("[SYSTEM] Автоматическое подключение к сохраненным клиентам...");
                await _connectionManager.AutoConnectToAllAsync();
            }

            // Основной цикл обработки команд
            while (true)
            {
                // Формируем приглашение командной строки с указанием активного чата
                string activeChat = _chatSessionManager.GetActiveChat();
                string prompt = string.IsNullOrEmpty(activeChat) ? "> " : $"[{activeChat}]> ";
                Console.Write(prompt);

                // Читаем команду пользователя
                string command = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(command))
                    continue;  // Пустая строка - ожидаем следующую команду

                // Обработка команды выхода
                if (command.ToLower() == "exit")
                {
                    var exitResult = await HandleExit();
                    Console.WriteLine(exitResult);
                    break;  // Завершаем цикл и работу приложения
                }

                // Обрабатываем команду и выводим результат
                string commandResult = await HandleCommand(command);
                if (!string.IsNullOrEmpty(commandResult))
                    Console.WriteLine(commandResult);
            }
        }

        // ==================== КОМАНДЫ РАБОТЫ С ЛОКАЛЬНЫМИ СООБЩЕНИЯМИ ====================

        // Обработать команду вывода дат с сообщениями в чате
        // args - никнейм пользователя
        // Возвращает список дат, в которые были сообщения с указанным пользователем
        private async Task<string> HandleChatDates(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите ник";

            var dates = _chatManager.GetChatDates(args);  // Получаем даты с сообщениями

            if (dates.Count == 0)
                return $"[INFO] Нет сохраненной истории для {args}";

            var sb = new StringBuilder();
            sb.AppendLine($"[DATES] Даты с сообщениями для {args}:");

            // Выводим даты в читаемом формате с русскими названиями дней недели
            foreach (var date in dates.OrderBy(d => d))
            {
                string dateStr = date.ToString("dd.MM.yyyy (dddd)", new System.Globalization.CultureInfo("ru-RU"));
                sb.AppendLine($"  • {date:yyyy-MM-dd} - {dateStr}");
            }

            sb.AppendLine($"[DATES] Всего дней: {dates.Count}");

            return sb.ToString();
        }

        // Обработать команду сохранения заметки
        // args - текст заметки и опционально категория
        // Сохраняет заметку локально (без отправки по сети)
        private async Task<string> HandleNote(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите текст заметки [и категорию через +]";

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 1)
                return "[ERROR] Укажите текст заметки";

            string noteText = parts[0];
            string category = "general";  // Категория по умолчанию

            // Проверяем, есть ли категория в формате "текст+категория"
            if (noteText.Contains('+'))
            {
                string[] noteParts = noteText.Split('+', 2);
                if (noteParts.Length == 2)
                {
                    noteText = noteParts[0];
                    category = noteParts[1];
                }
            }
            // Или категория указана отдельно как второй аргумент
            else if (parts.Length > 1)
            {
                category = parts[1];
            }

            // Сохраняем заметку
            await _chatManager.SaveLocalNoteAsync(noteText, DateTime.Now, category);

            return $"[SYSTEM] Заметка сохранена в категорию '{category}'";
        }

        // Обработать команду сохранения локального сообщения
        // args - текст сообщения или название диалога и текст
        // Сохраняет сообщение в локальный диалог (без отправки по сети)
        private async Task<string> HandleSave(string args)
        {
            if (string.IsNullOrEmpty(args))
                return "[ERROR] Укажите: save [текст] или save [название_диалога] [текст]";

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                // Сохраняем в локальный диалог по умолчанию
                await _chatManager.SaveLocalMessageAsync(AppSettings.Nickname, parts[0], DateTime.Now, "self");
                return $"[SYSTEM] Сообщение сохранено в локальный диалог";
            }
            else
            {
                // Сохраняем в указанный диалог
                await _chatManager.SaveLocalMessageAsync(AppSettings.Nickname, parts[1], DateTime.Now, parts[0]);
                return $"[SYSTEM] Сообщение сохранено в локальный диалог '{parts[0]}'";
            }
        }

        // Обработать команду управления локальными данными
        // args - подкоманда для работы с локальными данными
        // Управляет локальными диалогами и заметками (данные без сетевого взаимодействия)
        private async Task<string> HandleLocal(string args)
        {
            // Если аргументы не указаны - показываем общую информацию
            if (string.IsNullOrEmpty(args))
            {
                var conversations = _chatManager.GetLocalConversations();
                var categories = _chatManager.GetNoteCategories();

                var sb = new StringBuilder();
                sb.AppendLine("[LOCAL] === ЛОКАЛЬНЫЕ ДАННЫЕ ===");

                sb.AppendLine("[LOCAL] Локальные диалоги:");
                if (conversations.Count > 0)
                {
                    foreach (var conv in conversations)
                    {
                        sb.AppendLine($"  • {conv}");
                    }
                }
                else
                {
                    sb.AppendLine("  [нет диалогов]");
                }

                sb.AppendLine("\n[LOCAL] Категории заметок:");
                if (categories.Count > 0)
                {
                    foreach (var cat in categories)
                    {
                        sb.AppendLine($"  • {cat}");
                    }
                }
                else
                {
                    sb.AppendLine("  [нет категорий]");
                }

                sb.AppendLine("\n[LOCAL] Команды:");
                sb.AppendLine("  local list - этот список");
                sb.AppendLine("  local dialogs - список диалогов");
                sb.AppendLine("  local categories - список категорий");
                sb.AppendLine("  local history [диалог] [дата] - история диалога");
                sb.AppendLine("  local notes [категория] [дата] - заметки категории");

                return sb.ToString();
            }

            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string subCommand = parts[0].ToLower();

            // Обработка подкоманд локальных данных
            switch (subCommand)
            {
                case "list":  // Полный список локальных данных
                    var convs = _chatManager.GetLocalConversations();
                    var cats = _chatManager.GetNoteCategories();

                    var sb = new StringBuilder();
                    sb.AppendLine("[LOCAL] Локальные диалоги:");
                    foreach (var conv in convs)
                    {
                        sb.AppendLine($"  • {conv}");
                    }

                    sb.AppendLine("\n[LOCAL] Категории заметок:");
                    foreach (var cat in cats)
                    {
                        sb.AppendLine($"  • {cat}");
                    }

                    return sb.ToString();

                case "dialogs":  // Список локальных диалогов
                case "conversations":
                    var dialogs = _chatManager.GetLocalConversations();
                    if (dialogs.Count == 0)
                        return "[LOCAL] Нет локальных диалогов";

                    var dialogSb = new StringBuilder();
                    dialogSb.AppendLine("[LOCAL] Локальные диалоги:");
                    foreach (var dialog in dialogs)
                    {
                        dialogSb.AppendLine($"  • {dialog}");
                    }
                    return dialogSb.ToString();

                case "categories":  // Список категорий заметок
                    var categories = _chatManager.GetNoteCategories();
                    if (categories.Count == 0)
                        return "[LOCAL] Нет категорий заметок";

                    var catSb = new StringBuilder();
                    catSb.AppendLine("[LOCAL] Категории заметок:");
                    foreach (var cat in categories)
                    {
                        catSb.AppendLine($"  • {cat}");
                    }
                    return catSb.ToString();

                case "history":  // История локального диалога
                    if (parts.Length < 2)
                        return "[ERROR] Укажите название диалога [и дату]";

                    string[] historyParts = parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    string dialogName = historyParts[0];
                    DateTime? historyDate = null;

                    if (historyParts.Length > 1 && DateTime.TryParse(historyParts[1], out DateTime parsedDate))
                    {
                        historyDate = parsedDate;
                    }

                    string history = await _chatManager.GetLocalConversationHistoryAsync(dialogName, historyDate);
                    return $"[LOCAL] === ИСТОРИЯ ДИАЛОГА '{dialogName}' ===\n{history}";

                case "notes":  // Заметки категории
                    if (parts.Length < 2)
                        return "[ERROR] Укажите категорию [и дату]";

                    string[] notesParts = parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    string categoryName = notesParts[0];
                    DateTime? notesDate = null;

                    if (notesParts.Length > 1 && DateTime.TryParse(notesParts[1], out DateTime notesParsedDate))
                    {
                        notesDate = notesParsedDate;
                    }

                    string notes = await _chatManager.GetNotesHistoryAsync(categoryName, notesDate);
                    return $"[LOCAL] === ЗАМЕТКИ КАТЕГОРИИ '{categoryName}' ===\n{notes}";

                default:  // Неизвестная подкоманда
                    return "[ERROR] Неизвестная подкоманда. Используйте: list, dialogs, categories, history, notes";
            }
        }

        // ==================== КОМАНДЫ ДЛЯ РАБОТЫ С WIFI ====================

        // Обработать команду сканирования WiFi сети
        // Выполняет сканирование сети только через беспроводные интерфейсы
        private async Task<string> HandleWifiScan()
        {
            Console.WriteLine("[WIFI] Запуск сканирования WiFi сети...");

            // Сохраняем текущие настройки интерфейса
            string oldMode = AppSettings.InterfaceMode;
            //bool oldForce = AppSettings.ForceWirelessOnly;

            // Временно переключаемся в режим WiFi для сканирования
            //AppSettings.InterfaceMode = "wifi";
            //AppSettings.ForceWirelessOnly = true;

            var result = await _detector.ScanNetworkAsync(false);  // Выполняем сканирование

            // Восстанавливаем оригинальные настройки
            //AppSettings.InterfaceMode = oldMode;
            //AppSettings.ForceWirelessOnly = oldForce;

            if (!result.Success)
                return $"[ERROR] WiFi сканирование не удалось: {result.Error}";

            var sb = new StringBuilder();
            sb.AppendLine($"[WIFI] СКАНИРОВАНИЕ ЗАВЕРШЕНО");
            sb.AppendLine($"[WIFI] Интерфейс: {result.InterfaceName}");
            sb.AppendLine($"[WIFI] Тип: {result.InterfaceType}");
            sb.AppendLine($"[WIFI] Найдено клиентов: {result.FoundIps?.Count ?? 0}");
            sb.AppendLine($"[WIFI] Длительность: {(result.EndTime - result.StartTime).TotalSeconds:F2} сек");

            if (result.FoundIps?.Count > 0)
            {
                sb.AppendLine("[WIFI] Найденные беспроводные клиенты:");
                foreach (var ip in result.FoundIps)
                {
                    sb.AppendLine($"  • {ip}");
                }
            }

            return sb.ToString();
        }

        // Обработать команду вывода списка WiFi интерфейсов
        // Возвращает список всех беспроводных сетевых интерфейсов
        private string HandleWifiList()
        {
            var wirelessInterfaces = _detector.GetWirelessInterfaces();

            if (wirelessInterfaces.Count == 0)
                return "[WIFI] Беспроводные интерфейсы не найдены";

            var sb = new StringBuilder();
            sb.AppendLine("[WIFI] Беспроводные интерфейсы:");

            var activeInterface = Interface_list.GetPrimaryInterface();

            // Выводим информацию о каждом WiFi адаптере
            foreach (var iface in wirelessInterfaces)
            {
                // Помечаем активный интерфейс
                string isActive = activeInterface.HasValue && iface.InterfaceName == activeInterface.Value.InterfaceName ? "[АКТИВНЫЙ]" : "";
                sb.AppendLine($"  • {iface.InterfaceName} - {iface.Address} {isActive}");
            }

            sb.AppendLine($"[WIFI] Всего: {wirelessInterfaces.Count} адаптеров");

            return sb.ToString();
        }

        // Обработать команду управления настройками WiFi
        // args - аргументы команды (ключ и значение настройки)
        // Управляет всеми настройками, связанными с беспроводными сетями
        private async Task<string> HandleWifiSettings(string args)
        {
            // Если аргументы не указаны - показываем текущие настройки WiFi
            if (string.IsNullOrEmpty(args))
            {
                var sb = new StringBuilder();
                sb.AppendLine("[WIFI] === НАСТРОЙКИ БЕСПРОВОДНОЙ СЕТИ ===");
                sb.AppendLine($"Режим интерфейса: {AppSettings.InterfaceMode}");
                //sb.AppendLine($"Только WiFi: {AppSettings.ForceWirelessOnly}");
                sb.AppendLine($"Имя адаптера: {(!string.IsNullOrEmpty(AppSettings.WirelessAdapterName) ? AppSettings.WirelessAdapterName : "авто")}");
                sb.AppendLine($"Предпочитать WiFi: {AppSettings.PreferWireless}");
                sb.AppendLine($"Макс. клиентов: {AppSettings.MaxWirelessClients}");
                sb.AppendLine($"Таймаут сканирования: {AppSettings.WirelessScanTimeout} мс");

                // Проверяем наличие беспроводных интерфейсов
                if (_detector.HasWirelessInterface())
                {
                    sb.AppendLine("[WIFI] ✓ Беспроводные интерфейсы доступны");
                }
                else
                {
                    sb.AppendLine("[WIFI] ✗ Беспроводные интерфейсы не обнаружены");
                }

                return sb.ToString();
            }

            // Обработка изменения настроек
            string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return "[ERROR] Укажите ключ и значение";

            string key = parts[0].ToLower();
            string value = parts[1];

            // Обработка различных настроек WiFi
            switch (key)
            {
                case "mode":  // Режим интерфейса
                    if (new[] { "auto", "wifi", "ethernet" }.Contains(value.ToLower()))
                    {
                        AppSettings.InterfaceMode = value.ToLower();
                        return $"[WIFI] Режим интерфейса установлен: {value}";
                    }
                    return "[ERROR] Допустимые значения: auto, wifi, ethernet";

                case "adapter":  // Имя адаптера
                    AppSettings.WirelessAdapterName = value;
                    return $"[WIFI] Имя адаптера установлено: {value}";

                case "prefer":  // Предпочтение WiFi
                    if (bool.TryParse(value, out bool prefer))
                    {
                        AppSettings.PreferWireless = prefer;
                        return $"[WIFI] Предпочтение WiFi установлено: {prefer}";
                    }
                    return "[ERROR] Укажите true или false";

                case "force":  // Принудительный WiFi режим
                    if (bool.TryParse(value, out bool force))
                    {
                        //AppSettings.ForceWirelessOnly = force;
                        return $"[WIFI] Принудительный WiFi режим: {force}";
                    }
                    return "[ERROR] Укажите true или false";

                case "maxclients":  // Максимальное количество клиентов
                    if (int.TryParse(value, out int maxClients) && maxClients > 0)
                    {
                        AppSettings.MaxWirelessClients = maxClients;
                        return $"[WIFI] Максимум клиентов: {maxClients}";
                    }
                    return "[ERROR] Укажите число больше 0";

                case "timeout":  // Таймаут сканирования
                    if (int.TryParse(value, out int timeout) && timeout > 0)
                    {
                        AppSettings.WirelessScanTimeout = timeout;
                        return $"[WIFI] Таймаут сканирования: {timeout} мс";
                    }
                    return "[ERROR] Укажите число больше 0";

                default:  // Неизвестная настройка
                    return $"[ERROR] Неизвестная настройка WiFi: {key}";
            }
        }

        // Обработать команду вывода информации о настройках интерфейса
        // Возвращает информацию о текущих настройках интерфейса из Interface_list
        private string HandleInterfaceInfo()
        {
            return Interface_list.GetInterfaceSettingsInfo();
        }

        //Обработать команду управления принудительным WiFi режимом
        // args - значение(true/false) для принудительного режима WiFi
        // Включает/выключает режим, когда работа разрешена только через WiFi интерфейсы
        private async Task<string> HandleForceWifi(string args)
        {
            // Если аргументы не указаны - показываем текущее состояние
            //if (string.IsNullOrEmpty(args))
            //{
            //    return $"[WIFI] Принудительный WiFi режим: {AppSettings.ForceWirelessOnly}";
            //}

            // Устанавливаем новое значение
            if (bool.TryParse(args, out bool force))
            {
                //AppSettings.ForceWirelessOnly = force;

                // При включении принудительного WiFi режима автоматически настраиваем другие параметры
                if (force)
                {
                    AppSettings.InterfaceMode = "wifi";
                    AppSettings.PreferWireless = true;
                }

                return $"[WIFI] Принудительный WiFi режим установлен: {force}";
            }

            return "[ERROR] Укажите true или false";
        }
    }
}