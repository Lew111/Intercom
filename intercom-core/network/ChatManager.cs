using System;  // Основные системные классы
using System.IO;  // Работа с файловой системой
using System.Text;  // Кодировки строк
using System.Threading.Tasks;  // Асинхронное программирование
using System.Collections.Generic;  // Общие коллекции (List)
using System.Linq;  // LINQ для работы с коллекциями

namespace network
{
    // Менеджер чатов с поддержкой сохранения файлов и разделением по дням
    // Отвечает за хранение и управление историей сообщений, файлами и локальными заметками
    // Использует структурированную файловую систему с разделением по пользователям и датам
    public class ChatManager
    {
        // ==================== ОСНОВНЫЕ МЕТОДЫ ====================

        // Инициализировать структуру папок для хранения чатов
        // Создает необходимые директории для работы с историей сообщений
        public void InitializeStorage()
        {
            Console.WriteLine("[SYSTEM] Структура хранения готова"); // [LOG]
        }

        // Сохранить текстовое сообщение в формате Markdown (разделение по дням)
        // senderNickname - никнейм отправителя сообщения
        // receiverNickname - никнейм получателя (или название чата)
        // message - текст сообщения для сохранения
        // timestamp - время отправки сообщения
        // Сообщение сохраняется в отдельный файл для каждого дня в папке чата
        public async Task SaveMessageAsync(string senderNickname, string receiverNickname, string message, DateTime timestamp)
        {
            if (!AppSettings.AutoSaveMessages)  // Проверка настройки автосохранения
                return;

            try
            {
                // Получаем путь к папке чата с получателем
                string chatPath = AppSettings.GetChatPath(receiverNickname);
                if (!Directory.Exists(chatPath))
                {
                    // Создаем структуру папок для нового чата
                    Directory.CreateDirectory(chatPath);
                    await CreateChatInfoFileAsync(chatPath, receiverNickname);
                    CreateFilesFolder(chatPath);
                }

                // Определяем имя файла по дате (формат: chat_YYYY-MM-DD.md)
                string dateStr = timestamp.ToString("yyyy-MM-dd");
                string messagesFilePath = Path.Combine(chatPath, $"chat_{dateStr}.md");

                // Если файл не существует, создаем его с заголовком
                if (!File.Exists(messagesFilePath))
                {
                    await CreateDailyChatFileAsync(messagesFilePath, receiverNickname, timestamp);
                }

                // Форматируем сообщение в Markdown и добавляем в файл
                string formattedMessage = FormatMessageMarkdown(senderNickname, message, timestamp);
                await File.AppendAllTextAsync(messagesFilePath, formattedMessage, Encoding.UTF8);

                Console.WriteLine($"[SYSTEM] Сообщение сохранено в чат с {receiverNickname} ({dateStr})"); // [LOG]
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения сообщения: {ex.Message}"); // [LOG]
            }
        }

