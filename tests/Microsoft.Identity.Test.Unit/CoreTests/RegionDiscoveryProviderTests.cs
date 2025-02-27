﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Instance.Discovery;
using Microsoft.Identity.Client.Internal;
using Microsoft.Identity.Client.Region;
using Microsoft.Identity.Client.TelemetryCore.Internal;
using Microsoft.Identity.Client.TelemetryCore.Internal.Events;
using Microsoft.Identity.Test.Common.Core.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Identity.Test.Unit.CoreTests
{
    [TestClass]
    [DeploymentItem("Resources\\local-imds-error-response.json")]
    [DeploymentItem("Resources\\local-imds-error-response-versions-missing.json")]
    public class RegionDiscoveryProviderTests : TestBase
    {
        private MockHttpAndServiceBundle _harness;
        private MockHttpManager _httpManager;
        private RequestContext _testRequestContext;
        private ApiEvent _apiEvent;
        private CancellationTokenSource _userCancellationTokenSource;
        private IRegionDiscoveryProvider _regionDiscoveryProvider;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();

            _harness = base.CreateTestHarness();
            _httpManager = _harness.HttpManager;
            _userCancellationTokenSource = new CancellationTokenSource();
            _testRequestContext = new RequestContext(
                _harness.ServiceBundle,
                Guid.NewGuid(),
                _userCancellationTokenSource.Token);
            _apiEvent = new ApiEvent(
                _harness.ServiceBundle.ApplicationLogger,
                _harness.ServiceBundle.PlatformProxy.CryptographyManager,
                Guid.NewGuid().AsMatsCorrelationId());
            _testRequestContext.ApiEvent = _apiEvent;
            _regionDiscoveryProvider = new RegionDiscoveryProvider(_httpManager, true);
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            Environment.SetEnvironmentVariable(TestConstants.RegionName, "");
            _harness?.Dispose();
            _regionDiscoveryProvider = new RegionDiscoveryProvider(_httpManager, true);
            _httpManager.Dispose();
            base.TestCleanup();
        }

        [TestMethod]
        public async Task SuccessfulResponseFromEnvironmentVariableAsync()
        {
            Environment.SetEnvironmentVariable(TestConstants.RegionName, TestConstants.Region);

            _testRequestContext.ServiceBundle.Config.AzureRegion = null; // not configured

            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"), _testRequestContext)
                .ConfigureAwait(false);

            Assert.IsNull(regionalMetadata);
        }

        [TestMethod]
        public async Task SuccessfulResponseFromLocalImdsAsync()
        {
            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(TestConstants.Region));

            _testRequestContext.ServiceBundle.Config.AzureRegion =
                ConfidentialClientApplication.AttemptRegionDiscovery;

            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            ValidateInstanceMetadata(regionalMetadata);
        }

        [TestMethod]
        public async Task FetchRegionFromLocalImdsThenGetMetadataFromCacheAsync()
        {
            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(TestConstants.Region));

            _testRequestContext.ServiceBundle.Config.AzureRegion =
               ConfidentialClientApplication.AttemptRegionDiscovery;

            InstanceDiscoveryMetadataEntry regionalMetadata =
                await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            ValidateInstanceMetadata(regionalMetadata);

            //get metadata from the instance metadata cache
            regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            ValidateInstanceMetadata(regionalMetadata);
        }

        [TestMethod]
        public async Task SuccessfulResponseFromUserProvidedRegionAsync()
        {
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.NotFound));

            _testRequestContext.ServiceBundle.Config.AzureRegion = TestConstants.Region;

            IRegionDiscoveryProvider regionDiscoveryProvider = new RegionDiscoveryProvider(_httpManager, true);
            InstanceDiscoveryMetadataEntry regionalMetadata = await regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            Assert.IsNotNull(regionalMetadata);
            Assert.AreEqual($"centralus.{RegionDiscoveryProvider.PublicEnvForRegional}", regionalMetadata.PreferredNetwork);

            Assert.AreEqual(TestConstants.Region, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.FailedAutoDiscovery, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.UserProvidedAutodetectionFailed, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task ResponseFromUserProvidedRegionSameAsRegionDetectedAsync()
        {
            Environment.SetEnvironmentVariable(TestConstants.RegionName, TestConstants.Region);
            _testRequestContext.ServiceBundle.Config.AzureRegion = TestConstants.Region;

            //            IRegionDiscoveryProvider regionDiscoveryProvider = new RegionDiscoveryProvider(_httpManager);
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            Assert.IsNotNull(regionalMetadata);
            Assert.AreEqual($"centralus.{RegionDiscoveryProvider.PublicEnvForRegional}", regionalMetadata.PreferredNetwork);
            Assert.AreEqual(TestConstants.Region, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.EnvVariable, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.UserProvidedValid, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task ResponseFromUserProvidedRegionDifferentFromRegionDetectedAsync()
        {
            Environment.SetEnvironmentVariable(TestConstants.RegionName, "detected_region");
            _testRequestContext.ServiceBundle.Config.AzureRegion = "user_region";

            //IRegionDiscoveryProvider regionDiscoveryProvider = new RegionDiscoveryProvider(_httpManager, new NetworkCacheMetadataProvider());
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"),
                _testRequestContext).ConfigureAwait(false);

            Assert.IsNotNull(regionalMetadata);
            Assert.AreEqual($"user_region.{RegionDiscoveryProvider.PublicEnvForRegional}", regionalMetadata.PreferredNetwork);
            Assert.AreEqual("user_region", _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.EnvVariable, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.UserProvidedInvalid, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task SuccessfulResponseFromRegionalizedAuthorityAsync()
        {
            var regionalizedAuthority = new Uri($"https://{TestConstants.Region}.{RegionDiscoveryProvider.PublicEnvForRegional}/common/");
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            Environment.SetEnvironmentVariable(TestConstants.RegionName, TestConstants.Region);

            // In the instance discovery flow, GetMetadataAsync is always called with a known authority first, then with regionalized.
            await _regionDiscoveryProvider.GetMetadataAsync(new Uri(TestConstants.AuthorityCommonTenant), _testRequestContext).ConfigureAwait(false);
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(regionalizedAuthority, _testRequestContext).ConfigureAwait(false);

            ValidateInstanceMetadata(regionalMetadata);
            Assert.AreEqual(TestConstants.Region, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.EnvVariable, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.AutodetectSuccess, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [DataTestMethod]
        [DataRow("Region with spaces")]
        [DataRow("invalid`region")]
        public async Task InvalidRegionEnvVariableAsync(string region)
        {
            Environment.SetEnvironmentVariable(TestConstants.RegionName, region);

            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(TestConstants.Region)); // IMDS will return a valid region

            _testRequestContext.ServiceBundle.Config.AzureRegion =
                ConfidentialClientApplication.AttemptRegionDiscovery;

            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            ValidateInstanceMetadata(regionalMetadata);
        }

        [DataTestMethod]
        [DataRow("Region with spaces")]
        [DataRow("invalid`region")]
        public async Task InvalidImdsAsync(string region)
        {
            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(region)); // IMDS will return an invalid region

            _testRequestContext.ServiceBundle.Config.AzureRegion =
                ConfidentialClientApplication.AttemptRegionDiscovery;

            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(
                new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            Assert.IsNull(regionalMetadata, "Discovery requested, but it failed.");
        }

        [TestMethod]
        public async Task NonPublicCloudTestAsync()
        {
            // Arrange
            Environment.SetEnvironmentVariable(TestConstants.RegionName, TestConstants.Region);
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            // Act
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.someenv.com/common/"), _testRequestContext).ConfigureAwait(false);

            // Assert
            Assert.IsNotNull(regionalMetadata);
            Assert.AreEqual("centralus.login.someenv.com", regionalMetadata.PreferredNetwork);
        }

        [TestMethod]
        public async Task ResponseMissingRegionFromLocalImdsAsync()
        {
            // Arrange
            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(string.Empty));
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            // Act
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            // Assert
            Assert.IsNull(regionalMetadata, "Discovery requested, but it failed.");
            Assert.AreEqual(null, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.FailedAutoDiscovery, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.FallbackToGlobal, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task ErrorResponseFromLocalImdsAsync()
        {
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.NotFound));
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.
                 GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext)
                 .ConfigureAwait(false);

            Assert.IsNull(regionalMetadata, "Discovery requested, but it failed.");

            Assert.AreEqual(null, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.FailedAutoDiscovery, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.FallbackToGlobal, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task UpdateImdsApiVersionWhenCurrentVersionExpiresForImdsAsync()
        {
            // Arrange
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.BadRequest));
            AddMockedResponse(MockHelpers.CreateFailureMessage(System.Net.HttpStatusCode.BadRequest, File.ReadAllText(
                        ResourceHelper.GetTestResourceRelativePath("local-imds-error-response.json"))), expectedParams: false);
            AddMockedResponse(MockHelpers.CreateSuccessResponseMessage(TestConstants.Region), apiVersion: "2020-10-01");
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            // Act
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            // Assert
            ValidateInstanceMetadata(regionalMetadata);
            Assert.AreEqual(TestConstants.Region, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.Imds, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.AutodetectSuccess, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task UpdateApiversionFailsWithEmptyResponseBodyAsync()
        {
            // Arrange
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.BadRequest));
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.BadRequest), expectedParams: false);
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            // Act
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            // Assert
            Assert.IsNull(regionalMetadata, "Discovery requested, but it failed.");
            Assert.AreEqual(null, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.FailedAutoDiscovery, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.FallbackToGlobal, _testRequestContext.ApiEvent.RegionOutcome);
        }

        [TestMethod]
        public async Task UpdateApiversionFailsWithNoNewestVersionsAsync()
        {
            // Arrange
            AddMockedResponse(MockHelpers.CreateNullMessage(System.Net.HttpStatusCode.BadRequest));
            AddMockedResponse(MockHelpers.CreateFailureMessage(System.Net.HttpStatusCode.BadRequest, File.ReadAllText(
                        ResourceHelper.GetTestResourceRelativePath("local-imds-error-response-versions-missing.json"))), expectedParams: false);
            _testRequestContext.ServiceBundle.Config.AzureRegion = ConfidentialClientApplication.AttemptRegionDiscovery;

            // Act
            InstanceDiscoveryMetadataEntry regionalMetadata = await _regionDiscoveryProvider.GetMetadataAsync(new Uri("https://login.microsoftonline.com/common/"), _testRequestContext).ConfigureAwait(false);

            // Assert
            Assert.IsNull(regionalMetadata, "Discovery requested, but it failed.");

            Assert.AreEqual(null, _testRequestContext.ApiEvent.RegionUsed);
            Assert.AreEqual((int)RegionAutodetectionSource.FailedAutoDiscovery, _testRequestContext.ApiEvent.RegionAutodetectionSource);
            Assert.AreEqual((int)RegionOutcome.FallbackToGlobal, _testRequestContext.ApiEvent.RegionOutcome);

        }

        private void AddMockedResponse(HttpResponseMessage responseMessage, string apiVersion = "2020-06-01", bool expectedParams = true)
        {
            var queryParams = new Dictionary<string, string>();

            if (expectedParams)
            {
                queryParams.Add("api-version", apiVersion);
                queryParams.Add("format", "text");

                _httpManager.AddMockHandler(
                   new MockHttpMessageHandler
                   {
                       ExpectedMethod = HttpMethod.Get,
                       ExpectedUrl = TestConstants.ImdsUrl,
                       ExpectedRequestHeaders = new Dictionary<string, string>
                        {
                            { "Metadata", "true" }
                        },
                       ExpectedQueryParams = queryParams,
                       ResponseMessage = responseMessage
                   });
            }
            else
            {
                _httpManager.AddMockHandler(
                    new MockHttpMessageHandler
                    {
                        ExpectedMethod = HttpMethod.Get,
                        ExpectedUrl = TestConstants.ImdsUrl,
                        ExpectedRequestHeaders = new Dictionary<string, string>
                            {
                            { "Metadata", "true" }
                            },
                        ResponseMessage = responseMessage
                    });
            }
        }

        private void ValidateInstanceMetadata(InstanceDiscoveryMetadataEntry entry, string region = "centralus")
        {
            InstanceDiscoveryMetadataEntry expectedEntry = new InstanceDiscoveryMetadataEntry()
            {
                Aliases = new[] { $"{region}.{RegionDiscoveryProvider.PublicEnvForRegional}", "login.microsoftonline.com" },
                PreferredCache = "login.microsoftonline.com",
                PreferredNetwork = $"{region}.{RegionDiscoveryProvider.PublicEnvForRegional}"
            };

            CollectionAssert.AreEquivalent(expectedEntry.Aliases, entry.Aliases);
            Assert.AreEqual(expectedEntry.PreferredCache, entry.PreferredCache);
            Assert.AreEqual(expectedEntry.PreferredNetwork, entry.PreferredNetwork);
        }
    }
}
