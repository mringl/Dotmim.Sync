﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Dotmim.Sync.Web.Client
{
    /// <summary>
    /// Http steps involved during a sync beetween a proxy client and proxy server
    /// </summary>
    public enum HttpStep
    {
        EnsureScopes,
        SendChanges,
        GetChanges,
        InProgress
    }
}
