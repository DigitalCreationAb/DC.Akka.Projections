using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using DC.Akka.Projections.Configuration;
using DC.Akka.Projections.Storage;

namespace DC.Akka.Projections;

public class DocumentProjection<TId, TDocument> : ReceiveActor, IWithTimers 
    where TId : notnull where TDocument : notnull
{
    public static class Commands
    {
        public record ProjectEvents(object Id, IImmutableList<EventWithPosition> Events);
    }

    private readonly ProjectionConfiguration<TId, TDocument> _configuration;
    private readonly TId _id;
    private readonly TimeSpan? _passivateAfter;
    private readonly ILoggingAdapter _logger;
    
    public ITimerScheduler? Timers { get; set; }

    public DocumentProjection(string projectionName, TId id, TimeSpan? passivateAfter)
    {
        _id = id;
        _passivateAfter = passivateAfter;
        _logger = Context.GetLogger();

        var configuration = Context.System.GetExtension<ProjectionConfiguration<TId, TDocument>>() ??
                            throw new NoDocumentProjectionException<TDocument>(projectionName);

        _configuration = configuration ?? throw new NoDocumentProjectionException<TId, TDocument>(id);

        Become(NotLoaded);

        HandlePassivation();
    }

    private void NotLoaded()
    {
        ReceiveAsync<Commands.ProjectEvents>(async cmd =>
        {
            var document = await _configuration.DocumentStorage.LoadDocument<TDocument>(_id);

            document = await ProjectEvents(document, cmd.Events);
            
            Become(() => Loaded(document));
            
            HandlePassivation();
        });
    }

    private void Loaded(TDocument? document)
    {
        ReceiveAsync<Commands.ProjectEvents>(async cmd =>
        {
            document = await ProjectEvents(document, cmd.Events);
            
            Become(() => Loaded(document));
            
            HandlePassivation();
        });
    }

    private async Task<TDocument?> ProjectEvents(TDocument? document, IImmutableList<EventWithPosition> events)
    {
        return await Retries
            .Run<TDocument?, Exception>(
                RunProjections,
                _configuration.ProjectionStreamConfiguration.MaxProjectionRetries,
                (retries, exception) => _logger
                    .Warning(
                        exception, 
                        "Failed handling {0} events for {1}, retrying (tries: {2})", 
                        events.Count,
                        _id,
                        retries));
        
        async Task<TDocument?> RunProjections()
        {
            var exists = document != null;
        
            try
            {
                if (!events.Any())
                {
                    Sender.Tell(new Messages.Acknowledge(null));
                
                    return document;
                }
            
                foreach (var evnt in events)
                    document = await _configuration.ProjectionsHandler.Handle(document, evnt.Event, evnt.Position ?? 0);

                if (document != null)
                {
                    await _configuration
                        .DocumentStorage
                        .Store(
                            ImmutableList.Create(
                                new DocumentToStore(_id, document)), 
                            ImmutableList<DocumentToDelete>.Empty);

                    if (document is IResetDocument<TDocument> reset)
                        document = reset.Reset();
                }
                else if (exists)
                {
                    await _configuration
                        .DocumentStorage
                        .Store(
                            ImmutableList<DocumentToStore>.Empty, 
                            ImmutableList.Create(new DocumentToDelete(_id, typeof(TDocument))));
                }
            
                Sender.Tell(new Messages.Acknowledge(events.Select(x => x.Position).Max()));

                return document;
            }
            catch (Exception e)
            {
                Sender.Tell(new Messages.Reject(e));
            
                throw;
            }
        }
    }

    private void HandlePassivation()
    {
        if (_passivateAfter == null || Timers == null)
            return;
        
        Timers.StartSingleTimer(
            "passivation",
            PoisonPill.Instance,
            _passivateAfter.Value);
    }
}