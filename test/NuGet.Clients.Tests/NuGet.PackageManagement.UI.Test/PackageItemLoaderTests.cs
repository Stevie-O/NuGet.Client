// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.PackageManagement.VisualStudio;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Test.Utility;
using Xunit;

namespace NuGet.PackageManagement.UI.Test
{
    public class PackageItemLoaderTests
    {
        private const string TestSearchTerm = "nuget";

        [Fact]
        public async Task MultipleSources_Works()
        {
            var solutionManager = Mock.Of<IVsSolutionManager>();
            var uiContext = Mock.Of<INuGetUIContext>();
            Mock.Get(uiContext)
                .Setup(x => x.SolutionManager)
                .Returns(solutionManager);

            var source1 = new PackageSource("https://dotnet.myget.org/F/nuget-volatile/api/v3/index.json", "NuGetVolatile");
            var source2 = new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org");

            var sourceRepositoryProvider = TestSourceRepositoryUtility.CreateSourceRepositoryProvider(new[] { source1, source2 });
            var repositories = sourceRepositoryProvider.GetRepositories();

            var context = new PackageLoadContext(repositories, false, uiContext);

            var packageFeed = new MultiSourcePackageFeed(repositories, logger: null, telemetryService: null);
            var loader = new PackageItemLoader(context, packageFeed, "nuget");

            var loaded = new List<PackageItemListViewModel>();
            foreach (var page in Enumerable.Range(0, 5))
            {
                await loader.LoadNextAsync(null, CancellationToken.None);
                while (loader.State.LoadingStatus == LoadingStatus.Loading)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    await loader.UpdateStateAsync(null, CancellationToken.None);
                }

                var items = loader.GetCurrent();
                loaded.AddRange(items);

                if (loader.State.LoadingStatus != LoadingStatus.Ready)
                {
                    break;
                }
            }

            // All items should not have a prefix reserved because the feed is multisource
            foreach (var item in loaded)
            {
                Assert.False(item.PrefixReserved);
            }

            Assert.NotEmpty(loaded);
        }

