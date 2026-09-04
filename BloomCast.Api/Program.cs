using BloomCast.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// ============================================================
// Controllers
// ============================================================

builder.Services.AddControllers();


// ============================================================
// PostgreSQL + EF Core
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection"
    );

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No se encontró ConnectionStrings:DefaultConnection."
    );
}

builder.Services.AddDbContext<BloomCastDbContext>(
    options =>
        options.UseNpgsql(connectionString)
);


// ============================================================
// Swagger
// ============================================================

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();


var app = builder.Build();


// ============================================================
// Development
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI();
}


// ============================================================
// Pipeline
// ============================================================

app.UseHttpsRedirection();

app.MapControllers();

app.Run();