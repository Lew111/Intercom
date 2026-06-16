using System;  // Основные системные классы 
using System.Collections.Generic;  // Коллекции (List, HashSet)
using System.IO;  // Работа с файловой системой
using System.Linq;  // LINQ для работы с коллекциями
using System.Threading.Tasks;  // Асинхронное программирование
using network;  // Использование пространства имён network

namespace wr  // Пространство имен wr (сокращение от Writer)
{
    // Класс Writer для управления файлом со списком IP-адресов
    // Обеспечивает чтение, запись и отображение сохраненных IP-адресов
    public class WriterIP
    {
        // Приватное поле для хранения пути к файлу со списком IP-адресов, предназначенное только для чтения, изменить из этого скрипта не полукчится
        private readonly string _ipListPath;

        // Конструктор класса Writer
        public WriterIP()
        {
            // Получение пути к файлу из настроек приложения
            // AppSettings.GetIpListFilePath() возвращает полный путь из скрипта AppSettings, из класса GetIpListFilePath к файлу со списком IP
            _ipListPath = AppSettings.GetIpListFilePath();

            // Запускаем метод проверки наличия файла
            EnsureFileExists();
        }
        
        // Создаем директорию и файл, если они не существуют
        private void EnsureFileExists()
        {
            // Приравниваем переменную директории к переменой пути к директории из полного пути к файлу (замудрённо написал, но вроде читается)
            string directory = Path.GetDirectoryName(_ipListPath);

            // Если директория указана и не существует, создаем ее
            // Проверка string.IsNullOrEmpty(directory) нужна, так как GetDirectoryName может вернуть null
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);  // Создаём директорию
            }

            // Теперь проверяем на наличие файла в директории
            // Если файла не существует, то создаем пустой файл
            if (!File.Exists(_ipListPath))
            {
                // File.Create(путь к файлу) создает файл, если его нет, а .Close() закрывает чтение файла, чтобы не блокировать его для дальнейшего выполнения
                File.Create(_ipListPath).Close();
            }
        }

        // Асинхронно записываем IP-адрес в файл проверяя, существует ли уже такой IP в файле, чтобы избежать дублирования
        public async Task WriteIpInFile(string ipAddress)
        {
            // Проверка входных данных: если IP пустой или null, выходим из метода не пытаясь записывать адресс
            if (string.IsNullOrEmpty(ipAddress))
                return;

            try
            {
                // Асинхронно читаем все строки из файла
                var existingLines = await File.ReadAllLinesAsync(_ipListPath);

                // Создание HashSet для быстрого поиска существующих IP-адресов
                // HashSet<string> обеспечивает уникальность и быстрый поиск O(1), в отличие от перебора массива по одному
                // Подробнее о HashSet https://highload.tech/hashset/
                // StringComparer.OrdinalIgnoreCase делает сравнение без учета регистра
                var existingIps = new HashSet<string>(
                    existingLines.Where(line => !string.IsNullOrWhiteSpace(line)),  // Игнорируем пустые строки, если всё-таки была допущена такая запись
                    StringComparer.OrdinalIgnoreCase);

                // Проверяем, содержится ли уже такой IP-адрес (после удаления пробелов)
                if (existingIps.Contains(ipAddress.Trim()))
                {
                    // Если IP уже существует, выводим информационное сообщение
                    Console.WriteLine($"[SYSTEM] IP {ipAddress} уже существует в файле."); //[LOG]
                }
                else
                {
                    // Если IP не существует, добавляем его в конец файла
                    // AppendAllTextAsync добавляет текст в конец файла, создавая его при необходимости
                    // Добавляем IP и символ новой строки (\n) для ввода с новой строки
                    await File.AppendAllTextAsync(_ipListPath, $"{ipAddress.Trim()}\n");
                    Console.WriteLine($"[SYSTEM] IP {ipAddress} записан в файл"); //[LOG]
                }
            }
            catch (Exception ex)  // Обработка возможных исключений
            {
                // Вывод сообщения об ошибке (например, нет прав доступа к файлу)
                Console.WriteLine($"[ERROR] Ошибка записи IP: {ex.Message}"); //[LOG]
            }
        }

        
        // Асинхронно читает все IP-адреса из файла.
        // Возвращает коллекцию строк, содержащих IP-адреса.
        public async Task<IEnumerable<string>> ReadAllIpsFromFile()
        {
            // Проверяем существование файла перед чтением
            if (!File.Exists(_ipListPath))
                return new List<string>();  // Если файла нет, возвращаем пустой List 

            // Читаем все строки из файла асинхронно
            var lines = await File.ReadAllLinesAsync(_ipListPath);

            // Фильтруем результат, удаляя пустые строки и строки, содержащие только пробелы
            return lines.Where(line => !string.IsNullOrWhiteSpace(line));
        }
        
        // Отображает все сохраненные IP-адреса в консоли в отформатированном виде.
        // Показывает путь к файлу и количество найденных IP-адресов.
        public async Task DisplayAllIps()
        {
            // Получаем все IP-адреса из файла
            var ips = await ReadAllIpsFromFile(); // Запрашиваем данные из метода ReadAllIpsFromFiles, описанном выше

            // Выводим форматированный заголовок
            Console.WriteLine("\n[IP] === СПИСОК СОХРАНЕННЫХ IP ==="); //[LOG]
            Console.WriteLine($"[IP] Файл: {_ipListPath}");  //[LOG] Показываем путь к файлу
            Console.WriteLine($"[IP] Найдено IP: {ips.Count()}\n");  //[LOG] Показываем количество IP

            // Выводим каждый IP-адрес с отступом
            foreach (var ip in ips)
            {
                Console.WriteLine($"[IP]   {ip}");
            }

            // Выводим завершающую строку для отделения адресов от остальных сообщений
            Console.WriteLine("[IP] ==============================\n"); //[LOG]
        }

        
        // Если мы хотим очистить файл, то вызываем этот метод
        // После очистки файл остается, но в нём будут отсутствовать данные
        
        public async Task ClearIpFile()
        {
            // Записываем пустую строку в файл (перезаписывая все содержимое)
            await File.WriteAllTextAsync(_ipListPath, string.Empty);

            // Выводим подтверждение операции
            Console.WriteLine($"[SYSTEM] Файл {_ipListPath} очищен."); //[LOG]
        }

        
        // Возвращает путь к файлу со списком IP-адресов
        // Может использоваться для отладки или другими компонентами системы
        
        public string GetFilePath()
        {
            return _ipListPath;
        }
    }
}