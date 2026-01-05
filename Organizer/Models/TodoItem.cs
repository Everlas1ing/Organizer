using SQLite;

public class TodoItem
{
    [PrimaryKey, AutoIncrement]
    public int ID { get; set; }

    public string TaskName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public int ReminderValue { get; set; } = -1; 
    public string ReminderUnit { get; set; } = string.Empty;
}