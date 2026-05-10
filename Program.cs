using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Data;
using TodoApp.Models;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKey_ChangeInProduction_2024!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TodoApp";

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite("Data Source=todos.db"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => {
        opt.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true, ValidateAudience = false, ValidateLifetime = true,
            ValidateIssuerSigningKey = true, ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

// ── REGISTER ──────────────────────────────────────────────────────────────────
app.MapPost("/api/auth/register", async (RegisterRequest req, AppDbContext db) =>
{
    var errors = new Dictionary<string, string>();

    // Username
    if (string.IsNullOrWhiteSpace(req.Username))
        errors["username"] = "Username is required.";
    else if (req.Username.Trim().Length < 3)
        errors["username"] = "Username must be at least 3 characters.";
    else if (req.Username.Trim().Length > 30)
        errors["username"] = "Username must be 30 characters or fewer.";
    else if (!Regex.IsMatch(req.Username.Trim(), @"^[a-zA-Z0-9_]+$"))
        errors["username"] = "Username may only contain letters, numbers, and underscores.";

    // Email
    if (string.IsNullOrWhiteSpace(req.Email))
        errors["email"] = "Email is required.";
    else if (!Regex.IsMatch(req.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        errors["email"] = "Please enter a valid email address.";
    else if (req.Email.Length > 200)
        errors["email"] = "Email is too long.";

    // Password
    if (string.IsNullOrWhiteSpace(req.Password))
        errors["password"] = "Password is required.";
    else if (req.Password.Length < 6)
        errors["password"] = "Password must be at least 6 characters.";
    else if (req.Password.Length > 128)
        errors["password"] = "Password must be 128 characters or fewer.";

    if (errors.Count > 0)
        return Results.BadRequest(new { message = "Validation failed.", errors });

    if (await db.Users.AnyAsync(u => u.Email == req.Email.Trim().ToLower()))
        return Results.BadRequest(new { message = "Validation failed.", errors = new { email = "Email is already registered." } });

    if (await db.Users.AnyAsync(u => u.Username == req.Username.Trim()))
        return Results.BadRequest(new { message = "Validation failed.", errors = new { username = "Username is already taken." } });

    var user = new User {
        Username = req.Username.Trim(),
        Email = req.Email.Trim().ToLower(),
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Account created successfully." });
});

// ── LOGIN ─────────────────────────────────────────────────────────────────────
app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db) =>
{
    var errors = new Dictionary<string, string>();
    if (string.IsNullOrWhiteSpace(req.Email))
        errors["email"] = "Email is required.";
    else if (!Regex.IsMatch(req.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        errors["email"] = "Please enter a valid email address.";
    if (string.IsNullOrWhiteSpace(req.Password))
        errors["password"] = "Password is required.";
    if (errors.Count > 0)
        return Results.BadRequest(new { message = "Validation failed.", errors });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower());
    if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.BadRequest(new { message = "Validation failed.", errors = new { general = "Incorrect email or password." } });

    return Results.Ok(new { token = GenerateJwt(user, jwtKey, jwtIssuer), user = new { user.Id, user.Username, user.Email } });
});

// ── TODOS ─────────────────────────────────────────────────────────────────────
app.MapGet("/api/todos", [Authorize] async (ClaimsPrincipal p, AppDbContext db) => {
    var uid = GetUserId(p);
    var todos = await db.Todos.Where(t => t.UserId == uid).OrderByDescending(t => t.CreatedAt)
        .Select(t => new { t.Id, t.Title, t.Description, t.IsCompleted, t.Priority, t.CreatedAt, t.DueDate, t.TimerMinutes })
        .ToListAsync();
    return Results.Ok(todos);
});

app.MapPost("/api/todos", [Authorize] async (CreateTodoRequest req, ClaimsPrincipal p, AppDbContext db) =>
{
    var errors = ValidateTodo(req.Title, req.Priority, req.DueDate, req.TimerMinutes);
    if (errors.Count > 0) return Results.BadRequest(new { message = "Validation failed.", errors });

    var uid = GetUserId(p);
    var todo = new TodoItem {
        Title = req.Title.Trim(),
        Description = req.Description?.Trim(),
        Priority = req.Priority,
        DueDate = req.DueDate,
        TimerMinutes = req.TimerMinutes,
        UserId = uid
    };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/todos/{todo.Id}", new { todo.Id, todo.Title, todo.Description, todo.IsCompleted, todo.Priority, todo.CreatedAt, todo.DueDate, todo.TimerMinutes });
});

app.MapPut("/api/todos/{id:int}", [Authorize] async (int id, UpdateTodoRequest req, ClaimsPrincipal p, AppDbContext db) =>
{
    var errors = ValidateTodo(req.Title, req.Priority, req.DueDate, req.TimerMinutes);
    if (errors.Count > 0) return Results.BadRequest(new { message = "Validation failed.", errors });

    var uid = GetUserId(p);
    var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid);
    if (todo is null) return Results.NotFound();

    todo.Title = req.Title.Trim();
    todo.Description = req.Description?.Trim();
    todo.IsCompleted = req.IsCompleted;
    todo.Priority = req.Priority;
    todo.DueDate = req.DueDate;
    todo.TimerMinutes = req.TimerMinutes;
    await db.SaveChangesAsync();
    return Results.Ok(new { todo.Id, todo.Title, todo.Description, todo.IsCompleted, todo.Priority, todo.CreatedAt, todo.DueDate, todo.TimerMinutes });
});

app.MapPatch("/api/todos/{id:int}/toggle", [Authorize] async (int id, ClaimsPrincipal p, AppDbContext db) => {
    var uid = GetUserId(p);
    var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid);
    if (todo is null) return Results.NotFound();
    todo.IsCompleted = !todo.IsCompleted;
    await db.SaveChangesAsync();
    return Results.Ok(new { todo.IsCompleted });
});

app.MapDelete("/api/todos/{id:int}", [Authorize] async (int id, ClaimsPrincipal p, AppDbContext db) => {
    var uid = GetUserId(p);
    var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid);
    if (todo is null) return Results.NotFound();
    db.Todos.Remove(todo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapDelete("/api/todos/completed", [Authorize] async (ClaimsPrincipal p, AppDbContext db) => {
    var uid = GetUserId(p);
    var done = await db.Todos.Where(t => t.UserId == uid && t.IsCompleted).ToListAsync();
    db.Todos.RemoveRange(done);
    await db.SaveChangesAsync();
    return Results.Ok(new { deleted = done.Count });
});

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────
static Dictionary<string, string> ValidateTodo(string? title, string? priority, DateTime? dueDate, int? timerMinutes)
{
    var errors = new Dictionary<string, string>();
    if (string.IsNullOrWhiteSpace(title))
        errors["title"] = "Title is required.";
    else if (title.Trim().Length < 2)
        errors["title"] = "Title must be at least 2 characters.";
    else if (title.Trim().Length > 200)
        errors["title"] = "Title must be 200 characters or fewer.";

    var validPriorities = new[] { "low", "medium", "high" };
    if (string.IsNullOrWhiteSpace(priority) || !validPriorities.Contains(priority))
        errors["priority"] = "Priority must be low, medium, or high.";

    if (dueDate.HasValue && dueDate.Value.Date < DateTime.UtcNow.Date)
        errors["dueDate"] = "Due date cannot be in the past.";

    if (timerMinutes.HasValue && (timerMinutes < 1 || timerMinutes > 480))
        errors["timerMinutes"] = "Timer must be between 1 and 480 minutes.";

    return errors;
}

static string GenerateJwt(User user, string key, string issuer) {
    var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(issuer: issuer,
        claims: [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username), new(ClaimTypes.Email, user.Email)],
        expires: DateTime.UtcNow.AddDays(7), signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static int GetUserId(ClaimsPrincipal p) => int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
