﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

namespace network
{
    /// <summary>
    /// Статический класс для централизованного хранения и управления настройками приложения
    /// Реализует механизмы загрузки, сохранения и валидации всех конфигурационных параметров
    /// Включает настройки сети, интерфейсов, пользователя и файловых путей
    /// </summary>
    public static class AppSettings
    {
        // ============ ФАЙЛОВЫЕ ПУТИ И СТРУКТУРА ХРАНЕНИЯ ============

        // Базовый путь для хранения всех данных приложения (относительно рабочей директории)
        private static string _basePath = "other_data";

        // Путь для хранения сохраненных сообщений внутри базовой директории
        private static string _messagesPath = "saved_messages";

        // Путь для хранения чатов (каналов) внутри директории сообщений
        private static string _chatsPath = "saved-channels";

        // Путь для временных файлов (например, списка IP)
        private static string _tempPath = "temp";

        // Путь для пользовательских данных (никнейм, настройки)
        private static string _userDataPath = "user_data";

        // Имя файла для хранения списка обнаруженных IP-адресов
        private static string _ipListPath = "ip_list.txt";

        // Имя файла для хранения никнейма пользователя
        private static string _nicknamePath = "nickname.txt";

        // Имя файла для хранения всех настроек приложения
        private static string _settingsPath = "settings.conf";

        // ============ НАСТРОЙКИ ПОЛЬЗОВАТЕЛЯ И ПРИЛОЖЕНИЯ ============

        // Текущий никнейм пользователя (отображается в чате)
        private static string _nickname = "DefaultUser";

        // Флаг автоматического сохранения всех входящих и исходящих сообщений
        private static bool _autoSaveMessages = true;

        // Флаг автоматического подключения к обнаруженным в сети клиентам
        private static bool _autoConnectDiscovered = true;

        // Порт по умолчанию для сетевых соединений (должен совпадать у всех клиентов)
        private static int _defaultPort = 46000;

        // ============ НАСТРОЙКИ АВТОМАТИЧЕСКОГО СКАНИРОВАНИЯ СЕТИ ============

        // Интервал автоматического сканирования в секундах (0 = автосканирование отключено)
        private static int _autoScanInterval = 0;

        // Флаг активности автосканирования (вычисляется на основе интервала)
        private static bool _autoScanEnabled = false;

        // ============ НАСТРОЙКИ СЕТИ И ПОДКЛЮЧЕНИЙ ============

        // Диапазоны IP-адресов для сканирования (разбиты на группы для оптимизации)
        private static readonly string[] _defaultScanRanges = new string[]
        {
            "1-25",
            "26-50",
            "51-75",
            "76-100",
            "101-125",
            "126-150",
            "151-175",
            "176-200",
            "201-225",
            "226-254"
        };

        // Таймаут сетевого соединения в миллисекундах
        private static int _connectionTimeout = 5000;

        // Количество попыток переподключения при разрыве соединения
        private static int _reconnectAttempts = 3;

        // ============ НАСТРОЙКИ СЕТЕВЫХ ИНТЕРФЕЙСОВ ============

        // Режим выбора интерфейса: "auto", "wifi", "ethernet"
        private static string _interfaceMode = "wifi";

        // Имя конкретного беспроводного адаптера (пустая строка = автоопределение)
        private static string _wirelessAdapterName = "";

        // Приоритет беспроводных интерфейсов при автоматическом выборе
        private static bool _preferWireless = true;

        // Максимальное количество клиентов для сканирования в WiFi сетях (оптимизация производительности)
        private static int _maxWirelessClients = 50;

        // Таймаут сканирования для WiFi интерфейсов в миллисекундах (обычно больше чем для Ethernet)
        private static int _wirelessScanTimeout = 2000;

        // Принудительное использование только беспроводных интерфейсов (блокировка Ethernet)
        private static bool _forceWirelessOnly = true;

        // ============ ФОРМАТЫ ОТОБРАЖЕНИЯ ВРЕМЕНИ И ДАТЫ ============

        // Формат временной метки для сообщений (например, "HH:mm" = 14:30)
        private static string _timestampFormat = "HH:mm";

        // Формат даты для группировки сообщений (например, "yyyy-MM-dd" = 2024-01-15)
        private static string _dateFormat = "yyyy-MM-dd";

