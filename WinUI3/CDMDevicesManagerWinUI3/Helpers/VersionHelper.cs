// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Windows.ApplicationModel.WindowsAppRuntime;

namespace CDMDevicesManagerWinUI3.Helpers;
internal static partial class VersionHelper
{
    public static string WinAppSdkDetails =>
        $"Windows App SDK {ReleaseInfo.Major}.{ReleaseInfo.Minor}";

    public static string WinAppSdkRuntimeDetails =>
        WinAppSdkDetails + $", Windows App Runtime {RuntimeInfo.AsString}";
}
