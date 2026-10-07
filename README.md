# TaskManager: структурированные логи

Консольное приложение для управления задачами (.NET, C#).

- **КТ1**: базовые операции + логирование и трассировка
- **КТ2**: логирование через Serilog (консоль, файлы с датами)
- **КТ3**: структурированные логи в JSON и их анализ

## Запуск

```bash
dotnet run              # меню
dotnet run -- --demo    # демонстрационный сценарий
```

## Что сделано в КТ3

### 1. Логи в формате JSON (CLEF)

Добавлен пакет `Serilog.Formatting.Compact` и еще один приемник:

```csharp
.WriteTo.File(new RenderedCompactJsonFormatter(), "logs/structured-.json",
              rollingInterval: RollingInterval.Day)
```

Каждая строка файла `logs/structured-ГГГГММДД.json` - отдельный JSON-объект:

```json
{"@t":"2026-10-07T11:02:11.4521Z","@m":"Создана задача #1 \"Купить продукты\" с приоритетом Low","@i":"a1b2c3d4",
 "TaskId":1,"Title":"Купить продукты","Priority":"Low",
 "Task":{"Id":1,"Title":"Купить продукты","Priority":"Low","IsDone":false,"CreatedAt":"2026-10-07T14:02:11"},
 "EventType":"TaskCreated","OperationId":2,"Operation":"AddTask","SourceContext":"TaskManager.Services.TaskService",
 "Application":"TaskManager","Version":"3.0","SessionId":"5f3c9a1e","MachineName":"DESKTOP-PC"}
```

Служебные поля: `@t` - время (UTC), `@m` - сообщение, `@l` - уровень (для Information не пишется), `@x` - исключение.

### 2. Обогащение записей

| Свойство | Откуда | Зачем |
|---|---|---|
| `Application`, `Version`, `MachineName` | `Enrich.WithProperty` | откуда пришла запись |
| `SessionId` | `Enrich.WithProperty` | отличить записи разных запусков |
| `SourceContext` | `Log.ForContext<T>()` | класс-источник |
| `EventType` | `ForContext("EventType", ...)` | тип события для фильтрации |
| `OperationId`, `Operation`, `ParentOperationId` | `LogContext.Push` в `OperationTrace` | связать все записи одной операции |
| `TaskId`, `Title`, `Priority`, `ElapsedMs`... | параметры сообщения | данные события |
| `Task` | `ForContext("Task", task, destructureObjects: true)` | весь объект задачи |

### 3. Анализ логов в программе

- **Статистика** (пункт меню 6): записи текущей сессии по уровням, по типам событий, время выполнения операций, последние ошибки.
- **История задачи** (пункт меню 7): все события по задаче, выбранные по полю `TaskId`.

Это показывает главный плюс структурированных логов: их можно фильтровать и обрабатывать программно, а не искать по тексту.

## Файлы логов

| Файл | Формат | Содержимое |
|---|---|---|
| `logs/structured-ГГГГММДД.json` | JSON (CLEF) | все записи со всеми свойствами |
| `logs/log-ГГГГММДД.txt` | текст | все записи |
| `logs/errors-ГГГГММДД.txt` | текст | Warning и выше |
| `logs/trace-ГГГГММДД.txt` | текст | пошаговая трассировка |