        // ============ СВОЙСТВА ДЛЯ УПРАВЛЯЕМОГО ДОСТУПА К НАСТРОЙКАМ ============
        // Каждое свойство включает валидацию, уведомления и автоматическое сохранение

        // Никнейм пользователя для идентификации в сети
        // При изменении автоматически сохраняется в файл
        public static string Nickname
        {
            get => _nickname;
            set
            {
                _nickname = value;
                SaveNicknameToFile();  // Автосохранение при изменении
            }
        }
        // Флаг автоматического сохранения всех сообщений в локальные файлы
        // Включает/выключает механизм логирования переписки
        public static bool AutoSaveMessages
        {
            get => _autoSaveMessages;
            set => _autoSaveMessages = value;
        }

        // Флаг автоматического подключения к обнаруженным в сети клиентам
        // Упрощает установку соединений в автоматическом режиме
        public static bool AutoConnectDiscovered
        {
            get => _autoConnectDiscovered;
            set => _autoConnectDiscovered = value;
        }

        // Порт по умолчанию для всех сетевых операций (прослушивание, подключение)
        // Должен быть в диапазоне 1-65535 и одинаковым для всех клиентов сети
        public static int DefaultPort
        {
            get => _defaultPort;
            set => _defaultPort = value;
        }

        // Интервал автоматического сканирования сети в секундах
        // Значение 0 отключает автосканирование
        // При изменении обновляет флаг активности и сохраняет настройки
        public static int AutoScanInterval
        {
            get => _autoScanInterval;
            set
            {
                _autoScanInterval = value;
                _autoScanEnabled = value > 0;  // Автоматически вычисляем активность
                SaveAllSettings();  // Сохраняем изменения в файл
            }
        }

        // Флаг активности автосканирования (только для чтения)
        // Вычисляется на основе интервала: true если интервал > 0
        public static bool AutoScanEnabled => _autoScanEnabled;

        // Диапазоны IP-адресов для сканирования (только для чтения)
        // Предопределенные диапазоны для оптимизации сетевого сканирования
        public static string[] ScanRanges => _defaultScanRanges;

        // Таймаут сетевых соединений в миллисекундах
        // Определяет максимальное время ожидания ответа от удаленного хоста
        public static int ConnectionTimeout
        {
            get => _connectionTimeout;
            set => _connectionTimeout = value;
        }
        // Количество попыток переподключения при разрыве соединения
        // Увеличивает надежность сетевых соединений
        public static int ReconnectAttempts
        {
            get => _reconnectAttempts;
            set => _reconnectAttempts = value;
        }

        // Формат временных меток для отображения в сообщениях
        // Использует стандартные спецификаторы формата .NET DateTime
        public static string TimestampFormat
        {
            get => _timestampFormat;
            set => _timestampFormat = value;
        }
        
        // Формат даты для группировки и отображения сообщений
        // Используется при сохранении и организации истории переписки
        public static string DateFormat
        {
            get => _dateFormat;
            set => _dateFormat = value;
        }

        // ============ НАСТРОЙКИ СЕТЕВЫХ ИНТЕРФЕЙСОВ (ПРОДОЛЖЕНИЕ) ============

        // Режим выбора сетевого интерфейса: "auto", "wifi", "ethernet"
        // Определяет стратегию выбора активного интерфейса для сетевых операций
        public static string InterfaceMode
        {
            get => _interfaceMode;
            set => _interfaceMode = value;
        }

        // Имя конкретного беспроводного адаптера для принудительного использования
        // Пустая строка означает автоопределение наиболее подходящего адаптера
        public static string WirelessAdapterName
        {
            get => _wirelessAdapterName;
            set => _wirelessAdapterName = value;
        }

        // Приоритет беспроводных интерфейсов при автоматическом выборе
        // Если true и доступны оба типа интерфейсов, будет выбран WiFi
        public static bool PreferWireless
        {
            get => _preferWireless;
            set => _preferWireless = value;
        }

        // Максимальное количество клиентов для сканирования в WiFi сетях
        // Оптимизация производительности для беспроводных сетей
        public static int MaxWirelessClients
        {
            get => _maxWirelessClients;
            set => _maxWirelessClients = value;
        }

