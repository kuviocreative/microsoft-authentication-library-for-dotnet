﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client.OAuth2;
using Microsoft.Identity.Client.Utils;
using Microsoft.Identity.Test.Common.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Identity.Test.Common.Core.Mocks
{
    internal class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage ResponseMessage { get; set; }
        public string ExpectedUrl { get; set; }
        public IDictionary<string, string> ExpectedQueryParams { get; set; }
        public IDictionary<string, string> ExpectedPostData { get; set; }
        public IDictionary<string, string> ExpectedRequestHeaders { get; set; }
        public IList<string> UnexpectedRequestHeaders { get; set; }

        public HttpMethod ExpectedMethod { get; set; }
        
        public Exception ExceptionToThrow { get; set; }
        public Action<HttpRequestMessage> AdditionalRequestValidation { get; set; }

        /// <summary>
        /// Once the http message is executed, this property holds the request message
        /// </summary>
        public HttpRequestMessage ActualRequestMessage { get; private set; }

        public Dictionary<string, string> ActualRequestPostData { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ActualRequestMessage = request;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            var uri = request.RequestUri;
            if (!string.IsNullOrEmpty(ExpectedUrl))
            {
                Assert.AreEqual(
                    ExpectedUrl,
                    uri.AbsoluteUri.Split(
                        new[]
                        {
                            '?'
                        })[0]);
            }

            Assert.AreEqual(ExpectedMethod, request.Method);

            // Match QP passed in for validation.
            if (ExpectedQueryParams != null)
            {
                Assert.IsFalse(
                    string.IsNullOrEmpty(uri.Query),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "provided url ({0}) does not contain query parameters, as expected",
                        uri.AbsolutePath));
                IDictionary<string, string> inputQp = CoreHelpers.ParseKeyValueList(uri.Query.Substring(1), '&', false, null);
                Assert.AreEqual(ExpectedQueryParams.Count, inputQp.Count, "Different number of query params`");
                foreach (string key in ExpectedQueryParams.Keys)
                {
                    Assert.IsTrue(
                        inputQp.ContainsKey(key),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "expected QP ({0}) not found in the url ({1})",
                            key,
                            uri.AbsolutePath));
                    Assert.AreEqual(ExpectedQueryParams[key], inputQp[key]);
                }
            }

            if (request.Method != HttpMethod.Get && request.Content != null)
            {
                string postData = request.Content.ReadAsStringAsync().Result;
                ActualRequestPostData = CoreHelpers.ParseKeyValueList(postData, '&', true, null);
            }

            if (ExpectedPostData != null)
            {
                foreach (string key in ExpectedPostData.Keys)
                {
                    Assert.IsTrue(ActualRequestPostData.ContainsKey(key));
                    if (key.Equals(OAuth2Parameter.Scope, StringComparison.OrdinalIgnoreCase))
                    {
                        CoreAssert.AreScopesEqual(ExpectedPostData[key], ActualRequestPostData[key]);
                    }
                    else
                    {
                        Assert.AreEqual(ExpectedPostData[key], ActualRequestPostData[key]);
                    }
                }
            }       
            
            if (ExpectedRequestHeaders != null )
            {
                foreach (var kvp in ExpectedRequestHeaders)
                {
                    Assert.IsTrue(
                        request.Headers.Any(h =>
                            string.Equals(h.Key, kvp.Key, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(h.Value.AsSingleString(), kvp.Value, StringComparison.OrdinalIgnoreCase))
                        , $"Expecting a request header {kvp.Key}: {kvp.Value} but did not find in the actual request: {request}");
                }
            }

            if (UnexpectedRequestHeaders != null)
            {
                foreach (var item in UnexpectedRequestHeaders)
                {
                    Assert.IsTrue(
                        !request.Headers.Any(h => string.Equals(h.Key, item, StringComparison.OrdinalIgnoreCase))
                        , $"Not expecting a request header with key={item} but it was found in the actual request: {request}");
                }
            }

            AdditionalRequestValidation?.Invoke(request);

            return new TaskFactory().StartNew(() => ResponseMessage, cancellationToken);
        }

        private string ReturnValueFromRequestHeader(string telemRequest)
        {
            IEnumerable<string> telemRequestValue = ActualRequestMessage.Headers.GetValues(telemRequest);
            List<string> telemRequestValueAsList = telemRequestValue.ToList();
            string value = telemRequestValueAsList[0];
            return value;
        }
    }
}
