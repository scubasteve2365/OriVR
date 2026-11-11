using UnityEngine;

namespace OriVR
{
    // Soft bell tones for the mod menu, made in code (no game audio files): move, change, tab, open, close and a low tone
    // when a value is already at its limit. They play while the game is paused and follow the game's master volume.
    internal class MenuSound : MonoBehaviour
    {
        internal int Level = 2;  // 0 Off, 1 Low, 2 Medium, 3 High
        static readonly float[] Volumes = { 0f, 0.12f, 0.25f, 0.45f };
        const int Rate = 44100;

        AudioSource source;
        AudioClip move, change, tab, open, close, refused;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            move = Notes("menu move", new[] { 1318.5f }, 0.07f, 0.06f);
            change = Notes("menu change", new[] { 987.8f, 1318.5f }, 0.055f, 0.05f);
            tab = Chord("menu tab", new[] { 784f, 1174.7f }, 0.16f);
            open = Notes("menu open", new[] { 659.3f, 987.8f, 1318.5f }, 0.075f, 0.09f);
            close = Notes("menu close", new[] { 1318.5f, 987.8f, 659.3f }, 0.075f, 0.09f);
            refused = Notes("menu refused", new[] { 220f }, 0.12f, 0.08f);
        }

        internal void Move() { Play(move); }
        internal void Change() { Play(change); }
        internal void Tab() { Play(tab); }
        internal void Open() { Play(open); }
        internal void Close() { Play(close); }
        internal void Refused() { Play(refused); }

        void Play(AudioClip clip)
        {
            float v = Volumes[Mathf.Clamp(Level, 0, 3)];
            if (v > 0f && clip != null && source != null) source.PlayOneShot(clip, v);
        }

        // Notes one after another, each a soft sine with a little second harmonic and a quick fade.
        static AudioClip Notes(string name, float[] freqs, float noteSeconds, float tailSeconds)
        {
            int noteLen = (int)(noteSeconds * Rate), total = noteLen * freqs.Length + (int)(tailSeconds * Rate);
            var data = new float[total];
            for (int n = 0; n < freqs.Length; n++)
                AddBell(data, n * noteLen, total - n * noteLen, freqs[n], 0.5f);
            return Make(name, data);
        }

        static AudioClip Chord(string name, float[] freqs, float seconds)
        {
            var data = new float[(int)(seconds * Rate)];
            foreach (var f in freqs) AddBell(data, 0, data.Length, f, 0.4f);
            return Make(name, data);
        }

        static void AddBell(float[] data, int start, int length, float freq, float amp)
        {
            float decay = 6f / (length / (float)Rate);
            for (int i = 0; i < length && start + i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float env = attack * Mathf.Exp(-decay * t);
                data[start + i] += amp * env * (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * freq * t));
            }
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
