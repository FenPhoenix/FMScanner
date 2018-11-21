// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SysRuntime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class DisablePrivateReflectionAttribute : Attribute
    {
        public DisablePrivateReflectionAttribute() { }
    }
}

