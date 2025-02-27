﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client.AuthScheme.PoP;
using Microsoft.Identity.Client.Cache;
using Microsoft.Identity.Client.Core;
using Microsoft.Identity.Client.Internal;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs;
using Microsoft.Identity.Client.Platforms.net45.Http;
using Microsoft.Identity.Client.PlatformsCommon.Interfaces;
using Microsoft.Identity.Client.PlatformsCommon.Shared;
using Microsoft.Identity.Client.TelemetryCore.Internal;
using Microsoft.Identity.Client.UI;
using Microsoft.Win32;

namespace Microsoft.Identity.Client.Platforms.net45
{
    /// <summary>
    ///     Platform / OS specific logic.
    /// </summary>
    internal class NetDesktopPlatformProxy : AbstractPlatformProxy
    {
        /// <inheritdoc />
        public NetDesktopPlatformProxy(ICoreLogger logger)
            : base(logger)
        {
        }

        private bool IsWindows
        {
            get
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32S:
                    case PlatformID.Win32Windows:
                    case PlatformID.Win32NT:
                    case PlatformID.WinCE:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        ///     Get the user logged in to Windows or throws
        /// </summary>
        /// <returns>Upn or throws</returns>
        public override Task<string> GetUserPrincipalNameAsync()
        {
            const int NameUserPrincipal = 8;
            return Task.FromResult(GetUserPrincipalName(NameUserPrincipal));
        }

        private string GetUserPrincipalName(int nameFormat)
        {
            // TODO: there is discrepancy between the implementation of this method on net45 - throws if upn not found - and uap and
            // the rest of the platforms - returns ""

            uint userNameSize = 0;
            WindowsNativeMethods.GetUserNameEx(nameFormat, null, ref userNameSize);
            if (userNameSize == 0)
            {
                throw new MsalClientException(
                    MsalError.GetUserNameFailed,
                    MsalErrorMessage.GetUserNameFailed,
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            var sb = new StringBuilder((int)userNameSize);
            if (!WindowsNativeMethods.GetUserNameEx(nameFormat, sb, ref userNameSize))
            {
                throw new MsalClientException(
                    MsalError.GetUserNameFailed,
                    MsalErrorMessage.GetUserNameFailed,
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return sb.ToString();
        }

        /// <inheritdoc />
        public override string GetDefaultRedirectUri(string clientId, bool useRecommendedRedirectUri = false)
        {
            if (useRecommendedRedirectUri)
            {
                return Constants.NativeClientRedirectUri;
            }

            return Constants.DefaultRedirectUri;
        }

        /// <inheritdoc />
        public override ILegacyCachePersistence CreateLegacyCachePersistence()
        {
            return new InMemoryLegacyCachePersistance();
        }

        /// <inheritdoc />
        public override ITokenCacheAccessor CreateTokenCacheAccessor()
        {
            return new InMemoryTokenCacheAccessor(Logger);
        }

        /// <inheritdoc />
        protected override IWebUIFactory CreateWebUiFactory()
        {
            return new NetDesktopWebUIFactory();
        }

        /// <inheritdoc />
        protected override string InternalGetDeviceModel()
        {
            // Since MSAL .NET may be used on servers, for security reasons, we do not emit device type.
            return null;
        }

        /// <inheritdoc />
        protected override string InternalGetOperatingSystem()
        {
            return DesktopOsHelper.GetWindowsVersionString();
        }

        /// <inheritdoc />
        protected override string InternalGetProcessorArchitecture()
        {
            return IsWindows ? WindowsNativeMethods.GetProcessorArchitecture() : null;
        }

        /// <inheritdoc />
        protected override string InternalGetCallingApplicationName()
        {
            // Considered PII, ensure that it is hashed.
            return Assembly.GetEntryAssembly()?.GetName()?.Name;
        }

        /// <inheritdoc />
        protected override string InternalGetCallingApplicationVersion()
        {
            // Considered PII, ensure that it is hashed.
            return Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
        }

        /// <inheritdoc />
        protected override string InternalGetDeviceId()
        {
            try
            {
                // Considered PII, ensure that it is hashed.
                return NetworkInterface.GetAllNetworkInterfaces().Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                                .Select(nic => nic.GetPhysicalAddress()?.ToString()).FirstOrDefault();
            }
            catch (EntryPointNotFoundException)
            {
                // Thrown when ran in an Azure Runbook
                return null;
            }
        }

        /// <inheritdoc />
        protected override string InternalGetProductName()
        {
            return "MSAL.Desktop";
        }

        protected override string InternalGetRuntimeVersion()
        {
            // https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#query-the-registry-using-code
            try
            {
                string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";
                using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
                {
                    if (ndpKey?.GetValue("Release") != null)
                    {
                        int releaseKey = (int)ndpKey.GetValue("Release");
                        if (releaseKey >= 528040)
                            return "4.8 or later";
                        if (releaseKey >= 461808)
                            return "4.7.2";
                        if (releaseKey >= 461308)
                            return "4.7.1";
                        if (releaseKey >= 460798)
                            return "4.7";
                        if (releaseKey >= 394802)
                            return "4.6.2";
                        if (releaseKey >= 394254)
                            return "4.6.1";
                        if (releaseKey >= 393295)
                            return "4.6";
                        if (releaseKey >= 379893)
                            return "4.5.2";
                        if (releaseKey >= 378675)
                            return "4.5.1";
                        if (releaseKey >= 378389)
                            return "4.5";
                    }
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <inheritdoc />
        protected override ICryptographyManager InternalGetCryptographyManager() => new NetDesktopCryptographyManager();

        public override string GetDeviceNetworkState()
        {
            // TODO(mats):
            return string.Empty;
        }

        public override string GetDevicePlatformTelemetryId()
        {
            const int NameSamCompatible = 2;

            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient", false);
            object val = key.GetValue("MachineId");
            if (val == null)
            {
                return string.Empty;
            }

            string win32DeviceId = val.ToString();

            string userName = GetUserPrincipalName(NameSamCompatible);

            // NameSamCompatible might include an email address. remove the domain before hashing.
            int atIdx = userName.IndexOf('@');
            if (atIdx >= 0)
            {
                userName = userName.Substring(0, atIdx);
            }

            string unhashedDpti = win32DeviceId + userName;

            var hashedBytes = InternalGetCryptographyManager().CreateSha256HashBytes(unhashedDpti);
            var sb = new StringBuilder();

            foreach (var b in hashedBytes)
            {
                sb.Append($"{b:x2}");
            }

            string dptiOutput = sb.ToString();
            return dptiOutput;
        }

        public override string GetMatsOsPlatform()
        {
            return MatsConverter.AsString(OsPlatform.Win32);
        }

        public override int GetMatsOsPlatformCode()
        {
            return MatsConverter.AsInt(OsPlatform.Win32);
        }
        protected override IPlatformLogger InternalGetPlatformLogger() => new EventSourcePlatformLogger();

        protected override IFeatureFlags CreateFeatureFlags() => new NetDesktopFeatureFlags();

        public override Task StartDefaultOsBrowserAsync(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }

            return Task.FromResult(0);
        }

        public override IPoPCryptoProvider GetDefaultPoPCryptoProvider()
        {
            return PoPProviderFactory.GetOrCreateProvider();
        }

        public override IDeviceAuthManager CreateDeviceAuthManager()
        {
            return new NetDesktopDeviceAuthManager();
        }

        public override IMsalHttpClientFactory CreateDefaultHttpClientFactory()
        {
            return new NetDesktopHttpClientFactory();
        }

        public override bool BrokerSupportsWamAccounts => true;
    }
}
