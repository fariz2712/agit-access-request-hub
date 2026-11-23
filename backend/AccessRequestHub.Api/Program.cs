using System.Text.Json.Serialization;
using AccessRequestHub.API;
using AccessRequestHub.Application;
using AccessRequestHub.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
builder.Services.AddDbContext<HubDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Hub")
        ?? "Data Source=access-request-hub.db;Foreign Keys=True;Default Timeout=5"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DemoIdentity>();
builder.Services.AddScoped<RequestService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (RequestError error)
    {
        app.Logger.LogInformation("Request {TraceId} returned {Status}", context.TraceIdentifier, error.Status);
        await Results.Problem(statusCode: error.Status, detail: error.Message).ExecuteAsync(context);
    }
    catch (Exception error) when (IsBusy(error) || error.InnerException is SqliteException sqlite && IsBusy(sqlite))
    {
        app.Logger.LogWarning("Database busy for request {TraceId}", context.TraceIdentifier);
        context.Response.Headers.RetryAfter = "1";
        await Results.Problem(statusCode: 503, detail: "Storage is busy. Retry the operation.").ExecuteAsync(context);
    }
});
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DemoIdentity:Enabled"))
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });
    app.MapDemo();
}
app.Run();

static bool IsBusy(Exception error) => error is SqliteException { SqliteErrorCode: 5 or 6 };

public partial class Program;
