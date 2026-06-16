using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace network
{

    // Менеджер для хранения и управления связками IP-ник
    // Обеспечивает двустороннее отображение между IP-адресами и никнеймами пользователей,
    // сохраняет сопоставления в файл для восстановления между сеансами работы приложения
    // Используется для удобной работы с пользователями по именам вместо IP-адресов
    public class NicknameManager
    {
        private readonly string _mappingFilePath;  // Путь к файлу сохранения сопоставлений
        private readonly ConcurrentDictionary<string, string> _nicknameToIp;  // Отображение никнейм → IP (потокобезопасное)
        private readonly ConcurrentDictionary<string, string> _ipToNickname;  // Отображение IP → никнейм (потокобезопасное)

        
        // Конструктор менеджера никнеймов
        // Инициализирует коллекции и загружает сохраненные сопоставления из файла
        
        public NicknameManager()
        {
            _mappingFilePath = Path.Combine(AppSettings.GetUserDataPath(), "nickname_mappings.txt");
            _nicknameToIp = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _ipToNickname = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            LoadMappings();  // Загружаем сохраненные сопоставления при инициализации
        }

        
        // Загрузить сопоставления из файла
        // Восстанавливает отображения IP-адресов и никнеймов из текстового файла
        // Формат файла: каждая строка содержит "IP=никнейм"
        
        private void LoadMappings()
        {
            try
            {
                if (!File.Exists(_mappingFilePath))
                    return;  // Файл не существует - начинаем с пустых сопоставлений

                // Читаем все строки из файла
                var lines = File.ReadAllLines(_mappingFilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
                        continue;  // Пропускаем некорректные строки

                    // Разделяем строку на IP и никнейм
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        string ip = parts[0].Trim();
                        string nickname = parts[1].Trim();

                        // Добавляем в оба словаря для двустороннего доступа
                        _ipToNickname[ip] = nickname;
                        _nicknameToIp[nickname] = ip;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки маппингов: {ex.Message}");
            }
        }

        
        // Сохранить сопоставления в файл
        // Пробразует текущие отображения в текстовый файл для последующего восстановления
        
        private void SaveMappings()
        {
            try
            {
                // Формируем строки в формате "IP=никнейм" из словаря IP → никнейм
                var lines = _ipToNickname.Select(kvp => $"{kvp.Key}={kvp.Value}");
                // Записываем все строки в файл
                File.WriteAllLines(_mappingFilePath, lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения маппингов: {ex.Message}");
            }
        }


        // Обновить или добавить сопоставление IP-адреса и никнейма
        // Если никнейм уже связан с другим IP, старый IP удаляется
        // Обеспечивает уникальность сопоставлений (один никнейм → один IP)
        
        // ip - IP-адрес для сопоставления
        // nickname - Никнейм пользователя
        public void UpdateMapping(string ip, string nickname)
        {
            // Проверяем, не связан ли уже этот никнейм с другим IP
            if (_nicknameToIp.TryGetValue(nickname, out string oldIp) && oldIp != ip)
            {
                // Удаляем старое сопоставление IP → никнейм
                _ipToNickname.TryRemove(oldIp, out _);
                Console.WriteLine($"[SYSTEM] Обновлен IP для {nickname}: {oldIp} → {ip}");
            }

            // Добавляем/обновляем сопоставления в обоих словарях
            _nicknameToIp[nickname] = ip;
            _ipToNickname[ip] = nickname;

            SaveMappings();  // Сохраняем изменения в файл
        }


        // Получить IP-адрес по никнейму
        
        // nickname - Никнейм пользователя
        // возвращает IP-адрес, связанный с никнеймом, или null если не найден
        public string GetIpByNickname(string nickname)
        {
            return _nicknameToIp.TryGetValue(nickname, out string ip) ? ip : null;
        }

        
        // Получить никнейм по IP-адресу
        // ip - IP-адрес
        // возвращает никнейм, связанный с IP-адресом, или null если не найден
        public string GetNicknameByIp(string ip)
        {
            return _ipToNickname.TryGetValue(ip, out string nickname) ? nickname : null;
        }

        // Удалить сопоставление по IP-адресу
        // Удаляет связь для указанного IP-адреса из обоих словарей
        // ip - IP-адрес для удаления
        public void RemoveByIp(string ip)
        {
            // Пытаемся удалить из словаря IP → никнейм и получить связанный никнейм
            if (_ipToNickname.TryRemove(ip, out string nickname))
            {
                // Удаляем соответствующее сопоставление из словаря никнейм → IP
                _nicknameToIp.TryRemove(nickname, out _);
                SaveMappings();  // Сохраняем изменения
            }
        }

        // Удалить сопоставление по никнейму
        // Удаляет связь для указанного никнейма из обоих словарей
        // nickname - Никнейм для удаления
        public void RemoveByNickname(string nickname)
        {
            // Пытаемся удалить из словаря никнейм → IP и получить связанный IP
            if (_nicknameToIp.TryRemove(nickname, out string ip))
            {
                // Удаляем соответствующее сопоставление из словаря IP → никнейм
                _ipToNickname.TryRemove(ip, out _);
                SaveMappings();  // Сохраняем изменения
            }
        }

        // Получить строковое представление всех сопоставлений
        // Возвращает форматированный список всех связок IP ↔ никнейм
        // возвращает строку с перечислением всех сопоставлений
        public string GetAllMappings()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[MAPPINGS] Текущие связки:");

            // Сортируем по никнеймам для удобного чтения
            foreach (var kvp in _ipToNickname.OrderBy(x => x.Value))
            {
                sb.AppendLine($"  {kvp.Value} ↔ {kvp.Key}");
            }

            return sb.ToString();
        }

        // Поиск сопоставлений по запросу
        // Ищет совпадения как в IP-адресах, так и в никнеймах
        // query - Строка для поиска
        // возвращает строку с результатами поиска в формате никнейм (IP)
        public string Search(string query)
        {
            // Ищем совпадения в IP-адресах (точное совпадение) и никнеймах (без учета регистра)
            var results = _ipToNickname
                .Where(kvp => kvp.Key.Contains(query) || kvp.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => $"{kvp.Value} ({kvp.Key})");

            // Объединяем результаты через запятую
            return string.Join(", ", results);
        }
    }
}