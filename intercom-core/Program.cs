using System;
using System.Text;  // Кодировки строк для их понимания 
using System.Threading.Tasks;  // Асинхронные функции (Task, async/await)
using wr;  // Использование пространства имён wr (writer)
using network;  // Использование пространство имён network для сетевого взаимодействия

namespace InterCom_core
{
    public class Program
    {
        // Объявление статических полей для хранения экземпляров классов, отвечающих за разные аспекты работы мессенджера

        private static WriterIP _writer;  // Writer для записи и чтения IP адресов после поиска в сети
        private static ChatManager _chatManager;  // Управление чатами и сообщениями
        private static ConnectionManager _connectionManager;  // Управление сетевыми подключениями
        private static Detector _detector;  // Обнаружение сетевых устройств/клиентов
        private static Server _server;  // Сетевой сервер для приема входящих соединений
        private static BackgroundService _backgroundService;  // Фоновые задачи и сервисы
        private static CommandHandler _commandHandler;  // Обработчик команд
        private static NicknameManager _nicknameManager;  // Изменение никнейма пользователя
        private static ChatSessionManager _chatSessionManager;  // Управление активными чат-сессиями
        private static AutoDiscoveryService _autoDiscoveryService;  // Сервис автообнаружения клиентов
        private static Sender _sender;