        [Fact]
        public async Task EmitsSearchTelemetryEvents()
        {
            // Arrange
            var solutionManager = Mock.Of<IVsSolutionManager>();
            var uiContext = Mock.Of<INuGetUIContext>();
            Mock.Get(uiContext)
                .Setup(x => x.SolutionManager)
                .Returns(solutionManager);

            var telemetryService = new Mock<INuGetTelemetryService>();
            var eventsQueue = new ConcurrentQueue<TelemetryEvent>();
            telemetryService
                .Setup(x => x.EmitTelemetryEvent(It.IsAny<TelemetryEvent>()))
                .Callback<TelemetryEvent>(e => eventsQueue.Enqueue(e));

            // Mock all the remote calls our test will try to make.
            var source = NuGetConstants.V3FeedUrl;
            var query = "https://api-v2v3search-0.nuget.org/query";

            var responses = new Dictionary<string, string>
            {
                {
                    source,
                    ProtocolUtility.GetResource("NuGet.PackageManagement.UI.Test.compiler.resources.index.json", typeof(PackageItemLoaderTests))
                },
                {
                    query + "?q=nuget&skip=0&take=26&prerelease=true&semVerLevel=2.0.0",
                    ProtocolUtility.GetResource("NuGet.PackageManagement.UI.Test.compiler.resources.nugetSearchPage1.json", typeof(PackageItemLoaderTests))
                },
                {
                    query + "?q=nuget&skip=25&take=26&prerelease=true&semVerLevel=2.0.0",
                    ProtocolUtility.GetResource("NuGet.PackageManagement.UI.Test.compiler.resources.nugetSearchPage2.json", typeof(PackageItemLoaderTests))
                },
            };

            using (var httpSource = new TestHttpSource(new PackageSource(source), responses))
            {
                var injectedHttpSources = new Dictionary<string, HttpSource>();
                injectedHttpSources.Add(source, httpSource);
                var sourceRepositoryProvider = TestSourceRepositoryUtility.CreateSourceRepositoryProvider(new[] { new PackageSource(source) }, new Lazy<INuGetResourceProvider>[] { new Lazy<INuGetResourceProvider>(() => new TestHttpSourceResourceProvider(injectedHttpSources)) });
                var repositories = sourceRepositoryProvider.GetRepositories();

                var context = new PackageLoadContext(repositories, false, uiContext);

                var packageFeed = new MultiSourcePackageFeed(repositories, logger: null, telemetryService: telemetryService.Object);

                // Act
                var loader = new PackageItemLoader(context, packageFeed, searchText: "nuget", includePrerelease: true);
                await loader.LoadNextAsync(null, CancellationToken.None);
                await loader.LoadNextAsync(null, CancellationToken.None);

                // Assert
                var events = eventsQueue.ToArray();
                Assert.True(4 == events.Length, string.Join(Environment.NewLine, events.Select(e => e.Name)));

                var search = events[0];
                Assert.Equal("Search", search.Name);
                Assert.Equal(true, search["IncludePrerelease"]);
                Assert.Equal("nuget", search.GetPiiData().First(p => p.Key == "Query").Value);
                var operationId = Assert.IsType<string>(search["OperationId"]);
                var parsedOperationId = Guid.ParseExact(operationId, "D");

                var sources = events[1];
                Assert.Equal("SearchPackageSourceSummary", sources.Name);
                Assert.Equal(1, sources["NumHTTPv3Feeds"]);
                Assert.Equal("YesV3", sources["NuGetOrg"]);
                Assert.Equal(operationId, sources["ParentId"]);

                var page0 = events[2];
                Assert.Equal("SearchPage", page0.Name);
                Assert.Equal("Ready", page0["LoadingStatus"]);
                Assert.Equal(0, page0["PageIndex"]);
                Assert.Equal(operationId, page0["ParentId"]);
                Assert.IsType<int>(page0["ResultCount"]);
                Assert.IsType<double>(page0["Duration"]);
                Assert.IsType<double>(page0["ResultsAggregationDuration"]);
                Assert.IsType<string>(page0["IndividualSourceDurations"]);
                Assert.Equal(1, ((JArray)JsonConvert.DeserializeObject((string)page0["IndividualSourceDurations"])).Values<double>().Count());

                var page1 = events[3];
                Assert.Equal("SearchPage", page1.Name);
                Assert.Equal("Ready", page1["LoadingStatus"]);
                Assert.Equal(1, page1["PageIndex"]);
                Assert.Equal(operationId, page1["ParentId"]);
                Assert.IsType<int>(page1["ResultCount"]);
                Assert.IsType<double>(page1["Duration"]);
                Assert.IsType<double>(page1["ResultsAggregationDuration"]);
                Assert.IsType<string>(page1["IndividualSourceDurations"]);
                Assert.Equal(1, ((JArray)JsonConvert.DeserializeObject((string)page1["IndividualSourceDurations"])).Values<double>().Count());

                Assert.Equal(parsedOperationId, loader.State.OperationId);
            }
        }

        [Fact]
        public async Task GetTotalCountAsync_Works()
        {
            var solutionManager = Mock.Of<IVsSolutionManager>();
            var uiContext = Mock.Of<INuGetUIContext>();
            Mock.Get(uiContext)
                .Setup(x => x.SolutionManager)
                .Returns(solutionManager);

            var source1 = new PackageSource("https://dotnet.myget.org/F/nuget-volatile/api/v3/index.json", "NuGetVolatile");
            var source2 = new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org");

            var sourceRepositoryProvider = TestSourceRepositoryUtility.CreateSourceRepositoryProvider(new[] { source1, source2 });
            var repositories = sourceRepositoryProvider.GetRepositories();

            var context = new PackageLoadContext(repositories, false, uiContext);

            var packageFeed = new MultiSourcePackageFeed(repositories, logger: null, telemetryService: null);
            var loader = new PackageItemLoader(context, packageFeed, "nuget");

            var totalCount = await loader.GetTotalCountAsync(100, CancellationToken.None);

            Assert.NotInRange(totalCount, 0, 99);
        }

