﻿//            Copyright Keysight Technologies 2012-2019
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, you can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenTap
{
    /// <summary> Detects which operating system is used. </summary>
    class OperatingSystem
    {
        public static readonly OperatingSystem Windows = new OperatingSystem(nameof(Windows));
        public static readonly OperatingSystem Linux = new OperatingSystem(nameof(Linux));
        public static readonly OperatingSystem MacOS = new OperatingSystem(nameof(MacOS));
        public static readonly OperatingSystem Unsupported = new OperatingSystem(nameof(Unsupported));
        public override string ToString() => Name;
        public string Name { get; }
        OperatingSystem(string name)
        {
            Name = name;
        }

        static OperatingSystem getCurrent()
        {

            if (Path.DirectorySeparatorChar == '\\')
            {
                return OperatingSystem.Windows;
            }
            else
            {
                if (Directory.Exists("/proc/"))
                {
                    return OperatingSystem.Linux;
                }
                else if (Directory.Exists("/opt")) // If /opt exists and /proc does not we are on mac
                {
                    return OperatingSystem.MacOS;
                }
            }
            return OperatingSystem.Unsupported;
        }

        static OperatingSystem current;
        public static OperatingSystem Current
        {
            get
            {
                if (current == null)
                {
                    current = getCurrent();
                }
                return current;
            }
        }
    }

    /// <summary> Detection of the specific linux variant. </summary>
    class LinuxVariant
    {
        public string Name { get; }
        public static readonly LinuxVariant Debian = new LinuxVariant("debian");
        public static readonly LinuxVariant Ubuntu = new LinuxVariant("ubuntu");
        public static readonly LinuxVariant RedHat = new LinuxVariant("redhat");
        public static readonly LinuxVariant Unknown = new LinuxVariant("linux-x64");

        static LinuxVariant()
        {
            if (OperatingSystem.Current == OperatingSystem.Linux)
            {
                var os_release_file = new FileInfo("/etc/os-release");
                if (os_release_file.Exists)
                {
                    Current = Unknown;
                    using (var str = new StreamReader(os_release_file.OpenRead()))
                    {
                        
                        string line;
                        while ((line = str.ReadLine()?.ToLowerInvariant()) != null)
                        {
                            if(line.Contains("name=\"debian gnu/linux\""))
                            {
                                Current = Debian;
                                return;
                            }
                            if(line.Contains("name=\"ubuntu\"") || line.Contains("id_like=\"ubuntu"))
                            {
                                Current = Ubuntu;
                                return;
                            }
                            
                            if (line.Contains("name=\"red hat"))
                            {
                                Current = RedHat;
                                return;
                            }

                            if (line.Contains("name=\"centos linux\""))
                            {
                                // pretend CentOS is Red Hat for simplicity.
                                Current = RedHat;
                                return;
                            }
                        }
                    }
                }
            }
        }
        
        public static LinuxVariant Current { get; }

        public LinuxVariant(string name) => Name = name;
    }

    class MacOsVariant
    {
        public string Type { get; }
        public static readonly MacOsVariant Intel = new MacOsVariant("x64");
        public static readonly MacOsVariant Apple = new MacOsVariant("arm64");
        public static MacOsVariant Current { get; }
        static MacOsVariant()
        {
            try
            {
                var startInfo = new ProcessStartInfo("uname", "-m");
                startInfo.RedirectStandardOutput = true;
                var process = Process.Start(startInfo);
                var uname = process?.StandardOutput.ReadToEnd();
                Current = uname.Contains("arm64") ? Apple : Intel;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public MacOsVariant(string type) => Type = type;
    }
}
