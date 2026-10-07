using Serilog;
using TaskManager.Logging;
using TaskManager.Models;
using TaskManager.Tracing;

namespace TaskManager.Services
{
    public class OperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";

        public static OperationResult Ok(string message) => new() { Success = true, Message = message };
        public static OperationResult Error(string message) => new() { Success = false, Message = message };
    }

    public class TaskService
    {
        public const int MaxTitleLength = 100;

        private readonly ILogger _log = Log.ForContext<TaskService>();
        private readonly TaskRepository _repository;
        private readonly List<TaskItem> _tasks;

        public TaskService(TaskRepository repository)
        {
            _repository = repository;
            _tasks = _repository.Load();
            _log.Debug("Сервис задач инициализирован, задач в памяти: {Count}", _tasks.Count);
        }

        // ---------- создание ----------

        public OperationResult Add(string? title, TaskPriority priority = TaskPriority.Medium)
        {
            using var trace = OperationTrace.Begin("AddTask");
            _log.Debug("Запрос на добавление задачи: {RawTitle}, приоритет {Priority}", title, priority);

            trace.Step("проверка названия");
            title = title?.Trim() ?? "";

            if (title.Length == 0)
            {
                _log.Event(EventTypes.ValidationFailed)
                    .Warning("Ошибка проверки поля {Field}: {Reason}", "Title", "пустое значение");
                trace.Fail("пустое название");
                return OperationResult.Error("Название задачи не может быть пустым");
            }

            if (title.Length > MaxTitleLength)
            {
                _log.Event(EventTypes.ValidationFailed)
                    .Warning("Ошибка проверки поля {Field}: {Reason}, длина {Length}, максимум {MaxLength}",
                        "Title", "слишком длинное значение", title.Length, MaxTitleLength);
                trace.Fail("слишком длинное название");
                return OperationResult.Error($"Название не должно быть длиннее {MaxTitleLength} символов");
            }

            trace.Step("вычисление нового Id");
            int id = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;

            trace.Step("создание объекта задачи");
            var task = new TaskItem
            {
                Id = id,
                Title = title,
                Priority = priority,
                CreatedAt = DateTime.Now
            };
            _tasks.Add(task);

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            // ForContext("Task", task, true) - вся задача сохраняется в лог как вложенный объект
            _log.Event(EventTypes.TaskCreated)
                .ForContext("Task", task, destructureObjects: true)
                .Information("Создана задача #{TaskId} \"{Title}\" с приоритетом {Priority}",
                    task.Id, task.Title, task.Priority);

            trace.Success($"задача #{task.Id}");
            return OperationResult.Ok($"Задача #{task.Id} добавлена");
        }

        // ---------- удаление ----------

        public OperationResult Delete(int id)
        {
            using var trace = OperationTrace.Begin("DeleteTask");
            _log.Debug("Запрос на удаление задачи #{TaskId}", id);

            trace.Step($"поиск задачи #{id}");
            var task = _tasks.FirstOrDefault(t => t.Id == id);

            if (task == null)
            {
                _log.Event(EventTypes.TaskNotFound)
                    .Warning("Задача #{TaskId} не найдена (действие {Action}), всего задач {Count}",
                        id, "Delete", _tasks.Count);
                trace.Fail("задача не найдена");
                return OperationResult.Error($"Задача #{id} не найдена");
            }

            trace.Step("удаление из списка");
            _tasks.Remove(task);

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            double lifetime = Math.Round((DateTime.Now - task.CreatedAt).TotalMinutes, 1);

            _log.Event(EventTypes.TaskDeleted)
                .ForContext("Task", task, destructureObjects: true)
                .Information("Удалена задача #{TaskId} \"{Title}\", была выполнена: {WasDone}, существовала {LifetimeMinutes} мин",
                    task.Id, task.Title, task.IsDone, lifetime);

            trace.Success($"удалена #{id}");
            return OperationResult.Ok($"Задача #{id} \"{task.Title}\" удалена");
        }

        // ---------- выполнение ----------

        public OperationResult Complete(int id)
        {
            using var trace = OperationTrace.Begin("CompleteTask");
            _log.Debug("Запрос на выполнение задачи #{TaskId}", id);

            trace.Step($"поиск задачи #{id}");
            var task = _tasks.FirstOrDefault(t => t.Id == id);

            if (task == null)
            {
                _log.Event(EventTypes.TaskNotFound)
                    .Warning("Задача #{TaskId} не найдена (действие {Action}), всего задач {Count}",
                        id, "Complete", _tasks.Count);
                trace.Fail("задача не найдена");
                return OperationResult.Error($"Задача #{id} не найдена");
            }

            if (task.IsDone)
            {
                _log.Event(EventTypes.ValidationFailed)
                    .Warning("Задача #{TaskId} уже выполнена {CompletedAt}", id, task.CompletedAt);
                trace.Fail("уже выполнена");
                return OperationResult.Error($"Задача #{id} уже выполнена");
            }

            trace.Step("отметка выполнения");
            task.IsDone = true;
            task.CompletedAt = DateTime.Now;

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            double minutes = Math.Round((task.CompletedAt.Value - task.CreatedAt).TotalMinutes, 1);

            _log.Event(EventTypes.TaskCompleted)
                .Information("Задача #{TaskId} \"{Title}\" выполнена за {MinutesToComplete} мин, приоритет {Priority}",
                    task.Id, task.Title, minutes, task.Priority);

            trace.Success($"выполнена #{id}");
            return OperationResult.Ok($"Задача #{id} отмечена выполненной");
        }

        // ---------- импорт ----------

        public OperationResult Import(string path)
        {
            using var trace = OperationTrace.Begin("Import");
            _log.Debug("Запрос на импорт задач из {FilePath}", path);

            trace.Step("чтение файла");
            var imported = _repository.Import(path);

            if (imported == null)
            {
                trace.Fail("файл не прочитан");
                return OperationResult.Error("Не удалось импортировать задачи, подробности в логе");
            }

            trace.Step("добавление задач с новыми Id");
            int nextId = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
            int added = 0;
            foreach (var task in imported.Where(t => !string.IsNullOrWhiteSpace(t.Title)))
            {
                task.Id = nextId++;
                _tasks.Add(task);
                added++;
            }

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            _log.Event(EventTypes.TasksImported)
                .Information("Импорт из {FilePath}: добавлено {Added}, пропущено {Skipped}",
                    path, added, imported.Count - added);

            trace.Success($"импортировано {added}");
            return OperationResult.Ok($"Импортировано задач: {added}");
        }

        // ---------- вывод списка ----------

        public IReadOnlyList<TaskItem> GetAll()
        {
            using var trace = OperationTrace.Begin("ListTasks");

            trace.Step("сортировка: сначала невыполненные, затем по приоритету");
            var result = _tasks
                .OrderBy(t => t.IsDone)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.Id)
                .ToList();

            int done = result.Count(t => t.IsDone);
            int highActive = result.Count(t => !t.IsDone && t.Priority == TaskPriority.High);

            _log.Event(EventTypes.TasksListed)
                .Information("Список задач: всего {Total}, выполнено {Done}, в работе {Active}, срочных {HighPriorityActive}",
                    result.Count, done, result.Count - done, highActive);

            if (highActive >= 3)
                _log.Warning("Много невыполненных срочных задач: {HighPriorityActive}", highActive);

            trace.Success($"{result.Count} задач");
            return result;
        }
    }
}
