using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace network
{
    /// <summary>
    /// Улучшенный сервис ретрансляции сообщений с поддержкой цепочек маршрутов
    /// </summary>
    public class RelayService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ChatManager _chatManager;
        private readonly NicknameManager _nicknameManager;
        private readonly RouteManager _routeManager;
        private readonly string _myNickname;

        // Ожидающие подтверждения сообщения
        private readonly ConcurrentDictionary<string, PendingRelayMessage> _pendingMessages
            = new ConcurrentDictionary<string, PendingRelayMessage>();

        // Статистика по цепочкам
        private readonly ConcurrentDictionary<string, ChainStatistics> _chainStats
            = new ConcurrentDictionary<string, ChainStatistics>();

        public event Action<string, bool> OnRelayResult; // messageId, success

        public RelayService(
            ConnectionManager connectionManager, 
            ChatManager chatManager,
            NicknameManager nicknameManager, 
            RouteManager routeManager)
        {
            _connectionManager = connectionManager;
            _chatManager = chatManager;
            _nicknameManager = nicknameManager;
            _routeManager = routeManager;
            _myNickname = AppSettings.Nickname;
        }

        #region Отправка сообщений через цепочку

        /// <summary>
        /// Отправить сообщение через цепочку маршрутов
        /// </summary>
        public async Task<SendResult> SendRelayedMessageAsync(
    string targetNick,
    string message,
    CancellationToken ct = default)
        {
            string messageId = Guid.NewGuid().ToString();

            // Получаем лучшую цепочку
            var chain = _routeManager.GetBestChain(targetNick);

            if (chain == null)
            {
                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "Нет доступной цепочки маршрутов"
                };
            }

            // Всегда отправляем через цепочку (не пытаемся напрямую здесь)
            Console.WriteLine($"[RELAY] Отправляем через цепочку: {string.Join(" -> ", chain.Path)}");
            return await SendThroughChain(chain, targetNick, message, messageId, ct);
        }


        /// <summary>
        /// Попытка прямой отправки
        /// </summary>
        private async Task<SendResult> TryDirectSend(
        string targetNick, string message, string messageId, CancellationToken ct)
        {
            var targetIp = _routeManager.GetIpByNickname(targetNick);
            if (string.IsNullOrEmpty(targetIp))
            {
                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "IP адрес не найден"
                };
            }

            // Проверяем активное соединение
            var activeConnections = _connectionManager.GetActiveConnections();
            if (!activeConnections.Contains(targetIp))
            {
                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "Нет активного прямого соединения"
                };
            }

            // Отправляем через существующее соединение
            var result = await _connectionManager.SendMessageAsync(targetIp, message);
            if (result.Success)
            {
                _routeManager.MarkChainAsVerified(targetNick, new List<string> { _myNickname, targetNick });
            }
            return result;
        }


        /// <summary>
        /// Отправка через цепочку промежуточных узлов
        /// </summary>
        private async Task<SendResult> SendThroughChain(
            RouteChain chain, 
            string targetNick, 
            string message, 
            string messageId,
            CancellationToken ct)
        {
            var nextHopNick = chain.NextHop;
            if (string.IsNullOrEmpty(nextHopNick))
            {
                return new SendResult 
                { 
                    Success = false, 
                    ErrorMessage = "Некорректная цепочка" 
                };
            }

            // Формируем сообщение для ретрансляции
            var relayPayload = new RelayPayload
            {
                SourceNick = _myNickname,
                TargetNick = targetNick,
                MessageId = messageId,
                OriginalMessage = message,
                Path = chain.Path,
                CurrentHop = 0,
                MaxHops = chain.HopCount + 2, // Запас
                Timestamp = DateTime.UtcNow
            };

            string relayJson = JsonSerializer.Serialize(relayPayload);
            string relayMsg = $"RELAY_MSG:{relayJson}<END>";

            // Получаем IP следующего хопа
            var nextHopIps = _routeManager.GetAllIpsByNickname(nextHopNick);
            Console.WriteLine($"[DEBUG] Ищем IP для '{nextHopNick}', найдено: {nextHopIps.Count}");
            string nextHopIp = null;

            // Ищем активное соединение
            var activeConnections = _connectionManager.GetActiveConnections();
            nextHopIp = nextHopIps.FirstOrDefault(ip => activeConnections.Contains(ip));

            // Если нет активного, пробуем подключиться
            if (string.IsNullOrEmpty(nextHopIp) && nextHopIps.Count > 0)
            {
                nextHopIp = nextHopIps.First();
                Console.WriteLine($"[RELAY] Подключаемся к {nextHopNick} ({nextHopIp})...");
                
                bool connected = await _connectionManager.ConnectAsync(nextHopIp, AppSettings.DefaultPort, true);
                if (!connected)
                {
                    _routeManager.MarkChainAsFailed(targetNick, chain.Path);
                    return new SendResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Не удалось подключиться к {nextHopNick}" 
                    };
                }
            }

            if (string.IsNullOrEmpty(nextHopIp))
            {
                return new SendResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Нет IP адресов для {nextHopNick}" 
                };
            }

            // Регистрируем ожидание подтверждения
            var tcs = new TaskCompletionSource<bool>();
            _pendingMessages[messageId] = new PendingRelayMessage
            {
                MessageId = messageId,
                TargetNick = targetNick,
                OriginalMessage = message,  // ✅ ДОБАВИТЬ
                Chain = chain,
                CompletionSource = tcs,
                SentAt = DateTime.Now
            };


            // Отправляем
            bool sent = await _connectionManager.SendRawMessageAsync(nextHopIp, relayMsg);
            
            if (!sent)
            {
                _pendingMessages.TryRemove(messageId, out _);
                _routeManager.MarkChainAsFailed(targetNick, chain.Path);
                return new SendResult 
                { 
                    Success = false, 
                    ErrorMessage = "Не удалось отправить сообщение" 
                };
            }

            // Ждём подтверждения с таймаутом
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                
                try
                {
                    bool confirmed = await tcs.Task.WaitAsync(timeoutCts.Token);
                    
                    if (confirmed)
                    {
                        _routeManager.MarkChainAsVerified(targetNick, chain.Path);
                        OnRelayResult?.Invoke(messageId, true);
                        
                        return new SendResult 
                        { 
                            Success = true, 
                            MessageId = messageId,
                            Message = $"Доставлено через {chain.HopCount} хопов"
                        };
                    }
                    else
                    {
                        _routeManager.MarkChainAsFailed(targetNick, chain.Path);
                        OnRelayResult?.Invoke(messageId, false);
                        
                        return new SendResult 
                        { 
                            Success = false, 
                            ErrorMessage = "Подтверждение не получено" 
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    _pendingMessages.TryRemove(messageId, out _);
                    _routeManager.MarkChainAsFailed(targetNick, chain.Path);
                    OnRelayResult?.Invoke(messageId, false);
                    
                    return new SendResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Таймаут ожидания подтверждения" 
                    };
                }
            }
        }

        #endregion

        #region Обработка входящих ретранслируемых сообщений

        /// <summary>
        /// Обработать входящее ретранслируемое сообщение
        /// </summary>
        public async Task ProcessRelayMessageAsync(string fromIp, string relayData)
        {
            try
            {
                // Парсим JSON payload
                var payload = JsonSerializer.Deserialize<RelayPayload>(relayData);
                
                if (payload == null)
                {
                    Console.WriteLine($"[RELAY] Ошибка парсинга сообщения от {fromIp}");
                    return;
                }

                string fromNick = _nicknameManager.GetNicknameByIp(fromIp) ?? fromIp;

                Console.WriteLine($"[RELAY] Получено сообщение {payload.MessageId} " +
                                $"от {payload.SourceNick} к {payload.TargetNick} " +
                                $"(хоп {payload.CurrentHop + 1}/{payload.Path.Count - 1})");

                // Проверяем, не истёк ли TTL
                if (payload.CurrentHop >= payload.MaxHops || 
                    (DateTime.UtcNow - payload.Timestamp).TotalMinutes > 5)
                {
                    Console.WriteLine($"[RELAY] Сообщение {payload.MessageId} отброшено (TTL)");
                    return;
                }

                // Это сообщение для нас?
                if (payload.TargetNick.Equals(_myNickname, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMessageForMe(payload, fromNick);
                    return;
                }

                // Нужно переслать дальше
                await ForwardMessage(payload, fromNick);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RELAY] Ошибка обработки: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработка сообщения, адресованного нам
        /// </summary>
        private async Task HandleMessageForMe(RelayPayload payload, string fromNick)
        {
            Console.WriteLine($"[RELAY] Сообщение {payload.MessageId} доставлено конечному получателю");

            // ✅ ИСПРАВЛЕНИЕ: Проверяем что сообщение не пустое и не системное
            if (string.IsNullOrWhiteSpace(payload.OriginalMessage))
                return;

            // ✅ ИСПРАВЛЕНИЕ: Не сохраняем системные/служебные сообщения в чат
            if (IsSystemMessage(payload.OriginalMessage))
            {
                Console.WriteLine($"[RELAY] Системное сообщение не сохраняется в чат: {payload.OriginalMessage.Substring(0, Math.Min(30, payload.OriginalMessage.Length))}...");
                return;
            }

            // ✅ ИСПРАВЛЕНИЕ: Сохраняем в чат с отправителем (payload.SourceNick), а не с самим собой
            // Для получателя lew: чат с pis должен быть в папке chat_pis/
            await _chatManager.SaveMessageAsync(
                payload.SourceNick,      // Отправитель (pis)
                payload.SourceNick,      // Имя чата = собеседник (pis) - исправлено!
                payload.OriginalMessage,
                DateTime.Now
            );

            // Отправляем подтверждение обратно
            await SendConfirmation(payload, true);
        }
        private bool IsSystemMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return true;

            return message.StartsWith("ROUTE_UPDATE:") ||
                   message.StartsWith("RELAY:") ||
                   message.StartsWith("RELAY_MSG:") ||
                   message.StartsWith("RELAY_CONFIRM:") ||
                   message.StartsWith("PING") ||
                   message.StartsWith("PONG") ||
                   message.StartsWith("AUTH:") ||
                   message.StartsWith("AUTH_OK:") ||
                   message.StartsWith("CONFIRM:") ||
                   message.StartsWith("FILE_") ||
                   message.StartsWith("[SYSTEM]") ||
                   message.StartsWith("[RELAY]") ||
                   message.StartsWith("[CONNECT]") ||
                   message.StartsWith("[AUTO]") ||
                   message.StartsWith("[DEBUG]");
        }


        /// <summary>
        /// Пересылка сообщения следующему узлу
        /// </summary>
        private async Task ForwardMessage(RelayPayload payload, string fromNick)
        {
            // Находим следующий хоп
            int myIndex = payload.Path.IndexOf(_myNickname);
            if (myIndex < 0 || myIndex >= payload.Path.Count - 1)
            {
                Console.WriteLine($"[RELAY] Не найден следующий хоп для {payload.MessageId}");
                await SendConfirmation(payload, false);
                return;
            }

            string nextNick = payload.Path[myIndex + 1];
            var nextIps = _routeManager.GetAllIpsByNickname(nextNick);

            if (nextIps.Count == 0)
            {
                Console.WriteLine($"[RELAY] Нет IP для {nextNick}");
                await SendConfirmation(payload, false);
                return;
            }

            // Обновляем payload
            payload.CurrentHop++;

            string newPayload = JsonSerializer.Serialize(payload);
            string relayMsg = $"RELAY_MSG:{newPayload}<END>";

            // Ищем активное соединение или подключаемся
            var activeConnections = _connectionManager.GetActiveConnections();
            string nextIp = nextIps.FirstOrDefault(ip => activeConnections.Contains(ip));

            if (string.IsNullOrEmpty(nextIp))
            {
                nextIp = nextIps.First();
                bool connected = await _connectionManager.ConnectAsync(nextIp, AppSettings.DefaultPort, true);

                if (!connected)
                {
                    Console.WriteLine($"[RELAY] Не удалось подключиться к {nextNick}");
                    await SendConfirmation(payload, false);
                    return;
                }
            }

            // Отправляем
            bool sent = await _connectionManager.SendRawMessageAsync(nextIp, relayMsg);

            if (!sent)
            {
                Console.WriteLine($"[RELAY] Не удалось переслать {payload.MessageId} на {nextNick}");
                await SendConfirmation(payload, false);
            }
            else
            {
                Console.WriteLine($"[RELAY] Сообщение {payload.MessageId} переслано на {nextNick}");
            }
        }
        // В методе NotifyConfirmationReceived оставляем только:
        public void NotifyConfirmationReceived(string messageId)
        {
            if (_pendingMessages.TryRemove(messageId, out var pending))
            {
                pending.CompletionSource.TrySetResult(true);
                OnRelayResult?.Invoke(messageId, true);

                Console.WriteLine($"[RELAY] Подтверждение получено для сообщения {messageId}");

                // Сохранение уже сделано в ProcessConfirmation, здесь не нужно!
            }
        }





        /// <summary>
        /// Отправить подтверждение доставки обратно отправителю
        /// </summary>
        private async Task SendConfirmation(RelayPayload originalPayload, bool success)
        {
            // Строим обратный путь
            var reversePath = new List<string>(originalPayload.Path);
            reversePath.Reverse();

            var confirmation = new RelayConfirmation
            {
                OriginalMessageId = originalPayload.MessageId,
                Success = success,
                ConfirmedBy = _myNickname,
                Path = reversePath,
                Timestamp = DateTime.UtcNow
            };

            string confirmJson = JsonSerializer.Serialize(confirmation);
            string confirmMsg = $"RELAY_CONFIRM:{confirmJson}<END>";

            // Отправляем предыдущему узлу (который нам отправил)
            string prevNick = reversePath.Count > 1 ? reversePath[1] : originalPayload.SourceNick;
            var prevIps = _routeManager.GetAllIpsByNickname(prevNick);

            if (prevIps.Count > 0)
            {
                // ✅ Пробуем все IP пока не найдём рабочий
                foreach (var prevIp in prevIps)
                {
                    bool sent = await _connectionManager.SendRawMessageAsync(prevIp, confirmMsg);
                    if (sent)
                    {
                        Console.WriteLine($"[RELAY] Подтверждение отправлено к {prevNick} через {prevIp}");
                        return;
                    }
                }

                // ✅ Если нет прямого соединения - пробуем через других соседей (ретрансляция подтверждения)
                Console.WriteLine($"[RELAY] Нет прямого соединения с {prevNick}, ищем обходной путь...");

                var neighbors = _routeManager.GetNeighbors(_myNickname);
                foreach (var neighbor in neighbors)
                {
                    if (neighbor == prevNick) continue;

                    var neighborIps = _routeManager.GetAllIpsByNickname(neighbor);
                    foreach (var nip in neighborIps)
                    {
                        // Проверяем, знает ли сосед путь к предыдущему узлу
                        var neighborNeighbors = _routeManager.GetNeighbors(neighbor);
                        if (neighborNeighbors.Contains(prevNick))
                        {
                            // Отправляем подтверждение через этого соседа
                            var forwardConfirm = new RelayConfirmation
                            {
                                OriginalMessageId = originalPayload.MessageId,
                                Success = success,
                                ConfirmedBy = _myNickname,
                                Path = new List<string> { _myNickname, neighbor, prevNick },
                                Timestamp = DateTime.UtcNow
                            };

                            string forwardJson = JsonSerializer.Serialize(forwardConfirm);
                            string forwardMsg = $"RELAY_CONFIRM:{forwardJson}<END>";

                            bool sent = await _connectionManager.SendRawMessageAsync(nip, forwardMsg);
                            if (sent)
                            {
                                Console.WriteLine($"[RELAY] Подтверждение отправлено через {neighbor} к {prevNick}");
                                return;
                            }
                        }
                    }
                }

                Console.WriteLine($"[RELAY] Не удалось отправить подтверждение к {prevNick}");
            }
        }


        // В методе ProcessConfirmation оставляем сохранение только в одном месте:
        public void ProcessConfirmation(string confirmData)
        {
            try
            {
                var confirmation = JsonSerializer.Deserialize<RelayConfirmation>(confirmData);
                if (confirmation == null) return;

                Console.WriteLine($"[RELAY] Получено подтверждение для {confirmation.OriginalMessageId} " +
                                $"(успех: {confirmation.Success})");

                // Если мы отправитель оригинального сообщения
                if (confirmation.Path.Last().Equals(_myNickname, StringComparison.OrdinalIgnoreCase))
                {
                    if (_pendingMessages.TryRemove(confirmation.OriginalMessageId, out var pending))
                    {
                        pending.CompletionSource.TrySetResult(confirmation.Success);
                        OnRelayResult?.Invoke(confirmation.OriginalMessageId, confirmation.Success);

                        // Сохраняем сообщение ТОЛЬКО здесь, при получении подтверждения
                        // if (confirmation.Success && pending != null && !string.IsNullOrEmpty(pending.OriginalMessage))
                        // {
                        //     _ = Task.Run(async () =>
                        //     {
                        //         try
                        //         {
                        //             await _chatManager.SaveMessageAsync(
                        //                 AppSettings.Nickname,
                        //                 pending.TargetNick,
                        //                 pending.OriginalMessage,
                        //                 DateTime.Now
                        //             );

                        //             Console.ForegroundColor = ConsoleColor.Cyan;
                        //             Console.WriteLine($"[{DateTime.Now:HH:mm}] Я -> {pending.TargetNick}: {pending.OriginalMessage}");
                        //             Console.ResetColor();
                        //         }
                        //         catch (Exception ex)
                        //         {
                        //             Console.WriteLine($"[ERROR] Ошибка сохранения отправленного сообщения: {ex.Message}");
                        //         }
                        //     });
                        // }
                    }
                    return;
                }

                // Пересылаем подтверждение дальше по обратному пути
                _ = Task.Run(async () => await ForwardConfirmation(confirmation));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RELAY] Ошибка обработки подтверждения: {ex.Message}");
            }
        }


        /// <summary>
        /// Пересылка подтверждения по обратному пути
        /// </summary>
        private async Task ForwardConfirmation(RelayConfirmation confirmation)
        {
            int myIndex = confirmation.Path.IndexOf(_myNickname);
            if (myIndex < 0 || myIndex >= confirmation.Path.Count - 1) return;

            string nextNick = confirmation.Path[myIndex + 1];
            var nextIps = _routeManager.GetAllIpsByNickname(nextNick);
            
            if (nextIps.Count == 0) return;

            string confirmJson = JsonSerializer.Serialize(confirmation);
            string confirmMsg = $"RELAY_CONFIRM:{confirmJson}<END>";

            string nextIp = nextIps.First();
            await _connectionManager.SendRawMessageAsync(nextIp, confirmMsg);
        }

        #endregion

        #region Вспомогательные классы

        private class PendingRelayMessage
        {
            public string MessageId { get; set; }
            public string TargetNick { get; set; }
            public string OriginalMessage { get; set; }  // ✅ ДОБАВИТЬ
            public RouteChain Chain { get; set; }
            public TaskCompletionSource<bool> CompletionSource { get; set; }
            public DateTime SentAt { get; set; }
        }


        private class ChainStatistics
        {
            public string ChainKey { get; set; }
            public int SuccessCount { get; set; }
            public int FailCount { get; set; }
            public DateTime LastUsed { get; set; }
        }

        #endregion
    }

    #region DTO для сериализации

    public class RelayPayload
    {
        public string SourceNick { get; set; }
        public string TargetNick { get; set; }
        public string MessageId { get; set; }
        public string OriginalMessage { get; set; }
        public List<string> Path { get; set; } = new List<string>();
        public int CurrentHop { get; set; }
        public int MaxHops { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class RelayConfirmation
    {
        public string OriginalMessageId { get; set; }
        public bool Success { get; set; }
        public string ConfirmedBy { get; set; }
        public List<string> Path { get; set; } = new List<string>();
        public DateTime Timestamp { get; set; }
    }

    #endregion
}