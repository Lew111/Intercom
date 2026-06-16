using System;  // Основные системные классы
using System.Net;  // Работа с IP-адресами и сетевыми конечными точками
using System.Net.Sockets;  // Работа с TCP сокетами
using System.Text;  // Кодировки строк
using System.Threading.Tasks;  // Асинхронное программирование

namespace network
{
    // Сервер для приема входящих подключений и обработки сообщений
    // Является точкой входа для входящих соединений, управляет аутентификацией и маршрутизацией сообщений
  
    public class Server
    {
        // Зависимости (Dependency Injection):
        private readonly ConnectionManager _connectionManager;  // Менеджер подключений (управление активными соединениями)
        // private readonly NicknameManager _nicknameManager;  // Менеджер никнеймов (сопоставление IP-никнейм)
        // private readonly ChatSessionManager _chatSessionManager;  // Менеджер сессий чата (управление активными чатами)
        // private readonly ChatManager _chatManager;  // Менеджер чатов (сохранение сообщений)

        // ==================== КОНСТРУКТОР ====================


        // Конструктор класса Server
        // Принимает все зависимости через Dependency Injection

        // connectionManager - Менеджер подключений
        // nicknameManager - Менеджер никнеймов
        // chatSessionManager - Менеджер сессий чата
        // chatManager - Менеджер чатов
        public Server(ConnectionManager connectionManager, NicknameManager nicknameManager,
                     ChatSessionManager chatSessionManager, ChatManager chatManager)
        {
            // Инициализация зависимостей
            _connectionManager = connectionManager;
            // _nicknameManager = nicknameManager;
            // _chatSessionManager = chatSessionManager;
            // _chatManager = chatManager;
        }

        // ==================== МЕТОДЫ УПРАВЛЕНИЯ СЕРВЕРОМ ====================


        // Проверка доступности портов для запуска сервера
        // Автоматически находит свободный порт в заданном диапазоне
        
        // startport - Начальный порт для проверки (по умолчанию 46000)
        // range - Диапазон проверяемых портов (по умолчанию 200)
        // Номер свободного порта или -1 если свободных портов нет
        public static int Portchekertostart(int startport = 46000, int range = 200)
        {
            // Последовательная проверка портов в указанном диапазоне
            for (int i = 0; i < range; i++)
            {
                int port = startport + i;
                if (IsPortFree(port))
                    return port;  // Возврат первого свободного порта
            }
            return -1;  // Свободных портов не найдено
        }


        // Проверка доступности порта
        // Пытается создать TCP-слушатель на указанном порту
        
        // port - Порт для проверки
        // true - если порт свободен, false если занят
        private static bool IsPortFree(int port)
        {
            TcpListener listener = null;
            try
            {
                // Попытка создания TCP-слушателя на порту
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                return true;  // Порт свободен
            }
            catch (SocketException)
            {
                return false;  // Порт занят
            }
            finally
            {
                listener?.Stop();  // Остановка слушателя в любом случае
            }
        }


        // Запуск TCP-сервера для приема входящих подключений
        // Основной метод сервера, работает в бесконечном цикле

