﻿using System;
using System.Reflection;

namespace MagpieUpdater.Services
{
    internal static class MainAssembly
    {
        internal static string ProductName
        {
            get { return Assembly.GetEntryAssembly().GetName().Name; }
        }

        internal static Version Version
        {
            get { return Assembly.GetEntryAssembly().GetName().Version; }
        }
    }
}