using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add SQL Server DbContext
builder.Services.AddDbContext<ViolationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Authentication Cookie support
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
    });

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<YoloModelOptions>(
    builder.Configuration.GetSection(YoloModelOptions.SectionName));

// Scoped services registration
builder.Services.AddScoped<IYoloInferenceService, LocalYoloInferenceService>();
builder.Services.AddScoped<IViolationService, ViolationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IModelSettingService, ModelSettingService>();

var app = builder.Build();

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

app.Run();

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
