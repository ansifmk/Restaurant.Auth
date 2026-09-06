using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Restaurant.Auth.Api.Middleware;
using Restaurant.Auth.Application;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Infrastructure;
using Restaurant.Auth.Infrastructure.Data;
using Restaurant.Auth.Infrastructure.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Validate JWT Secret is configured (User Secrets or environment variable)
if (string.IsNullOrEmpty(builder.Configuration["Jwt:Secret"]))
{
    throw new InvalidOperationException(
        "JWT Secret is not configured. Set it via: dotnet user-secrets set \"Jwt:Secret\" \"your-secret-key-at-least-32-chars!!\"");
}

if ((builder.Configuration["Jwt:Secret"]?.Length ?? 0) < 32)
{
    throw new InvalidOperationException(
        "JWT Secret must be at least 32 characters long for HMAC-SHA256 security. Current length: " +
        (builder.Configuration["Jwt:Secret"]?.Length ?? 0));
}

// Controllers with JSON serialization options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new Restaurant.Auth.Api.Models.Responses.DateTimeUtcJsonConverter());
    });

// Application Layer
builder.Services.AddApplication();

// Infrastructure Layer
builder.Services.AddInfrastructure(builder.Configuration);

// Data Protection (encrypt tokens, persist keys to file)
builder.Services.AddDataProtection()
    .SetApplicationName("RestaurantAuth")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "keys")))
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Token Protector
builder.Services.AddScoped<ITokenProtector, DataProtectionTokenProtector>();

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = false;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = async context =>
        {
            // 1. Try Authorization header (for Swagger/testing)
            var token = context.Token;

            // 2. If no header, try access token cookie
            if (string.IsNullOrEmpty(token))
            {
                var accessTokenName = builder.Configuration["Cookie:AccessTokenName"] ?? "accessToken";
                if (context.Request.Cookies.TryGetValue(accessTokenName, out var cookieToken))
                {
                    token = cookieToken;
                }
            }

            // 3. Decrypt if present
            if (!string.IsNullOrEmpty(token))
            {
                var protector = context.HttpContext.RequestServices
                    .GetRequiredService<ITokenProtector>();
                context.Token = protector.Unprotect(token);
            }

            await Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// HttpContext Accessor (for client IP capture)
builder.Services.AddHttpContextAccessor();

// Rate Limiting (separate policies per endpoint)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth.register", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("auth.login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("auth.refresh", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("auth.logout", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// Cookie Policy
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.Secure = CookieSecurePolicy.Always;
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Restaurant Auth API",
        Version = "v1",
        Description = "A secure authentication and authorization API for a restaurant system."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Security Headers (first in pipeline)
app.UseMiddleware<SecurityHeadersMiddleware>();

// Global Exception Handling
app.UseMiddleware<ExceptionHandlingMiddleware>();

// HSTS (only in production)
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Swagger (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseCookiePolicy();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Auto-migrate database (development only)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
    }
}

app.Run();
