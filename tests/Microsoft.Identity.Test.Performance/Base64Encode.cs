// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Identity.Client.Utils;

namespace Microsoft.Identity.Test.Performance
{
    [SimpleJob(RuntimeMoniker.Net472)]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class Base64Encode
    {



        const string s1 = "The quick brown fox jumps over the lazy dog";
        const string s2 = "The quick brown fox jumps over the lazy dog==";
        const string s3 = "The quick +brown fox jumps //over the lazy dog=";

        static readonly byte[] d1 = Encoding.UTF8.GetBytes(s1);
        static readonly byte[] d2 = Encoding.UTF8.GetBytes(s2);
        static readonly byte[] d3 = Encoding.UTF8.GetBytes(s3);

        //[GlobalSetup]
        //public void Setup()
        //{
        //    data = Encoding.UTF8.GetBytes(s);
        //}

        [Benchmark(Baseline = true)]
        public void ExistingEncoding_String()
        {
            Base64UrlHelpers.Encode(s1);
            Base64UrlHelpers.Encode(s2);
            Base64UrlHelpers.Encode(s3);
        }



        [Benchmark]
        public void FastEncode_String()
        {
            Base64UrlHelpers.FastEncode(s1);
            Base64UrlHelpers.FastEncode(s2);
            Base64UrlHelpers.FastEncode(s3);
        }

        [Benchmark]
        public void ExistingEncoding_Byte()
        {
            Base64UrlHelpers.Encode(d1);
            Base64UrlHelpers.Encode(d2);
            Base64UrlHelpers.Encode(d3);
        }

        [Benchmark]
        public void AspNetCoreStyle_Byte()
        {
            WebEncoders.Base64UrlEncode(d1);
            WebEncoders.Base64UrlEncode(d2);
            WebEncoders.Base64UrlEncode(d3);
        }

    }
}
