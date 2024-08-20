using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Akka.Util;
using DC.Akka.Projections.Configuration;
using DC.Akka.Projections.InProc;
using DC.Akka.Projections.Storage;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace DC.Akka.Projections.Tests.KeepTrackOfProjectorsInProcTests;

public class When_projecting_three_batches_to_two_ids_with_settings_to_keep_one_projector_in_memory(
    When_projecting_three_batches_to_two_ids_with_settings_to_keep_one_projector_in_memory.Fixture fixture)
    : IClassFixture<When_projecting_three_batches_to_two_ids_with_settings_to_keep_one_projector_in_memory.Fixture>
{
    [Fact]
    public void Then_first_response_should_be_success()
    {
        fixture.FirstResponse.Should().BeOfType<Messages.Acknowledge>();
    }

    [Fact]
    public void Then_second_response_should_be_success()
    {
        fixture.SecondResponse.Should().BeOfType<Messages.Acknowledge>();
    }
    
    [Fact]
    public void Then_third_response_should_be_success()
    {
        fixture.ThirdResponse.Should().BeOfType<Messages.Acknowledge>();
    }

    [Fact]
    public void Then_first_projector_should_still_be_running()
    {
        fixture.FirstProjector.Should().NotBeNull();
    }

    [Fact]
    public void Then_second_projector_should_be_stopped()
    {
        fixture.SecondProjector.Should().BeNull();
    }
    
    [PublicAPI]
    public class Fixture : TestKit, IAsyncLifetime
    {
        public Messages.IProjectEventsResponse FirstResponse { get; private set; } = null!;
        public Messages.IProjectEventsResponse SecondResponse { get; private set; } = null!;
        public Messages.IProjectEventsResponse ThirdResponse { get; private set; } = null!;
        public IActorRef? FirstProjector { get; private set; }
        public IActorRef? SecondProjector { get; private set; }

        public async Task InitializeAsync()
        {
            var factory = new KeepTrackOfProjectorsInProc(Sys, new MaxNumberOfProjectorsPassivation(1));

            var firstId = Guid.NewGuid().ToString();
            var secondId = Guid.NewGuid().ToString();

            var projectionConfiguration = new ProjectionConfiguration<string, object>(
                new FakeProjection(TimeSpan.FromMilliseconds(100)),
                new InMemoryProjectionStorage(),
                new InMemoryPositionStorage(),
                factory,
                null,
                ProjectionStreamConfiguration.Default with
                {
                    ProjectDocumentTimeout = TimeSpan.FromSeconds(5)
                },
                new FakeEventsHandler());

            var firstProjector = await factory.GetProjector<string, object>(firstId, projectionConfiguration);
            var secondProjector = await factory.GetProjector<string, object>(secondId, projectionConfiguration);
            var thirdProjector = await factory.GetProjector<string, object>(firstId, projectionConfiguration);

            FirstResponse = await firstProjector.ProjectEvents(ImmutableList.Create(new EventWithPosition(new { }, 1)));
            SecondResponse =
                await secondProjector.ProjectEvents(ImmutableList.Create(new EventWithPosition(new { }, 2)));
            ThirdResponse = await thirdProjector.ProjectEvents(ImmutableList.Create(new EventWithPosition(new { }, 3)));

            var firstProjectorId = MurmurHash.StringHash(firstId).ToString();
            var secondProjectorId = MurmurHash.StringHash(secondId).ToString();

            var coordinator = await Sys.ActorSelection($"/user/in-proc-projector-{projectionConfiguration.Name}")
                .ResolveOne(TimeSpan.FromSeconds(1));

            try
            {
                FirstProjector = await
                    Sys.ActorSelection(coordinator, $"/{firstProjectorId}")
                        .ResolveOne(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                FirstProjector = null;
            }

            try
            {
                SecondProjector = await
                    Sys.ActorSelection(coordinator, $"/{secondProjectorId}")
                        .ResolveOne(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                SecondProjector = null;
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}