        // Таймаут сканирования для WiFi интерфейсов в миллисекундах
        // Обычно больше чем для Ethernet из-за особенностей беспроводных сетей
        public static int WirelessScanTimeout
        {
            get => _wirelessScanTimeout;
            set => _wirelessScanTimeout = value;
        }

        // Принудительное использование только беспроводных интерфейсов
        // Если true, Ethernet интерфейсы будут полностью игнорироваться
        public static bool ForceWirelessOnly
        {
            get => _forceWirelessOnly;
            set => _forceWirelessOnly = value;
        }

        // ============ ПУТИ К ФАЙЛАМ И ДИРЕКТОРИЯМ (ТОЛЬКО ДЛЯ ЧТЕНИЯ) ============
        // Предоставляют доступ к сконфигурированным путям файловой системы

        //Базовый путь для хранения всех данных приложения
        public static string BasePath => _basePath;

        // Путь для хранения сохраненных сообщений
        public static string MessagesPath => _messagesPath;

        // Путь для хранения чатов (каналов)
        public static string ChatsPath => _chatsPath;

        // Путь для временных файлов
        public static string TempPath => _tempPath;

        // Путь для пользовательских данных
        public static string UserDataPath => _userDataPath;

        // ============ МЕТОДЫ ДЛЯ РАБОТЫ С ПУТЯМИ И ФАЙЛАМИ ============

        // Получить полный путь к файлу списка IP-адресов
        // </summary>
        // <returns>Абсолютный путь к файлу ip_list.txt</returns>
        public static string GetIpListFilePath()
        {
            return Path.Combine(GetTempPath(), _ipListPath);
        }

        // Получить полный путь к файлу никнейма пользователя
        // возвращает абсолютный путь к файлу nickname.txt
        public static string GetNicknameFilePath()
        {
            return Path.Combine(GetUserDataPath(), _nicknamePath);
        }
        
