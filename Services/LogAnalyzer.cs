using System.Text.Json;

namespace TaskManager.Services
{
    // одна запись из структурированного лога
    public class LogRecord
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "Information";
        public string Message { get; set; } = "";
        public string? EventType { get; set; }
        public string? SessionId { get; set; }
        public string? Operation { get; set; }
        public string? Status { get; set; }
        public double? ElapsedMs { get; set; }
        public int? TaskId { get; set; }
        public string? Exception { get; set; }
    }

    // читает JSON-лог и строит по нему статистику.
    // это и есть главный плюс структурированных логов: их можно обрабатывать программно
    public class LogAnalyzer
    {
        private readonly string _folder;

        public LogAnalyzer(string folder)
        {
            _folder = folder;
        }

        public string? LatestFile()
        {
            if (!Directory.Exists(_folder))
                return null;

            return Directory.GetFiles(_folder, "structured-*.json")
                .OrderByDescending(f => f)
                .FirstOrDefault();
        }

        public List<LogRecord> ReadAll()
        {
            var result = new List<LogRecord>();
            string? path = LatestFile();
            if (path == null)
                return result;

            // FileShare.ReadWrite: файл в это время открыт Serilog на запись
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    result.Add(Parse(line));
                }
                catch (JsonException)
                {
                    // недописанная последняя строка - пропускаем
                }
            }

            return result;
        }

        // формат CLEF: служебные поля начинаются с @ (@t - время, @l - уровень, @m - сообщение, @x - исключение)
        private static LogRecord Parse(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            return new LogRecord
            {
                Time = root.TryGetProperty("@t", out var t) ? t.GetDateTime().ToLocalTime() : DateTime.MinValue,
                // для уровня Information поле @l не пишется
                Level = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information",
                Message = root.TryGetProperty("@m", out var m) ? m.GetString() ?? "" : "",
                Exception = root.TryGetProperty("@x", out var x) ? x.GetString() : null,
                EventType = GetString(root, "EventType"),
                SessionId = GetString(root, "SessionId"),
                Operation = GetString(root, "Operation"),
                Status = GetString(root, "Status"),
                ElapsedMs = root.TryGetProperty("ElapsedMs", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null,
                TaskId = root.TryGetProperty("TaskId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : null
            };
        }

        private static string? GetString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
