using System;  // Основные системные классы
using System.Collections.Generic;  // Общие коллекции (List)
using System.Linq;  // LINQ для работы с коллекциями
using System.Net;  // Работа с IP-адресами
using System.Net.NetworkInformation;  // Получение информации о сетевых интерфейсах
using System.Net.Sockets;  // Работа с сетевыми протоколами

namespace network
{
    // Менеджер сетевых интерфейсов
    // Отвечает за обнаружение, фильтрацию и выбор активных сетевых интерфейсов с учетом настроек приложения
    public class Interface_list
    {
        // ==================== ОСНОВНЫЕ МЕТОДЫ ====================

        // Получить список всех активных интерфейсов с IPv4 адресами
        // Возвращает список кортежей (IPAddress, InterfaceName, NetworkInterfaceType)
        // Фильтрует интерфейсы по статусу доступности и настройкам приложения
        public static List<(IPAddress Address, string InterfaceName, NetworkInterfaceType Type)> interface_list()
        {
            List<(IPAddress, string, NetworkInterfaceType)> result = new List<(IPAddress, string, NetworkInterfaceType)>();
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface ni in interfaces)
            {
                // Фильтруем только активные интерфейсы
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                // Режим принудительного WiFi (для работы в сети wifi, а не с ethernet подключением)
                // if (AppSettings.ForceWirelessOnly)
                // {
                //     if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                //         continue;
                // }
                else
                {
                    ////В зависимости от настроек фильтруем интерфейсы
                    switch (AppSettings.InterfaceMode.ToLower())
                    {
                        case "wifi":
                            if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                                continue;
                            break;
                        case "ethernet":
                            if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.FastEthernetT &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx)
                                continue;
                            break;
                        case "auto":
                        default:
                            // При режиме "auto" показываем все, кроме loopback
                            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                                continue;
                            break;
                            // }
                    }

                    IPInterfaceProperties props = ni.GetIPProperties();
                    UnicastIPAddressInformationCollection unicast = props.UnicastAddresses;

                    // Берем только первый IPv4 адрес для каждого интерфейса
                    UnicastIPAddressInformation ipv4Address = unicast.FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ipv4Address != null)
                    {
                        result.Add((ipv4Address.Address, ni.Name, ni.NetworkInterfaceType));
                    }
                }
            }
            return result;
        }

        // Получить самый приоритетный активный интерфейс с учетом настроек
        // Возвращает nullable кортеж с информацией об интерфейсе или null если подходящий интерфейс не найден
        // Приоритеты выбора: указанный адаптер -> принудительный WiFi -> режим из настроек -> авто-выбор



        public static (IPAddress Address, string InterfaceName, NetworkInterfaceType Type)? GetPrimaryInterface()
        {
            var interfaces = interface_list();

            if (interfaces.Count == 0)
            {
                Console.WriteLine("[INTERFACE] Нет подходящих интерфейсов");
                return null;
            }

            // Если указано конкретное имя адаптера в настройках
            if (!string.IsNullOrEmpty(AppSettings.WirelessAdapterName))
            {
                var namedInterface = interfaces.FirstOrDefault(i =>
                    string.Equals(i.InterfaceName, AppSettings.WirelessAdapterName, StringComparison.OrdinalIgnoreCase));

                if (namedInterface.Address != null)
                {
                    Console.WriteLine($"[INTERFACE] Выбран указанный адаптер: {namedInterface.InterfaceName}, IP: {namedInterface.Address}");
                    return namedInterface;
                }
                else
                {
                    Console.WriteLine($"[INTERFACE] Адаптер '{AppSettings.WirelessAdapterName}' не найден или не активен");
                }
            }

            // Принудительный режим WiFi - ищем только WiFi интерфейсы
            //if (AppSettings.ForceWirelessOnly)
            //{
            //    var wifiInterface = interfaces.FirstOrDefault(i =>
            //        i.Type == NetworkInterfaceType.Wireless80211);

            //    if (wifiInterface.Address != null)
            //    {
            //        Console.WriteLine($"[INTERFACE] Выбран WiFi интерфейс (принудительный режим): {wifiInterface.InterfaceName}, IP: {wifiInterface.Address}");
            //        return wifiInterface;
            //    }
            //    else
            //    {
            //        Console.WriteLine("[INTERFACE] WiFi интерфейсы не найдены в принудительном режиме");
            //        return null;
            //    }
            //}

            // В зависимости от режима выбираем интерфейс
            switch (AppSettings.InterfaceMode.ToLower())
            {
                case "wifi":
                    var wifiInterface = interfaces.FirstOrDefault(i =>
                        i.Type == NetworkInterfaceType.Wireless80211);

                    if (wifiInterface.Address != null)
                    {
                        Console.WriteLine($"[INTERFACE] Выбран WiFi интерфейс: {wifiInterface.InterfaceName}, IP: {wifiInterface.Address}");
                        return wifiInterface;
                    }
                    else
                    {
                        Console.WriteLine("[INTERFACE] WiFi интерфейсы не найдены");
                        return null;
                    }

                case "ethernet":
                    var ethernetInterface = interfaces.FirstOrDefault(i =>
                        i.Type == NetworkInterfaceType.Ethernet ||
                        i.Type == NetworkInterfaceType.GigabitEthernet ||
                        i.Type == NetworkInterfaceType.FastEthernetT ||
                        i.Type == NetworkInterfaceType.FastEthernetFx);

                    if (ethernetInterface.Address != null)
                    {
                        Console.WriteLine($"[INTERFACE] Выбран Ethernet интерфейс: {ethernetInterface.InterfaceName}, IP: {ethernetInterface.Address}");
                        return ethernetInterface;
                    }
                    else
                    {
                        Console.WriteLine("[INTERFACE] Ethernet интерфейсы не найдены");
                        return null;
                    }

                case "auto":
                default:
                    // В авторежиме используем приоритет из настроек
                    if (AppSettings.PreferWireless)
                    {
                        var wifiInterfaceAuto = interfaces.FirstOrDefault(i =>
                            i.Type == NetworkInterfaceType.Wireless80211);

                        if (wifiInterfaceAuto.Address != null)
                        {
                            Console.WriteLine($"[INTERFACE] Выбран WiFi интерфейс (приоритет): {wifiInterfaceAuto.InterfaceName}, IP: {wifiInterfaceAuto.Address}");
                            return wifiInterfaceAuto;
                        }
                    }

                    // Если WiFi не найден или не в приоритете, ищем Ethernet
                    var ethernetInterfaceAuto = interfaces.FirstOrDefault(i =>
                        i.Type == NetworkInterfaceType.Ethernet ||
                        i.Type == NetworkInterfaceType.GigabitEthernet ||
                        i.Type == NetworkInterfaceType.FastEthernetT ||
                        i.Type == NetworkInterfaceType.FastEthernetFx);

                    if (ethernetInterfaceAuto.Address != null)
                    {
                        Console.WriteLine($"[INTERFACE] Выбран Ethernet интерфейс: {ethernetInterfaceAuto.InterfaceName}, IP: {ethernetInterfaceAuto.Address}");
                        return ethernetInterfaceAuto;
                    }

                    // Если ничего не нашли, берем первый доступный
                    var firstInterface = interfaces.FirstOrDefault();
                    if (firstInterface.Address != null)
                    {
                        Console.WriteLine($"[INTERFACE] Выбран интерфейс (по умолчанию): {firstInterface.InterfaceName}, IP: {firstInterface.Address}");
                        return firstInterface;
                    }
                    break;
            }

            return null;
        }

        // ==================== СПЕЦИАЛИЗИРОВАННЫЕ МЕТОДЫ ====================

        // Получить список только беспроводных интерфейсов
        // Возвращает список WiFi-адаптеров с IPv4 адресами
        public static List<(IPAddress Address, string InterfaceName, NetworkInterfaceType Type)> GetWirelessInterfaces()
        {
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var result = new List<(IPAddress, string, NetworkInterfaceType)>();

            foreach (NetworkInterface ni in allInterfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    continue;

                IPInterfaceProperties props = ni.GetIPProperties();
                UnicastIPAddressInformationCollection unicast = props.UnicastAddresses;

                UnicastIPAddressInformation ipv4Address = unicast.FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4Address != null)
                {
                    result.Add((ipv4Address.Address, ni.Name, ni.NetworkInterfaceType));
                }
            }
            return result;
        }

        // Получить список только Ethernet интерфейсов
        // Возвращает список проводных адаптеров с IPv4 адресами
        public static List<(IPAddress Address, string InterfaceName, NetworkInterfaceType Type)> GetEthernetInterfaces()
        {
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var result = new List<(IPAddress, string, NetworkInterfaceType)>();

            foreach (NetworkInterface ni in allInterfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.FastEthernetT &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx)
                    continue;

                IPInterfaceProperties props = ni.GetIPProperties();
                UnicastIPAddressInformationCollection unicast = props.UnicastAddresses;

                UnicastIPAddressInformation ipv4Address = unicast.FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4Address != null)
                {
                    result.Add((ipv4Address.Address, ni.Name, ni.NetworkInterfaceType));
                }
            }
            return result;
        }

        // ==================== МЕТОДЫ ПРОВЕРКИ ====================

        // Проверить, есть ли активные беспроводные интерфейсы
        // Возвращает true если найден хотя бы один активный WiFi адаптер
        public static bool HasWirelessInterface()
        {
            return GetWirelessInterfaces().Count > 0;
        }

        // Проверить, есть ли активные Ethernet интерфейсы
        // Возвращает true если найден хотя бы один активный Ethernet адаптер
        public static bool HasEthernetInterface()
        {
            return GetEthernetInterfaces().Count > 0;
        }

        // Получить активный интерфейс с учетом настроек
        // Псевдоним для GetPrimaryInterface() для обратной совместимости
        public static (IPAddress Address, string InterfaceName, NetworkInterfaceType Type)? GetActiveInterface()
        {
            return GetPrimaryInterface();
        }

        // ==================== МЕТОДЫ ОТЛАДКИ И ВЫВОДА ====================

        // Отладочный метод: вывести все интерфейсы с учетом фильтров
        // Выводит в консоль информацию обо всех доступных интерфейсах и выделяет активный
        public static void PrintAllInterfaces()
        {
            var interfaces = interface_list();
            Console.WriteLine($"[INTERFACE] Найдено интерфейсов: {interfaces.Count}");
            // Console.WriteLine($"[INTERFACE] Режим: {AppSettings.InterfaceMode}");
            // Console.WriteLine($"[INTERFACE] Только WiFi: {AppSettings.ForceWirelessOnly}");
            Console.WriteLine($"[INTERFACE] Предпочитать WiFi: {AppSettings.PreferWireless}");

            if (interfaces.Count == 0)
            {
                Console.WriteLine("[INTERFACE] Нет интерфейсов, соответствующих настройкам");
                return;
            }

            var primaryInterface = GetPrimaryInterface();

            foreach (var iface in interfaces)
            {
                string typeName = GetInterfaceTypeString(iface.Type);
                string status = primaryInterface.HasValue && iface.InterfaceName == primaryInterface.Value.InterfaceName ? "[АКТИВНЫЙ]" : "";
                Console.WriteLine($"[INTERFACE] {typeName.PadRight(15)} {iface.InterfaceName.PadRight(30)} IP: {iface.Address} {status}");
            }
        }

        // Вспомогательный метод: преобразовать тип интерфейса в читаемую строку
        // type - тип сетевого интерфейса NetworkInterfaceType
        // Возвращает строковое представление типа интерфейса
        private static string GetInterfaceTypeString(NetworkInterfaceType type)
        {
            return type switch
            {
                NetworkInterfaceType.Wireless80211 => "WiFi",
                NetworkInterfaceType.Ethernet => "Ethernet",
                NetworkInterfaceType.GigabitEthernet => "Gigabit Ethernet",
                NetworkInterfaceType.FastEthernetT => "Fast Ethernet",
                NetworkInterfaceType.FastEthernetFx => "Fast Ethernet (Fiber)",
                _ => type.ToString()
            };
        }

        // Получить информацию о текущих настройках интерфейса
        // Возвращает форматированную строку с детальной информацией о настройках и состоянии интерфейсов
        public static string GetInterfaceSettingsInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[INTERFACE] === НАСТРОЙКИ ИНТЕРФЕЙСА ===");
            // sb.AppendLine($"[INTERFACE] Режим: {AppSettings.InterfaceMode}");
            // sb.AppendLine($"[INTERFACE] Только WiFi: {AppSettings.ForceWirelessOnly}");
            sb.AppendLine($"[INTERFACE] Предпочитать WiFi: {AppSettings.PreferWireless}");
            sb.AppendLine($"[INTERFACE] Имя адаптера: {(!string.IsNullOrEmpty(AppSettings.WirelessAdapterName) ? AppSettings.WirelessAdapterName : "авто")}");

            var primaryInterface = GetPrimaryInterface();
            if (primaryInterface.HasValue)
            {
                sb.AppendLine($"[INTERFACE] Активный интерфейс: {primaryInterface.Value.InterfaceName}");
                sb.AppendLine($"[INTERFACE] Тип: {GetInterfaceTypeString(primaryInterface.Value.Type)}");
                sb.AppendLine($"[INTERFACE] IP: {primaryInterface.Value.Address}");
            }
            else
            {
                sb.AppendLine("[INTERFACE] Активный интерфейс: не определен");
            }

            var wirelessInterfaces = GetWirelessInterfaces();
            sb.AppendLine($"[INTERFACE] Найдено WiFi адаптеров: {wirelessInterfaces.Count}");

            if (wirelessInterfaces.Count > 0)
            {
                sb.AppendLine("[INTERFACE] WiFi адаптеры:");
                foreach (var iface in wirelessInterfaces)
                {
                    string activeMarker = primaryInterface.HasValue && iface.InterfaceName == primaryInterface.Value.InterfaceName ? "✓" : " ";
                    sb.AppendLine($"[INTERFACE]   [{activeMarker}] {iface.InterfaceName} - {iface.Address}");
                }
            }

            return sb.ToString();
        }
    }
}