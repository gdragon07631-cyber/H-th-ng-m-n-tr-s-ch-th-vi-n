using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Project.Data;
using Project.Models;
using Project.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Data", "Keys")));
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (args.Contains("--create-admin", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminAccount>>();

    Console.Write("Email quản trị: ");
    var email = Console.ReadLine()?.Trim();
    Console.Write("Mật khẩu quản trị: ");
    var password = ReadPassword();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) ||
        string.IsNullOrEmpty(password))
    {
        Console.Error.WriteLine("Email hoặc mật khẩu không hợp lệ.");
        return;
    }

    await dbContext.Database.MigrateAsync();
    var normalizedEmail = email.ToUpperInvariant();
    if (await dbContext.AdminAccounts.AnyAsync(account => account.Email.ToUpper() == normalizedEmail))
    {
        Console.Error.WriteLine("Tài khoản email này đã tồn tại.");
        return;
    }

    var account = new AdminAccount { Email = email, IsActive = true };
    account.PasswordHash = passwordHasher.HashPassword(account, password);
    dbContext.AdminAccounts.Add(account);
    await dbContext.SaveChangesAsync();
    Console.WriteLine("Đã tạo tài khoản quản trị. Mật khẩu không được lưu dạng plaintext.");
    return;
}

if (args.Contains("--migrate-database", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied successfully.");
    return;
}

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) || System.Text.Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException(
        "Configure Jwt:SigningKey using dotnet user-secrets or the Jwt__SigningKey environment variable (minimum 32 bytes) before starting the web app.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();

static string ReadPassword()
{
    if (Console.IsInputRedirected)
    {
        throw new InvalidOperationException("Hãy chạy lệnh này trong terminal tương tác để nhập mật khẩu an toàn.");
    }

    var password = new System.Text.StringBuilder();
    ConsoleKeyInfo key;
    do
    {
        key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    } while (key.Key != ConsoleKey.Enter);

    return password.ToString();
}
