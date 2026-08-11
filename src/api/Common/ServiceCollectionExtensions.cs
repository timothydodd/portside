using System.IO.Compression;
using System.Text;
using k8s;
using PortsideApi.Common;
using PortsideApi.Common.HealthChecks;
using PortsideApi.Data;
using PortsideApi.Hubs;
using PortsideApi.Services;
using PortsideApi.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration config, ILogger logger)
    {
        var origins = config["AllowedOrigins"]?.Split(',');
        services.AddCors(options =>
        {
            options.AddPolicy("Origins", policy =>
            {
                if (origins is not null)
                {
                    logger.LogInformation("CORS policy set to allow origins: {origins}", string.Join(", ", origins));
                    policy.WithOrigins(origins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .SetIsOriginAllowed(_ => true)
                          .AllowCredentials();
                }
                else
                {
                    policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
            });
        });
        return services;
    }

    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        // Watchers are now lazy: ClusterWatchManager starts/stops them based on hub subscribers.
        services.AddSingleton<MonitorSettingsService>();
        services.AddSingleton<PodMonitorService>();
        services.AddSingleton<ClusterWatchManager>();
        return services;
    }

    public static IServiceCollection AddCompressionAndCaching(this IServiceCollection services)
    {
        services.AddRequestDecompression();
        services.AddResponseCaching();
        services.AddResponseCompression(options =>
        {
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json" });
        });
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
    {
        var kconfig = KubernetesClientConfiguration.BuildDefaultConfig();
        services.AddSingleton<IKubernetes>(_ => new Kubernetes(kconfig));

        services.AddSingleton<KubernetesService>();
        services.AddSingleton<IKubernetesService>(p => p.GetRequiredService<KubernetesService>());

        services.AddSingleton<TimedCache>();
        services.AddSingleton<KubernetesDashboardHubService>();
        services.AddSingleton<PodLogStreamService>();

        services.AddLogging(logging =>
        {
            logging.AddSimpleConsole(c =>
            {
                c.SingleLine = true;
                c.IncludeScopes = false;
                c.TimestampFormat = "HH:mm:ss ";
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? "Data Source=portside.db";

        services.AddSingleton(new SqliteConnectionFactory(connectionString));
        services.AddSingleton<UserStore>();
        services.AddSingleton<UserPreferenceStore>();
        services.AddSingleton<SystemSettingStore>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SqliteHealthCheck>("sqlite", tags: new[] { "ready" })
            .AddCheck<KubernetesHealthCheck>("kubernetes", tags: new[] { "ready" });
        return services;
    }

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<PasswordService>();
        services.AddSingleton<AuthService>();
        services.AddScoped<RefreshTokenService>();

        var jwt = config.GetSection("JwtSettings");
        var secret = jwt["Secret"] ?? throw new InvalidOperationException("JwtSettings:Secret not configured");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt["Issuer"],
                ValidAudience = jwt["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(token) &&
                        (path.StartsWithSegments("/kubernetes-hub") || path.StartsWithSegments("/podloghub")))
                    {
                        ctx.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("AuthPolicy", lo =>
            {
                lo.PermitLimit = 5;
                lo.Window = TimeSpan.FromMinutes(1);
                lo.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                lo.QueueLimit = 2;
            });
            options.OnRejected = async (ctx, token) =>
            {
                ctx.HttpContext.Response.StatusCode = 429;
                await ctx.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token);
            };
        });

        return services;
    }
}
