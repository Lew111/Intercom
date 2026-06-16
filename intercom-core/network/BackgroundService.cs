using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using wr;

namespace network
{
    /// <summary>
    /// Улучшенный фоновый сервис с поддержкой обмена цепочками маршрутов
    /// </summary>
    public class BackgroundService
    {
        private readonly Detector _detector;
        private readonly ConnectionManager _connectionManager;
        private readonly WriterIP _writer;
        private readonly RouteManager _routeManager;
        private readonly ChatSessionManager _chatSessionManager;

        // Таймеры
        private System.Timers.Timer _reconnectTimer;
        private System.Timers.Timer _routeConnectTimer;
        private System.Timers.Timer _routeBroadcastTimer;
        private System.Timers.Timer _chainRebuildTimer;

        public BackgroundService(
            Detector detector,
            ConnectionManager connectionManager,
            WriterIP writer,
            RouteManager routeManager,
            ChatSessionManager chatSessionManager)
        {
            _detector = detector;
            _connectionManager = connectionManager;
            _writer = writer;
            _routeManager = routeManager;
            _chatSessionManager = chatSessionManager;

            // Подписываемся на изменения маршрутов
            _routeManager.OnRoutesChanged += async () => await BroadcastRoutesToNeighborsAsync();
        }

        /// <summary>
        /// Запустить все фоновые службы
        /// </summary>
        public void StartAllServices()
        {
            // Автосканирование
            if (AppSettings.AutoScanEnabled && AppSettings.AutoScanInterval > 0)
            {
                StartAutoScanning(AppSettings.AutoScanInterval);
            }

            // Периодическая рассылка маршрутов (каждые 2 минуты)
            _routeBroadcastTimer = new System.Timers.Timer(120000);
            _routeBroadcastTimer.Elapsed += async (s, e) => await BroadcastRoutesToNeighborsAsync();
            _routeBroadcastTimer.AutoReset = true;
            _routeBroadcastTimer.Start();

            // Периодическое перестроение цепочек (каждые 5 минут)
            _chainRebuildTimer = new System.Timers.Timer(300000);
            _chainRebuildTimer.Elapsed += (s, e) => _routeManager.RebuildAllChains();
            _chainRebuildTimer.AutoReset = true;
            _chainRebuildTimer.Start();

            // Попытки подключения к известным маршрутам
            StartRouteConnectionAttempts(30);

            // Автопереподключение
            StartAutoReconnect(2);

            Console.WriteLine("[SYSTEM] Все фоновые службы запущены");
            
            // Немедленная попытка подключения к маршрутам
            _ = Task.Run(async () => await TryConnectToRoutesAsync());
        }

        /// <summary>
        /// Попытки подключения ко всем известным маршрутам
        /// </summary>
        private void StartRouteConnectionAttempts(int intervalSeconds)
        {
            _routeConnectTimer = new System.Timers.Timer(intervalSeconds * 1000);
            _routeConnectTimer.Elapsed += async (s, e) => await TryConnectToRoutesAsync();
            _routeConnectTimer.AutoReset = true;
            _routeConnectTimer.Start();
        }

        public async Task TryConnectToRoutesAsync()
        {
            var allRoutes = _routeManager.GetAllRoutes();
            var activeIps = _connectionManager.GetActiveConnections();

            foreach (var route in allRoutes)
            {
                // Пропускаем себя
                if (route.Nickname.Equals(AppSettings.Nickname, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Проверяем, есть ли уже активное соединение
                if (route.Ips.Any(ip => activeIps.Contains(ip)))
                    continue;

                foreach (var ip in route.Ips)
                {
                    if (activeIps.Contains(ip)) break;

                    Console.WriteLine($"[AUTO] Попытка подключения к {route.Nickname} ({ip})...");
                    
                    bool connected = await _connectionManager.ConnectAsync(ip, AppSettings.DefaultPort, true);
                    
                    if (connected)
                    {
                        Console.WriteLine($"[AUTO] Установлено соединение с {route.Nickname} ({ip})");
                        _chatSessionManager.CreateOrOpenChat(route.Nickname);
                        
                        // Добавляем связь
                        _routeManager.AddLink(AppSettings.Nickname, route.Nickname);
                        
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Рассылка информации о маршрутах соседям
        /// </summary>
        public async Task BroadcastRoutesToNeighborsAsync()
        {
            try
            {
                var neighbors = _routeManager.GetNeighbors(AppSettings.Nickname);
                if (neighbors.Count == 0) return;

                // Получаем цепочки для передачи
                var chainsToBroadcast = _routeManager.GetChainsForBroadcast();
                
                if (chainsToBroadcast.Count == 0) return;

                string chainsJson = JsonSerializer.Serialize(chainsToBroadcast);
                string message = $"ROUTE_CHAINS:{chainsJson}<END>";

                Console.WriteLine($"[ROUTE] Рассылка {chainsToBroadcast.Count} цепочек соседям: {string.Join(", ", neighbors)}");

                foreach (var neighborNick in neighbors)
                {
                    var ips = _routeManager.GetAllIpsByNickname(neighborNick);
                    
                    foreach (var ip in ips)
                    {
                        if (_connectionManager.GetActiveConnections().Contains(ip))
                        {
                            bool sent = await _connectionManager.SendRawMessageAsync(ip, message);
                            
                            if (sent)
                            {
                                Console.WriteLine($"[ROUTE] Цепочки отправлены соседу {neighborNick}");
                                break; // Отправили одному IP соседа, переходим к следующему
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка рассылки маршрутов: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработка полученных цепочек от соседа
        /// </summary>
        public void ProcessReceivedChains(string fromNick, string chainsJson)
        {
            try
            {
                var receivedChains = JsonSerializer.Deserialize<List<RouteChainInfo>>(chainsJson);
                
                if (receivedChains == null || receivedChains.Count == 0) return;

                Console.WriteLine($"[ROUTE] Получено {receivedChains.Count} цепочек от {fromNick}");
                
                _routeManager.ProcessReceivedChains(fromNick, receivedChains);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка обработки полученных цепочек: {ex.Message}");
            }
        }

        #region Стандартные методы

        public void StartAutoScanning(int intervalSeconds)
        {
            _detector.StartAutoScan(intervalSeconds);
        }

        public void StopAutoScanning()
        {
            _detector.StopAutoScan();
        }

        public void UpdateAutoScanInterval(int intervalSeconds)
        {
            _detector.StartAutoScan(intervalSeconds);
        }

        public (bool Enabled, int Interval) GetAutoScanStatus()
        {
            return _detector.GetAutoScanStatus();
        }

        private void StartAutoReconnect(int intervalMinutes)
        {
            _reconnectTimer = new System.Timers.Timer(intervalMinutes * 60 * 1000);
            _reconnectTimer.AutoReset = true;
            _reconnectTimer.Start();
        }

        public void StopAllServices()
        {
            _detector.StopAutoScan();
            _reconnectTimer?.Stop();
            _routeConnectTimer?.Stop();
            _routeBroadcastTimer?.Stop();
            _chainRebuildTimer?.Stop();
        }

        #endregion
    }
}