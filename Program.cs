using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;
using TaskManager.Logging;
using TaskManager.Models;
using TaskManager.Services;
using TaskManager.Tracing;

namespace TaskManager
{
    public static class Program
    {
        private const string LogTemplateConsole =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        private const string LogTemplateFile =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        private static TaskService _service = null!;
        private static readonly LogAnalyzer Analyzer = new("logs");
        private static TextWriterTraceListener _fileTrace = null!;
        private static readonly ColorConsoleTraceListener ConsoleTrace = new();
        private static bool _consoleTraceOn;

        // свойство, а не поле: логгер должен браться уже после настройки Log.Logger
        private static ILogger AppLog => Log.ForContext("SourceContext", "Program");

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // ================= инициализация Serilog =================
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                // свойства, которые добавляются к КАЖДОЙ записи
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "TaskManager")
                .Enrich.WithProperty("Version", "3.0")
                .Enrich.WithProperty("SessionId", Session.Id)
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                // консоль (для человека)
                .WriteTo.Console(
                    outputTemplate: LogTemplateConsole,
                    theme: AnsiConsoleTheme.Code)
                // текстовые файлы из КТ2 (для человека)
                .WriteTo.File(
                    "logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: LogTemplateFile)
                .WriteTo.File(
                    "logs/errors-.txt",
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: LogTemplateFile)
                // КТ3: структурированный лог, одна запись = один JSON-объект (формат CLEF)
                .WriteTo.File(
                    new RenderedCompactJsonFormatter(),
                    "logs/structured-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();
            // =========================================================

            var timer = Stopwatch.StartNew();

            try
            {
                AppLog.Event(EventTypes.AppStarted)
                    .Information("TaskManager запущен: сессия {SessionId}, ОС {OS}, .NET {Runtime}",
                        Session.Id, RuntimeInformation.OSDescription, Environment.Version.ToString());
                AppLog.Debug("Аргументы запуска {Args}, рабочая папка {Folder}", args, Directory.GetCurrentDirectory());

                SetupTracing();

                _service = new TaskService(new TaskRepository(Path.Combine("data", "tasks.json")));

                if (args.Contains("--demo"))
                    RunDemo();
                else
                    RunMenu();
            }
            catch (Exception ex)
            {
                AppLog.Event(EventTypes.AppCrashed).Fatal(ex, "Приложение аварийно завершилось");
                Console.WriteLine("Критическая ошибка: " + ex.Message);
            }
            finally
            {
                AppLog.Event(EventTypes.AppStopped)
                    .Information("TaskManager завершен, сессия длилась {SessionSeconds} с",
                        Math.Round(timer.Elapsed.TotalSeconds, 1));

                Trace.WriteLine($"========== Завершение {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
                Trace.Flush();
                _fileTrace?.Dispose();

                Log.CloseAndFlush();
            }
        }

        private static void SetupTracing()
        {
            Directory.CreateDirectory("logs");
            string traceFile = Path.Combine("logs", $"trace-{DateTime.Now:yyyyMMdd}.txt");
            _fileTrace = new TextWriterTraceListener(traceFile, "FileTrace");

            Trace.AutoFlush = true;
            Trace.IndentSize = 4;
            Trace.Listeners.Add(_fileTrace);

            Trace.WriteLine("");
            Trace.WriteLine($"========== Запуск {DateTime.Now:yyyy-MM-dd HH:mm:ss}, сессия {Session.Id} ==========");
        }

        // ================= меню =================

        private static void RunMenu()
        {
            while (true)
            {
                PrintMenu();
                Console.Write("Выберите пункт: ");
                string? choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                AppLog.Event(EventTypes.MenuSelected).Debug("Выбран пункт меню {MenuItem}", choice);

                switch (choice)
                {
                    case "1": AddTask(); break;
                    case "2": PrintTasks(_service.GetAll()); break;
                    case "3": DeleteTask(); break;
                    case "4": CompleteTask(); break;
                    case "5": ImportTasks(); break;
                    case "6": ShowLogStats(); break;
                    case "7": ShowTaskHistoryPrompt(); break;
                    case "8": ToggleConsoleTrace(); break;
                    case "9": ShowLogFiles(); break;
                    case "0": return;
                    default:
                        AppLog.Event(EventTypes.InvalidInput).Warning("Неизвестный пункт меню {MenuItem}", choice);
                        Error("Нет такого пункта меню");
                        break;
                }

                Console.WriteLine();
            }
        }

        private static void PrintMenu()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========== TaskManager (структурированные логи) ===========");
            Console.ResetColor();
            Console.WriteLine(" 1. Добавить задачу");
            Console.WriteLine(" 2. Показать задачи");
            Console.WriteLine(" 3. Удалить задачу");
            Console.WriteLine(" 4. Отметить задачу выполненной");
            Console.WriteLine(" 5. Импорт задач из JSON-файла");
            Console.WriteLine(" 6. Статистика по логам текущей сессии");
            Console.WriteLine(" 7. История задачи по логам");
            Console.WriteLine($" 8. Трассировка в консоли: {(_consoleTraceOn ? "ВКЛ" : "ВЫКЛ")}");
            Console.WriteLine(" 9. Файлы логов");
            Console.WriteLine(" 0. Выход");
        }

        private static void AddTask()
        {
            Console.Write("Название задачи: ");
            string? title = Console.ReadLine();

            Console.Write("Приоритет (1 - низкий, 2 - средний, 3 - высокий) [2]: ");
            string? input = Console.ReadLine()?.Trim();

            var priority = TaskPriority.Medium;
            if (!string.IsNullOrEmpty(input))
            {
                if (int.TryParse(input, out int p) && Enum.IsDefined(typeof(TaskPriority), p))
                    priority = (TaskPriority)p;
                else
                    AppLog.Event(EventTypes.InvalidInput)
                        .Warning("Некорректный приоритет {Input}, использован {Priority}", input, priority);
            }

            ShowResult(_service.Add(title, priority));
        }

        private static void DeleteTask()
        {
            int? id = ReadId("Id задачи для удаления: ");
            if (id != null)
                ShowResult(_service.Delete(id.Value));
        }

        private static void CompleteTask()
        {
            int? id = ReadId("Id выполненной задачи: ");
            if (id != null)
                ShowResult(_service.Complete(id.Value));
        }

        private static void ImportTasks()
        {
            Console.Write("Путь к JSON-файлу: ");
            string path = Console.ReadLine()?.Trim().Trim('"') ?? "";
            ShowResult(_service.Import(path));
        }

        private static void ShowTaskHistoryPrompt()
        {
            int? id = ReadId("Id задачи: ");
            if (id != null)
                ShowTaskHistory(id.Value);
        }

        private static int? ReadId(string prompt)
        {
            Console.Write(prompt);
            string? input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out int id) && id > 0)
                return id;

