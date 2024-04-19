using System.Diagnostics;
using System.Threading.Channels;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

/// <summary>
/// This code is taken from the official Microsoft documentation.
/// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0&tabs=visual-studio#queued-background-tasks
/// </summary>

public class BackgroundTaskQueue
{
    private readonly Channel<EmailInfo> _queue;

    public BackgroundTaskQueue(int capacity)
    {
        // Capacity should be set based on the expected application load and
        // number of concurrent threads accessing the queue.            
        // BoundedChannelFullMode.Wait will cause calls to WriteAsync() to return a task,
        // which completes only when space became available. This leads to backpressure,
        // in case too many publishers/calls start accumulating.
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<EmailInfo>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(
        EmailInfo workItem)
    {
        if (workItem == null)
        {
            throw new ArgumentNullException(nameof(workItem));
        }
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(Activity.Current?.Context ?? new ActivityContext(), Baggage.Current),
            workItem.Metadata,
            EmailInfo.SetMetadata);
        await _queue.Writer.WriteAsync(workItem);


        Activity.Current?.AddEvent(new ActivityEvent("Email added to the queue"));
    }

    public async ValueTask<EmailInfo> DequeueAsync(
        CancellationToken cancellationToken)
    {
        var workItem = await _queue.Reader.ReadAsync(cancellationToken);

        return workItem;
    }
}

public class QueuedHostedService : BackgroundService
{
    private readonly EmailSendingService _emailSendingService;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(BackgroundTaskQueue taskQueue, 
        EmailSendingService emailSendingService,
        ILogger<QueuedHostedService> logger)
    {
        TaskQueue = taskQueue;
        _emailSendingService = emailSendingService;
        _logger = logger;
    }

    public BackgroundTaskQueue TaskQueue { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BackgroundProcessing(stoppingToken);
    }

    private async Task BackgroundProcessing(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var emailInfo = 
                await TaskQueue.DequeueAsync(stoppingToken);

            try
            {
                var context = Propagators.DefaultTextMapPropagator.Extract(
                    new PropagationContext(Activity.Current?.Context ?? new ActivityContext(), Baggage.Current),
                    emailInfo.Metadata,
                    EmailInfo.GetMetadata);

                using var activity = emailInfo.UseLink ? 
                    DiagnosticConfig.Source.StartActivity("SendEmail", ActivityKind.Server, null, links: [new ActivityLink(context.ActivityContext)]) :
                    DiagnosticConfig.Source.StartActivity("SendEmail", ActivityKind.Internal, context.ActivityContext);
                await _emailSendingService.SendEmail(emailInfo);
            }
            catch (Exception ex)
            {
                Activity.Current?.RecordException(ex);
                _logger.LogError(ex, 
                    "Error occurred executing {WorkItem}.", nameof(emailInfo));
            }
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queued Hosted Service is stopping.");

        await base.StopAsync(stoppingToken);
    }
}