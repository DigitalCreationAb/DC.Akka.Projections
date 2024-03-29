using Akka.Actor;
using Akka.Streams;
using DC.Akka.Projections.Storage;

namespace DC.Akka.Projections.Configuration;

public interface IProjectionConfigurationSetup<TId, TDocument> where TId : notnull where TDocument : notnull
{
    IProjection<TId, TDocument> Projection { get; }
    public ProjectionsApplication Application { get; }
    
    IProjectionConfigurationSetup<TId, TDocument> AutoStart();

    IProjectionConfigurationSetup<TId, TDocument> WithCoordinatorFactory(
        Func<Task<IActorRef>> factory);
    
    IProjectionConfigurationSetup<TId, TDocument> WithProjectionFactory(
        Func<TId, Task<IActorRef>> factory);

    IProjectionConfigurationSetup<TId, TDocument> WithRestartSettings(
        RestartSettings restartSettings);
    
    IProjectionConfigurationSetup<TId, TDocument> WithProjectionStreamConfiguration(
        ProjectionStreamConfiguration projectionStreamConfiguration);
    
    IProjectionStorageConfigurationSetup<TId, TDocument> WithProjectionStorage(
        IProjectionStorage<TId, TDocument> storage);

    IProjectionConfigurationSetup<TId, TDocument> WithPositionStorage(IProjectionPositionStorage positionStorage);

    internal ProjectionConfiguration<TId, TDocument> Build();
}