        public static async Task Main(string[] args)
        {
            // Настройка кодировки консоли для поддержки кириллицы и других Unicode-символов
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            // // Дублирование установки кодировки (возможно, избыточно)
            //Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Установка заголовка консольного окна, просто по приколу
            Console.Title = "Intercom-core";

            // Вывод приветственного сообщения с текущим ником пользователя
            Console.WriteLine($"[SYSTEM] Система готова к запуску. Ваш ник: {AppSettings.Nickname}"); //[LOG]

            // Обработка возможных исключений во время инициализации
            try
            {
                // Инициализация настроек приложения
                AppSettings.Initialize();

                //log сообщение запуска системы, не обязательно
                Console.WriteLine("[SYSTEM] Инициализация системы..."); //[LOG]

                // Проверка, установлен ли у пользователя никнейм, если же ник пустой или стандартный "DefaultUser", запрашиваем его у пользователя
                if (string.IsNullOrEmpty(AppSettings.Nickname) || AppSettings.Nickname == "DefaultUser")
                {
                    Console.Write("[SYSTEM] Введите ваш ник: "); //[LOG]
                    string nickname = Console.ReadLine()?.Trim();  // ?. - защита от null, а Trim() удаляет пробелы для того, чтобы 

                    if (!string.IsNullOrEmpty(nickname))  // Если пользователь ввел не пустую строку
                    {
                        AppSettings.Nickname = nickname;  // то сохраняем ник в настройках
                        Console.WriteLine($"[SYSTEM] Ник установлен: {nickname}"); //[LOG] выводим сообщение о том, что ник установлен в консоль
                    }
                }

                // Создание экземпляров основных компонентов системы
                _writer = new WriterIP();  // Инициализация компонента записи/чтения
                _chatManager = new ChatManager();  // Инициализация менеджера чатов
                _nicknameManager = new NicknameManager();  // Инициализация менеджера никнеймов
                _chatSessionManager = new ChatSessionManager();  // Инициализация менеджера сессий чата
                _chatManager.InitializeStorage();  // Инициализация хранилища для чатов в ChatManager.cs

                // Создание менеджера подключений с зависимостями

                // Здесь используется паттерн Dependency Injection (иньекция зависимосте) для передачи зависимостей

                // Создание паттерна иньекция зависимосте позволяет запрашивать только значения у кода/файла/базы данных,
                // уменьшая зависимость от наполнения скрипта в коде "потребителе"
                // (здесь потребитель - это скрипт или метод, который использует данные или методы из скрипта, в котором задаются эти данные)
                // Почитать подробнее можно здесь https://habr.com/ru/articles/350068/


                RouteManager routeManager = new RouteManager();
                routeManager.LoadConfiguration();
                _connectionManager = new ConnectionManager(_writer, _chatManager, _nicknameManager, _chatSessionManager, routeManager);


                RelayService relayService = new RelayService(_connectionManager, _chatManager, _nicknameManager, routeManager);
                _connectionManager.RelayService = relayService;

                var sender = new Sender(_connectionManager, relayService, routeManager, _chatManager); // ← ЕСТЬ?
                _sender = sender;  // ← ЕСТЬ?


                // Создание детектора для обнаружения устройств в сети
                _detector = new Detector(_writer, _connectionManager);

                // Создание экземпляра сервиса автообнаружения клиентов в сети
                _autoDiscoveryService = new AutoDiscoveryService(
                    _connectionManager,
                    _nicknameManager,
                    _chatSessionManager,
                    _writer, routeManager);

                // Создаём экземпляр фонового сервиса для периодических задач, а также принимаем экземпляры классов Detector, ConnectionManager, Writer
                _backgroundService = new BackgroundService(_detector, _connectionManager, _writer, routeManager, _chatSessionManager);
                _backgroundService.StartAllServices();

                // Создаём обработчик команд и передаём в него экземплеры классов для обработки команд, вызывающих эти самые классы
                _commandHandler = new CommandHandler(
                    _writer,
                    _chatManager,
                    _connectionManager,
                    _detector,
                    _backgroundService,
                    _nicknameManager,
                    _chatSessionManager,
                    _autoDiscoveryService,
                    relayService,
                    routeManager,
                    sender
                );

                // Создаём сервер для приема входящих подключений
                _server = new Server(_connectionManager, _nicknameManager, _chatSessionManager, _chatManager);

                // Настройка обработчиков событий (подписок на события компонентов)
                SetupEventHandlers();

                // Запуск сервера в фоновом режиме (асинхронно)
                // _ = означает, что мы игнорируем возвращаемую задачу (Task)
                // Task.Run запускает метод server() в пуле потоков
                _ = Task.Run(async () => await _server.server());

                // Повторное сообщение о полной готовности системы (после полной инициализации необходимых компонентов)
                Console.WriteLine($"[SYSTEM] Система полностью готова. Ваш ник: {AppSettings.Nickname}"); //[LOG]

                // Получаем активный чат (если есть) и выводим информацию о нем
                string activeChat = _chatSessionManager.GetActiveChat();
                if (!string.IsNullOrEmpty(activeChat))
                {
                    Console.WriteLine($"[CHAT] Активный чат: {activeChat}"); //[LOG]
                }

                // Если в настройках включено автообнаружение клиентов, то запускаем его
                if (AppSettings.AutoConnectDiscovered)
                {
                    // Запуск непрерывного сканирования каждые 30 секунд
                    _autoDiscoveryService.StartContinuousScan(30);
                    Console.WriteLine("[SYSTEM] Автообнаружение запущено"); //[LOG]
                }

                // Если в настройках включено автосканирование сети с заданным интервалом
                if (AppSettings.AutoScanEnabled && AppSettings.AutoScanInterval > 0)
                {
                    // Запуск периодического сканирования сети
                    _backgroundService.StartAutoScanning(AppSettings.AutoScanInterval);
                }

                // Запуск основного цикла обработки команд пользователя
                await RunCommandLoopAsync();
            }
            // Обработка исключений, возникших во время инициализации
            catch (Exception ex)
            {
                // Вывод сообщения об ошибке и стека вызовов
                Console.WriteLine($"[ERROR] Возникла критическая ошибка: {ex.Message}"); //[LOG]
                Console.WriteLine(ex.StackTrace);

                // [Del] Ожидание нажатия клавиши перед закрытием (для отладки), после создания вывода для ошибок убрать
                Console.ReadKey();
                // [/Del\]
            }
        }

        // Основной цикл обработки команд пользователя
        private static async Task RunCommandLoopAsync()
        {
            Console.WriteLine("\n[SYSTEM] Введите команду (help - справка, exit - выход):"); //[LOG]

            //[Endless-Cycle] Бесконечный цикл, вывода данных в консоль, пока пользователь не введет команду exit
            while (true)
            {
                // Получение активного чата для отображения в приглашении командной строки
                string activeChat = _chatSessionManager.GetActiveChat();
                // Формирование приглашения: если есть активный чат, показываем его имя
                string prompt = string.IsNullOrEmpty(activeChat) ? "> " : $"[{activeChat}]> ";
                Console.Write(prompt);  // Вывод приглашения без перевода строки

                // Чтение команды от пользователя
                string command = Console.ReadLine()?.Trim();  // ?. - защита от null

                // Если введена пустая строка, пропускаем итерацию и ничего не делаем
                if (string.IsNullOrEmpty(command))
                    continue;

                // Обработка команды завершения работы приложения (пррограммы)
                if (command.ToLower() == "exit")
                {
                    Console.WriteLine("[SYSTEM] Сохранение настроек..."); //[LOG]
                    AppSettings.SaveAllSettings();  // Сохранение всех настроек приложения

                    Console.WriteLine("[SYSTEM] Остановка служб..."); //[LOG]
                    _backgroundService.StopAllServices();  // Остановка всех фоновых сервисов
                    _autoDiscoveryService.StopContinuousScan();  // Остановка автообнаружения

                    Console.WriteLine("[SYSTEM] Отключение..."); //[LOG]
                    await _connectionManager.DisconnectAllAsync();  // Корректное отключение всех соединений

                    Console.WriteLine("[SYSTEM] Завершение работы"); //[LOG]
                    break;  // Выход из цикла и завершение программы
                }

                // Обработка всех остальных команд через CommandHandler
                string result = await _commandHandler.HandleCommand(command);

                // Если обработчик вернул какое то значение, а не ничего (пустой результат), выводим это самое значение
                if (!string.IsNullOrEmpty(result))
                    Console.WriteLine(result);
            }
            //[/Endless-Cycle\]
        }

