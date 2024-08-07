using System.Collections.Immutable;

namespace DC.Akka.Projections.Storage;

public interface IProjectionStorage
{
    Task<TDocument?> LoadDocument<TDocument>(
        object id,
        CancellationToken cancellationToken = default);

    Task Store(
        IImmutableList<DocumentToStore> toUpsert,
        IImmutableList<DocumentToDelete> toDelete,
        CancellationToken cancellationToken = default);
}