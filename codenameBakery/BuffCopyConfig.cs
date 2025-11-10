using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace codenameBakery
{
    public class BuffCopyConfig
    {
        public string originalBuffId { get; set; }

        public string newBuffId { get; set; }

        public float newDuration { get; set; } = -1f;
    }
}   