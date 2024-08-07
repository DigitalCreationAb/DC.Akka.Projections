using System.Collections.Immutable;
using AutoFixture;
using DC.Akka.Projections.Configuration;
using DC.Akka.Projections.Storage;
using DC.Akka.Projections.Tests.TestData;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace DC.Akka.Projections.Tests.ProjectionCoordinator;

public class When_projecting_two_events_to_simple_document
{
    public class With_normal_storage
    {
        [PublicAPI]
        public class With_string_id(NormalStorageFixture<string> fixture) 
            : BaseTests<string, NormalStorageFixture<string>>(fixture);
        
        [PublicAPI]
        public class With_int_id(NormalStorageFixture<int> fixture) 
            : BaseTests<int, NormalStorageFixture<int>>(fixture);
    }

    public class With_batched_storage
    {
        [PublicAPI]
        public class With_string_id(BatchedStorageFixture<string> fixture) 
            : BaseTests<string, BatchedStorageFixture<string>>(fixture);
        
        [PublicAPI]
        public class With_int_id(BatchedStorageFixture<int> fixture) 
            : BaseTests<int, BatchedStorageFixture<int>>(fixture);
    }

    public abstract class BaseTests<TId, TFixture>(TFixture fixture)
        : IClassFixture<TFixture>
        where TFixture : BaseFixture<TId> where TId : notnull
    {
        protected virtual int ExpectedPosition => 2;

        [Fact]
        public async Task Then_position_should_be_correct()
        {
            var position = await fixture.LoadPosition(TestProjection<TId>.GetName());

            position.Should().Be(ExpectedPosition);
        }

        [Fact]
        public async Task Then_document_should_be_saved()
        {
            var doc = await fixture.LoadDocument<TestDocument<TId>>(fixture.DocumentId);

            doc.Should().NotBeNull();
        }

        [Fact]
        public async Task Exactly_one_document_should_be_saved()
        {
            var docs = await fixture.LoadAllDocuments();

            docs.Should().HaveCount(1);
        }

        [Fact]
        public async Task Then_document_should_have_added_two_events()
        {
            var doc = await fixture.LoadDocument<TestDocument<TId>>(fixture.DocumentId);

            doc!.HandledEvents.Should().HaveCount(2);
        }

        [Fact]
        public async Task Then_document_should_have_added_first_event()
        {
            var doc = await fixture.LoadDocument<TestDocument<TId>>(fixture.DocumentId);

            doc!.HandledEvents[0].Should().Be(fixture.FirstEventId);
        }

        [Fact]
        public async Task Then_document_should_have_added_second_event()
        {
            var doc = await fixture.LoadDocument<TestDocument<TId>>(fixture.DocumentId);

            doc!.HandledEvents[1].Should().Be(fixture.SecondEventId);
        }
    }

    [PublicAPI]
    public class NormalStorageFixture<TId> : BaseFixture<TId>
        where TId : notnull
    {
        protected override IProjectionConfigurationSetup<TId, TestDocument<TId>> ConfigureProjection(
            IProjectionConfigurationSetup<TId, TestDocument<TId>> setup)
        {
            return setup;
        }
    }

    [PublicAPI]
    public class BatchedStorageFixture<TId> : BaseFixture<TId>
        where TId : notnull
    {
        protected override IProjectionConfigurationSetup<TId, TestDocument<TId>> ConfigureProjection(
            IProjectionConfigurationSetup<TId, TestDocument<TId>> setup)
        {
            return setup
                .WithProjectionStorage(Storage.Batched(Sys));
        }
    }

    public abstract class BaseFixture<TId> : ProjectionCoordinatorTestsBase
        where TId : notnull
    {
        public TId DocumentId { get; } = new Fixture().Create<TId>();
        public string FirstEventId { get; } = Guid.NewGuid().ToString();
        public string SecondEventId { get; } = Guid.NewGuid().ToString();

        protected override IProjectionsSetup Configure(IProjectionsSetup setup)
        {
            return setup
                .WithTestProjection<TId>(
                    ImmutableList.Create<object>(
                        new Events<TId>.FirstEvent(DocumentId, FirstEventId),
                        new Events<TId>.SecondEvent(DocumentId, SecondEventId)),
                    ConfigureProjection);
        }

        protected abstract IProjectionConfigurationSetup<TId, TestDocument<TId>> ConfigureProjection(
            IProjectionConfigurationSetup<TId, TestDocument<TId>> setup);
    }
}