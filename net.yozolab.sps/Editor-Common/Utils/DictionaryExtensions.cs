using System;
using System.Collections.Generic;

namespace YozoLab.SPS.Utils {
    internal static class DictionaryExtensions {
        public static V GetOrCreate<K, V>(this Dictionary<K, V> dict, K key, Func<V> create) {
            if (dict.TryGetValue(key, out var value)) return value;
            return dict[key] = create();
        }
    }
}
