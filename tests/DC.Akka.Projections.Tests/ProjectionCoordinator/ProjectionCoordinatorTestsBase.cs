using System.Collections.Immutable;
using Akka.TestKit.Xunit2;
using DC.Akka.Projections.Configuration;
using DC.Akka.Projections.Storage;
using DC.Akka.Projections.Tests.TestData;
using JetBrains.Annotations;
using Xunit;

namespace DC.Akka.Projections.Tests.ProjectionCoordinator;

public abstract class ProjectionCoordinatorTestsBase : TestKit, IAsyncLifetime
{
    private InMemoryPositionStorage _positionStorage = null!;
    private readonly Dictionary<string, Exception> _projectionExceptions = new();
    protected TestInMemoryProjectionStorage Storage = null!;

    public async Task InitializeAsync()
    {
        _positionStorage = new InMemoryPositionStorage();
        Storage = new TestInMemoryProjectionStorage();

        var initialData = Given(GivenConfiguration.Empty);

        foreach (var position in initialData.InitialPositions)
            await _positionStorage.StoreLatestPosition(position.projectionName, position.position);

        await Storage
            .Store(
                initialData
                    .InitialDocuments
                    .Select(x => new DocumentToStore(x.id, x.document))
                    .ToImmutableList(),
                ImmutableList<DocumentToDelete>.Empty);

        var projectionsCoordinator = await Sys
            .StartProjections(
                conf => Configure(conf)
                    .WithProjectionStorage(Storage)
                    .WithPositionStorage(_positionStorage));

        var projections = await projectionsCoordinator.GetAll();

        foreach (var projection in projections)
        {
            try
            {
                await projection.WaitForCompletion(TimeSpan.FromSeconds(5));
            }
            catch (Exception e)
            {
                _projectionExceptions[projection.Projection.Name] = e;
            }
        }
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
    
    public Task<TDocument?> LoadDocument<TDocument>(object id)
    {
        return Storage.LoadDocument<TDocument>(id);
    }

    public Task<IImmutableList<object>> LoadAllDocuments()
    {
        return Storage.LoadAll();
    }

    public async Task<long?> LoadPosition(string projectionName)
    {
        var position = await _positionStorage.LoadLatestPosition(projectionName);

        return position;
    }

    public Exception? GetExceptionFor(string projection)
    {
        return _projectionExceptions.GetValueOrDefault(projection);
    }

    protected abstract IHaveConfiguration<ProjectionSystemConfiguration> Configure(
        IHaveConfiguration<ProjectionSystemConfiguration> setup);

    [PublicAPI]
    protected virtual GivenConfiguration Given(GivenConfiguration config)
    {
        return config;
    }
    
    [PublicAPI]
    protected virtual IImmutableList<(object id, object document)> GivenDocuments()
    {
        return ImmutableList<(object id, object document)>.Empty;
    }

    [PublicAPI]
    protected virtual IImmutableList<(string projectionName, long? position)> GivenPositions()
    {
        return ImmutableList<(string projectionName, long? position)>.Empty;
    }

    [PublicAPI]
    public record GivenConfiguration(
        IImmutableList<(object id, object document)> InitialDocuments,
        IImmutableList<(string projectionName, long? position)> InitialPositions)
    {
        public static GivenConfiguration Empty => new(
            ImmutableList<(object id, object document)>.Empty,
            ImmutableList<(string projectionName, long? position)>.Empty);

        public GivenConfiguration WithInitialDocument(object id, object document)
        {
            return this with
            {
                InitialDocuments = InitialDocuments.Add((id, document))
            };
        }

        public GivenConfiguration WithInitialPosition(string projectionName, long? position)
        {
            return this with
            {
                InitialPositions = InitialPositions.Add((projectionName, position))
            };
        }
    }
}