﻿using DC.Akka.Projections.Configuration;
using DC.Akka.Projections.Tests.TestData;

namespace DC.Akka.Projections.Tests;

public class When_projecting_two_events_to_simple_document_with_batch_storage 
    : When_projecting_two_events_to_simple_document_with_normal_storage
{
    protected override IProjectionConfigurationSetup<MutableTestDocument> Configure(
        IProjectionConfigurationSetup<MutableTestDocument> config)
    {
        return base.Configure(config)
            .WithStorage(Storage)
            .Batched((10, TimeSpan.FromMilliseconds(100)));
    }
}