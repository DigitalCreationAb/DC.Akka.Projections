using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace DC.Akka.Projections;

public abstract class BaseProjection<TId, TDocument> : IProjection<TId, TDocument>
    where TId : notnull where TDocument : notnull
{
    public abstract TId IdFromString(string id);
    public abstract string IdToString(TId id);

    public abstract ISetupProjection<TId, TDocument> Configure(ISetupProjection<TId, TDocument> config);
    public abstract Source<EventWithPosition, NotUsed> StartSource(long? fromPosition);

    public virtual string Name => GetType().Name;

    public Props CreateCoordinatorProps()
    {
        return ProjectionsCoordinator<TId, TDocument>.Init();
    }
}