// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// source https://github.com/mono/mono/commits/a44926a5be1c648deec836e9cdda3c93e25f9a51
using System;

namespace Mono.Profiler.Aot
{
    public sealed class ProfileData
    {
        public ProfileData ()
        {
            this.Modules = Array.Empty<ModuleRecord>();
            this.Types = Array.Empty<TypeRecord>();
            this.Methods = Array.Empty<MethodRecord>();
        }

        public ProfileData (ModuleRecord[] modules, TypeRecord[] types, MethodRecord[] methods)
        {
            this.Modules = modules;
            this.Types = types;
            this.Methods = methods;
        }

        public ModuleRecord[] Modules { get; set; }
        public TypeRecord[] Types { get; set; }
        public MethodRecord[] Methods { get; set; }
    }
}
