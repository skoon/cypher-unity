using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cypher.Core;
using UnityEditor;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Writes <c>Cypher.mixer</c> as text. Under the master, a group for every bus at every reverb-send tier (<see cref="AudioBuses"/>), each with its volume exposed, and a reverb return group that
    /// receives the sends and has Unity's own reverb with its settings exposed (<see cref="MixerReverb"/>). The file is the format the editor itself writes (read from a mixer it made); GUIDs and file
    /// ids are derived from names, so building twice writes the same file and leaves nothing to commit. The editor's own classes for this are internal, and a hand-written file is the same on every
    /// Unity that reads this one.
    /// </summary>
    public static class AudioMixerWriter
    {
        public const string Path = "Assets/_Cypher/Audio/Cypher.mixer";

        const long MixerId = 24100000, SnapshotId = 24500000, GroupIdBase = 24300000, EffectIdBase = 24400000;

        sealed class Effect
        {
            public long FileId;
            public string Name;
            public string EffectGuid, MixLevelGuid;
            public long SendTarget;
            public readonly List<(string name, string guid)> Parameters = new List<(string, string)>();
        }

        sealed class Group
        {
            public long FileId;
            public string Name, GroupGuid, VolumeGuid, PitchGuid;
            public readonly List<Effect> Effects = new List<Effect>();
            public readonly List<Group> Children = new List<Group>();
        }

        /// <summary>The mixer file's text.</summary>
        public static string Build()
        {
            var values = new List<(string guid, double value)>();
            var exposed = new List<(string guid, string name)>();
            var all = new List<Group>();
            long nextEffect = EffectIdBase, nextGroup = GroupIdBase;

            Effect NewEffect(string name, params string[] parameters)
            {
                var e = new Effect { FileId = ++nextEffect, Name = name, EffectGuid = Guid32("effect:" + nextEffect), MixLevelGuid = Guid32("mix:" + nextEffect) };
                foreach (string pname in parameters) e.Parameters.Add((pname, Guid32($"param:{nextEffect}:{pname}")));
                return e;
            }

            string ParameterGuid(Effect e, string name) => e.Parameters.Find(p => p.name == name).guid;

            Group NewGroup(string name, string volumeName, Group parent)
            {
                var g = new Group { FileId = ++nextGroup, Name = name, GroupGuid = Guid32("group:" + name), VolumeGuid = Guid32("volume:" + name), PitchGuid = Guid32("pitch:" + name) };
                g.Effects.Add(NewEffect("Attenuation"));
                parent?.Children.Add(g);
                all.Add(g);
                if (volumeName != null) exposed.Add((g.VolumeGuid, volumeName));
                return g;
            }

            Group master = NewGroup("Master", null, null);

            // the reverb return: whatever is sent to it comes through Unity's reverb, wet only
            Group reverb = NewGroup(AudioBuses.ReverbGroupName, AudioBuses.ReverbVolumeParameter, master);
            Effect receive = NewEffect("Receive");
            Effect reverbEffect = NewEffect("SFX Reverb", MixerReverb.EffectParameters);
            // the fader goes last so it governs what the reverb returns, not what is sent to it
            reverb.Effects.Insert(0, receive);
            reverb.Effects.Insert(1, reverbEffect);
            foreach (MixerReverb.Setting s in MixerReverb.Settings)
            {
                values.Add((ParameterGuid(reverbEffect, s.Effect), s.Default));
                if (s.Exposed != null) exposed.Add((ParameterGuid(reverbEffect, s.Effect), s.Exposed));
            }

            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int tier = 0; tier < AudioBuses.TierCount; tier++)
                {
                    Group g = NewGroup(AudioBuses.GroupName(bus, tier), AudioBuses.VolumeParameter(bus, tier), master);
                    if (tier == 0) continue;
                    Effect send = NewEffect("Send");
                    send.SendTarget = receive.FileId;
                    g.Effects.Add(send);
                    values.Add((send.MixLevelGuid, AudioBuses.TierSendDecibels(tier)));
                }
            }

            var sb = new StringBuilder();
            sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");

            Header(sb, 241, MixerId, "AudioMixerController");
            sb.Append("  m_Name: Cypher\n  m_OutputGroup: {fileID: 0}\n");
            sb.Append($"  m_MasterGroup: {{fileID: {master.FileId}}}\n  m_Snapshots:\n  - {{fileID: {SnapshotId}}}\n  m_StartSnapshot: {{fileID: {SnapshotId}}}\n");
            sb.Append("  m_SuspendThreshold: -80\n  m_EnableSuspend: 0\n  m_UpdateMode: 0\n  m_ExposedParameters:\n");
            foreach ((string guid, string name) in exposed) sb.Append($"  - guid: {guid}\n    name: {name}\n");
            sb.Append($"  m_AudioMixerGroupViews: []\n  m_CurrentViewIndex: 0\n  m_TargetSnapshot: {{fileID: {SnapshotId}}}\n");

            foreach (Group g in all)
            {
                Header(sb, 243, g.FileId, "AudioMixerGroupController");
                sb.Append($"  m_Name: {g.Name}\n  m_AudioMixer: {{fileID: {MixerId}}}\n  m_GroupID: {g.GroupGuid}\n");
                if (g.Children.Count == 0) sb.Append("  m_Children: []\n");
                else
                {
                    sb.Append("  m_Children:\n");
                    foreach (Group c in g.Children) sb.Append($"  - {{fileID: {c.FileId}}}\n");
                }
                sb.Append($"  m_Volume: {g.VolumeGuid}\n  m_Pitch: {g.PitchGuid}\n  m_Send: 00000000000000000000000000000000\n  m_Effects:\n");
                foreach (Effect e in g.Effects) sb.Append($"  - {{fileID: {e.FileId}}}\n");
                sb.Append("  m_UserColorIndex: 0\n  m_Mute: 0\n  m_Solo: 0\n  m_BypassEffects: 0\n");
            }

            foreach (Group g in all)
            {
                foreach (Effect e in g.Effects)
                {
                    Header(sb, 244, e.FileId, "AudioMixerEffectController", hideFlags: 3);
                    sb.Append($"  m_Name: \n  m_EffectID: {e.EffectGuid}\n  m_EffectName: {e.Name}\n  m_MixLevel: {e.MixLevelGuid}\n");
                    if (e.Parameters.Count == 0) sb.Append("  m_Parameters: []\n");
                    else
                    {
                        sb.Append("  m_Parameters:\n");
                        foreach ((string name, string guid) in e.Parameters) sb.Append($"  - m_ParameterName: {name}\n    m_GUID: {guid}\n");
                    }
                    sb.Append($"  m_SendTarget: {{fileID: {e.SendTarget}}}\n  m_EnableWetMix: 0\n  m_Bypass: 0\n");
                }
            }

            // the one snapshot holds the values that are not an effect's default
            Header(sb, 245, SnapshotId, "AudioMixerSnapshotController");
            sb.Append($"  m_Name: Snapshot\n  m_AudioMixer: {{fileID: {MixerId}}}\n  m_SnapshotID: {Guid32("snapshot")}\n");
            if (values.Count == 0) sb.Append("  m_FloatValues: {}\n");
            else
            {
                sb.Append("  m_FloatValues:\n");
                foreach ((string guid, double value) in values) sb.Append($"    {guid}: {value.ToString("0.######", CultureInfo.InvariantCulture)}\n");
            }
            sb.Append("  m_TransitionOverrides: {}\n");
            return sb.ToString();
        }

        static void Header(StringBuilder sb, int classId, long fileId, string type, int hideFlags = 0)
        {
            sb.Append($"--- !u!{classId} &{fileId}\n{type}:\n  m_ObjectHideFlags: {hideFlags}\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n");
        }

        /// <summary>A GUID (32 hex digits) that is the same every time for the same key.</summary>
        static string Guid32(string key)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("cypher-mixer:" + key));
                var sb = new StringBuilder(32);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Write the file and import it; does nothing (and touches nothing) if it is already as it should be.</summary>
        [MenuItem("Cypher/Write Audio Mixer")]
        public static void Write()
        {
            string text = Build();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            if (File.Exists(Path) && File.ReadAllText(Path) == text) return;
            File.WriteAllText(Path, text);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
        }
    }
}
