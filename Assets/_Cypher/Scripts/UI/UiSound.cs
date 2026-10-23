using System;

namespace Cypher.UI
{
    /// <summary>
    /// What the interface asks to be heard. The controls and screens call <see cref="Raise"/> with the name of a sound (the prototype's: ui.hover, ui.tick, ui.select, ui.confirm, ui.back,
    /// ui.open, ui.close, ui.error, ui.alert, ui.notify); the audio engine, which this assembly knows nothing about, installs <see cref="Sink"/> to play them. With no sink nothing happens.
    /// </summary>
    public static class UiSound
    {
        public const string Hover = "ui.hover", Tick = "ui.tick", Select = "ui.select", Confirm = "ui.confirm", Back = "ui.back";
        public const string Open = "ui.open", Close = "ui.close", Error = "ui.error", Alert = "ui.alert", Notify = "ui.notify";

        /// <summary>Where requests go. Set by the audio engine; tests set it to listen.</summary>
        public static Action<string> Sink { get; set; }

        public static void Raise(string sound)
        {
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            Sink?.Invoke(sound);
        }
    }
}
