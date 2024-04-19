using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("BackgroundProcessingService"))
    .WithTracing(tpb => 
    {
        tpb.AddAspNetCoreInstrumentation()
           .AddSource(DiagnosticConfig.Source.Name)
           .AddOtlpExporter();
    });

builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddSingleton(new BackgroundTaskQueue(100));
builder.Services.AddSingleton<EmailSendingService>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/", async ([FromServices]BackgroundTaskQueue queue, [AsParameters]EmailRequest email) => {

    await queue.QueueBackgroundWorkItemAsync(new EmailInfo(email.To, email.Subject, email.Body, email.UseLink));

    return Results.Accepted();
})
.WithName("SendEmail")
.WithOpenApi();

app.Run();

public record EmailRequest(string To, string Subject, string Body, bool UseLink = false);

public static class DiagnosticConfig
{
    public static string ServiceName = "BackgroundProcessingService";
    public static ActivitySource Source = new(ServiceName);
}