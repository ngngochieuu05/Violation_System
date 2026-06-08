using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<YoloModelOptions>(
    builder.Configuration.GetSection(YoloModelOptions.SectionName));
builder.Services.AddScoped<IYoloInferenceService, LocalYoloInferenceService>();
builder.Services.AddScoped<IViolationService, ViolationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
