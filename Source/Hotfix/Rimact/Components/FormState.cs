using System;
using System.Collections.Generic;

namespace Rimact.Components
{
    /// <summary>Form values, validation and submit state. Get it from <see cref="Hooks.UseForm{T}"/>.</summary>
    /// <remarks>Values and Valid update right after Set, so one handler can set and submit.</remarks>
    internal sealed class FormState<T>
    {
        internal sealed record Snapshot(T Values, HashSet<string> Touched, bool Submitted, bool Dirty);

        private readonly Ref<Snapshot> _state;
        private readonly Func<T, IEnumerable<(string Field, string Message)>>? _validate;
        private readonly T _initial;
        private readonly Action _changed;
        private Snapshot? _checked;
        private Dictionary<string, string> _errors = new();

        internal FormState(Ref<Snapshot> state, Func<T, IEnumerable<(string, string)>>? validate, T initial, Action changed)
        {
            _state = state;
            _validate = validate;
            _initial = initial;
            _changed = changed;
        }

        public T Values => _state.Current.Values;
        /// <summary>True once any value is set, until Reset.</summary>
        public bool Dirty => _state.Current.Dirty;
        public bool Submitted => _state.Current.Submitted;
        /// <summary>First message per field, shown or not.</summary>
        public IReadOnlyDictionary<string, string> Errors => Check();
        public bool Valid => Check().Count == 0;

        /// <summary>First error for field, once it is touched or the form was submitted.</summary>
        public string? Error(string field)
        {
            var s = _state.Current;
            return (s.Submitted || s.Touched.Contains(field)) && Check().TryGetValue(field, out var message) ? message : null;
        }

        /// <summary>Also marks field touched, so its error shows.</summary>
        public void Set(string field, Func<T, T> update)
        {
            var s = _state.Current;
            var touched = new HashSet<string>(s.Touched) { field };
            _state.Current = s with { Values = update(s.Values), Touched = touched, Dirty = true };
            _changed();
        }

        /// <summary>Touches no field. For loading defaults and such.</summary>
        public void Set(Func<T, T> update)
        {
            var s = _state.Current;
            _state.Current = s with { Values = update(s.Values), Dirty = true };
            _changed();
        }

        /// <summary>Shows all errors. Calls onValid and returns true if there are none.</summary>
        public bool Submit(Action<T> onValid)
        {
            _state.Current = _state.Current with { Submitted = true };
            _changed();
            if (!Valid) return false;
            onValid(Values);
            return true;
        }

        /// <summary>Back to the initial values, errors hidden.</summary>
        public void Reset()
        {
            _state.Current = new Snapshot(_initial, new HashSet<string>(), false, false);
            _changed();
        }

        private Dictionary<string, string> Check()
        {
            var s = _state.Current;
            if (ReferenceEquals(s, _checked)) return _errors;
            _checked = s;
            _errors = new Dictionary<string, string>();
            if (_validate != null)
                foreach (var (field, message) in _validate(s.Values))
                    _errors.TryAdd(field, message);
            return _errors;
        }
    }
}

namespace Rimact
{
    internal static partial class Hooks
    {
        /// <summary>Form state. validate returns (field, message) pairs, the first per field shows.</summary>
        /// <example><code>var form = UseForm(new Engine("", 60), e =&gt; e.Key.Length == 0 ? new[] { ("Key", "Required") } : Array.Empty&lt;(string, string)&gt;());
        /// Field("API key", Input(form.Values.Key, v =&gt; form.Set("Key", e =&gt; e with { Key = v })), error: form.Error("Key"))</code></example>
        public static Components.FormState<T> UseForm<T>(T initial, Func<T, IEnumerable<(string Field, string Message)>>? validate = null)
        {
            var state = UseRef(new Components.FormState<T>.Snapshot(initial, new HashSet<string>(), false, false));
            var version = UseState(0);
            return UseMemo(() => new Components.FormState<T>(state, validate, initial, () => version.Set(v => v + 1)), validate, initial);
        }
    }
}
