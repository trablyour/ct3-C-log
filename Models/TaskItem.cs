namespace TaskManager.Models
{
    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public bool IsDone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public override string ToString() => $"#{Id} \"{Title}\" [{Priority}]";
    }

    public enum TaskPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }
}
