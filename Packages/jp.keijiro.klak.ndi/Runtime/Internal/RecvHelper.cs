using System;
using System.Collections.Generic;
using UnityEngine;

namespace Klak.Ndi {

// Small helper class for NDI recv interop
static class RecvHelper
{
    // A name without parentheses also matches the stream name part of
    // "MACHINE (STREAM)", so the source is found on any machine. An exact
    // match takes precedence. When several machines send the stream, the
    // first machine name in ordinal order is chosen, so the choice doesn't
    // depend on the discovery order (a warning is shown once).
    public static Interop.Source? FindSource(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;

        var sources = SharedInstance.Find.CurrentSources;

        foreach (var source in sources)
            if (string.Equals(source.NdiName, sourceName, StringComparison.Ordinal))
                return source;

        if (sourceName.Contains("(")) return null;

        var suffix = " (" + sourceName + ")";
        Interop.Source? found = null;
        string foundName = null;
        var count = 0;

        foreach (var source in sources)
        {
            var name = source.NdiName;
            if (name == null || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            count++;
            if (foundName == null || string.CompareOrdinal(name, foundName) < 0)
                (found, foundName) = (source, name);
        }

        if (count > 1 && _warnedAmbiguousNames.Add(sourceName))
            Debug.LogWarning($"[KlakNDI] {count} sources match \"{sourceName}\". " +
                             $"Receiving \"{foundName}\" (the first in ordinal order).");

        return found;
    }

    static readonly HashSet<string> _warnedAmbiguousNames = new HashSet<string>();

    public static unsafe Interop.Recv TryCreateRecv(string sourceName)
    {
        var source = FindSource(sourceName);
        if (source == null) return null;

        var opt = new Interop.Recv.Settings
          { Source = (Interop.Source)source,
            ColorFormat = Interop.ColorFormat.Fastest,
            Bandwidth = Interop.Bandwidth.Highest };

        return Interop.Recv.Create(opt);
    }
}

} // namespace Klak.Ndi