        // Настройка подписок на события от различных компонентов системы
        // Это позволяет реагировать на события в реальном времени (новые подключения, сообщения и т.д.)
        private static void SetupEventHandlers()
        {
            // Событие при успешном подключении к клиенту
            _connectionManager.OnConnected += (ip) =>
            {
                Console.WriteLine($"[CONNECT] {ip} - подключен"); //[LOG]
            };

            // Событие при отключении клиента
            _connectionManager.OnDisconnected += (ip) =>
            {
                Console.WriteLine($"[CONNECT] {ip} - отключен"); //[LOG]
            };

            // Событие при успешной доставке сообщения
            _connectionManager.OnMessageDelivered += (ip, message) =>
            {
                // Получение никнейма по IP-адресу (если известен)
                string nickname = _nicknameManager.GetNicknameByIp(ip);
                if (string.IsNullOrEmpty(nickname))
                    nickname = ip;  // Если ник неизвестен, используем IP

                Console.WriteLine($"[ОТПРАВЛЕНО] {nickname}: {message}"); //[LOG]
            };

            // Событие при ошибке доставки сообщения
            _connectionManager.OnMessageFailed += (ip, message, reason) =>
            {
                // Получение никнейма по IP-адресу
                string nickname = _nicknameManager.GetNicknameByIp(ip);
                if (string.IsNullOrEmpty(nickname))
                    nickname = ip;

                Console.WriteLine($"[ERROR] Не удалось отправить {nickname}: {message}"); //[LOG]
                Console.WriteLine($"[ERROR] Причина: {reason}");  //[LOG] Вывод причины ошибки
            };

            // Событие при автоматическом подключении по автообнаружению
            _connectionManager.OnAutoConnected += (ip) =>
            {
                Console.WriteLine($"[CONNECT] {ip} - автоподключение"); //[LOG]
            };

            // Событие при изменении активного чата, для переключения между чатами
            _chatSessionManager.OnActiveChatChanged += (nickname) =>
            {
                if (!string.IsNullOrEmpty(nickname))
                {
                    Console.WriteLine($"[CHAT] Переключен на чат с {nickname}"); //[LOG]
                }
            };

            //[WARN] - возможное неудобство с выводом только части сообщения, возможно нужно по другому выводить
            // Событие при получении нового сообщения
            _chatSessionManager.OnNewMessage += (fromNickname, message, isActive) =>
            {
                // isActive - указывает, является ли чат с отправителем активным в данный момент
                if (isActive)
                {
                    // Если чат активен, выводим полное сообщение доступное для чтения
                    Console.WriteLine($"[ЧАТ] {fromNickname}: {message}"); //[LOG]
                }
                else
                {
                    // Если чат неактивен, выводим краткое уведомление (первые 30 символов)
                    string preview = message.Length > 30 ? message.Substring(0, 30) + "..." : message;
                    Console.WriteLine($"[УВЕДОМЛЕНИЕ] Новое сообщение от {fromNickname}: {preview}"); //[LOG]
                }
            };
            //[/WARN\]

            // Событие при обнаружении нового клиента в сети
            _autoDiscoveryService.OnClientDiscovered += (ip, nickname) =>
            {
                Console.WriteLine($"[AUTO DISCOVERY] Обнаружен новый клиент: {nickname} ({ip})"); //[LOG]
            };

            // Событие при потере клиента (перестал отвечать на запросы автообнаружения)
            _autoDiscoveryService.OnClientLost += (ip) =>
            {
                Console.WriteLine($"[AUTO DISCOVERY] Клиент потерян: {ip}"); //[LOG]
            };
        }
    }
}