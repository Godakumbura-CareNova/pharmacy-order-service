using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("JwtSettings"));
builder.Services.AddOptions<KafkaSettings>()
    .Bind(builder.Configuration.GetSection("Kafka"));
builder.Services.AddOptions<FileStorageSettings>()
    .Bind(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddOptions<PrescriptionStorageSettings>()
    .Bind(builder.Configuration.GetSection("PrescriptionStorage"));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JwtSettings configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
    throw new InvalidOperationException("JwtSettings:Secret must be configured through user secrets or environment variables.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddPharmacyStaffAuthorization();
builder.Services.AddScoped<IKafkaEventProducer, KafkaEventProducer>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IMedicineService, MedicineService>();
builder.Services.AddScoped<ICatalogueService, CatalogueService>();
builder.Services.AddScoped<IInventoryService>(serviceProvider => new InventoryService(
    serviceProvider.GetRequiredService<AppDbContext>(),
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaSettings>>().Value));
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IPrescriptionCleanupService, PrescriptionCleanupService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<OrderService>(provider => new OrderService(
    provider.GetRequiredService<AppDbContext>(),
    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaSettings>>().Value));
builder.Services.AddScoped<IPrescriptionService, PrescriptionService>();
builder.Services.AddScoped<StaffPrescriptionService>();
builder.Services.AddScoped<PharmacyDashboardService>();
builder.Services.AddScoped<StaffOrderService>(provider => new StaffOrderService(
    provider.GetRequiredService<AppDbContext>(),
    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaSettings>>().Value));
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<KafkaConsumerService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 0))));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

app.MapControllers();
app.MapMetrics();

app.Run();
