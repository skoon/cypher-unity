// Milestone 5's content: ours, not the prototype's. Written by hand; edit it here.
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>
    /// The game's jobs and conversations: the prototype's (<see cref="RpgContent"/>, which the goldens replay unchanged) with Milestone 5's fights added. Two gigs
    /// with kill objectives (Kaz's <c>q_gig_jackals</c>, Marlene's <c>q_gig_protection</c>), the options that offer them and the lines when they are done, and the
    /// Data Core's guards in <i>Ghost in the Rain</i> (a kill objective the breach waits on). <see cref="EncountersJson"/> is who holds which place, and when.
    /// </summary>
    public static class RpgGameContent
    {
        public const string QuestsJson = @"[
  {
    ""id"": ""q_gig_jackals"",
    ""title"": ""Jackal Season"",
    ""type"": ""gig"",
    ""giver"": ""Mama Kaz"",
    ""recommendedLevel"": 3,
    ""summary"": ""The Rust Jackals ran Sixth Street out of the Chrome Cathedral Garage, and now they strip anything that slows down on Kaz's delivery route. Kaz wants the garage quiet."",
    ""requires"": {
      ""level"": 2,
      ""flag"": ""kaz_job_taken""
    },
    ""stages"": [
      {
        ""id"": ""s_brief"",
        ""title"": ""Hear Kaz out"",
        ""journal"": ""Kaz has a second job on the stove, and she only serves it to people already eating."",
        ""objectives"": [
          {
            ""id"": ""o_bar"",
            ""text"": ""Get to the noodle bar"",
            ""trigger"": ""reach"",
            ""location"": ""noodle""
          },
          {
            ""id"": ""o_talk"",
            ""text"": ""Take the job"",
            ""trigger"": ""talk"",
            ""dialogue"": ""dlg_kaz""
          }
        ],
        ""next"": ""s_clear""
      },
      {
        ""id"": ""s_clear"",
        ""title"": ""Clear out the Rust Jackals"",
        ""journal"": ""Five of them, Kaz says, and one big enough to count twice. They hold the garage like they paid for it."",
        ""onEnter"": [
          {
            ""type"": ""notify"",
            ""text"": ""Rust Jackals marked — Chrome Cathedral Garage"",
            ""kind"": ""quest""
          }
        ],
        ""objectives"": [
          {
            ""id"": ""o_garage"",
            ""text"": ""Get to the Chrome Cathedral Garage"",
            ""trigger"": ""reach"",
            ""location"": ""garage""
          },
          {
            ""id"": ""o_jackals"",
            ""text"": ""Put down the Rust Jackals"",
            ""trigger"": ""kill"",
            ""tag"": ""jackals"",
            ""count"": 5
          }
        ],
        ""next"": ""s_back""
      },
      {
        ""id"": ""s_back"",
        ""title"": ""Tell Kaz"",
        ""journal"": ""The garage is quiet. Kaz will want to hear it from you, not from the delivery boy."",
        ""objectives"": [
          {
            ""id"": ""o_back"",
            ""text"": ""Get back to the noodle bar"",
            ""trigger"": ""reach"",
            ""location"": ""noodle""
          },
          {
            ""id"": ""o_tell"",
            ""text"": ""Tell Kaz"",
            ""trigger"": ""talk"",
            ""dialogue"": ""dlg_kaz""
          }
        ],
        ""complete"": ""cleared""
      }
    ],
    ""outcomes"": {
      ""cleared"": {
        ""title"": ""Quiet garage"",
        ""summary"": ""Kaz paid in cash and a bowl she said was on the house, then added the bowl to your tab anyway."",
        ""rewards"": {
          ""xp"": 600,
          ""cred"": 2,
          ""money"": 1800
        }
      }
    }
  },
  {
    ""id"": ""q_gig_protection"",
    ""title"": ""Protection"",
    ""type"": ""gig"",
    ""giver"": ""Tin Marlene"",
    ""recommendedLevel"": 4,
    ""summary"": ""Three collectors in matching jackets take a tenth of Tin Marlene's till every week and call it insurance. She wants to know whose they are, and she wants them to stop coming."",
    ""requires"": {
      ""level"": 3
    },
    ""stages"": [
      {
        ""id"": ""s_ask"",
        ""title"": ""Hear it from Marlene"",
        ""journal"": ""Marlene does not usually look worried. She usually looks like she is pricing you."",
        ""objectives"": [
          {
            ""id"": ""o_shop"",
            ""text"": ""Get to the pawn shop"",
            ""trigger"": ""reach"",
            ""location"": ""pawn""
          },
          {
            ""id"": ""o_talk"",
            ""text"": ""Hear Marlene out"",
            ""trigger"": ""talk"",
            ""dialogue"": ""dlg_marlene""
          }
        ],
        ""next"": ""s_collect""
      },
      {
        ""id"": ""s_collect"",
        ""title"": ""Meet the collectors"",
        ""journal"": ""They only come when the shop looks empty. Take a walk, come back, and be the customer they did not expect."",
        ""objectives"": [
          {
            ""id"": ""o_collectors"",
            ""text"": ""Deal with Marlene's collectors"",
            ""trigger"": ""kill"",
            ""tag"": ""collectors"",
            ""count"": 3
          }
        ],
        ""next"": ""s_back""
      },
      {
        ""id"": ""s_back"",
        ""title"": ""Tell Marlene"",
        ""journal"": ""One of them carried a shard with a name on it. Marlene will want the name more than the shard."",
        ""objectives"": [
          {
            ""id"": ""o_back"",
            ""text"": ""Get back to the pawn shop"",
            ""trigger"": ""reach"",
            ""location"": ""pawn""
          },
          {
            ""id"": ""o_tell"",
            ""text"": ""Tell Marlene"",
            ""trigger"": ""talk"",
            ""dialogue"": ""dlg_marlene""
          }
        ],
        ""complete"": ""paid""
      }
    ],
    ""outcomes"": {
      ""paid"": {
        ""title"": ""Insurance"",
        ""summary"": ""Marlene paid in eddies and in counter prices, and asked you never to tell her the name twice."",
        ""rewards"": {
          ""xp"": 500,
          ""cred"": 1,
          ""money"": 1400,
          ""flags"": {
            ""marlene_counter_prices"": true
          }
        }
      }
    }
  }
]";

        /// <summary>New nodes for existing conversations, by conversation id.</summary>
        public const string NodesJson = @"{
  ""dlg_kaz"": {
    ""k_jackals_pitch"": {
      ""text"": ""Something else. Since you are eating my food anyway.\nRust Jackals. Scavengers. They strip a car to the frame in four minutes and sell you back the frame. Last week they ran Sixth Street out of the Chrome Cathedral, and now my delivery boy goes round three blocks or comes back without a scooter."",
      ""options"": [
        {
          ""id"": ""k_o_jackals_how"",
          ""text"": ""How many?"",
          ""to"": ""k_jackals_terms""
        },
        {
          ""id"": ""k_o_jackals_no"",
          ""text"": ""Not my fight."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""k_jackals_terms"": {
      ""text"": ""Five that I have counted. One of them is big enough to count twice.\nEighteen hundred to make the garage quiet again. I do not need them talked to. I need them gone."",
      ""options"": [
        {
          ""id"": ""k_o_jackals_yes"",
          ""text"": ""Garage. Quiet. Done."",
          ""to"": ""k_jackals_accept""
        },
        {
          ""id"": ""k_o_jackals_later"",
          ""text"": ""Later, Kaz."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""k_jackals_accept"": {
      ""text"": ""Go. And eat something first. You fight like a man who skipped breakfast."",
      ""effects"": [
        {
          ""type"": ""startQuest"",
          ""questId"": ""q_gig_jackals""
        },
        {
          ""type"": ""notify"",
          ""text"": ""Gig accepted — Jackal Season"",
          ""kind"": ""quest""
        }
      ],
      ""options"": [
        {
          ""id"": ""k_o_jackals_end"",
          ""text"": ""After the broth."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""k_jackals_back"": {
      ""text"": ""The boy came back with his scooter. He says the garage smells of burnt plastic and nobody is in it.\nThat is the smell of you earning eighteen hundred. Sit. Eat."",
      ""options"": [
        {
          ""id"": ""k_o_jackals_paid"",
          ""text"": ""Pleasure, Kaz."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    }
  },
  ""dlg_marlene"": {
    ""m_protect_pitch"": {
      ""text"": ""I look like a woman who pays rent twice.\nOnce to the landlord. Once to three gentlemen in matching jackets who come by when the shop is empty, take a tenth of the till, and call it insurance. Insurance against them."",
      ""options"": [
        {
          ""id"": ""m_o_protect_who"",
          ""text"": ""Whose are they?"",
          ""to"": ""m_protect_terms""
        },
        {
          ""id"": ""m_o_protect_no"",
          ""text"": ""Not my business."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""m_protect_terms"": {
      ""text"": ""That is the question I would like answered. The answer I would like is that they stop coming.\nFourteen hundred. And my counter prices, for life, which for someone in your line of work is not as generous as it sounds."",
      ""options"": [
        {
          ""id"": ""m_o_protect_yes"",
          ""text"": ""They'll stop coming."",
          ""to"": ""m_protect_accept""
        },
        {
          ""id"": ""m_o_protect_later"",
          ""text"": ""I'll think about it."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""m_protect_accept"": {
      ""text"": ""They will not come while there is a customer in the shop. Take a walk. Come back when the street is empty and the shop looks like easy money."",
      ""effects"": [
        {
          ""type"": ""startQuest"",
          ""questId"": ""q_gig_protection""
        },
        {
          ""type"": ""notify"",
          ""text"": ""Gig accepted — Protection"",
          ""kind"": ""quest""
        }
      ],
      ""options"": [
        {
          ""id"": ""m_o_protect_end"",
          ""text"": ""Easy money."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    },
    ""m_protect_back"": {
      ""text"": ""Nobody came for the till tonight. Somebody came for them, I hear.\nThe shard says they answered to a man who rents an office in the Spire. I will be very polite to the Spire from now on. You get counter prices. For life."",
      ""options"": [
        {
          ""id"": ""m_o_protect_paid"",
          ""text"": ""Pleasure doing business."",
          ""effects"": [
            {
              ""type"": ""endDialogue""
            }
          ]
        }
      ]
    }
  }
}";

        /// <summary>The Data Core guards' objective, put into <i>Ghost in the Rain</i>'s breach stage ahead of the breach.</summary>
        public const string GuardObjectiveJson = @"{
  ""id"": ""o_guards"",
  ""text"": ""Deal with the Data Core's guards"",
  ""trigger"": ""kill"",
  ""tag"": ""corpsec"",
  ""count"": 4
}";

        /// <summary>The set encounters (<see cref="EncounterContent.Build"/>).</summary>
        public const string EncountersJson = @"[
  {
    ""id"": ""enc_garage_jackals"",
    ""tag"": ""jackals"",
    ""place"": ""garage"",
    ""reach"": 16,
    ""members"": [
      ""heavy"",
      ""gunner"",
      ""gunner"",
      ""gunner"",
      ""gunner""
    ],
    ""when"": {
      ""quest"": {
        ""id"": ""q_gig_jackals"",
        ""stage"": ""s_clear""
      }
    }
  },
  {
    ""id"": ""enc_pawn_collectors"",
    ""tag"": ""collectors"",
    ""place"": ""pawn"",
    ""reach"": 10,
    ""arriveBeyond"": 40,
    ""members"": [
      ""heavy"",
      ""gunner"",
      ""gunner""
    ],
    ""when"": {
      ""quest"": {
        ""id"": ""q_gig_protection"",
        ""stage"": ""s_collect""
      }
    }
  },
  {
    ""id"": ""enc_datacore_guards"",
    ""tag"": ""corpsec"",
    ""place"": ""datacore"",
    ""reach"": 16,
    ""members"": [
      ""heavy"",
      ""gunner"",
      ""gunner"",
      ""gunner""
    ],
    ""when"": {
      ""quest"": {
        ""id"": ""q_main_ghost"",
        ""stage"": ""s_datacore""
      }
    }
  }
]";

        static List<JsonObject> _quests, _dialogues;

        /// <summary>Every job, checked.</summary>
        public static IReadOnlyList<JsonObject> Quests
        {
            get
            {
                if (_quests != null) return _quests;
                List<JsonObject> list = Parse(RpgContent.QuestsJson);
                JsonObject stage = Stage(list, "q_main_ghost", "s_datacore");
                var objectives = (List<object>)stage["objectives"];
                int breach = objectives.FindIndex(o => (string)((JsonObject)o)["id"] == "o_breach");
                ((JsonObject)objectives[breach])["after"] = new List<object> { "o_guards" };
                objectives.Insert(breach, Json.Parse(GuardObjectiveJson));
                list.AddRange(Parse(QuestsJson));
                QuestLog.Validate(list);
                return _quests = list;
            }
        }

        /// <summary>Every conversation, checked.</summary>
        public static IReadOnlyList<JsonObject> Dialogues
        {
            get
            {
                if (_dialogues != null) return _dialogues;
                List<JsonObject> list = Parse(RpgContent.DialoguesJson);
                var added = (JsonObject)Json.Parse(NodesJson);
                foreach (KeyValuePair<string, object> kv in added)
                {
                    var nodes = (JsonObject)Graph(list, kv.Key)["nodes"];
                    foreach (KeyValuePair<string, object> n in (JsonObject)kv.Value)
                    {
                        if (nodes.ContainsKey(n.Key)) throw new System.InvalidOperationException($"{kv.Key} already has a node {n.Key}");
                        nodes[n.Key] = n.Value;
                    }
                }
                // Kaz: the second job once the first is taken, and her thanks
                Entry(list, "dlg_kaz", Stage("q_gig_jackals", "s_back"), "k_jackals_back");
                Offer(list, "dlg_kaz", "k_already", "k_o_jackals", "Anything else on the stove?", "q_gig_jackals", 2, "k_jackals_pitch");
                Offer(list, "dlg_kaz", "k_after", "k_o_after_jackals", "Anything else on the stove?", "q_gig_jackals", 2, "k_jackals_pitch");
                // Marlene: the collectors
                Entry(list, "dlg_marlene", Stage("q_gig_protection", "s_back"), "m_protect_back");
                Offer(list, "dlg_marlene", "m_open", "m_o_protect", "You look worried.", "q_gig_protection", 3, "m_protect_pitch");
                DialogueRunner.Validate(list);
                return _dialogues = list;
            }
        }

        static List<JsonObject> Parse(string json) => ((List<object>)Json.Parse(json)).Cast<JsonObject>().ToList();

        static JsonObject Stage(List<JsonObject> quests, string questId, string stageId)
        {
            JsonObject quest = quests.Single(q => (string)q["id"] == questId);
            return ((List<object>)quest["stages"]).Cast<JsonObject>().Single(s => (string)s["id"] == stageId);
        }

        static JsonObject Stage(string questId, string stageId) => new JsonObject { ["quest"] = new JsonObject { ["id"] = questId, ["stage"] = stageId } };

        static JsonObject Graph(List<JsonObject> graphs, string id) => graphs.Single(g => (string)g["id"] == id);

        /// <summary>A conversation opens on <paramref name="node"/> when <paramref name="when"/> holds, ahead of its other entries.</summary>
        static void Entry(List<JsonObject> graphs, string id, JsonObject when, string node)
        {
            JsonObject g = Graph(graphs, id);
            if (!g.TryGetValue("entries", out object e)) g["entries"] = e = new List<object>();
            ((List<object>)e).Insert(0, new JsonObject { ["when"] = when, ["node"] = node });
        }

        /// <summary>A gig offered from a node: shown while the gig is not started, open from <paramref name="level"/>, placed above the node's way out (its last option).</summary>
        static void Offer(List<JsonObject> graphs, string id, string node, string optionId, string text, string questId, int level, string to)
        {
            var options = (List<object>)((JsonObject)((JsonObject)Graph(graphs, id)["nodes"])[node])["options"];
            options.Insert(options.Count - 1, new JsonObject
            {
                ["id"] = optionId, ["text"] = text, ["to"] = to,
                ["condition"] = new JsonObject { ["quest"] = new JsonObject { ["id"] = questId, ["state"] = "inactive" } },
                ["gate"] = new JsonObject { ["level"] = (double)level },
            });
        }
    }
}
