# KIT Alpha

KIT — Windows-first приложение, которое применяет выбранное окружение при запуске Counter-Strike 2 и возвращает систему в исходное состояние после завершения игровой сессии.

> **KIT works around the game, never inside it.**

KIT не внедряется в игру, не читает её память, не изменяет игровые файлы, не автоматизирует игровой ввод и не взаимодействует с античитом.

## Что уже работает

- ручной выбор точного `cs2.exe`;
- обнаружение запуска и завершения именно выбранного executable;
- обязательный неизменяемый **Vanilla Kit**;
- создание, переименование, удаление и выбор Active Kit;
- **Clean Mode**: выбранные приложения полностью завершаются при старте CS2, включая фоновые процессы в системном трее;
- **Launch Apps**: выбранные приложения запускаются вместе с игровой сессией;
- **Restore**: приложения из Clean Mode открываются снова, а запущенные самим KIT приложения получают обычный запрос на закрытие;
- сохранение recovery state и повторный Restore после аварийного завершения KIT;
- persistent session/activity history;
- live CPU/RAM monitoring of the selected CS2 process using standard Windows process counters;
- Sessions view with duration and average/peak CPU and RAM metrics;
- tray-режим: закрытие окна скрывает KIT, выход выполняется через tray menu;
- автоматическое скрытие окна в tray сразу после обнаружения запуска CS2;
- автоматическое возвращение окна из tray после завершения CS2 и Restore;
- переключение интерфейса между English и Русским с сохранением выбора;
- минималистичная тёмно-серая оболочка с левой навигацией и компактной строкой состояния;
- dashboard на Home с Active Kit, последней сессией, CPU/RAM и пятью последними действиями вместо технической стены логов;
- раздел Settings/Настройки с четырьмя сохраняемыми комбинациями оформления: Calm/Aggressive и Dark/Light;
- работа без постоянных прав администратора;
- локальное хранение без аккаунта и облака.

Для Clean Mode пользователь явно разрешает завершение выбранного `.exe`: KIT сначала запрашивает штатный выход, ждёт 2 секунды, а затем завершает оставшийся фоновый процесс. Это необходимо для tray-приложений вроде Telegram, но может привести к потере несохранённых данных, поэтому интерфейс показывает предупреждение перед добавлением. Завершённое приложение записывается в recovery state и запускается снова при Restore.

## Локальные данные

Файлы находятся в `%LOCALAPPDATA%\KIT`:

- `configuration.json` — выбранный `cs2.exe`;
- `kits.json` — Kits и Active Kit;
- `active-session.json` — состояние для аварийного Restore; удаляется после успешного отката;
- `preferences.json` — выбранный язык интерфейса;
- `activity.jsonl` — append-only журнал событий и предупреждений.
- `sessions.jsonl` — завершённые сессии и агрегированные CPU/RAM-метрики.

## Архитектура

- `KIT.Core` — Kits, session lifecycle, action/restore orchestration и recovery;
- `KIT.Infrastructure.Windows` — наблюдение за процессами и безопасные Windows-действия;
- `KIT.Data` — локальная JSON/JSONL персистентность;
- `KIT.App` — WPF UI, tray и composition root;
- `KIT.Core.Tests` — автономные проверки основных сценариев.

WPF выбран вместо WinUI 3, чтобы получить нативный Windows desktop UI без Windows App SDK, MSIX и дополнительного deployment-слоя. Core и Data от WPF не зависят.

## Сборка

Требуются Windows 10/11 и .NET 10 SDK.

```powershell
dotnet build KIT.sln
dotnet run --project src/KIT.App/KIT.App.csproj
dotnet run --project tests/KIT.Core.Tests/KIT.Core.Tests.csproj
```

## Как проверить сценарий Kit

1. Выбрать `cs2.exe` на Home.
2. На вкладке Kits создать Kit и переименовать его.
3. Добавить безопасное тестовое приложение в Clean Mode, например Notepad.
4. Добавить другое приложение в Launch Apps.
5. Нажать **Save**, затем **Make active**.
6. Открыть Clean Mode приложение и запустить CS2.
7. Проверить закрытие первого приложения, запуск второго и события `SessionStarted`/`KitApplied`.
8. Закрыть CS2 и проверить обратные действия и `KitRestored`/`SessionEnded`.
9. Для recovery-теста завершить KIT через Task Manager во время сессии, закрыть CS2 и снова открыть KIT.

## Пока не входит

GPU/VRAM и температуры, графики и расширенная аналитика Sessions, Deck, import/export `.kit`, установщик, подпись и автообновление.

Принципиально не входят: FPS, injection, memory reading, изменение файлов игры, arbitrary scripts, Workshop/plugins/cloud, macOS и другие игры.
