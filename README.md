## 🖥 Системные требования

### Windows
- **ОС:** Windows 10/11 (x64)
- **.NET:** SDK 8.0
- **Qt:** 5.15+ или 6.x (Widgets, Core)
- **Сборка:** CMake 3.16+, компилятор MinGW 64-bit или MSVC
- **Среда:** Git (опционально, для клонирования)

### Linux
- **ОС:** Arch Linux, Ubuntu 22.04+, Fedora 38+ или совместимые дистрибутивы
- **.NET:** SDK 8.0
- **Qt:** 5.15+ или 6.x (пакеты `qtbase`, `qttools`)
- **Сборка:** CMake 3.16+, GCC 11+ или Clang 15+, `build-essential`/`base-devel`
- **Зависимости (Debian/Ubuntu):** `qt6-base-dev qt6-tools-dev cmake g++`
- **Зависимости (Arch):** `qt6-base qt6-tools cmake gcc`

---

## 🔨 Сборка проекта

### 1. Backend (`intercom-core`)
Консольное ядро на C#/.NET 8. Управляет сетью, маршрутизацией и хранением данных.

```bash
cd intercom-core
dotnet build -c Release
```
> 💡 Для получения готового к распространению бинарника используйте:  
> `dotnet publish -c Release -r win-x64 --self-contained false` (Windows)  
> `dotnet publish -c Release -r linux-x64 --self-contained false` (Linux)

### 2. Frontend (`InterCom-desktop`)
Графическая оболочка на C++/Qt. Рекомендуется собирать через консоль (кроссплатформенно и прозрачно).

#### 🖥 Консольная сборка (рекомендуется)
```bash
cd InterCom-desktop
cmake -B build -S .
cmake --build build --config Release
```

####  Сборка через Qt Creator
1. Откройте `InterCom-desktop/CMakeLists.txt` в Qt Creator.
2. Дождитесь завершения конфигурации CMake.
3. Выберите профиль сборки `Release`.
4. Нажмите `Ctrl+B` (или кнопку 🔨).

> ⚠️ **Важно:** Не размещайте исходный код в путях с пробелами или кириллицей (например, `Новая папка (1)`). Это вызывает ошибки экранирования в Makefile/CMake на Linux.

---

##  Подготовка к запуску (Deployment)

### Windows (портативная версия)
1. Соберите фронтенд в режиме `Release`.
2. Перейдите в папку сборки: `InterCom-desktop\build\Release`
3. Запустите деплой Qt-библиotec:
   ```cmd
   windeployqt InterCom-desktop.exe
   ```
4. **Удалите папку `styles`** из директории с `.exe`.  
   *Причина:* `windeployqt` копирует системные плагины стилей Qt, которые конфликтуют с кастомной темой `Fusion`, заданной в `main.cpp`. Без этой папки интерфейс отрисовывается коррект.
   ```cmd
   rmdir /s /q styles
   ```
5. Скопируйте `InterCom-core.exe` из `intercom-core\bin\Release\net8.0\` в ту же папку.
6. Готово. Запускайте `InterCom-desktop.exe`. Фронтенд автоматически найдёт бэкенд по относительному пути.

### Linux
- Бинарник фронтенда **динамически линкуется** с системными библиотеками Qt. Дополнительная упаковка не требуется для локального запуска.
- Проверьте зависимости командой: `ldd build/InterCom-desktop`
- Убедитесь, что `InterCom-core` находится в той же директории, что и фронтенд, или укажите путь в настройках.
- Для создания AppImage/Flatpak можно использовать `linuxdeployqt` или `craft`, но это выходит за рамки базовой сборки.

---

##  Запуск и использование

- **Полный режим:** Запустите `InterCom-desktop.exe` (или `./InterCom-desktop`). Фронтенд автоматически запустит бэкенд и установит IPC-соединение.
- **Headless/CLI режим:** Бэкенд может работать независимо. Запустите `InterCom-core.exe` (или `dotnet run` в папке ядра) и управляйте им через консольные команды (`help`, `scan`, `connect`, `send` и т.д.). GUI не обязателен для работы сети.
- **Настройки:** Сохраняются в `~/.config/Intercom-desktop/Intercom-desktop.ini` (Linux) или `%APPDATA%/MyCompany/Intercom-desktop.ini` (Windows).

---

## ⚠️ Известные особенности и ограничения

| Компонент | Статус | Примечание |
|-----------|--------|------------|
| **.NET Runtime** | ✅ .NET 8 | Другие версии не тестировались. Рекомендуется использовать LTS. |
| **Архитектура CPU** | ✅ x64 / ✅ ARM (Termux) | Backend протестирован на ARM64 в Termux. GUI на ARM не проверялся. |
| **Шифрование** | ❌ Отсутствует | Трафик передаётся в открытом виде. Проект ориентирован на доверенные LAN. |
| **Передача файлов** | 🚧 В разработке | Архитектура подготовлена, но чанкинг и контрольные суммы не реализованы. |
| **NAT Traversal** | 🔄 Ретрансляция | Обход NAT реализован через промежуточные узлы (mesh-relay). STUN/TURN не используется. |
