using System.Collections.Concurrent;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Util;
using DC.Akka.Projections.Configuration;

namespace DC.Akka.Projections.InProc;

public class KeepTrackOfProjectorsInProc(ActorSystem actorSystem, IHandleProjectionPassivation passivationHandler)
    : IKeepTrackOfProjectors
{
    private readonly ConcurrentDictionary<string, IActorRef> _coordinators = new();

    public Task<IProjectorProxy> GetProjector<TId, TDocument>(TId id, ProjectionConfiguration configuration)
        where TId : notnull where TDocument : notnull
    {
        var coordinator = _coordinators
            .GetOrAdd(
                configuration.Name,
                _ => actorSystem.ActorOf(Props.Create(() =>
                    new InProcDocumentProjectionCoordinator<TId, TDocument>(passivationHandler, configuration)),
                    $"in-proc-projector-{configuration.Name}"));

        return Task.FromResult<IProjectorProxy>(new ActorRefProjectorProxy<TId, TDocument>(
            id,
            coordinator,
            configuration.ProjectionStreamConfiguration.ProjectDocumentTimeout));
    }

    private class InProcDocumentProjectionCoordinator<TId, TDocument> : ReceiveActor
        where TId : notnull where TDocument : notnull
    {
        private static class Commands
        {
            public record RemoveChild(string ChildId);

            public record FinishedProjectingToChild(string ProjectionId);
        }

        private readonly Dictionary<string, string> _inProcessHandler = new();
        private readonly List<string> _waitingToBeRemoved = [];

        public InProcDocumentProjectionCoordinator(
            IHandleProjectionPassivation passivationHandler,
            ProjectionConfiguration projectionConfiguration)
        {
            var handlePassivation = passivationHandler.StartNew();

            Receive<DocumentProjection<TId, TDocument>.Commands.ProjectEvents>(cmd =>
            {
                var id = MurmurHash.StringHash(projectionConfiguration
                        .IdToString(cmd.Id))
                    .ToString();

                var projectionId = Guid.NewGuid().ToString();

                _inProcessHandler[projectionId] = id;

                var projectionRef = Context.Child(id);

                if (projectionRef.IsNobody())
                {
                    projectionRef = Context.ActorOf(
                        projectionConfiguration.GetProjection().CreateProjectionProps(cmd.Id),
                        id);
                }

                var self = Self;
                var sender = Sender;
                
                projectionRef
                    .Ask<Messages.IProjectEventsResponse>(
                        cmd,
                        projectionConfiguration.ProjectionStreamConfiguration.ProjectDocumentTimeout)
                    .ContinueWith(response =>
                    {
                        self.Tell(new Commands.FinishedProjectingToChild(projectionId));

                        sender.Tell(response.Result);
                    });

                handlePassivation.SetAndMaybeRemove(
                    id,
                    toRemove => { self.Tell(new Commands.RemoveChild(toRemove)); });
            });

            Receive<Commands.RemoveChild>(cmd =>
            {
                if (_inProcessHandler.Any(x => x.Value == cmd.ChildId))
                {
                    if (!_waitingToBeRemoved.Contains(cmd.ChildId))
                        _waitingToBeRemoved.Add(cmd.ChildId);
                }
                else
                {
                    var childToRemove = Context.Child(cmd.ChildId);

                    if (!childToRemove.IsNobody())
                        Context.Stop(childToRemove);

                    if (_waitingToBeRemoved.Contains(cmd.ChildId))
                        _waitingToBeRemoved.Remove(cmd.ChildId);
                }
            });

            Receive<Commands.FinishedProjectingToChild>(cmd =>
            {
                _inProcessHandler.Remove(cmd.ProjectionId);

                var childrenToRemove = _waitingToBeRemoved
                    .Where(x => _inProcessHandler.All(y => y.Value != x))
                    .ToImmutableList();

                foreach (var childId in childrenToRemove)
                {
                    var childToRemove = Context.Child(childId);

                    if (!childToRemove.IsNobody())
                        Context.Stop(childToRemove);

                    _waitingToBeRemoved.Remove(childId);
                }
            });
        }
    }
}