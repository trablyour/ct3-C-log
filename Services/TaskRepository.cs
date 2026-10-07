using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Serilog;
using TaskManager.Logging;
using TaskManager.Models;
using TaskManager.Tracing;

namespace TaskManager.Services
{
    public class TaskRepository
    {
        private readonly ILogger _log = Log.ForContext<TaskRepository>();
        private readonly string _filePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public TaskRepository(string filePath)
        {
            _filePath = filePath;
            _log.Debug("Репозиторий создан, файл данных {FilePath}", _filePath);
        }

        public List<TaskItem> Load()
        {
            using var trace = OperationTrace.Begin("LoadTasks");

            trace.Step($"проверка файла {_filePath}");
            if (!File.Exists(_filePath))
            {
                _log.Event(EventTypes.StorageLoaded)
                    .Information("Файл задач {FilePath} не найден, список пуст", _filePath);
                trace.Success("файла нет");
                return new List<TaskItem>();
            }

            trace.Step("чтение и разбор JSON");
            var tasks = ReadFile(_filePath);

            if (tasks == null)
            {
                trace.Fail("поврежденный файл");
                return new List<TaskItem>();
            }

            _log.Event(EventTypes.StorageLoaded)
                .Information("Загружено задач: {Count} из {FilePath} ({FileSizeBytes} байт)",
                    tasks.Count, _filePath, new FileInfo(_filePath).Length);

            trace.Success($"загружено {tasks.Count}");
            return tasks;
        }

        public void Save(List<TaskItem> tasks)
        {
            using var trace = OperationTrace.Begin("SaveTasks");
            var timer = Stopwatch.StartNew();

            try
            {
                trace.Step("сериализация в JSON");
                string json = JsonSerializer.Serialize(tasks, JsonOptions);

                trace.Step("запись в файл");
                string? folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(_filePath, json);

                _log.Event(EventTypes.StorageSaved)
                    .Debug("Сохранено задач: {Count}, размер {FileSizeBytes} байт, за {ElapsedMs} мс",
                        tasks.Count, new FileInfo(_filePath).Length, timer.ElapsedMilliseconds);

                trace.Success($"сохранено {tasks.Count}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Event(EventTypes.StorageError)
                    .Error(ex, "Не удалось сохранить задачи в {FilePath}", _filePath);
                trace.Fail(ex.Message);
                throw;
            }
        }

        public List<TaskItem>? Import(string path)
        {
            using var trace = OperationTrace.Begin("ImportTasks");

            trace.Step($"проверка файла {path}");
            if (!File.Exists(path))
            {
                _log.Event(EventTypes.StorageError)
                    .Warning("Файл для импорта не найден: {FilePath}", path);
                trace.Fail("файл не найден");
                return null;
            }

            trace.Step("чтение и разбор JSON");
            var tasks = ReadFile(path);

            if (tasks == null)
            {
                trace.Fail("ошибка чтения");
                return null;
            }

            trace.Success($"прочитано {tasks.Count}");
            return tasks;
        }

        private List<TaskItem>? ReadFile(string path)
        {
            try
            {
                _log.Debug("Чтение файла {FilePath}", path);
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
            }
            catch (JsonException ex)
            {
                // путь, строка и позиция ошибки сохраняются отдельными полями
                _log.Event(EventTypes.StorageError)
                    .Error(ex, "Файл {FilePath} поврежден: ошибка JSON в строке {LineNumber}, позиция {BytePosition}",
                        path, ex.LineNumber, ex.BytePositionInLine);
                return null;
            }
            catch (IOException ex)
            {
                _log.Event(EventTypes.StorageError)
                    .Error(ex, "Ошибка чтения файла {FilePath}", path);
                return null;
            }
        }
    }
}