        // Сохранить файл в папке чата с разделением по дням
        // senderNickname - никнейм отправителя файла
        // receiverNickname - никнейм получателя (или название чата)
        // originalFileName - оригинальное имя файла
        // fileContent - содержимое файла в виде массива байтов
        // timestamp - время отправки файла
        // Возвращает кортеж (success, filePath, relativePath) с результатами сохранения
        public async Task<(bool success, string filePath, string relativePath)> SaveFileAsync(
            string senderNickname,
            string receiverNickname,
            string originalFileName,
            byte[] fileContent,
            DateTime timestamp)
        {
            if (!AppSettings.AutoSaveMessages)  // Проверка настройки автосохранения
                return (false, null, null);

            try
            {
                // Получаем путь к папке чата
                string chatPath = AppSettings.GetChatPath(receiverNickname);
                if (!Directory.Exists(chatPath))
                {
                    Directory.CreateDirectory(chatPath);
                    await CreateChatInfoFileAsync(chatPath, receiverNickname);
                }

                // Создаем папку для файлов и сохраняем файл
                string filesFolder = CreateFilesFolder(chatPath);
                string safeFileName = GetSafeFileName(originalFileName);  // Безопасное имя файла
                string filePath = Path.Combine(filesFolder, safeFileName);

                await File.WriteAllBytesAsync(filePath, fileContent);

                // Формируем относительный путь для ссылки в Markdown
                string relativePath = $"files/{safeFileName}";
                string fileInfo = FormatFileInfo(senderNickname, originalFileName, fileContent.Length, timestamp, relativePath);

                // Сохраняем информацию о файле в соответствующий дневной файл чата
                string dateStr = timestamp.ToString("yyyy-MM-dd");
                string messagesFilePath = Path.Combine(chatPath, $"chat_{dateStr}.md");

                if (!File.Exists(messagesFilePath))
                {
                    await CreateDailyChatFileAsync(messagesFilePath, receiverNickname, timestamp);
                }

                await File.AppendAllTextAsync(messagesFilePath, fileInfo, Encoding.UTF8);

                Console.WriteLine($"[SYSTEM] Файл '{originalFileName}' сохранен в чат с {receiverNickname} ({dateStr})"); // [LOG]

                return (true, filePath, relativePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения файла: {ex.Message}"); // [LOG]
                return (false, null, null);
            }
        }

        // Создать папку для файлов внутри папки чата
        // chatPath - путь к папке чата
        // Возвращает путь к созданной папке files
        private string CreateFilesFolder(string chatPath)
        {
            string filesFolder = Path.Combine(chatPath, "files");
            if (!Directory.Exists(filesFolder))
            {
                Directory.CreateDirectory(filesFolder);
                Console.WriteLine($"[SYSTEM] Создана папка для файлов: {filesFolder}"); // [LOG]
            }
            return filesFolder;
        }

        // Создать чат с пользователем (без сообщений)
        // nickname - никнейм пользователя для создания чата
        // Создает структуру папок и базовые файлы для нового чата
        public void CreateChat(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return;

            try
            {
                string chatPath = AppSettings.GetChatPath(nickname);

                // Создаем папку чата если не существует
                if (!Directory.Exists(chatPath))
                {
                    Directory.CreateDirectory(chatPath);

                    // Создаем папку для файлов
                    string filesFolder = Path.Combine(chatPath, "files");
                    if (!Directory.Exists(filesFolder))
                    {
                        Directory.CreateDirectory(filesFolder);
                    }

                    // Создаем файл chat.md с метаинформацией
                    string chatInfoPath = Path.Combine(chatPath, "chat.md");
                    string chatId = $"chat_{nickname}";

                    string chatInfo =
                        $"id: {chatId}\n" +
                        $"title: {nickname}\n" +
                        $"created_at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                        $"---\n";

                    File.WriteAllText(chatInfoPath, chatInfo, Encoding.UTF8);

                    Console.WriteLine($"[SYSTEM] Чат с {nickname} создан");
                }
                else
                {
                    // Чат уже существует, проверяем наличие chat.md
                    string chatInfoPath = Path.Combine(chatPath, "chat.md");
                    if (!File.Exists(chatInfoPath))
                    {
                        // Создаем если отсутствует
                        string chatId = $"chat_{nickname}";
                        string chatInfo =
                            $"id: {chatId}\n" +
                            $"title: {nickname}\n" +
                            $"updated_at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                            $"---\n";

                        File.WriteAllText(chatInfoPath, chatInfo, Encoding.UTF8);
                        Console.WriteLine($"[SYSTEM] Файл chat.md создан для существующего чата {nickname}"); // [LOG]
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка создания чата: {ex.Message}"); // [LOG]
            }
        }

        // ==================== МЕТОДЫ СОЗДАНИЯ ФАЙЛОВ ====================

        // Создать файл информации о чате
        // chatPath - путь к папке чата
        // nickname - никнейм пользователя для создания файла информации
        // Создает файл с метаинформацией о чате в формате YAML фронтматера
        private async Task CreateChatInfoFileAsync(string chatPath, string nickname)
        {
            string infoFilePath = Path.Combine(chatPath, "chat.md");
            string chatId = $"chat_{nickname}";

            string chatInfo =
        $"id: {chatId}\n" +
        $"title: {nickname}\n" +

        $"---\n";


            //string infoFilePath = Path.Combine(chatPath, "chat_info.md");

            //string chatInfo = $"# Чат с пользователем: {nickname}\n" +
            //                 $"## Дата создания: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
            //                 $"## Участники:\n" +
            //                 $"- {AppSettings.Nickname}\n" +
            //                 $"- {nickname}\n\n" +
            //                 $"## Структура файлов:\n" +
            //                 $"- `chat_YYYY-MM-DD.md` - сообщения за определенный день\n" +
            //                 $"- `files/` - сохраненные файлы\n" +
            //                 $"---\n";

            await File.WriteAllTextAsync(infoFilePath, chatInfo, Encoding.UTF8);
        }

        // Создать дневной файл чата с заголовком
        // filePath - полный путь к создаваемому файлу
        // nickname - никнейм пользователя для заголовка
        // date - дата для заголовка файла
        // Создает файл с заголовком дня в формате Markdown
        private async Task CreateDailyChatFileAsync(string filePath, string nickname, DateTime date)
        {
            string dateHeader = date.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
            string header = "\n";

            await File.WriteAllTextAsync(filePath, header, Encoding.UTF8);
            Console.WriteLine($"[SYSTEM] Создан файл чата за {date:yyyy-MM-dd}");
        }

        // ==================== МЕТОДЫ ФОРМАТИРОВАНИЯ ====================

        // Форматировать сообщение в Markdown
        // senderNickname - никнейм отправителя сообщения
        // message - текст сообщения
        // timestamp - время отправки сообщения
        // Возвращает отформатированную строку в формате Markdown
        private string FormatMessageMarkdown(string senderNickname, string message, DateTime timestamp)
        {
            //string timeStr = timestamp.ToString("HH:mm");
            //return $"## {timeStr} | {senderNickname}\n{message}\n\n";

            string timeStr = timestamp.ToString("HH:mm");
            // Проверяем, является ли отправитель текущим пользователем
            string displayNickname = senderNickname.Equals(AppSettings.Nickname, StringComparison.OrdinalIgnoreCase)
                ? "me"
                : senderNickname;
            return $"## {timeStr} | {displayNickname}\n{message}\n\n";

        }

        // Форматировать информацию о файле со ссылкой
        // senderNickname - никнейм отправителя файла
        // fileName - оригинальное имя файла
        // fileSize - размер файла в байтах
        // timestamp - время отправки файла
        // relativePath - относительный путь к файлу для ссылки
        // Возвращает отформатированную строку с информацией о файле в формате Markdown
        private string FormatFileInfo(string senderNickname, string fileName, long fileSize, DateTime timestamp, string relativePath)
        {
            string timeStr = timestamp.ToString("HH:mm");
            string fileType = GetFileType(fileName);  // Определяем тип файла
            string fileLink = $"[{fileName}]({relativePath})";  // Создаем Markdown-ссылку
            string sizeStr = FormatFileSize(fileSize);  // Форматируем размер файла

            return $"## {timeStr} | {senderNickname}\n📎 {fileType}: {fileLink} ({sizeStr})\n\n";
        }

        // Определить тип файла по расширению
        // fileName - имя файла с расширением
        // Возвращает читаемое описание типа файла
        private string GetFileType(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();

            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "Фото",
                ".txt" or ".md" => "Текст",
                ".pdf" => "PDF",
                ".doc" or ".docx" => "Документ Word",
                ".xls" or ".xlsx" => "Таблица Excel",
                ".zip" or ".rar" => "Архив",
                _ => "Файл"
            };
        }

        // Форматировать размер файла в читаемый вид
        // bytes - размер файла в байтах
        // Возвращает строку с размером в соответствующей единице измерения (B, KB, MB, GB)
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        // Создать безопасное имя файла
        // fileName - оригинальное имя файла
        // Возвращает безопасное имя файла с timestamp для уникальности
        private string GetSafeFileName(string fileName)
        {
            string safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string extension = Path.GetExtension(fileName);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);

            return $"{nameWithoutExt}_{timestamp}{extension}";
        }

        // ==================== МЕТОДЫ ПОЛУЧЕНИЯ ИНФОРМАЦИИ ====================

        // Получить список всех чатов
        // Возвращает список никнеймов пользователей, с которыми есть чаты
        public List<string> GetChatList()
        {
            var chats = new List<string>();
            try
            {
                string chatsPath = AppSettings.GetChatsPath();

                if (Directory.Exists(chatsPath))
                {
                    var directories = Directory.GetDirectories(chatsPath);
                    foreach (var dir in directories)
                    {
                        chats.Add(Path.GetFileName(dir));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения списка чатов: {ex.Message}");
            }
            return chats;
        }

        // Получить историю сообщений за определенный день
        // nickname - никнейм пользователя для получения истории
        // date - дата для получения истории (по умолчанию текущая дата)
        // Возвращает строку с историей сообщений в формате Markdown
        public async Task<string> GetChatHistoryAsync(string nickname, DateTime? date = null)
        {
            try
            {
                DateTime targetDate = date ?? DateTime.Now;
                string dateStr = targetDate.ToString("yyyy-MM-dd");
                string messagesPath = Path.Combine(AppSettings.GetChatPath(nickname), $"chat_{dateStr}.md");

                if (File.Exists(messagesPath))
                {
                    return await File.ReadAllTextAsync(messagesPath);
                }
                else
                {
                    // Пытаемся найти ближайший существующий файл
                    var chatFiles = GetChatFilesForUser(nickname);
                    if (chatFiles.Count > 0)
                    {
                        // Сортируем по дате (последний первый)
                        var sortedFiles = chatFiles.OrderByDescending(f => f.Date);
                        var latestFile = sortedFiles.First();

                        string history = await File.ReadAllTextAsync(latestFile.FilePath);
                        return $"# История за {dateStr} не найдена\n" +
                               $"# Показана последняя доступная история ({latestFile.Date:yyyy-MM-dd})\n\n" +
                               history;
                    }

                    return $"Чат с {nickname} не найден или пуст.";
                }
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения истории: {ex.Message}";
            }
        }

        // Получить историю сообщений за диапазон дат
        // nickname - никнейм пользователя для получения истории
        // startDate - начальная дата диапазона
        // endDate - конечная дата диапазона
        // Возвращает конкатенированную историю сообщений за указанный период
        public async Task<string> GetChatHistoryRangeAsync(string nickname, DateTime startDate, DateTime endDate)
        {
            try
            {
                var sb = new StringBuilder();
                var chatFiles = GetChatFilesForUser(nickname);

                // Фильтруем файлы по диапазону дат
                var filesInRange = chatFiles
                    .Where(f => f.Date >= startDate.Date && f.Date <= endDate.Date)
                    .OrderBy(f => f.Date);

                if (!filesInRange.Any())
                {
                    return $"Нет сообщений за период с {startDate:yyyy-MM-dd} по {endDate:yyyy-MM-dd}";
                }

                // Читаем и объединяем содержимое всех файлов в диапазоне
                foreach (var fileInfo in filesInRange)
                {
                    string fileContent = await File.ReadAllTextAsync(fileInfo.FilePath);
                    sb.AppendLine(fileContent);
                    sb.AppendLine("\n---\n");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения истории: {ex.Message}";
            }
        }

        // Получить список файлов чатов для пользователя
        // nickname - никнейм пользователя для поиска файлов
        // Возвращает список кортежей (FilePath, Date) для всех файлов чата пользователя
        private List<(string FilePath, DateTime Date)> GetChatFilesForUser(string nickname)
        {
            var files = new List<(string, DateTime)>();
            try
            {
                string chatPath = AppSettings.GetChatPath(nickname);

                if (Directory.Exists(chatPath))
                {
                    var mdFiles = Directory.GetFiles(chatPath, "chat_*.md");

                    foreach (var file in mdFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.StartsWith("chat_") && fileName.Length > 10)
                        {
                            string dateStr = fileName.Substring(5, 10);
                            if (DateTime.TryParse(dateStr, out DateTime date))
                            {
                                files.Add((file, date));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения файлов чата: {ex.Message}");
            }
            return files;
        }

        // Получить список файлов в чате
        // nickname - никнейм пользователя для получения списка файлов
        // Возвращает список путей к файлам, сохраненным в чате
        public List<string> GetChatFiles(string nickname)
        {
            var files = new List<string>();
            try
            {
                string filesFolder = Path.Combine(AppSettings.GetChatPath(nickname), "files");

                if (Directory.Exists(filesFolder))
                {
                    files.AddRange(Directory.GetFiles(filesFolder));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения списка файлов: {ex.Message}");
            }
            return files;
        }

        // Получить список дат, за которые есть сообщения
        // nickname - никнейм пользователя для получения дат
        // Возвращает список дат, для которых существуют файлы с сообщениями
        public List<DateTime> GetChatDates(string nickname)
        {
            var dates = new List<DateTime>();
            try
            {
                var chatFiles = GetChatFilesForUser(nickname);
                dates = chatFiles.Select(f => f.Date).OrderBy(d => d).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения дат чата: {ex.Message}");
            }
            return dates;
        }

        // ==================== МЕТОДЫ УПРАВЛЕНИЯ ИСТОРИЕЙ ====================

        // Очистить историю чата за определенный день
        // nickname - никнейм пользователя для очистки истории
        // date - дата для очистки (если null, очищает всю историю)
        public void ClearChatHistory(string nickname, DateTime? date = null)
        {
            try
            {
                if (date.HasValue)
                {
                    // Очистка за конкретный день
                    string dateStr = date.Value.ToString("yyyy-MM-dd");
                    string messagesPath = Path.Combine(AppSettings.GetChatPath(nickname), $"chat_{dateStr}.md");

                    if (File.Exists(messagesPath))
                    {
                        File.Delete(messagesPath);
                        Console.WriteLine($"[SYSTEM] История чата с {nickname} за {dateStr} очищена");
                    }
                }
                else
                {
                    // Очистка всей истории
                    var chatFiles = GetChatFilesForUser(nickname);
                    foreach (var file in chatFiles)
                    {
                        File.Delete(file.FilePath);
                    }
                    Console.WriteLine($"[SYSTEM] Вся история чата с {nickname} очищена");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка очистки истории: {ex.Message}");
            }
        }

        // ==================== МЕТОДЫ РАБОТЫ С ЛОКАЛЬНЫМИ ЗАМЕТКАМИ ====================

        // Сохранить заметку в локальный дневник без отправки
        // noteContent - текст заметки
        // timestamp - время создания заметки
        // category - категория заметки (по умолчанию "general")
        // Сохраняет заметку в структурированную файловую систему локальных заметок
        public async Task SaveLocalNoteAsync(string noteContent, DateTime timestamp, string category = "general")
        {
            if (string.IsNullOrWhiteSpace(noteContent))
                return;

            try
            {
                // Создаем папку для локальных заметок
                string localNotesPath = Path.Combine(AppSettings.GetUserDataPath(), "local_notes");
                if (!Directory.Exists(localNotesPath))
                {
                    Directory.CreateDirectory(localNotesPath);
                    Console.WriteLine($"[SYSTEM] Создана папка для локальных заметок: {localNotesPath}");
                }

                // Папка для категории (если указана)
                string categoryPath = Path.Combine(localNotesPath, AppSettings.MakePathSafe(category));
                if (!Directory.Exists(categoryPath))
                {
                    Directory.CreateDirectory(categoryPath);
                    Console.WriteLine($"[SYSTEM] Создана папка категории: {category}");
                }

                // Определяем имя файла по дате
                string dateStr = timestamp.ToString("yyyy-MM-dd");
                string dailyFilePath = Path.Combine(categoryPath, $"notes_{dateStr}.md");

                // Если файл не существует, создаем его с заголовком
                if (!File.Exists(dailyFilePath))
                {
                    await CreateDailyNotesFileAsync(dailyFilePath, category, timestamp);
                }

                // Форматируем заметку
                string formattedNote = FormatLocalNote(noteContent, timestamp);
                await File.AppendAllTextAsync(dailyFilePath, formattedNote, Encoding.UTF8);

                Console.WriteLine($"[SYSTEM] Заметка сохранена в категорию '{category}' ({dateStr})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения заметки: {ex.Message}");
            }
        }

        // Сохранить локальное сообщение (имитация чата с самим собой)
        // senderNickname - никнейм отправителя (обычно текущий пользователь)
        // message - текст сообщения
        // timestamp - время сообщения
        // conversationName - название диалога/разговора (по умолчанию "self")
        // Сохраняет сообщение в структурированную файловую систему локальных диалогов
        public async Task SaveLocalMessageAsync(string senderNickname, string message, DateTime timestamp, string conversationName = "self")
        {
            try
            {
                // Папка для локальных диалогов
                string localChatsPath = Path.Combine(AppSettings.GetUserDataPath(), "local_chats");
                if (!Directory.Exists(localChatsPath))
                {
                    Directory.CreateDirectory(localChatsPath);
                    Console.WriteLine($"[SYSTEM] Создана папка для локальных диалогов: {localChatsPath}");
                }

                // Папка для конкретного диалога
                string conversationPath = Path.Combine(localChatsPath, AppSettings.MakePathSafe(conversationName));
                if (!Directory.Exists(conversationPath))
                {
                    Directory.CreateDirectory(conversationPath);
                    await CreateConversationInfoFileAsync(conversationPath, conversationName);
                    CreateFilesFolder(conversationPath);
                }

                // Определяем имя файла по дате
                string dateStr = timestamp.ToString("yyyy-MM-dd");
                string dailyFilePath = Path.Combine(conversationPath, $"chat_{dateStr}.md");

                // Если файл не существует, создаем его с заголовком
                if (!File.Exists(dailyFilePath))
                {
                    await CreateLocalDailyChatFileAsync(dailyFilePath, conversationName, timestamp);
                }

                // Форматируем сообщение
                string formattedMessage = FormatMessageMarkdown(senderNickname, message, timestamp);
                await File.AppendAllTextAsync(dailyFilePath, formattedMessage, Encoding.UTF8);

                Console.WriteLine($"[SYSTEM] Локальное сообщение сохранено в '{conversationName}' ({dateStr})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения локального сообщения: {ex.Message}");
            }
        }

        // Создать дневной файл для локальных заметок
        // filePath - полный путь к создаваемому файлу
        // category - категория заметок
        // date - дата для заголовка файла
        private async Task CreateDailyNotesFileAsync(string filePath, string category, DateTime date)
        {
            string dateHeader = date.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
            string header = $"# Дневник: {category}\n" +
                           $"## Дата: {dateHeader}\n" +
                           $"## {date:yyyy-MM-dd}\n\n";

            await File.WriteAllTextAsync(filePath, header, Encoding.UTF8);
            Console.WriteLine($"[SYSTEM] Создан файл заметок за {date:yyyy-MM-dd}");
        }

        // Создать дневной файл для локального диалога
        // filePath - полный путь к создаваемому файлу
        // conversationName - название диалога
        // date - дата для заголовка файла
        private async Task CreateLocalDailyChatFileAsync(string filePath, string conversationName, DateTime date)
        {
            string dateHeader = date.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
            string header = $"# Локальный диалог: {conversationName}\n" +
                           $"## Дата: {dateHeader}\n" +
                           $"## {date:yyyy-MM-dd}\n\n" +
                           $"---\n\n";

            await File.WriteAllTextAsync(filePath, header, Encoding.UTF8);
        }

        // Форматировать локальную заметку
        // noteContent - текст заметки
        // timestamp - время создания заметки
        // Возвращает отформатированную заметку в формате Markdown
        private string FormatLocalNote(string noteContent, DateTime timestamp)
        {
            string timeStr = timestamp.ToString("HH:mm");
            return $"## {timeStr} | 📝 Заметка\n{noteContent}\n\n";
        }

        // Создать файл информации о локальном диалоге
        // conversationPath - путь к папке диалога
        // conversationName - название диалога
        private async Task CreateConversationInfoFileAsync(string conversationPath, string conversationName)
        {
            string infoFilePath = Path.Combine(conversationPath, "chat_info.md");

            string chatInfo = $"# Локальный диалог: {conversationName}\n" +
                             $"## Дата создания: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                             $"## Участники:\n" +
                             $"- {AppSettings.Nickname}\n\n" +
                             $"---\n";

            await File.WriteAllTextAsync(infoFilePath, chatInfo, Encoding.UTF8);
        }

        // ==================== МЕТОДЫ ПОЛУЧЕНИЯ ЛОКАЛЬНОЙ ИНФОРМАЦИИ ====================

        // Получить список локальных диалогов
        // Возвращает список названий локальных диалогов
        public List<string> GetLocalConversations()
        {
            var conversations = new List<string>();
            try
            {
                string localChatsPath = Path.Combine(AppSettings.GetUserDataPath(), "local_chats");

                if (Directory.Exists(localChatsPath))
                {
                    var directories = Directory.GetDirectories(localChatsPath);
                    foreach (var dir in directories)
                    {
                        conversations.Add(Path.GetFileName(dir));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения списка локальных диалогов: {ex.Message}");
            }
            return conversations;
        }

        // Получить список категорий заметок
        // Возвращает список названий категорий локальных заметок
        public List<string> GetNoteCategories()
        {
            var categories = new List<string>();
            try
            {
                string localNotesPath = Path.Combine(AppSettings.GetUserDataPath(), "local_notes");

                if (Directory.Exists(localNotesPath))
                {
                    var directories = Directory.GetDirectories(localNotesPath);
                    foreach (var dir in directories)
                    {
                        categories.Add(Path.GetFileName(dir));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка получения категорий заметок: {ex.Message}");
            }
            return categories;
        }

        // Получить историю локального диалога
        // conversationName - название диалога
        // date - дата для получения истории (по умолчанию текущая)
        // Возвращает историю диалога в формате Markdown
        public async Task<string> GetLocalConversationHistoryAsync(string conversationName, DateTime? date = null)
        {
            try
            {
                DateTime targetDate = date ?? DateTime.Now;
                string dateStr = targetDate.ToString("yyyy-MM-dd");
                string conversationPath = Path.Combine(AppSettings.GetUserDataPath(), "local_chats", AppSettings.MakePathSafe(conversationName));

                string messagesPath = Path.Combine(conversationPath, $"chat_{dateStr}.md");

                if (File.Exists(messagesPath))
                {
                    return await File.ReadAllTextAsync(messagesPath);
                }
                else
                {
                    // Ищем все файлы диалога
                    if (Directory.Exists(conversationPath))
                    {
                        var mdFiles = Directory.GetFiles(conversationPath, "chat_*.md");
                        if (mdFiles.Length > 0)
                        {
                            // Берем последний файл
                            var latestFile = mdFiles.OrderByDescending(f => f).First();
                            string history = await File.ReadAllTextAsync(latestFile);
                            return $"# История за {dateStr} не найдена\n" +
                                   $"# Показана последняя доступная история\n\n" +
                                   history;
                        }
                    }

                    return $"Локальный диалог '{conversationName}' не найден или пуст.";
                }
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения истории: {ex.Message}";
            }
        }

        // Получить историю заметок по категории
        // category - категория заметок
        // date - дата для получения истории (по умолчанию текущая)
        // Возвращает историю заметок в формате Markdown
        public async Task<string> GetNotesHistoryAsync(string category, DateTime? date = null)
        {
            try
            {
                DateTime targetDate = date ?? DateTime.Now;
                string dateStr = targetDate.ToString("yyyy-MM-dd");
                string categoryPath = Path.Combine(AppSettings.GetUserDataPath(), "local_notes", AppSettings.MakePathSafe(category));

                string notesPath = Path.Combine(categoryPath, $"notes_{dateStr}.md");

                if (File.Exists(notesPath))
                {
                    return await File.ReadAllTextAsync(notesPath);
                }
                else
                {
                    // Ищем все файлы категории
                    if (Directory.Exists(categoryPath))
                    {
                        var mdFiles = Directory.GetFiles(categoryPath, "notes_*.md");
                        if (mdFiles.Length > 0)
                        {
                            // Берем последний файл
                            var latestFile = mdFiles.OrderByDescending(f => f).First();
                            string history = await File.ReadAllTextAsync(latestFile);
                            return $"# Заметки за {dateStr} не найдены\n" +
                                   $"# Показаны последние доступные заметки\n\n" +
                                   history;
                        }
                    }

                    return $"Категория заметок '{category}' не найдена или пуста.";
                }
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения заметок: {ex.Message}";
            }
        }
    }
}