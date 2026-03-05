using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;
using SignalTracker.Models;
using SignalTracker.Services;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ----------------------------------------------------
        // CONTROLLERS & JSON
        // ----------------------------------------------------

        // Add this line to Program.cs
        builder.Services.AddScoped<UserScopeService>();


        builder.Services.AddHttpContextAccessor();
        builder.Services.AddControllersWithViews()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

        // ----------------------------------------------------
        // CORS (React)
        // ----------------------------------------------------
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowReactApp", policy =>
            {
                policy.WithOrigins(
                        "http://192.168.1.82:5173",
                        "http://192.168.1.147:5173",
                        "http://localhost:5173",
                        "https://singnaltracker.netlify.app",
                        "https://stracer.vinfocom.co.in"
                    )
                
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // ----------------------------------------------------
       // ----------------------------------------------------
        // DATABASE (DYNAMIC SELECTION) - UPDATED SECTION
        // ----------------------------------------------------
        // 1. Register the provider that chooses the connection string based on the user
        ;
       
// Ensure there are no ++ or -- symbols at the end of these lines.
builder.Services.AddScoped<IDbConnectionProvider, DbConnectionProvider>();


// Register DbContext without passing options here; 
// it will configure itself in its OnConfiguring method.
builder.Services.AddDbContext<ApplicationDbContext>();

Console.WriteLine("✅ Dynamic Database Provider configured");
        

        // ----------------------------------------------------
        // DATA PROTECTION
        // ----------------------------------------------------
        var dpKeysPath = builder.Configuration["DataProtection:KeysPath"]
                         ?? Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH");

        if (!string.IsNullOrWhiteSpace(dpKeysPath))
        {
            Directory.CreateDirectory(dpKeysPath);
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
                .SetApplicationName("SignalTracker");
        }

        // ----------------------------------------------------
        // REDIS CONFIGURATION (Safe + Fallback)
        // ----------------------------------------------------
        var redisConnString = builder.Configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redisConnString))
        {
            Console.WriteLine("⚠️ Redis not configured. Using in-memory cache.");
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSingleton(_ => new RedisService(null));
        }
        else
        {
            try
            {
                var redisOptions = ConfigurationOptions.Parse(redisConnString, true);
                redisOptions.AbortOnConnectFail = false;
                redisOptions.ConnectTimeout = 5000;
                redisOptions.SyncTimeout = 5000;
                redisOptions.ConnectRetry = 5;
                redisOptions.KeepAlive = 60;

                builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
                {
                    var mux = ConnectionMultiplexer.Connect(redisOptions);

                    mux.ConnectionFailed += (_, e) =>
                        Console.WriteLine($"❌ Redis connection failed: {e.Exception?.Message}");

                    mux.ConnectionRestored += (_, _) =>
                        Console.WriteLine("✅ Redis connection restored");

                    Console.WriteLine("✅ Redis connected");
                    return mux;
                });

                builder.Services.AddSingleton(sp =>
                {
                    var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                    return new RedisService(mux);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Redis failed: {ex.Message}");
                builder.Services.AddDistributedMemoryCache();
                builder.Services.AddSingleton(_ => new RedisService(null));
            }
        }

        // ----------------------------------------------------
        // SESSION
        // ----------------------------------------------------
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(300);
            options.Cookie.Name = "st.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        // ----------------------------------------------------
        // AUTHENTICATION
        // ----------------------------------------------------
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "st.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(300);
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization();

        // ----------------------------------------------------
        // LARGE FILE UPLOADS (500MB)
        // ----------------------------------------------------
        builder.Services.Configure<FormOptions>(o =>
        {
            o.ValueLengthLimit = int.MaxValue;
            o.MultipartBodyLengthLimit = 524288000;
            o.MultipartHeadersLengthLimit = int.MaxValue;
        });

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestBodySize = 524288000;
            o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(20);
        });

        // ----------------------------------------------------
        // COOKIE POLICY
        // ----------------------------------------------------
        builder.Services.Configure<CookiePolicyOptions>(o =>
        {
            o.MinimumSameSitePolicy = SameSiteMode.None;
            o.HttpOnly = HttpOnlyPolicy.Always;
            o.Secure = CookieSecurePolicy.Always;
        });

        // ----------------------------------------------------
        // BUILD APP
        // ----------------------------------------------------
        var app = builder.Build();

        // ----------------------------------------------------
        // MIDDLEWARE PIPELINE
        // ----------------------------------------------------

        // Disable caching
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await next();
        });

        // Exception handling
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // Forwarded headers for reverse proxy
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors("AllowReactApp");
        app.UseCookiePolicy();
        app.UseSession();

        // Keep session alive
        app.Use(async (ctx, next) =>
        {
            ctx.Session.Set("st.pulse", BitConverter.GetBytes(DateTime.UtcNow.Ticks));
            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();

        // Add Partitioned flag to cookies
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                if (ctx.Response.Headers.TryGetValue("Set-Cookie", out var cookies))
                {
                    var updated = cookies
                        .Select(c =>
                            c.Contains("SameSite=None", StringComparison.OrdinalIgnoreCase) &&
                            c.Contains("Secure", StringComparison.OrdinalIgnoreCase) &&
                            !c.Contains("Partitioned", StringComparison.OrdinalIgnoreCase)
                                ? c + "; Partitioned"
                                : c)
                        .ToArray();

                    ctx.Response.Headers["Set-Cookie"] = new StringValues(updated);
                }
                return Task.CompletedTask;
            });

            await next();
        });

        // ----------------------------------------------------
        // ROUTES
        // ----------------------------------------------------
        app.MapControllers();

        Console.WriteLine("🚀 Application started successfully");
        app.Run();
    }
}