        [Fact]
        public async Task LoadNextAsync_Works()
        {
            var solutionManager = Mock.Of<IVsSolutionManager>();
            var uiContext = Mock.Of<INuGetUIContext>();
            Mock.Get(uiContext)
                .Setup(x => x.SolutionManager)
                .Returns(solutionManager);

            var context = new PackageLoadContext(null, false, uiContext);
            var packageFeed = new TestPackageFeed();
            var loader = new PackageItemLoader(context, packageFeed, TestSearchTerm, true);

            Assert.Equal(LoadingStatus.Unknown, loader.State.LoadingStatus);
            var initial = loader.GetCurrent();
            Assert.Empty(initial);

            var loaded = new List<PackageItemListViewModel>();

            await loader.LoadNextAsync(null, CancellationToken.None);

            Assert.Equal(LoadingStatus.Loading, loader.State.LoadingStatus);
            var partial = loader.GetCurrent();
            Assert.Empty(partial);

            await Task.Delay(TimeSpan.FromSeconds(1));
            await loader.UpdateStateAsync(null, CancellationToken.None);

            Assert.NotEqual(LoadingStatus.Loading, loader.State.LoadingStatus);
            loaded.AddRange(loader.GetCurrent());

            Assert.Equal(LoadingStatus.Ready, loader.State.LoadingStatus);
            await loader.LoadNextAsync(null, CancellationToken.None);

            Assert.Equal(LoadingStatus.NoMoreItems, loader.State.LoadingStatus);
            loaded.AddRange(loader.GetCurrent());

            Assert.NotEmpty(loaded);
        }

        private class TestPackageFeed : IPackageFeed
        {
            public bool IsMultiSource => false;

            public Task<SearchResult<IPackageSearchMetadata>> ContinueSearchAsync(ContinuationToken continuationToken, CancellationToken cancellationToken)
            {
                Assert.NotNull(continuationToken);

                var packageB = new PackageIdentity("B", new NuGetVersion("2.0.0"));
                var metadata = PackageSearchMetadataBuilder.FromIdentity(packageB).Build();

                var results = SearchResult.FromItems(metadata);
                results.SourceSearchStatus = new Dictionary<string, LoadingStatus> { { "test", LoadingStatus.NoMoreItems } };
                return Task.FromResult(results);
            }

            public Task<SearchResult<IPackageSearchMetadata>> RefreshSearchAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
            {
                Assert.NotNull(refreshToken);

                var packageA = new PackageIdentity("A", new NuGetVersion("1.0.0"));
                var metadata = PackageSearchMetadataBuilder.FromIdentity(packageA).Build();

                var results = SearchResult.FromItems(metadata);
                results.NextToken = new ContinuationToken { };
                results.SourceSearchStatus = new Dictionary<string, LoadingStatus> { { "test", LoadingStatus.Ready } };
                return Task.FromResult(results);
            }

            public Task<SearchResult<IPackageSearchMetadata>> SearchAsync(string searchText, SearchFilter filter, CancellationToken cancellationToken)
            {
                var results = new SearchResult<IPackageSearchMetadata>
                {
                    RefreshToken = new RefreshToken { },
                    SourceSearchStatus = new Dictionary<string, LoadingStatus> { { "test", LoadingStatus.Loading } }
                };

                return Task.FromResult(results);
            }
        }

        private class TestHttpSourceResourceProvider : ResourceProvider
        {
            private Dictionary<string, HttpSource> InjectedHttpSources { get; }

            public TestHttpSourceResourceProvider(Dictionary<string, HttpSource> injectedHttpSources)
                : base(typeof(HttpSourceResource),
                      nameof(HttpSourceResource),
                      NuGetResourceProviderPositions.First)
            {
                InjectedHttpSources = injectedHttpSources;
            }

            public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
            {
                HttpSourceResource curResource = null;

                if (InjectedHttpSources.TryGetValue(source.PackageSource.Source, out HttpSource httpSource))
                {
                    curResource = new HttpSourceResource(httpSource);
                }

                return Task.FromResult(new Tuple<bool, INuGetResource>(curResource != null, curResource));
            }
        }
    }
}
