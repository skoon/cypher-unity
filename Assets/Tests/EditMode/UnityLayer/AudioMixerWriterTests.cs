using System.IO;
using System.Text.RegularExpressions;
using Cypher.Core;
using Cypher.EditorTools;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class AudioMixerWriterTests
    {
        [Test]
        public void TheTextIsTheSameEveryTimeItIsBuilt()
        {
            Assert.AreEqual(AudioMixerWriter.Build(), AudioMixerWriter.Build());
        }

        [Test]
        public void TheFileInTheProjectIsWhatTheWriterMakes()
        {
            Assert.IsTrue(File.Exists(AudioMixerWriter.Path), "run Cypher > Write Audio Mixer");
            Assert.AreEqual(AudioMixerWriter.Build(), File.ReadAllText(AudioMixerWriter.Path), "the mixer file is out of date: run Cypher > Write Audio Mixer");
        }

        [Test]
        public void ThereIsAGroupAndAnExposedVolumeForEveryBusAtEveryTier()
        {
            string text = AudioMixerWriter.Build();
            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int tier = 0; tier < AudioBuses.TierCount; tier++)
                {
                    StringAssert.Contains($"m_Name: {AudioBuses.GroupName(bus, tier)}\n", text);
                    StringAssert.Contains($"name: {AudioBuses.VolumeParameter(bus, tier)}\n", text);
                }
            }
            StringAssert.Contains($"name: {AudioBuses.ReverbVolumeParameter}\n", text);
            foreach (MixerReverb.Setting s in MixerReverb.Settings)
            {
                StringAssert.Contains($"m_ParameterName: {s.Effect}\n", text);
                if (s.Exposed != null) StringAssert.Contains($"name: {s.Exposed}\n", text);
            }
        }

        [Test]
        public void EveryWetTierSendsToTheReverbsReceiveAndTheDryOneSendsNowhere()
        {
            string text = AudioMixerWriter.Build();
            Match receive = Regex.Match(text, @"--- !u!244 &(\d+)\nAudioMixerEffectController:(?:(?!---).)*?m_EffectName: Receive\n", RegexOptions.Singleline);
            Assert.IsTrue(receive.Success, "a Receive effect");
            string receiveId = receive.Groups[1].Value;
            MatchCollection sends = Regex.Matches(text, @"m_EffectName: Send\n(?:(?!---).)*?m_SendTarget: \{fileID: (\d+)\}", RegexOptions.Singleline);
            Assert.AreEqual(5 * (AudioBuses.TierCount - 1), sends.Count, "a Send for every wet tier of every bus");
            foreach (Match m in sends) Assert.AreEqual(receiveId, m.Groups[1].Value);
        }

        [Test]
        public void EveryFileIdAndGuidIsUnique()
        {
            string text = AudioMixerWriter.Build();
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (Match m in Regex.Matches(text, @"^--- !u!\d+ &(\d+)$", RegexOptions.Multiline)) Assert.IsTrue(ids.Add(m.Groups[1].Value), "file id " + m.Groups[1].Value);
            Assert.Greater(ids.Count, 80);
            var guids = new System.Collections.Generic.HashSet<string>();
            foreach (Match m in Regex.Matches(text, @"(?:m_GroupID|m_Volume|m_Pitch|m_EffectID|m_MixLevel|m_GUID|m_SnapshotID): ([0-9a-f]{32})$", RegexOptions.Multiline)) Assert.IsTrue(guids.Add(m.Groups[1].Value), "guid " + m.Groups[1].Value);
        }

        [Test]
        public void TheReverbsFaderComesAfterItsEffectsSoItGovernsTheReturn()
        {
            string text = AudioMixerWriter.Build();
            Match reverb = Regex.Match(text, @"m_Name: Reverb\n(?:(?!---).)*?m_Effects:\n  - \{fileID: (\d+)\}\n  - \{fileID: (\d+)\}\n  - \{fileID: (\d+)\}\n", RegexOptions.Singleline);
            Assert.IsTrue(reverb.Success);
            string[] names = new string[3];
            for (int i = 0; i < 3; i++)
            {
                Match e = Regex.Match(text, $@"&{reverb.Groups[i + 1].Value}\nAudioMixerEffectController:(?:(?!---).)*?m_EffectName: ([^\n]+)\n", RegexOptions.Singleline);
                names[i] = e.Groups[1].Value;
            }
            CollectionAssert.AreEqual(new[] { "Receive", "SFX Reverb", "Attenuation" }, names);
        }
    }
}
