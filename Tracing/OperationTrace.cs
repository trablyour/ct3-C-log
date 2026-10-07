using System.Diagnostics;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;
using TaskManager.Logging;

namespace TaskManager.Tracing
{
    // трассировка операции (из КТ1) + в КТ3:
    // пока операция выполняется, все логи получают свойства OperationId, Operation и ParentOperationId,
    // а по завершении пишется структурированная запись с длительностью
    public sealed class OperationTrace : IDisposable
    {
        private static int _counter;
        private static readonly AsyncLocal<int?> CurrentOperation = new();

        private static ILogger Log => Serilog.Log.ForContext<OperationTrace>();

        private readonly int _id;
        private readonly int? _parentId;
        private readonly string _operation;
        private readonly Stopwatch _timer;
        private readonly IDisposable _logContext;
        private int _step;
        private bool _finished;

        private OperationTrace(string operation)
        {
            _id = Interlocked.Increment(ref _counter);
            _parentId = CurrentOperation.Value;
            _operation = operation;
            _timer = Stopwatch.StartNew();

            var properties = new List<ILogEventEnricher>
            {
                new PropertyEnricher("OperationId", _id),
                new PropertyEnricher("Operation", _operation)
            };
            if (_parentId != null)
                properties.Add(new PropertyEnricher("ParentOperationId", _parentId));

            _logContext = LogContext.Push(properties.ToArray());
            CurrentOperation.Value = _id;

            Write($">> START {_operation}");
            Trace.Indent();
        }

        public static OperationTrace Begin(string operation) => new OperationTrace(operation);

        public void Step(string description)
        {
            _step++;
            Write($"шаг {_step}: {description}");
        }

        public void Success(string? result = null) => Finish("OK", result);

        public void Fail(string reason) => Finish("FAIL", reason);

        public void Dispose()
        {
            if (!_finished)
                Finish("ABORTED", "операция прервана исключением");
        }

        private void Finish(string status, string? details)
        {
            if (_finished)
                return;

            _finished = true;
            _timer.Stop();
            double elapsed = Math.Round(_timer.Elapsed.TotalMilliseconds, 2);

            Trace.Unindent();
            string text = $"<< END {_operation}: {status} за {elapsed:0.00} мс";
            if (!string.IsNullOrEmpty(details))
                text += $" ({details})";
            Write(text);

            // структурированная запись: длительность и статус как отдельные поля
            Log.Event(EventTypes.OperationFinished)
               .Debug("Операция {Operation} завершена: {Status} за {ElapsedMs} мс, шагов {Steps}",
                      _operation, status, elapsed, _step);

            _logContext.Dispose();
            CurrentOperation.Value = _parentId;
        }

        private void Write(string message)
        {
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [op#{_id}] {message}");
        }
    }
}