            AppLog.Event(EventTypes.InvalidInput).Warning("Некорректный Id {Input}", input);
            Error("Id должен быть положительным числом");
            return null;
        }

        private static void ToggleConsoleTrace()
        {
            _consoleTraceOn = !_consoleTraceOn;

            if (_consoleTraceOn)
                Trace.Listeners.Add(ConsoleTrace);
            else
                Trace.Listeners.Remove(ConsoleTrace);

            AppLog.Information("Трассировка в консоли: {TraceEnabled}", _consoleTraceOn);
        }

        private static void ShowLogFiles()
        {
            if (!Directory.Exists("logs"))
            {
                Info("Папка logs пока пуста");
                return;
            }

            Info($"Папка: {Path.GetFullPath("logs")}");
            foreach (var file in new DirectoryInfo("logs").GetFiles().OrderBy(f => f.Name))
                Info($"  {file.Name,-28} {file.Length,9} байт   {file.LastWriteTime:dd.MM.yyyy HH:mm:ss}");
        }

        // ================= анализ структурированного лога =================

        private static void ShowLogStats()
        {
            var records = Analyzer.ReadAll().Where(r => r.SessionId == Session.Id).ToList();

            if (records.Count == 0)
            {
                Info("В структурированном логе нет записей текущей сессии");
                return;
            }

            Header($"Статистика по логам (сессия {Session.Id}, файл {Path.GetFileName(Analyzer.LatestFile())})");
            Info($" Всего записей: {records.Count}");

            Console.WriteLine();
            Info(" По уровням:");
            foreach (var g in records.GroupBy(r => r.Level).OrderByDescending(g => g.Count()))
                Info($"   {g.Key,-12} {g.Count(),5}");

            Console.WriteLine();
            Info(" По типам событий:");
            foreach (var g in records.Where(r => r.EventType != null && r.EventType != EventTypes.OperationFinished)
                                     .GroupBy(r => r.EventType!)
                                     .OrderByDescending(g => g.Count()))
                Info($"   {g.Key,-18} {g.Count(),5}");

            var operations = records.Where(r => r.EventType == EventTypes.OperationFinished && r.ElapsedMs != null).ToList();
            if (operations.Count > 0)
            {
                Console.WriteLine();
                Info(" Операции (время выполнения, мс):");
                Info($"   {"Операция",-14} {"кол-во",7} {"ошибок",7} {"среднее",9} {"макс",9}");
                foreach (var g in operations.GroupBy(r => r.Operation ?? "?").OrderBy(g => g.Key))
                {
                    int failed = g.Count(r => r.Status != "OK");
                    Info($"   {g.Key,-14} {g.Count(),7} {failed,7} {g.Average(r => r.ElapsedMs!.Value),9:0.00} {g.Max(r => r.ElapsedMs!.Value),9:0.00}");
                }
            }

            var problems = records.Where(r => r.Level is "Warning" or "Error" or "Fatal").TakeLast(5).ToList();
            if (problems.Count > 0)
            {
                Console.WriteLine();
                Info(" Последние предупреждения и ошибки:");
                foreach (var r in problems)
                    WriteColored($"   {r.Time:HH:mm:ss} {r.Level,-8} {r.Message}",
                        r.Level == "Warning" ? ConsoleColor.Yellow : ConsoleColor.Red);
            }
        }

        private static void ShowTaskHistory(int taskId)
        {
            // выборка по полю TaskId: в обычном текстовом логе так просто не сделать
            var history = Analyzer.ReadAll().Where(r => r.TaskId == taskId).ToList();

            Header($"История задачи #{taskId} по логам");

            if (history.Count == 0)
            {
                Info(" Записей нет");
                return;
            }

            foreach (var r in history)
            {
                var color = r.Level switch
                {
                    "Warning" => ConsoleColor.Yellow,
                    "Error" or "Fatal" => ConsoleColor.Red,
                    _ => ConsoleColor.Gray
                };
                WriteColored($" {r.Time:dd.MM HH:mm:ss}  {r.EventType ?? r.Level,-16} {r.Message}", color);
            }
        }

        // ================= демонстрация =================

        private static void RunDemo()
        {
            AppLog.Information("Запущен демонстрационный сценарий");

            DemoStep(1, "Добавляем три задачи");
            _service.Add("Подготовить отчет по структурированным логам", TaskPriority.High);
            _service.Add("Купить продукты", TaskPriority.Low);
            _service.Add("Сделать домашнее задание", TaskPriority.Medium);

            DemoStep(2, "Ошибка проверки: пустое название");
            ShowResult(_service.Add("   "));

            DemoStep(3, "Список задач");
            var tasks = _service.GetAll();
            PrintTasks(tasks);

            DemoStep(4, "Выполняем задачу и пробуем выполнить ее повторно");
            int firstId = tasks.Count > 0 ? tasks[0].Id : 1;
            ShowResult(_service.Complete(firstId));
            ShowResult(_service.Complete(firstId));

            DemoStep(5, "Удаляем последнюю задачу и несуществующую #999");
            if (tasks.Count > 1)
                ShowResult(_service.Delete(tasks[^1].Id));
            ShowResult(_service.Delete(999));

            DemoStep(6, "Импорт из поврежденного файла");
            Directory.CreateDirectory("data");
            string broken = Path.Combine("data", "broken.json");
            File.WriteAllText(broken, "[ { \"Title\": \"ok\" }, { это не JSON ]");
            ShowResult(_service.Import(broken));

            DemoStep(7, "Анализ структурированного лога");
            ShowLogStats();

            DemoStep(8, $"История задачи #{firstId} из логов");
            ShowTaskHistory(firstId);

            Console.WriteLine();
            ShowLogFiles();
        }

        private static void DemoStep(int number, string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"--- Шаг {number}. {title} ---");
            Console.ResetColor();
        }

        // ================= вывод =================

        private static void PrintTasks(IReadOnlyList<TaskItem> list)
        {
            if (list.Count == 0)
            {
                Info("Список задач пуст");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($" {"Id",-4} {"Статус",-8} {"Приоритет",-10} {"Создана",-17} Название");
            Console.WriteLine(" " + new string('-', 75));

            foreach (var t in list)
            {
                Console.ForegroundColor = t.IsDone ? ConsoleColor.DarkGray
                    : t.Priority == TaskPriority.High ? ConsoleColor.Yellow
                    : ConsoleColor.White;

                string status = t.IsDone ? "готово" : "в работе";
                string priority = t.Priority switch
                {
                    TaskPriority.High => "высокий",
                    TaskPriority.Low => "низкий",
                    _ => "средний"
                };

                Console.WriteLine($" {t.Id,-4} {status,-8} {priority,-10} {t.CreatedAt,-17:dd.MM.yyyy HH:mm} {t.Title}");
            }

            Console.ResetColor();
            Console.WriteLine($" Всего: {list.Count}, выполнено: {list.Count(t => t.IsDone)}");
        }

        private static void ShowResult(OperationResult result)
        {
            if (result.Success)
                WriteColored("✓ " + result.Message, ConsoleColor.Green);
            else
                Error(result.Message);
        }

        private static void Header(string text) => WriteColored(text, ConsoleColor.Cyan);

        private static void Error(string text) => WriteColored("✗ " + text, ConsoleColor.Red);

        private static void Info(string text) => WriteColored(text, ConsoleColor.Gray);

        private static void WriteColored(string text, ConsoleColor color)
        {
            lock (ColorConsoleTraceListener.ConsoleLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ResetColor();
            }
        }
    }
}
