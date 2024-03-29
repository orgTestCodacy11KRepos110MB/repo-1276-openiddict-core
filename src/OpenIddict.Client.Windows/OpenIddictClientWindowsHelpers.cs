﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

#if SUPPORTS_WINDOWS_RUNTIME
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.System;
#endif

namespace OpenIddict.Client.Windows;

/// <summary>
/// Exposes companion extensions for the OpenIddict/Windows integration.
/// </summary>
public static class OpenIddictClientWindowsHelpers
{
    /// <summary>
    /// Gets the <see cref="OpenIddictClientWindowsActivation"/> associated with the current context.
    /// </summary>
    /// <param name="transaction">The transaction instance.</param>
    /// <returns>The <see cref="OpenIddictClientWindowsActivation"/> instance or <see langword="null"/> if it couldn't be found.</returns>
    public static OpenIddictClientWindowsActivation? GetWindowsActivation(this OpenIddictClientTransaction transaction)
        => transaction.GetProperty<OpenIddictClientWindowsActivation>(typeof(OpenIddictClientWindowsActivation).FullName!);

#if SUPPORTS_WINDOWS_RUNTIME
    /// <summary>
    /// Determines whether the Windows Runtime APIs are supported on this platform.
    /// </summary>
    /// <returns><see langword="true"/> if the Windows Runtime APIs are supported, <see langword="false"/> otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining), SupportedOSPlatformGuard("windows10.0.17763")]
    internal static bool IsWindowsRuntimeSupported()
    {
        // Note: as WinRT is only supported on Windows 8 and higher, trying to call any of the
        // WinRT APIs on previous versions of Windows will typically result in type-load or
        // type-initialization exceptions. To prevent that, this method acts as a platform
        // guard that will prevent the WinRT projections from being loaded by the runtime on
        // platforms that don't support it. Since OpenIddict declares Windows 10 1809 as the
        // oldest supported version in the package, it is also used for the runtime check.

#if SUPPORTS_OPERATING_SYSTEM_VERSIONS_COMPARISON
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
#else
        if (Environment.OSVersion.Platform is PlatformID.Win32NT &&
            Environment.OSVersion.Version >= new Version(10, 0, 17763))
        {
            return true;
        }

        // Note: on older versions of .NET, Environment.OSVersion.Version is known to be affected by
        // the compatibility shims used by Windows 10+ when the application doesn't have a manifest
        // that explicitly indicates it's compatible with Windows 10 and higher. To avoid that, a
        // second pass using RuntimeInformation.OSDescription (that calls NtDll.RtlGetVersion() under
        // the hood) is made. Note: no version is returned on UWP due to the missing Win32 API.
        return RuntimeInformation.OSDescription.StartsWith("Microsoft Windows ", StringComparison.OrdinalIgnoreCase) &&
               RuntimeInformation.OSDescription["Microsoft Windows ".Length..] is string value &&
               Version.TryParse(value, out Version? version) && version >= new Version(10, 0, 17763);
#endif
    }

    /// <summary>
    /// Determines whether the specified identity contains an AppContainer
    /// token, indicating it's running in an AppContainer sandbox.
    /// </summary>
    /// <param name="identity">The <see cref="WindowsIdentity"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the specified identity contains an
    /// AppContainer token, <see langword="false"/> otherwise.
    /// </returns>
    [SupportedOSPlatformGuard("windows10.0.17763")]
    internal static unsafe bool HasAppContainerToken(WindowsIdentity identity)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        int* buffer = stackalloc int[1];

        if (!GetTokenInformation(
            TokenHandle           : identity.Token,
            TokenInformationClass : /* TokenIsAppContainer */ 29,
            TokenInformation      : new IntPtr(buffer),
            TokenInformationLength: sizeof(int),
            ReturnLength          : out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return *buffer is not 0;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetTokenInformation(
            IntPtr TokenHandle,
            uint TokenInformationClass,
            IntPtr TokenInformation,
            uint TokenInformationLength,
            out uint ReturnLength);
    }

    /// <summary>
    /// Resolves the protocol activation using the Windows Runtime APIs, if applicable.
    /// </summary>
    /// <returns>
    /// The <see cref="Uri"/> if the application instance was activated
    /// via a protocol activation, <see langword="null"/> otherwise.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows10.0.17763")]
    internal static Uri? GetProtocolActivationUriWithWindowsRuntime()
    {
        if (!ApiInformation.IsMethodPresent(typeof(AppInstance).FullName,
            nameof(AppInstance.GetActivatedEventArgs), inputParameterCount: 0))
        {
            return null;
        }

        try
        {
            return AppInstance.GetActivatedEventArgs() is
                ProtocolActivatedEventArgs args ? args.Uri : null;
        }

        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the system browser using the Windows Runtime APIs.
    /// </summary>
    /// <param name="uri">The <see cref="Uri"/> to use.</param>
    /// <returns><see langword="true"/> if the browser could be started, <see langword="false"/> otherwise.</returns>
    [MethodImpl(MethodImplOptions.NoInlining), SupportedOSPlatform("windows10.0.17763")]
    internal static async Task<bool> TryLaunchBrowserWithWindowsRuntimeAsync(Uri uri)
    {
        // Note: with the materialization of Project Centennial/Desktop Bridge in Windows 10 1607
        // (also known as Anniversary Update), desktop applications that don't have a package
        // identity are now allowed to use most of the WinRT APIs. Since OpenIddict's UWP support
        // is implemented via a .NET Standard 2.0 TFM (which requires Windows 10 1809), it is assumed
        // at this point that Launcher.LaunchUriAsync() can be used in both types of applications.

        if (!ApiInformation.IsMethodPresent(typeof(Launcher).FullName,
            nameof(Launcher.LaunchUriAsync), inputParameterCount: 1))
        {
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(uri);
        }

        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
#endif

    /// <summary>
    /// Starts the system browser using ShellExecute.
    /// </summary>
    /// <param name="uri">The <see cref="Uri"/> to use.</param>
    /// <returns><see langword="true"/> if the browser could be started, <see langword="false"/> otherwise.</returns>
    internal static async Task<bool> TryLaunchBrowserWithShellExecuteAsync(Uri uri)
    {
        try
        {
            await Task.Run(() => Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            }));

            return true;
        }

        catch (UnauthorizedAccessException)
        {
            return false;
        }

        catch (Win32Exception exception) when (exception.NativeErrorCode is 5)
        {
            return false;
        }
    }
}
