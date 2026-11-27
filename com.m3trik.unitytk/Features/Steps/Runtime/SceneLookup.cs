using UnityEngine;

namespace M3trik.Unitytk.Steps
{
    // The loaded scenes' T, for a part that names none of its own: kept once found and looked up again once it is gone;
    // where none is found, not looked up again (a reader asks several times a frame) until Forget, which its owner calls
    // as it is switched on.
    internal sealed class SceneLookup<T> where T : Object
    {
        T _found;
        bool _none;

        // The one found, else (unless none was found since the last Forget) the scenes' first; null when there is none.
        internal T Get()
        {
            if (_found == null && !_none)
            {
                _found = Object.FindAnyObjectByType<T>();
                _none = _found == null;
            }
            return _found;
        }

        // Look again the next time it is asked, where none was found.
        internal void Forget() => _none = false;
    }
}