        public async Task server()
        {
            // Поиск свободного порта для запуска
            int port = Portchekertostart();
            if (port == -1)
            {
                // Если свободных портов нет, используется порт по умолчанию
                port = AppSettings.DefaultPort;
            }

            Console.WriteLine($"Сервер запущен на порту: {port}");
            IPAddress address = IPAddress.Any;  // Принимаем подключения на все сетевые интерфейсы

            // Создание и запуск TCP-слушателя
            using var listener = new TcpListener(address, port);
            listener.Start();

            // Бесконечный цикл приема подключений
            while (true)
            {
                try
                {
                    // Асинхронное ожидание входящего подключения
                    // var clientSocket = await listener.AcceptTcpClientAsync();
                    var client = await listener.AcceptTcpClientAsync();
                    // Запуск обработки клиента в отдельной задаче
                    // _ = Task.Run(async () => await HandleClientAsync(clientSocket));
                    _ = Task.Run(() => _connectionManager.RegisterIncomingConnection(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при принятии клиента: {ex}");
                }
            }
        }

        // ==================== МЕТОДЫ ОБРАБОТКИ КЛИЕНТОВ ====================


        // Обработка входящего подключения клиента
        // Выполняет аутентификацию и регистрацию клиента в системе

        // client - TCP-клиент подключившегося пользователя
        // private async Task HandleClientAsync(TcpClient client)
        // {
        //     try
        //     {
        //         using var networkStream = client.GetStream();  // Получение сетевого потока
        //         byte[] buffer = new byte[1024];  // Буфер для приема данных
        //         StringBuilder dataBuilder = new StringBuilder();  // Построитель строки данных
        //         int totalBytesRead = 0;  // Счетчик прочитанных байт

        //         // Цикл чтения данных от клиента
        //         while (true)
        //         {
        //             int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
        //             if (bytesRead == 0)
        //                 break;  // Разрыв соединения

        //             totalBytesRead += bytesRead;
        //             dataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

        //             // Проверка завершения сообщения
        //             if (dataBuilder.ToString().Contains("<END>"))
        //             {
        //                 break;  // Полное сообщение получено
        //             }

        //             // Защита от переполнения буфера
        //             if (totalBytesRead > 4096)
        //                 break;
        //         }

        //         string data = dataBuilder.ToString();

        //         if (string.IsNullOrEmpty(data))
        //         {
        //             client.Close();  // Закрытие пустого соединения
        //             return;
        //         }

        //         // Получение IP-адреса клиента
        //         string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

        //         // Обработка аутентификации
        //         if (data.StartsWith("AUTH:"))
        //         {
        //             string nickname = data.Replace("AUTH:", "").Replace("<END>", "").Trim();

        //             Console.WriteLine($"[SERVER] Клиент {nickname} подключился с IP: {clientIp}");

        //             // Обновление сопоставления IP-никнейм
        //             _nicknameManager.UpdateMapping(clientIp, nickname);
        //             // Создание или открытие чата с клиентом
        //             _chatSessionManager.CreateOrOpenChat(nickname);

        //             // Отправка подтверждения аутентификации
        //             string response = $"AUTH_OK:{AppSettings.Nickname}<END>";
        //             byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        //             await networkStream.WriteAsync(responseBytes, 0, responseBytes.Length);

        //             // Регистрация клиентского подключения
        //             await RegisterClientConnection(client, networkStream, clientIp, nickname);
        //         }
        //         else
        //         {
        //             Console.WriteLine($"[SERVER] Неверный формат аутентификации от {clientIp}");
        //             client.Close();  // Закрытие неаутентифицированного соединения
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         // Обработка ошибок подключения
        //         client.Close();
        //     }
        // }

        // Регистрация и обслуживание клиентского подключения
        // Управляет постоянным соединением с клиентом и обработкой его сообщений

        // client - TCP-клиент
        // stream - Сетевой поток для обмена данными
        // ip - IP-адрес клиента
        // nickname - Никнейм клиента
        // private async Task RegisterClientConnection(TcpClient client, NetworkStream stream, string ip, string nickname)
        // {
        //     try
        //     {
        //         byte[] buffer = new byte[8192];  // Увеличенный буфер для поддержки передачи файлов
        //         StringBuilder dataBuilder = new StringBuilder();  // Построитель строки данных

        //         // Цикл обслуживания подключения, пока клиент подключен
        //         while (client.Connected)
        //         {
        //             int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        //             if (bytesRead == 0)
        //                 break;  // Разрыв соединения

        //             string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        //             dataBuilder.Append(data);

        //             // Обработка всех завершенных сообщений в буфере
        //             while (dataBuilder.ToString().Contains("<END>"))
        //             {
        //                 int endIndex = dataBuilder.ToString().IndexOf("<END>") + 5;
        //                 string message = dataBuilder.ToString().Substring(0, endIndex);
        //                 dataBuilder.Remove(0, endIndex);

        //                 // Обработка отдельного сообщения
        //                 await ProcessMessage(stream, message, ip, nickname);
        //             }
        //         }
        //     }
        //     catch
        //     {
        //         // Игнорирование ошибок при разрыве соединения
        //     }
        //     finally
        //     {
        //         client.Close();  // Гарантированное закрытие соединения
        //         Console.WriteLine($"[SERVER] Клиент {nickname} отключился");
        //     }
        // }


        // Обработчик соообщений, определяет тип сообщения и вызывает соответствующий обработчик
        
        // stream - Сетевой поток для ответа
        // message - Полное сообщение с маркером конца
        // ip - IP-адрес отправителя
        // nickname - Никнейм отправителя
    //     private async Task ProcessMessage(NetworkStream stream, string message, string ip, string nickname)
    //     {
    //         try
    //         {
    //             // ОБРАБОТКА ОБЫЧНЫХ СООБЩЕНИЙ (префикс MSG:)
    //             if (message.StartsWith("MSG:"))
    //             {
    //                 string[] parts = message.Replace("MSG:", "").Split(':', 2);
    //                 if (parts.Length >= 2)
    //                 {
    //                     string messageId = parts[0];
    //                     string messageText = parts[1].Replace("<END>", "");

    //                     // Отправка подтверждения получения сообщения
    //                     string confirm = $"CONFIRM:{messageId}<END>";
    //                     byte[] confirmData = Encoding.UTF8.GetBytes(confirm);
    //                     await stream.WriteAsync(confirmData, 0, confirmData.Length);

    //                     // Передача сообщения в менеджер сессий для отображения
    //                     _chatSessionManager.HandleNewMessage(nickname, messageText);
    //                     // Сохранение сообщения в историю чата
    //                     await _chatManager.SaveMessageAsync(nickname, nickname, messageText, DateTime.Now);
    //                 }
    //             }
    //             // [WARN] - не работает как надо, необходимо переработать
    //             // ОБРАБОТКА НАЧАЛА ПЕРЕДАЧИ ФАЙЛА (префикс FILE_START:)
    //             else if (message.StartsWith("FILE_START:"))
    //             {
    //                 string fileData = message.Replace("FILE_START:", "").Replace("<END>", "");
    //                 string[] parts = fileData.Split(':');

    //                 if (parts.Length >= 4)
    //                 {
    //                     string fileId = parts[0];
    //                     string fileName = parts[1];
    //                     long fileSize = long.Parse(parts[2]);
    //                     int totalChunks = int.Parse(parts[3]);

    //                     Console.WriteLine($"[SERVER] Начало получения файла: {fileName} ({fileSize} байт) от {nickname}");
    //                 }
    //             }
    //             // ОБРАБОТКА ЧАНКА ФАЙЛА (префикс FILE_CHUNK:)
    //             else if (message.StartsWith("FILE_CHUNK:"))
    //             {
    //                 string chunkData = message.Replace("FILE_CHUNK:", "").Replace("<END>", "");
    //                 string[] parts = chunkData.Split(':');

    //                 if (parts.Length >= 3)
    //                 {
    //                     string fileId = parts[0];
    //                     int chunkIndex = int.Parse(parts[1]);
    //                     int chunkSize = int.Parse(parts[2]);

    //                     // Отправка подтверждения получения чанка
    //                     string ack = $"FILE_ACK:{fileId}:{chunkIndex}<END>";
    //                     byte[] ackData = Encoding.UTF8.GetBytes(ack);
    //                     await stream.WriteAsync(ackData, 0, ackData.Length);

    //                     Console.WriteLine($"[SERVER] Получен чанк {chunkIndex} файла {fileId} от {nickname}");
    //                 }
    //             }
    //             // ОБРАБОТКА ЗАВЕРШЕНИЯ ПЕРЕДАЧИ ФАЙЛА (префикс FILE_END:)
    //             else if (message.StartsWith("FILE_END:"))
    //             {
    //                 string fileId = message.Replace("FILE_END:", "").Replace("<END>", "");
    //                 Console.WriteLine($"[SERVER] Получение файла {fileId} завершено от {nickname}");
    //             }
    //             // ОБРАБОТКА ПОДТВЕРЖДЕНИЙ ДОСТАВКИ (префикс CONFIRM:)
    //             else if (message.StartsWith("CONFIRM:"))
    //             {
    //                 string messageId = message.Substring(8).Replace("<END>", "");
    //                 Console.WriteLine($"[SERVER] Подтверждение получения сообщения {messageId} от {nickname}");
    //             }
    //             // [/WARN\]


    //             // ОБРАБОТКА ПОДТВЕРЖДЕНИЯ АУТЕНТИФИКАЦИИ (префикс AUTH_OK:)
    //             else if (message.StartsWith("AUTH_OK:"))
    //             {
    //                 Console.WriteLine($"[SERVER] Получен ответ на аутентификацию от {nickname}");
    //             }
    //             // ОБРАБОТКА ПРОСТЫХ СООБЩЕНИЙ (без префикса)
    //             else if (!string.IsNullOrWhiteSpace(message) && message != "<END>")
    //             {
    //                 string text = message.Replace("<END>", "");

    //                 // Игнорирование системных сообщений
    //                 if (text.StartsWith("AUTH_OK:") || text.StartsWith("CONFIRM:") ||
    //                     text.StartsWith("FILE_") || string.IsNullOrWhiteSpace(text))
    //                 {
    //                     return;
    //                 }

    //                 // Сохранение обычного сообщения
    //                 _chatSessionManager.HandleNewMessage(nickname, text);
    //                 await _chatManager.SaveMessageAsync(nickname, AppSettings.Nickname, text, DateTime.Now);
    //             }
    //         }
    //         catch (Exception ex)
    //         {
    //             Console.WriteLine($"[SERVER] Ошибка обработки сообщения от {nickname}: {ex.Message}");
    //         }
    //     }
    }
}