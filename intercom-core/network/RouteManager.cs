using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace network
{
    /// <summary>
    /// Улучшенный менеджер маршрутов с поддержкой построения цепочек через промежуточные узлы
    /// и учётом NAT (клиенты за NAT могут инициировать исходящие, но не принимать входящие)
    /// </summary>
    public class RouteManager
    {
        // Пути к файлам конфигурации
        private readonly string _staticConfigPath;
        private readonly string _dynamicConfigPath;
        private readonly string _chainsPath;
        private readonly string _verifiedChainsPath; // Только проверенные цепочки
        private readonly string _natConfigPath; // Конфигурация NAT

        // Хранилища данных
        private List<ClientInfo> _staticClients;
        private ConcurrentDictionary<string, ClientInfo> _dynamicClients;
        private List<Link> _links;
        private Dictionary<string, List<string>> _adjacencyList;
        
        // Цепочки маршрутов: ключ - целевой ник, значение - список возможных цепочек
        private ConcurrentDictionary<string, List<RouteChain>> _chains;
        
        // Проверенные цепочки (по которым успешно прошла отправка)
        private ConcurrentDictionary<string, List<RouteChain>> _verifiedChains;
        
        // Информация о NAT: клиенты, которые не могут принимать входящие
        private ConcurrentDictionary<string, NatInfo> _natInfo;

        // События
        public event Func<Task> OnRouteTableChanged;
        public event Func<Task> OnRoutesChanged;
        public event Action<string, RouteChain> OnChainVerified; // Цепочка подтверждена

        public RouteManager()
        {
            _staticConfigPath = Path.Combine(AppSettings.BasePath, "config", "clients.json");
            _dynamicConfigPath = Path.Combine(AppSettings.GetUserDataPath(), "dynamic_routes.json");
            _chainsPath = Path.Combine(AppSettings.GetUserDataPath(), "route_chains.json");
            _verifiedChainsPath = Path.Combine(AppSettings.GetUserDataPath(), "verified_chains.json");
            _natConfigPath = Path.Combine(AppSettings.GetUserDataPath(), "nat_config.json");

            _staticClients = new List<ClientInfo>();
            _dynamicClients = new ConcurrentDictionary<string, ClientInfo>();
            _links = new List<Link>();
            _adjacencyList = new Dictionary<string, List<string>>();
            _chains = new ConcurrentDictionary<string, List<RouteChain>>();
            _verifiedChains = new ConcurrentDictionary<string, List<RouteChain>>();
            _natInfo = new ConcurrentDictionary<string, NatInfo>();
        }

        /// <summary>
        /// Загрузка всей конфигурации при старте
        /// </summary>
        public void LoadConfiguration()
        {
            LoadStaticRoutes();
            LoadDynamicRoutes();
            LoadChains();
            LoadVerifiedChains();
            LoadNatConfig();
            BuildGraph();
            
            Console.WriteLine($"[ROUTE] Конфигурация загружена: {_staticClients.Count} статических, " +
                            $"{_dynamicClients.Count} динамических, {_links.Count} связей");
        }

        #region Загрузка и сохранение

        private void LoadStaticRoutes()
        {
            try
            {
                if (!File.Exists(_staticConfigPath)) return;
                string json = File.ReadAllText(_staticConfigPath);
                var config = JsonSerializer.Deserialize<RouteConfig>(json);
                if (config?.Clients != null)
                {
                    _staticClients = config.Clients;
                    _links = config.Links ?? new List<Link>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки статических маршрутов: {ex.Message}");
            }
        }

        private void LoadDynamicRoutes()
        {
            try
            {
                if (!File.Exists(_dynamicConfigPath)) return;
                string json = File.ReadAllText(_dynamicConfigPath);
                var dynRoutes = JsonSerializer.Deserialize<List<ClientInfo>>(json);
                foreach (var route in dynRoutes)
                {
                    _dynamicClients[route.Nickname] = route;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки динамических маршрутов: {ex.Message}");
            }
        }

        private void LoadChains()
        {
            try
            {
                if (!File.Exists(_chainsPath)) return;
                string json = File.ReadAllText(_chainsPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<RouteChain>>>(json);
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                    {
                        _chains[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки цепочек: {ex.Message}");
            }
        }

        private void LoadVerifiedChains()
        {
            try
            {
                if (!File.Exists(_verifiedChainsPath)) return;
                string json = File.ReadAllText(_verifiedChainsPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<RouteChain>>>(json);
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                    {
                        _verifiedChains[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки проверенных цепочек: {ex.Message}");
            }
        }

        private void LoadNatConfig()
        {
            try
            {
                if (!File.Exists(_natConfigPath)) return;
                string json = File.ReadAllText(_natConfigPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, NatInfo>>(json);
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                    {
                        _natInfo[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки NAT конфигурации: {ex.Message}");
            }
        }

        private void SaveDynamicRoutes()
        {
            try
            {
                var allDyn = _dynamicClients.Values.ToList();
                string json = JsonSerializer.Serialize(allDyn, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_dynamicConfigPath));
                File.WriteAllText(_dynamicConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения динамических маршрутов: {ex.Message}");
            }
        }

        private void SaveStaticRoutes()
        {
            try
            {
                var config = new RouteConfig { Clients = _staticClients, Links = _links };
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_staticConfigPath));
                File.WriteAllText(_staticConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения статической конфигурации: {ex.Message}");
            }
        }

        private void SaveChains()
        {
            try
            {
                var chainsList = _chains.ToDictionary(k => k.Key, v => v.Value);
                string json = JsonSerializer.Serialize(chainsList, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_chainsPath));
                File.WriteAllText(_chainsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения цепочек: {ex.Message}");
            }
        }

        private void SaveVerifiedChains()
        {
            try
            {
                var chainsList = _verifiedChains.ToDictionary(k => k.Key, v => v.Value);
                string json = JsonSerializer.Serialize(chainsList, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_verifiedChainsPath));
                File.WriteAllText(_verifiedChainsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения проверенных цепочек: {ex.Message}");
            }
        }

        private void SaveNatConfig()
        {
            try
            {
                var natList = _natInfo.ToDictionary(k => k.Key, v => v.Value);
                string json = JsonSerializer.Serialize(natList, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_natConfigPath));
                File.WriteAllText(_natConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения NAT конфигурации: {ex.Message}");
            }
        }

        #endregion

        #region Построение графа и цепочек

        /// <summary>
        /// Перестроить граф связей на основе известных клиентов и связей
        /// </summary>
        private void BuildGraph()
        {
            _adjacencyList.Clear();

            // Собираем всех известных клиентов
            var allNicknames = _staticClients.Select(c => c.Nickname)
                .Union(_dynamicClients.Keys)
                .Union(new[] { AppSettings.Nickname })
                .Distinct();

            foreach (var nick in allNicknames)
            {
                _adjacencyList[nick] = new List<string>();
            }

            // Добавляем связи из _links
            foreach (var link in _links)
            {
                AddBidirectionalLink(link.From, link.To);
            }

            Console.WriteLine($"[ROUTE] Граф построен: {_adjacencyList.Count} узлов, {_links.Count} связей");
            
            // Перестраиваем цепочки
            RebuildAllChains();
        }

        private void AddBidirectionalLink(string from, string to)
        {
            if (_adjacencyList.ContainsKey(from) && !_adjacencyList[from].Contains(to))
            {
                _adjacencyList[from].Add(to);
            }
            if (_adjacencyList.ContainsKey(to) && !_adjacencyList[to].Contains(from))
            {
                _adjacencyList[to].Add(from);
            }
        }

        /// <summary>
        /// Перестроить все возможные цепочки от текущего узла до всех остальных
        /// </summary>
        public void RebuildAllChains()
        {
            var newChains = new ConcurrentDictionary<string, List<RouteChain>>();
            string myNick = AppSettings.Nickname;

            if (!_adjacencyList.ContainsKey(myNick))
            {
                Console.WriteLine("[ROUTE] Я не в графе, цепочки не строятся");
                return;
            }

            // Получаем всех соседей (прямые подключения)
            var myNeighbors = _adjacencyList.ContainsKey(myNick) ? _adjacencyList[myNick] : new List<string>();

            // Получаем всех известных клиентов (кроме себя)
            var allTargets = _adjacencyList.Keys.Where(n => n != myNick).ToList();

            foreach (var target in allTargets)
            {
                var chains = new List<RouteChain>();

                // ✅ ПРИОРИТЕТ 1: Прямое соединение (если есть)
                if (myNeighbors.Contains(target))
                {
                    chains.Add(new RouteChain
                    {
                        TargetNick = target,
                        Path = new List<string> { myNick, target },
                        DiscoveredAt = DateTime.Now,
                        IsVerified = true,
                        ChainType = ChainType.Direct,
                        LastUsed = DateTime.Now,
                        SuccessCount = 1
                    });
                }

                // ✅ ПРИОРИТЕТ 2: Поиск через соседей (2 хопа: я -> сосед -> цель)
                foreach (var neighbor in myNeighbors)
                {
                    if (neighbor == target) continue;

                    var neighborNeighbors = _adjacencyList.ContainsKey(neighbor) ? _adjacencyList[neighbor] : new List<string>();

                    if (neighborNeighbors.Contains(target))
                    {
                        var path = new List<string> { myNick, neighbor, target };
                        if (!chains.Any(c => c.Path.SequenceEqual(path)))
                        {
                            chains.Add(new RouteChain
                            {
                                TargetNick = target,
                                Path = path,
                                DiscoveredAt = DateTime.Now,
                                IsVerified = false,
                                ChainType = ChainType.Relay,
                                LastUsed = null,
                                SuccessCount = 0
                            });
                            Console.WriteLine($"[ROUTE] Найдена 2-хоповая цепочка: {string.Join(" -> ", path)}");
                        }
                    }
                }

                // ✅ ПРИОРИТЕТ 3: Поиск через соседей соседей (3 хопа)
                foreach (var neighbor in myNeighbors)
                {
                    if (neighbor == target) continue;

                    var neighborNeighbors = _adjacencyList.ContainsKey(neighbor) ? _adjacencyList[neighbor] : new List<string>();

                    foreach (var secondHop in neighborNeighbors)
                    {
                        if (secondHop == myNick || secondHop == neighbor || secondHop == target) continue;

                        var secondHopNeighbors = _adjacencyList.ContainsKey(secondHop) ? _adjacencyList[secondHop] : new List<string>();

                        if (secondHopNeighbors.Contains(target))
                        {
                            var path = new List<string> { myNick, neighbor, secondHop, target };
                            if (!chains.Any(c => c.Path.SequenceEqual(path)))
                            {
                                chains.Add(new RouteChain
                                {
                                    TargetNick = target,
                                    Path = path,
                                    DiscoveredAt = DateTime.Now,
                                    IsVerified = false,
                                    ChainType = ChainType.Relay,
                                    LastUsed = null,
                                    SuccessCount = 0
                                });
                                Console.WriteLine($"[ROUTE] Найдена 3-хоповая цепочка: {string.Join(" -> ", path)}");
                            }
                        }
                    }
                }

                // ✅ ПРИОРИТЕТ 4: DFS для более длинных путей (4+ хопов)
                if (chains.Count == 0 || chains.All(c => c.HopCount <= 1))
                {
                    var paths = FindAllPathsDFS(myNick, target, maxDepth: 5);
                    foreach (var path in paths)
                    {
                        // Пропускаем уже найденные короткие пути
                        if (path.Count <= 3) continue;

                        var chainType = DetermineChainType(path);
                        if (!chains.Any(c => c.Path.SequenceEqual(path)))
                        {
                            chains.Add(new RouteChain
                            {
                                TargetNick = target,
                                Path = path,
                                DiscoveredAt = DateTime.Now,
                                IsVerified = false,
                                ChainType = chainType,
                                LastUsed = null,
                                SuccessCount = 0
                            });
                            Console.WriteLine($"[ROUTE] Найдена {path.Count - 1}-хоповая цепочка: {string.Join(" -> ", path)}");
                        }
                    }
                }

                if (chains.Count > 0)
                {
                    // Сортируем: Direct первые, потом по длине, потом по успешности
                    chains = chains.OrderBy(c => c.ChainType == ChainType.Direct ? 0 : 1)
                                   .ThenBy(c => c.HopCount)
                                   .ThenByDescending(c => c.SuccessCount)
                                   .ToList();

                    newChains[target] = chains;

                    var bestChain = chains.First();
                    Console.WriteLine($"[ROUTE] Найдено {chains.Count} цепочек до {target} " +
                                    $"(лучшая: {string.Join(" -> ", bestChain.Path)}, " +
                                    $"тип: {bestChain.ChainType})");
                }
            }

            _chains = newChains;
            SaveChains();

            // Уведомляем об изменении
            _ = OnRoutesChanged?.Invoke();
        }



        /// <summary>
        /// Определить тип цепочки с учётом NAT
        /// </summary>
        private ChainType DetermineChainType(List<string> path)
        {
            if (path.Count < 2) return ChainType.Direct;
            
            // Проверяем, есть ли клиенты за NAT в цепочке (кроме первого - это мы)
            for (int i = 1; i < path.Count; i++)
            {
                if (_natInfo.ContainsKey(path[i]) && _natInfo[path[i]].IsBehindNat)
                {
                    // Если клиент за NAT не в конце цепочки, это проблема
                    if (i < path.Count - 1)
                    {
                        return ChainType.NatRestricted;
                    }
                }
            }
            
            return path.Count == 2 ? ChainType.Direct : ChainType.Relay;
        }

        /// <summary>
        /// Поиск всех путей от start до end с ограничением по глубине (DFS)
        /// </summary>
        private List<List<string>> FindAllPathsDFS(string start, string end, int maxDepth)
        {
            var result = new List<List<string>>();
            var visited = new HashSet<string>();
            var path = new List<string>();

            DFS(start, end, visited, path, result, maxDepth, 0);

            return result;
        }

        private void DFS(string current, string target, HashSet<string> visited,
                        List<string> path, List<List<string>> result, int maxDepth, int depth)
        {
            if (depth > maxDepth) return;

            visited.Add(current);
            path.Add(current);

            if (current == target)
            {
                result.Add(new List<string>(path));
            }
            else if (_adjacencyList.ContainsKey(current))
            {
                foreach (var neighbor in _adjacencyList[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        DFS(neighbor, target, visited, path, result, maxDepth, depth + 1);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(current);
        }

        #endregion

        #region Получение маршрутов

        /// <summary>
        /// Получить следующий хоп для отправки сообщения к целевому клиенту
        /// Учитывает NAT и выбирает оптимальную цепочку
        /// </summary>
        public string GetNextHop(string currentNick, string targetNick)
        {
            if (currentNick == targetNick) return null;

            // Сначала проверяем проверенные цепочки
            if (_verifiedChains.TryGetValue(targetNick, out var verifiedChains) && verifiedChains.Count > 0)
            {
                var bestVerified = verifiedChains
                    .Where(c => c.IsValid && c.CanUseFrom(currentNick))
                    .OrderBy(c => c.HopCount)
                    .ThenByDescending(c => c.SuccessCount)
                    .FirstOrDefault();

                if (bestVerified != null)
                {
                    var nextHop = bestVerified.GetNextHopFrom(currentNick);
                    if (nextHop != null)
                    {
                        Console.WriteLine($"[ROUTE] Использую проверенную цепочку: {string.Join(" -> ", bestVerified.Path)}");
                        return nextHop;
                    }
                }
            }

            // Ищем в обычных цепочках
            if (_chains.TryGetValue(targetNick, out var chains))
            {
                // Ищем первую подходящую цепочку
                var bestChain = chains
                    .Where(c => c.IsValid && c.CanUseFrom(currentNick))
                    .OrderBy(c => c.ChainType == ChainType.NatRestricted ? 1 : 0) // Избегаем NAT-ограниченных
                    .ThenBy(c => c.HopCount)
                    .FirstOrDefault();

                if (bestChain != null)
                {
                    var nextHop = bestChain.GetNextHopFrom(currentNick);
                    if (nextHop != null)
                    {
                        Console.WriteLine($"[ROUTE] Использую цепочку: {string.Join(" -> ", bestChain.Path)} " +
                                        $"(тип: {bestChain.ChainType})");
                        return nextHop;
                    }
                }
            }

            // Fallback: поиск в реальном времени
            Console.WriteLine($"[ROUTE] Цепочка не найдена, ищу путь в реальном времени...");
            return FindPathBFS(currentNick, targetNick);
        }

        /// <summary>
        /// BFS для поиска кратчайшего пути (fallback)
        /// </summary>
        private string FindPathBFS(string start, string end)
        {
            if (!_adjacencyList.ContainsKey(start) || !_adjacencyList.ContainsKey(end))
                return null;

            var queue = new Queue<List<string>>();
            var visited = new HashSet<string>();
            
            queue.Enqueue(new List<string> { start });
            visited.Add(start);

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                var current = path.Last();

                if (current == end)
                {
                    // Восстанавливаем путь
                    return path.Count > 1 ? path[1] : null;
                }

                foreach (var neighbor in _adjacencyList[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        var newPath = new List<string>(path) { neighbor };
                        queue.Enqueue(newPath);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Получить полную цепочку для отправки к целевому клиенту
        /// </summary>
        public RouteChain GetBestChain(string targetNick)
        {
            // Сначала проверяем проверенные цепочки
            if (_verifiedChains.TryGetValue(targetNick, out var verified) && verified.Count > 0)
            {
                // Сортируем: сначала по успешности, потом по длине
                var bestVerified = verified
                    .Where(c => c.IsValid)
                    .OrderByDescending(c => c.SuccessCount)
                    .ThenBy(c => c.HopCount)
                    .FirstOrDefault();

                if (bestVerified != null)
                {
                    Console.WriteLine($"[ROUTE] Используем проверенную цепочку: {string.Join(" -> ", bestVerified.Path)}");
                    return bestVerified;
                }
            }

            // Иначе берём из обычных
            if (_chains.TryGetValue(targetNick, out var chains) && chains.Count > 0)
            {
                // Если есть Direct и она проверена - используем её
                var directVerified = chains.FirstOrDefault(c => c.ChainType == ChainType.Direct && c.IsVerified);
                if (directVerified != null)
                {
                    Console.WriteLine($"[ROUTE] Используем прямую проверенную цепочку: {string.Join(" -> ", directVerified.Path)}");
                    return directVerified;
                }

                // Если Direct не проверена или не работает - используем Relay
                var relayChain = chains
                    .Where(c => c.ChainType == ChainType.Relay && c.HopCount >= 2 && c.IsValid)
                    .OrderBy(c => c.HopCount)
                    .FirstOrDefault();

                if (relayChain != null)
                {
                    Console.WriteLine($"[ROUTE] Используем Relay цепочку: {string.Join(" -> ", relayChain.Path)}");
                    return relayChain;
                }

                // Fallback: любая доступная цепочка
                var anyChain = chains.FirstOrDefault(c => c.IsValid);
                if (anyChain != null)
                {
                    Console.WriteLine($"[ROUTE] Используем доступную цепочку: {string.Join(" -> ", anyChain.Path)}");
                    return anyChain;
                }
            }

            // Последняя попытка: построить цепочку через соседей в реальном времени
            Console.WriteLine($"[ROUTE] Нет сохранённой цепочки, строим через соседей...");
            RebuildAllChains();

            // Повторяем поиск после перестроения
            if (_chains.TryGetValue(targetNick, out var newChains) && newChains.Count > 0)
            {
                var relayChain = newChains
                    .Where(c => c.ChainType == ChainType.Relay && c.HopCount >= 2)
                    .OrderBy(c => c.HopCount)
                    .FirstOrDefault();

                if (relayChain != null)
                {
                    Console.WriteLine($"[ROUTE] Используем построенную Relay цепочку: {string.Join(" -> ", relayChain.Path)}");
                    return relayChain;
                }

                return newChains.FirstOrDefault(c => c.IsValid);
            }

            return null;
        }



        #endregion

        #region Управление клиентами и связями

        /// <summary>
        /// Добавить статический маршрут
        /// </summary>
        public void AddStaticRoute(string nickname, string ip, bool addLink = true)
        {
            var existing = _staticClients.FirstOrDefault(c => 
                c.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                if (!existing.Ips.Contains(ip))
                    existing.Ips.Add(ip);
            }
            else
            {
                var newClient = new ClientInfo 
                { 
                    Nickname = nickname, 
                    Ips = new List<string> { ip },
                    IsStatic = true
                };
                _staticClients.Add(newClient);
            }

            if (addLink)
            {
                string myNick = AppSettings.Nickname;
                AddLink(myNick, nickname);
            }
            
            SaveStaticRoutes();
            BuildGraph();
            OnRouteTableChanged?.Invoke();
            OnRoutesChanged?.Invoke();
        }

        /// <summary>
        /// Добавить динамического клиента (обнаружен через сеть)
        /// </summary>
        public void AddDynamicClient(string nickname, string ip, string sourceNick = null, bool isBehindNat = false)
        {
            if (ip == "127.0.0.1" || ip == "::1") return;
            if (nickname.Equals(AppSettings.Nickname, StringComparison.OrdinalIgnoreCase)) return;
            
            // Не добавляем как динамического, если есть статический
            if (_staticClients.Any(c => c.Nickname == nickname)) return;

            var client = _dynamicClients.GetOrAdd(nickname, new ClientInfo 
            { 
                Nickname = nickname, 
                Ips = new List<string>(),
                IsStatic = false
            });

            if (!client.Ips.Contains(ip))
            {
                client.Ips.Add(ip);
                Console.WriteLine($"[ROUTE] Добавлен динамический клиент: {nickname} -> {ip} (через {sourceNick ?? "self"})");
            }
            SaveDynamicRoutes();

            // Обновляем информацию о NAT
            if (isBehindNat)
            {
                _natInfo[nickname] = new NatInfo 
                { 
                    Nickname = nickname, 
                    IsBehindNat = true,
                    PublicEndpoint = ip 
                };
                SaveNatConfig();
            }

            // Добавляем связи
            if (!string.IsNullOrEmpty(sourceNick) && sourceNick != nickname)
            {
                AddLink(sourceNick, nickname);
            }

            string myNick = AppSettings.Nickname;
            if (!string.IsNullOrEmpty(sourceNick) && sourceNick != myNick && sourceNick != nickname)
            {
                AddLink(myNick, sourceNick);
            }

            SaveStaticRoutes();
            BuildGraph();
        }

        /// <summary>
        /// Добавить связь между двумя клиентами
        /// </summary>
        public void AddLink(string from, string to)
        {
            if (from == to) return; // Игнорируем петли
            
            bool changed = false;
            
            if (!_links.Any(l => l.From == from && l.To == to))
            {
                _links.Add(new Link { From = from, To = to });
                changed = true;
            }
            
            // Двунаправленная связь (для большинства случаев)
            if (!_links.Any(l => l.From == to && l.To == from))
            {
                _links.Add(new Link { From = to, To = from });
                changed = true;
            }

            if (changed)
            {
                SaveStaticRoutes();
                if (_adjacencyList.Count > 0) // Если граф уже построен
                {
                    AddBidirectionalLink(from, to);
                    RebuildAllChains();
                }
            }
        }

        /// <summary>
        /// Удалить статический маршрут
        /// </summary>
        public void RemoveStaticRoute(string nickname)
        {
            var client = _staticClients.FirstOrDefault(c => 
                c.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
            
            if (client == null) return;
            
            _staticClients.Remove(client);
            _links.RemoveAll(l => l.From == nickname || l.To == nickname);
            
            SaveStaticRoutes();
            BuildGraph();
            OnRouteTableChanged?.Invoke();
        }

        /// <summary>
        /// Очистить все динамические маршруты
        /// </summary>
        public void ClearDynamicRoutes()
        {
            _dynamicClients.Clear();
            SaveDynamicRoutes();
            BuildGraph();
            OnRouteTableChanged?.Invoke();
        }

        #endregion

        #region Управление цепочками

        /// <summary>
        /// Отметить цепочку как проверенную (успешная отправка)
        /// </summary>
        public void MarkChainAsVerified(string targetNick, List<string> path)
        {
            // Ищем цепочку
            RouteChain chain = null;
            
            if (_chains.TryGetValue(targetNick, out var chains))
            {
                chain = chains.FirstOrDefault(c => c.Path.SequenceEqual(path));
            }

            if (chain == null)
            {
                // Создаём новую цепочку, если не нашли
                chain = new RouteChain
                {
                    TargetNick = targetNick,
                    Path = path,
                    DiscoveredAt = DateTime.Now,
                    IsVerified = true,
                    LastUsed = DateTime.Now,
                    SuccessCount = 1,
                    ChainType = DetermineChainType(path)
                };
            }
            else
            {
                chain.IsVerified = true;
                chain.LastUsed = DateTime.Now;
                chain.SuccessCount++;
            }

            // Добавляем в проверенные
            var verifiedList = _verifiedChains.GetOrAdd(targetNick, new List<RouteChain>());
            
            // Удаляем старую версию, если есть
            verifiedList.RemoveAll(c => c.Path.SequenceEqual(path));
            verifiedList.Add(chain);
            
            // Сортируем по успешности
            verifiedList.Sort((a, b) => b.SuccessCount.CompareTo(a.SuccessCount));
            
            SaveVerifiedChains();
            
            Console.WriteLine($"[ROUTE] Цепочка подтверждена: {string.Join(" -> ", path)} " +
                            $"(успехов: {chain.SuccessCount})");
            
            OnChainVerified?.Invoke(targetNick, chain);
        }

        /// <summary>
        /// Отметить цепочку как неудачную
        /// </summary>
        public void MarkChainAsFailed(string targetNick, List<string> path)
        {
            if (_chains.TryGetValue(targetNick, out var chains))
            {
                var chain = chains.FirstOrDefault(c => c.Path.SequenceEqual(path));
                if (chain != null)
                {
                    chain.FailCount++;
                    chain.LastFailure = DateTime.Now;
                    
                    // Если слишком много неудач, помечаем как невалидную
                    if (chain.FailCount > 3)
                    {
                        chain.IsValid = false;
                        Console.WriteLine($"[ROUTE] Цепочка помечена как невалидная: {string.Join(" -> ", path)}");
                    }
                    
                    SaveChains();
                }
            }
        }

        /// <summary>
        /// Получить информацию о цепочках для передачи другим клиентам
        /// </summary>
        public List<RouteChainInfo> GetChainsForBroadcast()
        {
            var result = new List<RouteChainInfo>();
            string myNick = AppSettings.Nickname;

            // Передаём только проверенные цепочки и прямые связи
            foreach (var kv in _verifiedChains)
            {
                foreach (var chain in kv.Value.Where(c => c.IsVerified && c.HopCount <= 3))
                {
                    result.Add(new RouteChainInfo
                    {
                        TargetNick = chain.TargetNick,
                        Path = chain.Path,
                        IsVerified = true,
                        SuccessRate = chain.SuccessRate
                    });
                }
            }

            // Добавляем информацию о наших прямых соседях
            if (_adjacencyList.ContainsKey(myNick))
            {
                foreach (var neighbor in _adjacencyList[myNick])
                {
                    // Проверяем, не добавили ли уже
                    if (!result.Any(r => r.TargetNick == neighbor && r.Path.Count == 2))
                    {
                        result.Add(new RouteChainInfo
                        {
                            TargetNick = neighbor,
                            Path = new List<string> { myNick, neighbor },
                            IsVerified = true,
                            SuccessRate = 1.0
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Обработать полученные от другого клиента цепочки
        /// </summary>
        public void ProcessReceivedChains(string sourceNick, List<RouteChainInfo> receivedChains)
        {
            string myNick = AppSettings.Nickname;
            bool changed = false;

            foreach (var chainInfo in receivedChains)
            {
                // Игнорируем цепочки к нам самим
                if (chainInfo.TargetNick.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Строим расширенную цепочку: мы -> sourceNick -> ... -> target
                var extendedPath = new List<string> { myNick };
                extendedPath.AddRange(chainInfo.Path.SkipWhile(n => 
                    n.Equals(sourceNick, StringComparison.OrdinalIgnoreCase)));

                // Проверяем валидность
                if (extendedPath.Count < 2) continue;
                if (extendedPath.Count > 6) continue; // Максимальная длина
                if (extendedPath.Distinct().Count() != extendedPath.Count) continue; // Нет петель

                var target = chainInfo.TargetNick;
                var chains = _chains.GetOrAdd(target, new List<RouteChain>());

                // Проверяем, нет ли уже такой цепочки
                if (!chains.Any(c => c.Path.SequenceEqual(extendedPath)))
                {
                    var newChain = new RouteChain
                    {
                        TargetNick = target,
                        Path = extendedPath,
                        DiscoveredAt = DateTime.Now,
                        IsVerified = false, // Полученная цепочка не проверена нами
                        ChainType = DetermineChainType(extendedPath),
                        SourceChain = chainInfo
                    };

                    chains.Add(newChain);
                    changed = true;
                    
                    Console.WriteLine($"[ROUTE] Получена цепочка от {sourceNick}: {string.Join(" -> ", extendedPath)}");
                }
            }

            if (changed)
            {
                SaveChains();
                
                // Пересортируем
                foreach (var kv in _chains)
                {
                    _chains[kv.Key] = kv.Value
                        .OrderBy(c => c.IsVerified ? 0 : 1)
                        .ThenBy(c => c.HopCount)
                        .ToList();
                }
                
                OnRoutesChanged?.Invoke();
            }
        }

        #endregion

        #region Вспомогательные методы

        public string GetIpByNickname(string nickname)
        {
            var client = GetClientInfo(nickname);
            return client?.Ips.FirstOrDefault();
        }

        public List<string> GetAllIpsByNickname(string nickname)
        {
            var client = GetClientInfo(nickname);
            if (client == null)
            {
                Console.WriteLine($"[DEBUG] GetAllIpsByNickname({nickname}): клиент не найден!");
                return new List<string>();
            }

            if (client.Ips == null || client.Ips.Count == 0)
            {
                Console.WriteLine($"[DEBUG] GetAllIpsByNickname({nickname}): у клиента нет IP!");
                return new List<string>();
            }

            return client.Ips;
        }

        private ClientInfo GetClientInfo(string nickname)
        {
            // Сначала ищем в статических
            var staticClient = _staticClients.FirstOrDefault(c =>
                c.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
            if (staticClient != null) return staticClient;

            // Потом в динамических
            if (_dynamicClients.TryGetValue(nickname, out var dynamicClient))
                return dynamicClient;

            return null;
        }

        public string GetNicknameByIp(string ip)
        {
            var staticClient = _staticClients.FirstOrDefault(c => c.Ips.Contains(ip));
            if (staticClient != null) return staticClient.Nickname;
            var dynClient = _dynamicClients.Values.FirstOrDefault(c => c.Ips.Contains(ip));
            return dynClient?.Nickname;
        }

        public List<ClientInfo> GetAllRoutes()
        {
            var all = new List<ClientInfo>();
            all.AddRange(_staticClients);
            all.AddRange(_dynamicClients.Values);
            return all;
        }

        public List<ClientInfo> GetAllStaticRoutes() => _staticClients.ToList();
        public List<ClientInfo> GetAllDynamicRoutes() => _dynamicClients.Values.ToList();
        public List<Link> GetAllLinks() => _links.ToList();
        
        public List<string> GetNeighbors(string nickname) =>
            _adjacencyList.ContainsKey(nickname) ? _adjacencyList[nickname].ToList() : new List<string>();

        public Dictionary<string, List<RouteChain>> GetAllChains() => 
            _chains.ToDictionary(k => k.Key, v => v.Value);

        public Dictionary<string, List<RouteChain>> GetAllVerifiedChains() => 
            _verifiedChains.ToDictionary(k => k.Key, v => v.Value);

        public bool IsClientBehindNat(string nickname) => 
            _natInfo.ContainsKey(nickname) && _natInfo[nickname].IsBehindNat;

        #endregion
    }

    #region Вспомогательные классы

    public class RouteConfig
    {
        public List<ClientInfo> Clients { get; set; } = new List<ClientInfo>();
        public List<Link> Links { get; set; } = new List<Link>();
    }

    public class ClientInfo
    {
        public string Nickname { get; set; }
        public List<string> Ips { get; set; } = new List<string>();
        public bool IsStatic { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.Now;
    }

    public class Link
    {
        public string From { get; set; }
        public string To { get; set; }
    }

    /// <summary>
    /// Тип цепочки маршрута
    /// </summary>
    public enum ChainType
    {
        Direct,         // Прямое соединение
        Relay,          // Через промежуточные узлы
        NatRestricted   // Есть клиенты за NAT в середине цепочки (проблематично)
    }

    /// <summary>
    /// Цепочка маршрута от текущего узла до целевого
    /// </summary>
    public class RouteChain
    {
        public string TargetNick { get; set; }
        public List<string> Path { get; set; } = new List<string>();
        public DateTime DiscoveredAt { get; set; }
        public DateTime? LastUsed { get; set; }
        public DateTime? LastFailure { get; set; }
        public bool IsVerified { get; set; }
        public bool IsValid { get; set; } = true;
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public ChainType ChainType { get; set; }
        public RouteChainInfo SourceChain { get; set; }

        public int HopCount => Path.Count - 1;
        
        public string NextHop => Path.Count > 1 ? Path[1] : null;
        
        public double SuccessRate => (SuccessCount + FailCount) == 0 ? 0 : 
            (double)SuccessCount / (SuccessCount + FailCount);

        /// <summary>
        /// Получить следующий хоп от указанной позиции в цепочке
        /// </summary>
        public string GetNextHopFrom(string fromNick)
        {
            int index = Path.IndexOf(fromNick);
            if (index >= 0 && index < Path.Count - 1)
            {
                return Path[index + 1];
            }
            return null;
        }

        /// <summary>
        /// Можно ли использовать эту цепочку от указанного узла
        /// </summary>
        public bool CanUseFrom(string fromNick)
        {
            return Path.Contains(fromNick) && 
                   Path.IndexOf(fromNick) < Path.Count - 1 &&
                   IsValid;
        }
    }

    /// <summary>
    /// Информация о цепочке для передачи по сети
    /// </summary>
    public class RouteChainInfo
    {
        public string TargetNick { get; set; }
        public List<string> Path { get; set; } = new List<string>();
        public bool IsVerified { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Информация о NAT для клиента
    /// </summary>
    public class NatInfo
    {
        public string Nickname { get; set; }
        public bool IsBehindNat { get; set; }
        public string PublicEndpoint { get; set; }
        public List<string> KnownPublicIps { get; set; } = new List<string>();
    }

    #endregion
}