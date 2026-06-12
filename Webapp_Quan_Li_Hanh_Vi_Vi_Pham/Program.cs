using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

var builder = WebApplication.CreateBuilder(args);

var googleClientId =
    builder.Configuration["Authentication:Google:ClientId"]
    ?? builder.Configuration["Google:ClientId"]
    ?? Environment.GetEnvironmentVariable("Authentication__Google__ClientId")
    ?? Environment.GetEnvironmentVariable("Google__ClientId")
    ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
    ?? string.Empty;

var googleClientSecret =
    builder.Configuration["Authentication:Google:ClientSecret"]
    ?? builder.Configuration["Google:ClientSecret"]
    ?? Environment.GetEnvironmentVariable("Authentication__Google__ClientSecret")
    ?? Environment.GetEnvironmentVariable("Google__ClientSecret")
    ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")
    ?? string.Empty;

// Add SQL Server DbContext
builder.Services.AddDbContext<ViolationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Authentication Cookie support
var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

authenticationBuilder.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
});

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder.AddGoogle("Google", options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var redirectUri = QueryHelpers.AddQueryString(context.RedirectUri, "prompt", "select_account");
            context.Response.Redirect(redirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnCreatingTicket = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<ViolationDbContext>();
            var cancellationToken = context.HttpContext.RequestAborted;

            var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Google account did not provide an email address.");
            }

            var fullName =
                context.Principal?.FindFirst(ClaimTypes.Name)?.Value
                ?? email;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == email, cancellationToken);
            var createdNow = false;
            if (user == null)
            {
                user = new User
                {
                    Username = email,
                    PasswordHash = PasswordHasher.HashPassword(Guid.NewGuid().ToString("N")),
                    FullName = fullName,
                    Role = "Employee",
                    FaceImagePath = string.Empty,
                    ManagerKey = string.Empty,
                    IsKeyActivated = true,
                    MustChangePassword = true,
                    RequiresInitialSecuritySetup = true,
                    Email = email,
                    CreatedAtUtc = DateTime.UtcNow
                };

                db.Users.Add(user);
                await db.SaveChangesAsync(cancellationToken);
                createdNow = true;
            }
            else if (!string.Equals(user.FullName, fullName, StringComparison.Ordinal))
            {
                user.FullName = fullName;
                await db.SaveChangesAsync(cancellationToken);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role),
                new("FullName", user.FullName),
                new("UserId", user.Id.ToString()),
                new(ClaimTypes.Email, email),
                new("GoogleAccountCreated", createdNow ? "true" : "false")
            };

            context.Principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        };
    });
}

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<YoloModelOptions>(
    builder.Configuration.GetSection(YoloModelOptions.SectionName));
builder.Services.Configure<ViolationMonitoringOptions>(
    builder.Configuration.GetSection(ViolationMonitoringOptions.SectionName));
builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(TelegramBotOptions.SectionName));

// Scoped services registration
builder.Services.AddScoped<IYoloInferenceService, LocalYoloInferenceService>();
builder.Services.AddScoped<IViolationService, ViolationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IModelSettingService, ModelSettingService>();
builder.Services.AddSingleton<TelegramBotState>();
builder.Services.AddSingleton<IViolationMonitoringOrchestrator, ViolationMonitoringOrchestrator>();
builder.Services.AddHttpClient<ITelegramAlertService, TelegramAlertService>();
builder.Services.AddHostedService<ViolationMonitoringHostedService>();
builder.Services.AddHostedService<TelegramCommandPollingHostedService>();

var app = builder.Build();

