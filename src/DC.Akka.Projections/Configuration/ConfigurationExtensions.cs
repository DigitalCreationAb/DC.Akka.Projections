using System.Collections.Immutable;
using Akka.Streams;
using DC.Akka.Projections.Storage;

namespace DC.Akka.Projections.Configuration;

public static class ConfigurationExtensions
{
    public static IConfigurePart<T, TStorage> WithProjectionStorage<T, TStorage>(
        this IHaveConfiguration<T> source,
        TStorage storage) where TStorage : IProjectionStorage
        where T : ProjectionConfig
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            ProjectionStorage = storage
        });

        return new ConfigurePart<T, TStorage>(config, storage);
    }

    public static IConfigurePart<T, TStorage> WithPositionStorage<T, TStorage>(
        this IHaveConfiguration<T> source,
        TStorage storage)
        where TStorage : IProjectionPositionStorage
        where T : ProjectionConfig
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            PositionStorage = storage
        });

        return new ConfigurePart<T, TStorage>(config, storage);
    }

    public static IConfigurePart<T, RestartSettings?> WithRestartSettings<T>(
        this IHaveConfiguration<T> source,
        RestartSettings? restartSettings)
        where T : ProjectionConfig
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            RestartSettings = restartSettings
        });

        return new ConfigurePart<T, RestartSettings?>(config, restartSettings);
    }
    
    public static IConfigurePart<T, ProjectionStreamConfiguration> WithProjectionStreamConfiguration<T>(
        this IHaveConfiguration<T> source,
        ProjectionStreamConfiguration streamConfiguration)
        where T : ProjectionConfig
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            StreamConfiguration = streamConfiguration
        });

        return new ConfigurePart<T, ProjectionStreamConfiguration>(config, streamConfiguration);
    }
    
    public static IConfigurePart<ProjectionSystemConfiguration, TFactory> WithProjectionFactory<TFactory>(
        this IHaveConfiguration<ProjectionSystemConfiguration> source,
        TFactory factory) 
        where TFactory : IKeepTrackOfProjectors
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            ProjectorFactory = factory
        });

        return new ConfigurePart<ProjectionSystemConfiguration, TFactory>(config, factory);
    }
    
    public static IConfigurePart<ProjectionSystemConfiguration, TCoordinator> WithCoordinator<TCoordinator>(
        this IHaveConfiguration<ProjectionSystemConfiguration> source,
        TCoordinator coordinator) 
        where TCoordinator : IConfigureProjectionCoordinator
    {
        var config = source.WithModifiedConfig(conf => conf with
        {
            Coordinator = coordinator
        });

        return new ConfigurePart<ProjectionSystemConfiguration, TCoordinator>(config, coordinator);
    }

    internal static async Task<IProjectionsCoordinator> Build(
        this IHaveConfiguration<ProjectionSystemConfiguration> conf)
    {
        var result = new Dictionary<string, ProjectionConfiguration>();

        foreach (var projection in conf.Config.Projections)
        {
            var configuration = projection.Value(conf.Config);

            await conf.Config.Coordinator.WithProjection(configuration.GetProjection());

            result[projection.Key] = configuration;
        }

        ProjectionConfigurationsSupplier.Register(conf.ActorSystem, result.ToImmutableDictionary());

        return await conf.Config.Coordinator.Start();
    }
}