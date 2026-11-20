using System;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Shows a fallback in place of a subtree that threw. The subtree is unmounted and loses its state.</summary>
    /// <remarks>Catches Build, effects, UseExternal readers and UseInterval. Not event handlers, painting, Native leaves or the fallback.</remarks>
    /// <example><code>ErrorBoundary(new ModList(), (ex, reset) =&gt; Button("Reload list", reset))</code></example>
    internal sealed record ErrorBoundary(Node Child) : UIComponent
    {
        /// <summary>Takes the error and a reset that remounts the child. null shows an error alert with Retry.</summary>
        public Func<Exception, Action, Node>? Fallback { get; init; }

        protected internal override bool CatchesErrors => true;

        protected internal override Node Build()
        {
            var self = UseFiber();
            var host = self.Host;
            var reset = UseCallback(() =>
            {
                if (self.CaughtError is null) return;
                self.CaughtError = null;
                host.MarkDirty(self);
            }, []);
            if (self.CaughtError is not { } error) return Child;
            if (Fallback != null) return Fallback(error, reset);
            var s = UseStrings();
            return Column(
                    Alert(s.ErrorTitle, $"{error.GetType().Name}: {error.Message}", AlertVariant.Destructive),
                    Button(s.Retry, reset))
                .Gap(UseTheme().Space(2)).Align(Align.Start);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.ErrorBoundary ErrorBoundary(Node child, Func<Exception, Action, Node>? fallback = null) =>
            new(child) { Fallback = fallback };
    }
}
