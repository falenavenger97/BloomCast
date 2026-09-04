using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using BloomCast.Data;
using BloomCast.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<BloomCastDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.ExpireTimeSpan   = TimeSpan.FromMinutes(30);
        options.SlidingExpiration= true;
        options.Cookie.MaxAge = null;   
        options.Cookie.IsEssential = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

builder.Services.AddSession(options =>
{
    options.IdleTimeout      = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly  = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddScoped<IMfaService, MfaService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ── Add MFA columns if they don't exist yet ───────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BloomCastDbContext>();
    try
    {
        // Add MFA columns to the existing usuarios table (safe, idempotent)
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE usuarios
                ADD COLUMN IF NOT EXISTS mfa_secret      TEXT,
                ADD COLUMN IF NOT EXISTS mfa_habilitado  BOOLEAN NOT NULL DEFAULT FALSE;
        ");
    }
    catch { /* columns already exist */ }
}

app.Run();
