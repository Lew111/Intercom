using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace network
{

    // Менеджер сессий чатов (система вкладок)
    // Управляет активными чат-сессиями пользователей, предоставляет функционал
    // создания, переключения, закрытия чатов и обработки сообщений
    // Реализует механизм "активного чата" для работы в многопользовательском режиме
    // Все данные сессий сохраняются в JSON файл для восстановления между сеансами [WARN] - сохранение в Json, правильно или нет?
    public class ChatSessionManager
    {
        private readonly string _sessionsFilePath;  // Путь к файлу сохранения сессий
        private string _activeChat;  // Имя текущего активного чата (точнее ник пользователя)
        private readonly ConcurrentDictionary<string, ChatSession> _sessions;  // Потокобезопасная коллекция сессий

        // События для оповещения о изменениях в сессиях:
        public event Action<string> OnActiveChatChanged;  // Срабатывает при смене активного чата
        public event Action<string, string, bool> OnNewMessage;  // Срабатывает при получении нового сообщения


        // Конструктор менеджера сессий чатов
        // Инициализирует пути к файлам данных, загружает сохраненные сессии
        // и восстанавливает состояние системы чатов
        
        public ChatSessionManager()
        {
            _sessionsFilePath = Path.Combine(AppSettings.GetUserDataPath(), "chat_sessions.json");
            _sessions = new ConcurrentDictionary<string, ChatSession>();
            _activeChat = string.Empty;

            LoadSessions();  // Загружаем сохраненные сессии из файла
        }


        // Загрузить сессии чатов из файла
        // Восстанавливает список активных чатов и последний активный чат
        // из JSON файла для обеспечения сохранности между запусками приложения
        
        private void LoadSessions()
        {
            try
            {
                if (!File.Exists(_sessionsFilePath))
                    return;  // Файл не существует - начинаем с чистого состояния

                var json = File.ReadAllText(_sessionsFilePath);
                var data = JsonSerializer.Deserialize<SessionData>(json);  // Десериализуем данные

                if (data != null)
                {
                    _activeChat = data.ActiveChat;  // Восстанавливаем активный чат

                    // Восстанавливаем все сохраненные сессии
                    foreach (var chat in data.Sessions)
                    {
                        _sessions[chat] = new ChatSession { Nickname = chat };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки сессий: {ex.Message}");
            }
        }

        // Сохранить сессии чатов в файл
        // Сериализует текущее состояние сессий в JSON файл для последующего восстановления

        private void SaveSessions()
        {
            try
            {
                var data = new SessionData
                {
                    ActiveChat = _activeChat,
                    Sessions = _sessions.Keys.ToList()  // Сохраняем список всех сессий
                };

                var json = JsonSerializer.Serialize(data);  // Сериализуем данные в JSON
                File.WriteAllText(_sessionsFilePath, json);  // Сохраняем в файл
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения сессий: {ex.Message}");
            }
        }

        // Создать или открыть чат с пользователем
        // Если чат с указанным пользователем не существует, создает новую сессию
        // Если уже существует, возвращает успешный результат
        // nickname - Никнейм пользователя для чата
        // True если чат успешно создан/открыт, False при ошибке
        public bool CreateOrOpenChat(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return false;

            var session = new ChatSession { Nickname = nickname };

            // Добавляем новую сессию если ее еще нет в коллекции
            if (_sessions.TryAdd(nickname, session))
            {
                SaveSessions();  // Сохраняем изменения
                Console.WriteLine($"[CHAT] Чат с {nickname} создан");
            }

            return true;
        }


        // Переключиться на чат с указанным пользователем
        // Делает указанный чат активным. Если чата не существует, сначала создает его
        
        // nickname- Никнейм пользователя для переключения
        // True если переключение успешно, False при ошибке
        public bool SwitchToChat(string nickname)
        {
            // Если чат не существует, создаем его
            if (!_sessions.ContainsKey(nickname))
            {
                CreateOrOpenChat(nickname);
            }

            // Проверяем, не является ли уже этот чат активным
            if (string.Equals(_activeChat, nickname, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[CHAT] Уже в чате с {nickname}");
                return true;
            }

            _activeChat = nickname;  // Устанавливаем новый активный чат
            SaveSessions();  // Сохраняем изменения

            OnActiveChatChanged?.Invoke(nickname);  // Оповещаем о смене активного чата
            Console.WriteLine($"[CHAT] Переключен на чат с {nickname}");

            return true;
        }


        // Закрыть чат с пользователем
        // Удаляет сессию чата из коллекции. Если закрывается активный чат,
        // переключает активность на первый доступный чат
        
        // nickname - Никнейм пользователя для закрытия чата
        // True если чат успешно закрыт, False если закрытие чата не удалось
        public bool CloseChat(string nickname)
        {
            // Пытаемся удалить сессию из коллекции
            if (_sessions.TryRemove(nickname, out _))
            {
                // Если закрываем активный чат, переключаем активность
                if (string.Equals(_activeChat, nickname, StringComparison.OrdinalIgnoreCase))
                {
                    _activeChat = _sessions.Keys.FirstOrDefault() ?? string.Empty;  // Переключаем на первый доступный чат
                    OnActiveChatChanged?.Invoke(_activeChat);  // Оповещаем о смене активного чата
                }

                SaveSessions();  // Сохраняем изменения
                Console.WriteLine($"[CHAT] Чат с {nickname} закрыт");
                return true;
            }

            return false;  // Чат не найден
        }

        // Получить текущий активный чат
        // Имя пользователя активного чата или пустая строка
        public string GetActiveChat()
        {
            return _activeChat;
        }

        
        // Проверить, является ли указанный чат активным
        // nickname - Никнейм для проверки
        // True если чат активен, False если нет
        public bool IsChatActive(string nickname)
        {
            return string.Equals(_activeChat, nickname, StringComparison.OrdinalIgnoreCase);
        }

        // Получить список всех активных сессий
        // Список никнеймов всех открытых чатов
        public List<string> GetAllSessions()
        {
            return _sessions.Keys.ToList();
        }


        // Обработать новое входящее сообщение
        // Определяет, активен ли чат отправителя, и вызывает соответствующие события

        // fromNickname - Никнейм отправителя
        // message - Текст сообщения
        public void HandleNewMessage(string fromNickname, string message)
        {
            bool isActive = IsChatActive(fromNickname);  // Проверяем активность чата

            // Оповещаем о новом сообщении с указанием активности чата
            OnNewMessage?.Invoke(fromNickname, message, isActive);

            if (isActive)
            {
                // Если чат активен - выводим сообщение в обычном формате
                Console.WriteLine($"[ЧАТ] {fromNickname}: {message}");
            }
            else
            {
                // Если чат неактивен - выводим уведомление с превью сообщения
                string preview = message.Length > 30 ? message.Substring(0, 30) + "..." : message;
                Console.WriteLine($"[УВЕДОМЛЕНИЕ] Новое сообщение от {fromNickname}: {preview}");
            }
        }

        // Отправить сообщение в чат
        // Создает/активирует чат с получателем и выводит сообщение в консоль

        // toNickname - Никнейм получателя
        // message - Текст сообщения
        public void SendMessage(string toNickname, string message)
        {
            // Создаем чат если он не существует
            if (!_sessions.ContainsKey(toNickname))
            {
                CreateOrOpenChat(toNickname);
            }

            // Переключаемся на чат с получателем
            SwitchToChat(toNickname);

            // Выводим сообщение в консоль
            Console.WriteLine($"[ЧАТ] Вы: {message}");
        }
    }


    // Класс сессии чата
    // Хранит информацию об одном активном чате с пользователем
    
    public class ChatSession
    {
        public string Nickname { get; set; }  // Никнейм пользователя в чате
        public DateTime LastActivity { get; set; } = DateTime.Now;  // Время последней активности в чате
        public int UnreadCount { get; set; }  // Количество непрочитанных сообщений в чате
    }

    // Структура данных для сериализации сессий
    // Используется для сохранения и восстановления состояния менеджера сессий
    
    public class SessionData
    {
        public string ActiveChat { get; set; } = string.Empty;  // Текущий активный чат
        public List<string> Sessions { get; set; } = new List<string>();  // Список всех активных сессий
    }
}