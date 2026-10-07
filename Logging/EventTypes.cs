using Serilog;

namespace TaskManager.Logging
{
    // тип события - отдельное поле в каждой записи, по нему удобно фильтровать логи
    public static class EventTypes
    {
        public const string AppStarted = "AppStarted";
        public const string AppStopped = "AppStopped";
        public const string AppCrashed = "AppCrashed";
        public const string MenuSelected = "MenuSelected";
        public const string InvalidInput = "InvalidInput";

        public const string TaskCreated = "TaskCreated";
        public const string TaskDeleted = "TaskDeleted";
        public const string TaskCompleted = "TaskCompleted";
        public const string TasksListed = "TasksListed";
        public const string TasksImported = "TasksImported";
        public const string ValidationFailed = "ValidationFailed";
        public const string TaskNotFound = "TaskNotFound";

        public const string StorageLoaded = "StorageLoaded";
        public const string StorageSaved = "StorageSaved";
        public const string StorageError = "StorageError";

        public const string OperationFinished = "OperationFinished";
    }

    public static class LoggerExtensions
    {
        // _log.Event(EventTypes.TaskCreated).Information(...)
        public static ILogger Event(this ILogger logger, string eventType) =>
            logger.ForContext("EventType", eventType);
    }

    // идентификатор текущего запуска программы: отличает записи разных сессий в одном файле
    public static class Session
    {
        public static readonly string Id = Guid.NewGuid().ToString("N")[..8];
    }
}
