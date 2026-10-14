using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Web.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("PropertyManagement")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:PropertyManagement before starting the website.");

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddDbContext<PropertyDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(150)));
builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<ReportingQueries>();
builder.Services.AddScoped<RentPaymentService>();
builder.Services.AddScoped<LeaseService>();
var culture = builder.Configuration["Display:Culture"] ?? "en-ZA";
builder.Services.Configure<RequestLocalizationOptions>(options =>
    options.SetDefaultCulture(culture).AddSupportedCultures(culture).AddSupportedUICultures(culture));

var app = builder.Build();
app.UseExceptionHandler("/Home/Error");
app.UseRequestLocalization();
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;