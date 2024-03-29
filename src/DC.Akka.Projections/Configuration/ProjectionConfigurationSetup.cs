using System.Collections.Immutable;
using Akka.Actor;
using Akka.Streams;
using DC.Akka.Projections.Storage;

namespace DC.Akka.Projections.Configuration;

public class ProjectionConfigurationSetup<TId, TDocument>(
    IProjection<TId, TDocument> projection,
    ProjectionsApplication application)
    : IProjectionStorageConfigurationSetup<TId, TDocument>
    where TId : notnull where TDocument : notnull
{
    private bool _autoStart;

    private Func<TId, Task<IActorRef>>? _projectionRefFactory;

    private Func<Task<IActorRef>>? _projectionCoordinatorFactory;

    private IProjectionStorage<TId, TDocument> _storage = new InMemoryProjectionStorage<TId, TDocument>();

    private IProjectionPositionStorage _positionStorage = new InMemoryProjectionPositionStorage();

    private IStorageSession _storageSession = new InProcStorageSession(application);

    private ProjectionStreamConfiguration _projectionStreamConfiguration =
        ProjectionStreamConfiguration.Default;

    private RestartSettings _restartSettings = RestartSettings
        .Create(TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(1), 0.2)
        .WithMaxRestarts(5, TimeSpan.FromMinutes(15));

    public IProjection<TId, TDocument> Projection { get; } = projection;
    public ProjectionsApplication Application => application;

    public IProjectionConfigurationSetup<TId, TDocument> AutoStart()
    {
        _autoStart = true;

        return this;
    }

    public IProjectionConfigurationSetup<TId, TDocument> WithCoordinatorFactory(Func<Task<IActorRef>> factory)
    {
        _projectionCoordinatorFactory = factory;

        return this;
    }

    public IProjectionConfigurationSetup<TId, TDocument> WithProjectionFactory(Func<TId, Task<IActorRef>> factory)
    {
        _projectionRefFactory = factory;

        return this;
    }

    public IProjectionConfigurationSetup<TId, TDocument> WithRestartSettings(RestartSettings restartSettings)
    {
        _restartSettings = restartSettings;

        return this;
    }

    public IProjectionConfigurationSetup<TId, TDocument> WithProjectionStreamConfiguration(
        ProjectionStreamConfiguration projectionStreamConfiguration)
    {
        _projectionStreamConfiguration = projectionStreamConfiguration;

        return this;
    }

    public IProjectionStorageConfigurationSetup<TId, TDocument> WithProjectionStorage(
        IProjectionStorage<TId, TDocument> storage)
    {
        _storage = storage;

        return this;
    }

    public IProjectionConfigurationSetup<TId, TDocument> WithPositionStorage(IProjectionPositionStorage positionStorage)
    {
        _positionStorage = positionStorage;

        return this;
    }
    
    public IProjectionStorageConfigurationSetup<TId, TDocument> WithStorageSession(IStorageSession session)
    {
        _storageSession = session;

        return this;
    }
    
    public ProjectionConfiguration<TId, TDocument> Build()
    {
        var setup = new SetupProjection();

        var eventHandler = Projection.Configure(setup).Build();

        if (_projectionRefFactory == null)
        {
            var documentProjectionCoordinator =
                Application.ActorSystem.ActorOf(Props.Create(() => new InProcDocumentProjectionCoordinator(Projection.Name)));

            _projectionRefFactory = async id =>
            {
                var response = await documentProjectionCoordinator
                    .Ask<InProcDocumentProjectionCoordinator.Responses.GetProjectionRefResponse>(
                        new InProcDocumentProjectionCoordinator.Queries.GetProjectionRef(id));

                return response.ProjectionRef ?? throw new NoDocumentProjectionException<TId, TDocument>(id);
            };
        }

        return new ProjectionConfiguration<TId, TDocument>(
            Projection.Name,
            _autoStart,
            _storageSession,
            _storage,
            _positionStorage,
            eventHandler,
            Projection.StartSource,
            _projectionRefFactory,
            _projectionCoordinatorFactory ??= () =>
            {
                return Task.FromResult(Application.ActorSystem.ActorOf(
                    Props.Create(() => new ProjectionsCoordinator<TId, TDocument>(Projection.Name)), Projection.Name));
            },
            _restartSettings,
            _projectionStreamConfiguration);
    }

    private class SetupProjection : ISetupProjection<TId, TDocument>
    {
        private readonly IImmutableDictionary<Type, (Func<object, TId> GetId,
            Func<object, TDocument?, long, Task<TDocument?>> Handle)> _handlers;

        public SetupProjection()
            : this(
                ImmutableDictionary<Type, (Func<object, TId>, Func<object, TDocument?, long, Task<TDocument?>>)>
                    .Empty)
        {
        }

        private SetupProjection(
            IImmutableDictionary<Type, (Func<object, TId>, Func<object, TDocument?, long, Task<TDocument?>>)>
                handlers)
        {
            _handlers = handlers;
        }

        public ISetupProjection<TId, TDocument> RegisterHandler<TProjectedEvent>(
            Func<TProjectedEvent, TId> getId,
            Func<TProjectedEvent, TDocument?, long, Task<TDocument?>> handler)
        {
            return new SetupProjection(
                _handlers.SetItem(typeof(TProjectedEvent), (
                    evnt => getId((TProjectedEvent)evnt),
                    (evnt, doc, position) => handler((TProjectedEvent)evnt, doc, position))));
        }

        public ISetupProjection<TId, TDocument> RegisterHandler<TProjectedEvent>(
            Func<TProjectedEvent, TId> getId,
            Func<TProjectedEvent, TDocument?, Task<TDocument?>> handler)
        {
            return RegisterHandler(
                getId,
                (evnt, doc, _) => handler(evnt, doc));
        }

        public ISetupProjection<TId, TDocument> RegisterHandler<TProjectedEvent>(
            Func<TProjectedEvent, TId> getId,
            Func<TProjectedEvent, TDocument?, TDocument?> handler)
        {
            return RegisterHandler(
                getId,
                (evnt, doc, _) => handler(evnt, doc));
        }

        public ISetupProjection<TId, TDocument> RegisterHandler<TProjectedEvent>(
            Func<TProjectedEvent, TId> getId,
            Func<TProjectedEvent, TDocument?, long, TDocument?> handler)
        {
            return RegisterHandler(
                getId,
                (evnt, doc, offset) => Task.FromResult(handler(evnt, doc, offset)));
        }

        public IHandleEventInProjection<TId, TDocument> Build()
        {
            return new EventHandler(_handlers);
        }

        private class EventHandler(
            IImmutableDictionary<Type, (Func<object, TId>, Func<object, TDocument?, long, Task<TDocument?>>)>
                handlers)
            : IHandleEventInProjection<TId, TDocument>
        {
            private readonly IImmutableDictionary<Type, (Func<object, TId> GetId,
                Func<object, TDocument?, long, Task<TDocument?>> Handle)> _handlers = handlers;

            public TId? GetDocumentIdFrom(object evnt)
            {
                var typesToCheck = evnt.GetType().GetInheritedTypes();

                return (from type in typesToCheck
                        where _handlers.ContainsKey(type)
                        select _handlers[type].GetId(evnt))
                    .FirstOrDefault();
            }

            public async Task<TDocument?> Handle(TDocument? document, object evnt, long position)
            {
                var typesToCheck = evnt.GetType().GetInheritedTypes();

                foreach (var type in typesToCheck)
                {
                    if (!_handlers.ContainsKey(type))
                        continue;

                    document = await _handlers[type].Handle(evnt, document, position);
                }

                return document;
            }
        }
    }

    private class InProcDocumentProjectionCoordinator : ReceiveActor
    {
        public static class Queries
        {
            public record GetProjectionRef(TId Id);
        }

        public static class Responses
        {
            public record GetProjectionRefResponse(IActorRef? ProjectionRef);
        }

        public InProcDocumentProjectionCoordinator(string projectionName)
        {
            Receive<Queries.GetProjectionRef>(cmd =>
            {
                var id = cmd.Id.ToString();

                if (string.IsNullOrEmpty(id))
                {
                    Sender.Tell(new Responses.GetProjectionRefResponse(null));

                    return;
                }

                var projectionRef = Context.Child(id);

                if (projectionRef.IsNobody())
                    projectionRef =
                        Context.ActorOf(
                            Props.Create(() => new DocumentProjection<TId, TDocument>(projectionName, cmd.Id)), id);

                Sender.Tell(new Responses.GetProjectionRefResponse(projectionRef));
            });
        }
    }
}