        // Получить полный путь к файлу настроек приложения
        // возвращает абсолютный путь к файлу settings.conf
        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetUserDataPath(), _settingsPath);
        }
        

        // Получить полный путь к директории временных файлов
        // возвращает абсолютный путь к директории temp
        public static string GetTempPath()
        {
            return Path.Combine(_basePath, _tempPath);
        }
        // Получить полный путь к директории пользовательских данных
        // возвращает абсолютный путь к директории user_data
        public static string GetUserDataPath()
        {
            return Path.Combine(_basePath, _userDataPath);
        }

        // Получить полный путь к директории сохраненных сообщений
        // возвращает абсолютный путь к директории saved_messages
        public static string GetMessagesPath()
        {
            return Path.Combine(_basePath, _messagesPath);
        }

        
        // Получить полный путь к директории чатов
        // возвращает абсолютный путь к директории saved-channels
        public static string GetChatsPath()
        {
            return Path.Combine(GetMessagesPath(), _chatsPath);
        }

        // Получить путь к директории конкретного чата по никнейму собеседника 
        // nickname - Никнейм собеседника
        // возвращает абсолютный путь к директории чата
        public static string GetChatPath(string nickname)
        {
            return Path.Combine(GetChatsPath(), $"chat_{MakePathSafe(nickname)}");
        }

        // Преобразовать строку в безопасный для файловой системы формат
        // Удаляет запрещенные символы из имени файла/директории
        // input - Исходная строка
        // возвращает безопасную для файловой системы строку
        public static string MakePathSafe(string input)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return string.Concat(input.Where(c => !invalidChars.Contains(c)));
        }

        // ============ МЕТОДЫ ИНИЦИАЛИЗАЦИИ И ЗАГРУЗКИ НАСТРОЕК ============

        // Основной метод инициализации системы настроек
        // Выполняет создание структуры директорий, загрузку сохраненных настроек
        // Должен вызываться при старте приложения до использования любых настроек
        public static void Initialize()
        {
            Console.WriteLine("[SYSTEM] Инициализация настроек...");

            DeterminePaths();          // Определение оптимальных путей для текущей ОС
            CreateDirectoryStructure(); // Создание необходимой структуры директорий
            LoadSavedSettings();       // Загрузка настроек из файлов

            Console.WriteLine("[SYSTEM] Настройки инициализированы");
            Console.WriteLine($"[SYSTEM] Ник: {_nickname}");
            Console.WriteLine($"[SYSTEM] Порт: {_defaultPort}");
            Console.WriteLine($"[SYSTEM] Режим интерфейса: {_interfaceMode}");
            Console.WriteLine($"[SYSTEM] Только WiFi: {_forceWirelessOnly}");
            Console.WriteLine($"[SYSTEM] Путь к данным: {_basePath}");
        }

        // Определение оптимальных путей для файлов в зависимости от операционной системы
        // В текущей реализации только логирует тип ОС, но может быть расширен
        // для разных платформ (Windows, Linux, macOS)
        private static void DeterminePaths()
        {
            Console.WriteLine($"[SYSTEM] ОС: {Environment.OSVersion.Platform}");
            // В будущем здесь может быть логика адаптации путей под разные ОС
        }

        // Создание полной структуры директорий для хранения данных приложения
        // Гарантирует существование всех необходимых папок перед началом работы
        private static void CreateDirectoryStructure()
        {
            try
            {
                string[] directories = {
                    _basePath,          // Корневая директория данных
                    GetTempPath(),      // Временные файлы
                    GetUserDataPath(),  // Пользовательские данные
                    GetMessagesPath(),  // Сообщения
                    GetChatsPath()      // Чаты
                };

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        Console.WriteLine($"[SYSTEM] Создана папка: {dir}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка создания структуры папок: {ex.Message}");
            }
        }

        // Загрузка сохраненных настроек из файлов
        // Загружает никнейм из nickname.txt и настройки из settings.conf
        // Сохраняет значения по умолчанию если файлы не существуют или повреждены
        private static void LoadSavedSettings()
        {
            try
            {
                string nicknameFile = GetNicknameFilePath();
                if (File.Exists(nicknameFile))
                {
                    string savedNickname = File.ReadAllText(nicknameFile).Trim();
                    if (!string.IsNullOrWhiteSpace(savedNickname))
                    {
                        _nickname = savedNickname;  // Восстанавливаем сохраненный никнейм
                    }
                }

                string settingsFile = GetSettingsFilePath();
                if (File.Exists(settingsFile))
                {
                    LoadSettingsFromFile(settingsFile);  // Загружаем настройки из конфигурационного файла
                }

                Console.WriteLine($"[SYSTEM] Ник загружен: {_nickname}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки настроек: {ex.Message}");
            }
        }

        // Загрузка настроек из конкретного конфигурационного файла
        // Парсит файл в формате "ключ=значение", игнорирует комментарии и пустые строки
        // filePath - Путь к конфигурационному файлу
        private static void LoadSettingsFromFile(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;  // Пропускаем пустые строки и комментарии

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        // Обработка различных параметров конфигурации
                        switch (key.ToLower())
                        {
                            case "autosavemessages":
                                if (bool.TryParse(value, out bool autoSave))
                                    _autoSaveMessages = autoSave;
                                break;

                            case "autoconnectdiscovered":
                                if (bool.TryParse(value, out bool autoConnect))
                                    _autoConnectDiscovered = autoConnect;
                                break;

                            case "defaultport":
                                if (int.TryParse(value, out int port) && port > 0 && port <= 65535)
                                    _defaultPort = port;
                                break;

                            case "connectiontimeout":
                                if (int.TryParse(value, out int timeout) && timeout > 0)
                                    _connectionTimeout = timeout;
                                break;

                            case "reconnectattempts":
                                if (int.TryParse(value, out int attempts) && attempts >= 0)
                                    _reconnectAttempts = attempts;
                                break;

                            case "timestampformat":
                                _timestampFormat = value;
                                break;

                            case "dateformat":
                                _dateFormat = value;
                                break;

                            case "autoscaninterval":
                                if (int.TryParse(value, out int interval) && interval >= 0)
                                    _autoScanInterval = interval;
                                break;

                            case "interfacemode":
                                _interfaceMode = value.ToLower();
                                break;

                            case "wirelessadaptername":
                                _wirelessAdapterName = value;
                                break;

                            case "preferwireless":
                                if (bool.TryParse(value, out bool preferWireless))
                                    _preferWireless = preferWireless;
                                break;

                            case "forcewirelessonly":
                                if (bool.TryParse(value, out bool forceWireless))
                                    _forceWirelessOnly = forceWireless;
                                break;

                            case "maxwirelessclients":
                                if (int.TryParse(value, out int maxClients) && maxClients > 0)
                                    _maxWirelessClients = maxClients;
                                break;

                            case "wirelessscantimeout":
                                if (int.TryParse(value, out int wirelessTimeout) && wirelessTimeout > 0)
                                    _wirelessScanTimeout = wirelessTimeout;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка чтения настроек из файла: {ex.Message}");
            }
        }

        // ============ МЕТОДЫ СОХРАНЕНИЯ НАСТРОЕК ============

        // Сохранение всех текущих настроек в файлы
        // Сохраняет никнейм и все параметры конфигурации
        // Вызывается автоматически при изменении критических настроек
        public static void SaveAllSettings()
        {
            try
            {
                SaveNicknameToFile();  // Сохраняем никнейм
                SaveSettingsToFile();  // Сохраняем все настройки
                Console.WriteLine("[SYSTEM] Все настройки сохранены");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения настроек: {ex.Message}");
            }
        }

        // Сохранение текущего никнейма в файл
        // Вызывается автоматически при изменении свойства Nickname
        private static void SaveNicknameToFile()
        {
            try
            {
                string nicknameFile = GetNicknameFilePath();
                File.WriteAllText(nicknameFile, _nickname);
                Console.WriteLine($"[SYSTEM] Ник сохранен: {_nickname}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения ника: {ex.Message}");
            }
        }

        // Сохранение всех настроек в конфигурационный файл
        // Создает файл с комментариями и структурированными разделами
        // Формат: "ключ = значение" с группировкой по разделам
        private static void SaveSettingsToFile()
        {
            try
            {
                string settingsFile = GetSettingsFilePath();
                var lines = new List<string>
                {
                    "# Настройки сетевого мессенджера",
                    "",
                    "# ============ НАСТРОЙКИ ИНТЕРФЕЙСА ============",
                    "# Режим интерфейса (auto, wifi, ethernet)",
                    $"InterfaceMode = {_interfaceMode}",
                    "",
                    "# Имя беспроводного адаптера (оставьте пустым для автоопределения)",
                    $"WirelessAdapterName = {_wirelessAdapterName}",
                    "",
                    "# Предпочитать беспроводные интерфейсы (true/false)",
                    $"PreferWireless = {_preferWireless}",
                    "",
                    "# Принудительно использовать только WiFi (true/false)",
                    $"ForceWirelessOnly = {_forceWirelessOnly}",
                    "",
                    "# Максимальное количество клиентов для сканирования WiFi",
                    $"MaxWirelessClients = {_maxWirelessClients}",
                    "",
                    "# Таймаут сканирования WiFi (миллисекунды)",
                    $"WirelessScanTimeout = {_wirelessScanTimeout}",
                    "",
                    "# ============ НАСТРОЙКИ ПОЛЬЗОВАТЕЛЯ ============",
                    "# Автоматически сохранять сообщения",
                    $"AutoSaveMessages = {_autoSaveMessages}",
                    "",
                    "# Автоматически подключаться к найденным клиентам",
                    $"AutoConnectDiscovered = {_autoConnectDiscovered}",
                    "",
                    "# Порт по умолчанию",
                    $"DefaultPort = {_defaultPort}",
                    "",
                    "# ============ НАСТРОЙКИ СЕТИ ============",
                    "# Таймаут соединения (мс)",
                    $"ConnectionTimeout = {_connectionTimeout}",
                    "",
                    "# Количество попыток переподключения",
                    $"ReconnectAttempts = {_reconnectAttempts}",
                    "",
                    "# Формат временной метки",
                    $"TimestampFormat = {_timestampFormat}",
                    "",
                    "# Формат даты",
                    $"DateFormat = {_dateFormat}",
                    "",
                    "# ============ НАСТРОЙКИ АВТОСКАНИРОВАНИЯ ============",
                    "# Интервал автосканирования (секунды, 0 = выключено)",
                    $"AutoScanInterval = {_autoScanInterval}"
                };

                File.WriteAllLines(settingsFile, lines);
                Console.WriteLine($"[SYSTEM] Настройки сохранены в: {settingsFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения настроек: {ex.Message}");
            }
        }

        // ============ МЕТОДЫ ОТОБРАЖЕНИЯ И ОБНОВЛЕНИЯ НАСТРОЕК ============

        // Получить форматированную строку с текущими настройками приложения
        // Используется для вывода в консоль, лог или интерфейс пользователя
        // возвращает отформатированную строку со сводкой всех настроек
        public static string GetSettingsSummary()
        {
            var interfaceInfo = GetInterfaceInfo();

            return $"[SYSTEM] === ТЕКУЩИЕ НАСТРОЙКИ ===\n" +
                   $"Ник: {_nickname}\n" +
                   $"Порт: {_defaultPort}\n" +
                   $"Режим интерфейса: {_interfaceMode}\n" +
                   $"Только WiFi: {_forceWirelessOnly}\n" +
                   $"Адаптер: {(!string.IsNullOrEmpty(_wirelessAdapterName) ? _wirelessAdapterName : "авто")}\n" +
                   $"Авто-сохранение: {_autoSaveMessages}\n" +
                   $"Авто-подключение: {_autoConnectDiscovered}\n" +
                   $"Автосканирование: {(_autoScanEnabled ? $"каждые {_autoScanInterval} секунд" : "выключено")}\n" +
                   $"Таймаут: {_connectionTimeout} мс\n" +
                   $"Путь к данным: {_basePath}";
        }

        // Получить информацию о доступных сетевых интерфейсах системы
        // Используется для диагностики и отладки сетевых настроек
        // возвращает информацию с строкой с информацией о WiFi адаптерах
        private static string GetInterfaceInfo()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                var wifiInterfaces = interfaces
                    .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                if (wifiInterfaces.Count == 0)
                    return "WiFi адаптеры не найдены";

                return $"Найдено WiFi адаптеров: {wifiInterfaces.Count}";
            }
            catch
            {
                return "Ошибка получения информации об интерфейсах";
            }
        }

        // Обновить конкретную настройку по имени
        // Универсальный метод для изменения настроек через командный интерфейс
        // settingName - Имя настройки (регистронезависимое)
        // value - Новое значение настройки (в строковом формате)
        public static void UpdateSetting(string settingName, string value)
        {
            switch (settingName.ToLower())
            {
                case "nickname":
                    Nickname = value;
                    break;

                case "autosavemessages":
                    if (bool.TryParse(value, out bool autoSave))
                        AutoSaveMessages = autoSave;
                    break;

                case "autoconnectdiscovered":
                    if (bool.TryParse(value, out bool autoConnect))
                        AutoConnectDiscovered = autoConnect;
                    break;

                case "defaultport":
                    if (int.TryParse(value, out int port) && port > 0 && port <= 65535)
                        DefaultPort = port;
                    break;

                case "connectiontimeout":
                    if (int.TryParse(value, out int timeout) && timeout > 0)
                        ConnectionTimeout = timeout;
                    break;

                case "autoscaninterval":
                    if (int.TryParse(value, out int interval) && interval >= 0)
                        AutoScanInterval = interval;
                    break;

                case "timestampformat":
                    TimestampFormat = value;
                    break;

                case "interfacemode":
                    InterfaceMode = value.ToLower();
                    break;

                case "wirelessadaptername":
                    WirelessAdapterName = value;
                    break;

                case "preferwireless":
                    if (bool.TryParse(value, out bool preferWireless))
                        PreferWireless = preferWireless;
                    break;

                // case "forcewirelessonly":
                //     if (bool.TryParse(value, out bool forceWireless))
                //         ForceWirelessOnly = forceWireless;
                //     break;

                case "maxwirelessclients":
                    if (int.TryParse(value, out int maxClients) && maxClients > 0)
                        MaxWirelessClients = maxClients;
                    break;

                case "wirelessscantimeout":
                    if (int.TryParse(value, out int wirelessTimeout) && wirelessTimeout > 0)
                        WirelessScanTimeout = wirelessTimeout;
                    break;

                default:
                    Console.WriteLine($"[ERROR] Неизвестная настройка: {settingName}");
                    break;
            }

            SaveAllSettings();  // Сохраняем изменения после обновления
        }
    }
}