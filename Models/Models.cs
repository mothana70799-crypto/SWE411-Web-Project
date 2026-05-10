namespace TodoApp.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TodoItem> Todos { get; set; } = new();
}

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsCompleted { get; set; } = false;
    public string Priority { get; set; } = "medium";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public int? TimerMinutes { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
}

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record CreateTodoRequest(string Title, string? Description, string Priority, DateTime? DueDate, int? TimerMinutes);
public record UpdateTodoRequest(string Title, string? Description, bool IsCompleted, string Priority, DateTime? DueDate, int? TimerMinutes);