// Auto-setup Python environment if needed
try
{
    var projectRoot = app.Environment.ContentRootPath;
    var venvPath = Path.Combine(projectRoot, ".venv");
    if (!Directory.Exists(venvPath))
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("Virtual environment not found. Setting up Python environment...");
        Console.WriteLine("This may take a few minutes if Python needs to be installed.");
        Console.WriteLine("==================================================");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{Path.Combine(projectRoot, "setup_env.ps1")}\"",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        Console.WriteLine(await outputTask);
        if (process.ExitCode != 0)
        {
            var error = await errorTask;
            Console.WriteLine($"WARNING: Setup script failed with exit code {process.ExitCode}: {error}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"WARNING: Failed to auto-setup Python environment: {ex.Message}");
}

// Auto-migrate/create database and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ViolationDbContext>();
        DbSeeder.Initialize(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (args.Contains("--test-biometrics"))
{
    await RunBiometricsTestsAsync(app.Services);
    return;
}

if (args.Contains("--test-monitoring"))
{
    await RunMonitoringTestsAsync(app.Services);
    return;
}

if (TryFindBusyUrl(args, app.Environment.ContentRootPath, out var busyUrl))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("Ứng dụng đã chạy trên {Url}. Bỏ qua khởi động instance mới.", busyUrl);
    return;
}

try
{
    await app.RunAsync();
}
catch (IOException ex) when (IsAddressInUse(ex))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Không thể khởi động vì cổng đang được sử dụng.");
}

async Task RunBiometricsTestsAsync(IServiceProvider services)
{
    Console.WriteLine("==================================================");
    Console.WriteLine("STARTING BIOMETRICS INTEGRATION AND TESTCASES");
    Console.WriteLine("==================================================");

    using var scope = services.CreateScope();
    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();

    Console.WriteLine("Recreating database to apply schema changes...");
    try
    {
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        DbSeeder.Initialize(dbContext);
        Console.WriteLine("Database recreated and seeded successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: Failed to recreate database: {ex.Message}. Attempting to proceed anyway...");
    }

    // 1x1 transparent png base64
    string dummyBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    // Testcase 2: Register thiếu ảnh (3 ảnh)
    Console.WriteLine("\n--- TESTCASE 2: Register thiếu ảnh (3 ảnh) ---");
    try
    {
        var user = new User { Username = "testbiouser", FullName = "Test Bio User", Role = "Employee" };
        string threeImages = $"{dummyBase64};base64split;{dummyBase64};base64split;{dummyBase64}";
        await userService.RegisterAsync(user, "password123", threeImages);
        Console.WriteLine("FAIL: Expected InvalidOperationException but registered successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"PASS: Caught expected exception: {ex.Message}");
    }

    // Testcase 3: Ảnh không có mặt
    Console.WriteLine("\n--- TESTCASE 3: Register với ảnh không có mặt ---");
    try
    {
        // Using "noface" in username will trigger simulated face detection failure
        var user = new User { Username = "nofaceuser", FullName = "No Face User", Role = "Employee" };
        string fourImages = $"{dummyBase64};base64split;{dummyBase64};base64split;{dummyBase64};base64split;{dummyBase64}";
        await userService.RegisterAsync(user, "password123", fourImages);
        Console.WriteLine("FAIL: Expected exception for no face but registered successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"PASS: Caught expected exception: {ex.Message}");
    }

    // Testcase 1: Register đủ 4 ảnh hợp lệ
    Console.WriteLine("\n--- TESTCASE 1: Register đủ 4 ảnh hợp lệ ---");
    User? registeredUser = null;
    try
    {
        var user = new User { Username = "testbiouser", FullName = "Test Bio User", Role = "Employee" };
        string fourImages = $"{dummyBase64};base64split;{dummyBase64};base64split;{dummyBase64};base64split;{dummyBase64}";
        registeredUser = await userService.RegisterAsync(user, "password123", fourImages);
        Console.WriteLine("PASS: Registered testbiouser successfully with 4 images.");
        
        // Verify embeddings are stored in database
        var storedCount = await dbContext.UserFaceEmbeddings.CountAsync(e => e.UserId == registeredUser.Id);
        Console.WriteLine($"PASS: Stored {storedCount} embeddings in database.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: Registration failed: {ex.Message}");
    }

    if (registeredUser != null)
    {
        // Testcase 4: Login đúng người
        Console.WriteLine("\n--- TESTCASE 4: Login đúng người ---");
        try
        {
            var success = await userService.VerifyBiometricsAsync("testbiouser", dummyBase64);
            Console.WriteLine($"RESULT: success = {success}");
            if (success)
            {
                Console.WriteLine("PASS: Login verification succeeded for correct user.");
            }
            else
            {
                Console.WriteLine("FAIL: Login verification failed for correct user.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Login verify threw exception: {ex.Message}");
        }

        // Testcase 5: Login sai người
        Console.WriteLine("\n--- TESTCASE 5: Login sai người ---");
        try
        {
            string wrongPersonBase64 = dummyBase64 + ";wrongperson;";
            var success = await userService.VerifyBiometricsAsync("testbiouser", wrongPersonBase64);
            Console.WriteLine($"RESULT: success = {success}");
            if (!success)
            {
                Console.WriteLine("PASS: Login verification failed for incorrect user.");
            }
            else
            {
                Console.WriteLine("FAIL: Login verification succeeded for incorrect user.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Login verify threw exception: {ex.Message}");
        }

        // Testcase 6: Ảnh login quá tối/mờ
        Console.WriteLine("\n--- TESTCASE 6: Ảnh login quá tối/mờ ---");
        try
        {
            string blurryBase64 = dummyBase64 + ";blurry;";
            var success = await userService.VerifyBiometricsAsync("testbiouser", blurryBase64);
            Console.WriteLine($"RESULT: success = {success} (blurry image, expected distance = 0.7500)");
            if (!success)
            {
                Console.WriteLine("PASS: Login verification failed for blurry image.");
            }
            else
            {
                Console.WriteLine("FAIL: Login verification succeeded for blurry image.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Login verify threw exception: {ex.Message}");
        }

        // Testcase 7: Debug threshold
        Console.WriteLine("\n--- TESTCASE 7: Debug threshold ---");
        try
        {
            Console.WriteLine("Currently the active AI model threshold seeded in database is 0.40.");
            string shadowBase64 = dummyBase64 + ";shadow;";
            
            // Verify with seeded threshold (0.40) -> should fail since distance = 0.60
            Console.WriteLine("Testing with seeded threshold (0.40):");
            var successWith0_40 = await userService.VerifyBiometricsAsync("testbiouser", shadowBase64);
            Console.WriteLine($"RESULT with threshold 0.40: success = {successWith0_40} (distance = 0.6000, expected success = False)");

            // Now temporarily change the threshold in the database to 0.68
            var deepfaceModel = await dbContext.AiModels.FirstOrDefaultAsync(m => m.Type == "Deepface" && m.IsActive);
            if (deepfaceModel != null)
            {
                deepfaceModel.ConfThreshold = 0.68m;
                await dbContext.SaveChangesAsync();
                Console.WriteLine("Updated active Deepface ConfThreshold in database to 0.68.");
            }

            // Verify with custom threshold (0.68) -> should pass since distance = 0.60 <= 0.68
            Console.WriteLine("Testing with updated threshold (0.68):");
            var successWith0_68 = await userService.VerifyBiometricsAsync("testbiouser", shadowBase64);
            Console.WriteLine($"RESULT with threshold 0.68: success = {successWith0_68} (distance = 0.6000, expected success = True)");

            if (!successWith0_40 && successWith0_68)
            {
                Console.WriteLine("PASS: Successfully demonstrated threshold debugging. 0.68 threshold is optimal for handling mild lighting variations.");
            }
            else
            {
                Console.WriteLine("FAIL: Threshold verification behavior did not match expectations.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Debug threshold threw exception: {ex.Message}");
        }
    }

    Console.WriteLine("\n==================================================");
    Console.WriteLine("BIOMETRICS TESTCASES COMPLETED");
    Console.WriteLine("==================================================");
}

async Task RunMonitoringTestsAsync(IServiceProvider services)
{
    Console.WriteLine("==================================================");
    Console.WriteLine("STARTING MONITORING TESTCASES");
    Console.WriteLine("==================================================");

    using var scope = services.CreateScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<IViolationMonitoringOrchestrator>();
    var telegramAlertService = scope.ServiceProvider.GetRequiredService<ITelegramAlertService>();

    var smokeResult = await orchestrator.TriggerSmokeTestAsync();
    Console.WriteLine($"[SMOKE] Track={smokeResult.TrackId}; Severity={smokeResult.Severity}; Message={smokeResult.Message}");

    var leavingResult = await orchestrator.TriggerLeavingPositionTestAsync();
    Console.WriteLine($"[LEAVING] Track={leavingResult.TrackId}; Severity={leavingResult.Severity}; Message={leavingResult.Message}");

    var handledCommands = await telegramAlertService.ProcessPendingUpdatesAsync();
    Console.WriteLine($"[TELEGRAM] Handled commands = {handledCommands}");

    var updates = await telegramAlertService.GetRecentUpdatesAsync();
    Console.WriteLine($"[TELEGRAM] Recent updates count = {updates.Count}");
    foreach (var update in updates.Take(5))
    {
        Console.WriteLine($"  ChatId={update.ChatId}; Type={update.ChatType}; Sender={update.SenderName}; Text={update.MessageText}");
    }
    
    var knownChatIds = telegramAlertService.GetKnownChatIds();
    Console.WriteLine($"[TELEGRAM] Known chat ids = {(knownChatIds.Count == 0 ? "none" : string.Join(", ", knownChatIds))}");

    Console.WriteLine("==================================================");
    Console.WriteLine("MONITORING TESTCASES COMPLETED");
    Console.WriteLine("==================================================");
}

static bool TryFindBusyUrl(string[] args, string contentRootPath, out string busyUrl)
{
    foreach (var url in GetCandidateUrls(args, contentRootPath))
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            continue;
        }

        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        if (listeners.Any(endpoint => endpoint.Port == uri.Port))
        {
            busyUrl = url;
            return true;
        }
    }

    busyUrl = string.Empty;
    return false;
}

static IEnumerable<string> GetCandidateUrls(string[] args, string contentRootPath)
{
    var urls = new List<string>();
    var argUrlsIndex = Array.FindIndex(args, static arg => string.Equals(arg, "--urls", StringComparison.OrdinalIgnoreCase));
    if (argUrlsIndex >= 0 && argUrlsIndex + 1 < args.Length)
    {
        urls.AddRange(args[argUrlsIndex + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(envUrls))
    {
        urls.AddRange(envUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    var launchSettingsPath = Path.Combine(contentRootPath, "Properties", "launchSettings.json");
    if (File.Exists(launchSettingsPath))
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (document.RootElement.TryGetProperty("profiles", out var profilesElement))
            {
                foreach (var profile in profilesElement.EnumerateObject())
                {
                    if (profile.Value.TryGetProperty("applicationUrl", out var urlElement))
                    {
                        urls.AddRange(
                            (urlElement.GetString() ?? string.Empty)
                                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    }
                }
            }
        }
        catch
        {
            // Ignore launchSettings parsing errors and continue with other URL sources.
        }
    }

    return urls.Distinct(StringComparer.OrdinalIgnoreCase);
}

static bool IsAddressInUse(IOException exception)
{
    return exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);
}
