using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace DC.Akka.Projections;

public interface IProjection
{
    string Name { get; }
    Props CreateCoordinatorProps();
}

public interface IProjection<TId, TDocument> : IProjection where TId : notnull where TDocument : notnull
{
    TId IdFromString(string id);
    string IdToString(TId id);
    ISetupProjection<TId, TDocument> Configure(ISetupProjection<TId, TDocument> config);
    Source<EventWithPosition, NotUsed> StartSource(long? fromPosition);
}