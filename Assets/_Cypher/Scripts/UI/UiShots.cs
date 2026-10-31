using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Cypher.Core;
using Cypher.Game;
using Cypher.Rpg;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher.UI
{
    /// <summary>
    /// <c>--shots DIR</c>: in a built player, walks through every menu screen and saves a screenshot of each, then quits. It drives the
    /// menus through their own controls (the same code a click runs) and presses the keys for a rebind on a virtual keyboard, so the
    /// pictures show the real screens, fonts and layout at the real window size, which an Editor test run cannot. Exit code 0 when it
    /// finished, 1 on a timeout, 2 on an exception. Does nothing without the switch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiShots : MonoBehaviour
    {
        /// <summary>Time for fades, layout and focus to settle before each picture.</summary>
        const float SettleSeconds = 0.45f;

        const float WatchdogSeconds = 300f;

        [SerializeField] UiRoot ui;
        [SerializeField] GameFlow flow;
        [SerializeField] SettingsService settings;

        ShotsOptions _options;
        readonly StringBuilder _taken = new StringBuilder();
        Keyboard _keyboard;
        float _started;
        bool _finished;

        public void Configure(UiRoot uiRoot, GameFlow gameFlow, SettingsService settingsService)
        {
            ui = uiRoot;
            flow = gameFlow;
            settings = settingsService;
        }

        void Awake()
        {
            _options = ShotsOptions.Parse(Environment.GetCommandLineArgs());
            if (_options == null || Application.isEditor)
            {
                enabled = false;
                return;
            }
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy() => Application.logMessageReceived -= OnLog;

        IEnumerator Start()
        {
            if (_options == null || Application.isEditor) yield break;
            _started = Time.realtimeSinceStartup;
            Directory.CreateDirectory(_options.Directory);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return Walk();
        }

        void Update()
        {
            if (!_finished && Time.realtimeSinceStartup - _started > WatchdogSeconds && _started > 0)
            {
                Debug.LogError("[Cypher] the screenshot walk timed out");
                Finish(1);
            }
        }

        IEnumerator Walk()
        {
            // The loading screen is the first thing seen, so it is taken before anything else has happened.
            yield return null;
            yield return Shot("01_loading");

            yield return WaitFor(() => flow.State == FlowState.Title && !ui.Loading.IsShown, "the title screen");
            yield return Shot("02_title");

            ui.Title.Options.Activate();
            yield return Settle();
            yield return Shot("03_options_graphics");
            ui.Options.SelectTab(1);
            yield return Shot("04_options_audio");
            ui.Options.SelectTab(2);
            yield return Shot("05_options_gameplay");
            ui.Options.SelectTab(3);
            yield return Shot("06_options_controls");

            // A key prompt, then a key another action already uses (the conflict question), then a free one.
            ui.Options.Controls.Button(GameAction.Jump, BindingSlot.Primary).Activate();
            yield return Shot("07_rebind_prompt");
            PressVirtual(Key.W);
            yield return WaitFor(() => ui.Top == UiScreen.Conflict, "the conflict question");
            yield return Shot("08_rebind_conflict");
            ui.Dialog.Buttons[1].Activate();   // CANCEL
            yield return Settle();

            ui.Options.Controls.Button(GameAction.Jump, BindingSlot.Primary).Activate();
            yield return Settle();
            PressVirtual(Key.Y);   // a free key (J is the journal's, K the character sheet's)
            yield return WaitFor(() => ui.Top == UiScreen.Options, "the options after a rebind");
            ui.Options.Controls.Button(GameAction.Sprint, BindingSlot.Secondary).Activate();
            yield return Settle();
            ui.Dialog.Buttons[0].Activate();   // CLEAR THIS BINDING
            yield return Settle();
            yield return Shot("09_controls_changed");

            ui.Options.ResetButton.Activate();   // RESET ALL CONTROLS, so the walk leaves no trace
            ui.OnBack(fromBButton: false);
            yield return Settle();

            ui.Title.Quit.Activate();
            yield return Shot("10_confirm_quit");
            ui.Dialog.Buttons[1].Activate();   // NO
            yield return Settle();

            flow.NewGame();
            yield return WaitFor(() => flow.State == FlowState.Playing, "the game");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("11_playing");
            yield return HudShots();
            flow.Pause();
            yield return Shot("12_pause");
            ui.Pause.ReturnToTitle.Activate();
            yield return Shot("13_confirm_title");
            ui.Dialog.Buttons[1].Activate();   // NO
            yield return Settle();
            ui.ShowNotice("Quality lowered from High to Medium because the frame rate was about 24 FPS. You can raise it in Options.");
            yield return Shot("14_notice");

            File.WriteAllText(Path.Combine(_options.Directory, "shots.txt"), _taken.ToString());
            Finish(0);
        }

        /// <summary>The HUD with notices and a hit, the way to the tracked job, the E prompt at the noodle bar, and a place's marker up close.</summary>
        IEnumerator HudShots()
        {
            RpgService rpg = FindAnyObjectByType<RpgService>() ?? throw new InvalidOperationException("the shots need the RPG");
            RpgCore core = rpg.Core;
            Transform player = rpg.Player.transform;

            void Stand(Vector3 at, double faceX, double faceZ)
            {
                float yaw = (float)(Math.Atan2(faceX - at.x, faceZ - at.z) * 180 / Math.PI);
                rpg.Player.Teleport(at, yaw);
                Physics.SyncTransforms();
            }

            core.AddMoney(250, "found");
            core.AddXp(120, "shots");
            core.GiveItem("c_maxdoc");
            core.Damage(30);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("11a_hud_notices");

            JsonObject tracked = HudModel.TrackedMarker(core.Quests.Markers()) ?? throw new InvalidOperationException("a new game tracks the main job");
            Stand(player.position, (double)tracked["x"], (double)tracked["z"]);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11b_hud_towards_job");

            RpgLocation noodle = core.GetLocation("noodle");
            float y = player.position.y + 0.3f;
            Stand(new Vector3((float)noodle.X, y, (float)noodle.Z), noodle.X, noodle.Z + 10);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11c_hud_prompt");

            RpgLocation plaza = core.GetLocation("plaza");
            Vector3 back = new Vector3((float)(plaza.X - noodle.X), 0, (float)(plaza.Z - noodle.Z)).normalized * 14;
            Stand(new Vector3((float)noodle.X + back.x, y, (float)noodle.Z + back.z), noodle.X, noodle.Z);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11d_marker_close");

            // the scanner, held on the virtual keyboard
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.F));
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("11e_scanner");
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return new WaitForSecondsRealtime(0.6f);

            core.StartDialogue("dlg_kaz");
            yield return null;
            ui.Conversation.Skip();
            yield return Shot("11f_conversation");
            ((ChoiceButton)ui.Conversation.Buttons.First(b => b is ChoiceButton c && !c.Choice.Locked)).Activate();
            yield return null;
            ui.Conversation.Skip();
            yield return Shot("11f2_conversation_answered");
            ui.OnBack(fromBButton: false);
            yield return Settle();

            core.StartBreach("market_terminal");
            yield return null;
            GridCell first = core.Breach.S.SolutionPath[0], second = core.Breach.S.SolutionPath[1];
            ui.Breach.Cells[first.R][first.C].Activate();
            ui.Breach.Cells[second.R][second.C].Activate();
            yield return Shot("11g_breach");
            foreach (GridCell p in core.Breach.S.SolutionPath.Skip(2).ToList())
            {
                if (core.Breach.Done) break;
                ui.Breach.Cells[p.R][p.C].Activate();
            }
            yield return new WaitForSecondsRealtime(0.4f);   // the outcome is up for a moment before the screen closes
            yield return ShotNow("11g2_breach_complete");
            yield return WaitFor(() => ui.Top == UiScreen.None, "the breach screen to close");
            yield return Settle();

            core.AddXp(5000);
            core.GiveItem("w_pistol", "epic");
            ui.ToggleMenu(MenuTab.Inventory);
            yield return null;
            var inventory = (InventoryPage)ui.Menu.Page(MenuTab.Inventory);
            inventory.Tiles[inventory.Tiles.Count - 1].Focus();   // the new gun, compared with the worn one
            yield return Shot("11h_inventory");
            ui.ToggleMenu(MenuTab.Character);
            yield return Shot("11i_character");
            ui.ToggleMenu(MenuTab.Journal);
            yield return Shot("11j_journal");
            ui.OnBack(fromBButton: false);
            yield return Settle();

            core.OpenVendor("v_kaz");
            yield return Shot("11k_vendor");
            ui.OnBack(fromBButton: false);
            yield return Settle();

            yield return GunShots(rpg);
        }

        /// <summary>The guns: at the hip, firing down the plaza (the flash, tracers, sparks, holes in the ground), aimed, mid-reload, the revolver, the rail charging and firing.</summary>
        IEnumerator GunShots(RpgService rpg)
        {
            var guns = FindAnyObjectByType<Cypher.Combat.WeaponSystem>() ?? throw new InvalidOperationException("the shots need the guns");
            var still = new WeaponBody { Grounded = true };
            RpgLocation plaza = rpg.Core.GetLocation("plaza"), noodle = rpg.Core.GetLocation("noodle");
            float yaw = (float)(Math.Atan2(noodle.X - plaza.X, noodle.Z - plaza.Z) * 180 / Math.PI);
            rpg.Player.Teleport(new Vector3((float)plaza.X, rpg.Player.transform.position.y + 0.3f, (float)plaza.Z), yaw);
            rpg.Player.Look.Set(rpg.Player.Look.Yaw, -0.18);
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot("11l_gun_hip");

            // one step a frame, as play would; the picture is taken in the frame of a shot, while its flash is up
            for (int i = 0; i < 24; i++)
            {
                guns.Step(new WeaponInput { Acting = true, FireHeld = true }, still);
                yield return null;
            }
            guns.Step(new WeaponInput { Acting = true, FireHeld = true }, still);
            yield return ShotNow("11m_gun_fire");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11n_gun_holes");

            for (int i = 0; i < 40; i++)
            {
                guns.Step(new WeaponInput { Acting = true, AimHeld = true }, still);
                yield return null;
            }
            yield return ShotNow("11o_gun_ads");

            guns.Step(new WeaponInput { Acting = true, ReloadPressed = true }, still);
            for (int i = 0; i < 70; i++)
            {
                guns.Step(new WeaponInput { Acting = true }, still);
                if (i % 2 == 0) yield return null;
            }
            yield return ShotNow("11p_gun_reload");
            for (int i = 0; i < 80; i++) guns.Step(new WeaponInput { Acting = true }, still);

            guns.Step(new WeaponInput { Acting = true, Select = 2 }, still);
            for (int i = 0; i < 50; i++)
            {
                guns.Step(new WeaponInput { Acting = true }, still);
                yield return null;
            }
            yield return Shot("11q_revolver");
            guns.Step(new WeaponInput { Acting = true, FireHeld = true, FireClicked = true }, still);
            yield return ShotNow("11r_revolver_fire");
            yield return new WaitForSecondsRealtime(0.6f);

            guns.Step(new WeaponInput { Acting = true, Select = 3 }, still);
            for (int i = 0; i < 50; i++)
            {
                guns.Step(new WeaponInput { Acting = true }, still);
                yield return null;
            }
            for (int i = 0; i < 50; i++)
            {
                guns.Step(new WeaponInput { Acting = true, FireHeld = true }, still);
                yield return null;
            }
            yield return ShotNow("11s_rail_charged");
            guns.Step(new WeaponInput { Acting = true }, still);
            yield return ShotNow("11t_rail_fire");
            yield return new WaitForSecondsRealtime(0.5f);

            guns.Step(new WeaponInput { Acting = true, Select = 1 }, still);
            for (int i = 0; i < 50; i++)
            {
                guns.Step(new WeaponInput { Acting = true }, still);
                yield return null;
            }
            yield return FightShots(rpg);
        }

        /// <summary>
        /// A fight on the plaza: five in cover ahead, fire coming in (tracers, hit arcs), the health bar over the one aimed at, the hack deck with a target, the squad
        /// pinged, a Short Circuit landing, one of them down, the flatline screen and waking at Doc Vey's.
        /// </summary>
        IEnumerator FightShots(RpgService rpg)
        {
            var fight = FindAnyObjectByType<Cypher.Fight.EnemyController>() ?? throw new InvalidOperationException("the shots need the fights");
            RpgCore core = rpg.Core;
            Transform player = rpg.Player.transform;
            fight.PatrolsOn = false;
            fight.Patrols.Reset();
            void KeepStanding() => core.Heal(core.Derived["maxHealth"]);
            void LookAt(Vector3 target)
            {
                Vector3 eye = Camera.main.transform.position, d = target - eye;
                rpg.Player.Look.Set(Math.Atan2(d.x, d.z), Math.Atan2(d.y, Math.Sqrt(d.x * d.x + d.z * d.z)));
            }
            Vector3 Chest(Enemy e) => new Vector3((float)e.Position.X, (float)e.Position.Y + Cypher.Fight.EnemyController.ChestHeight, (float)e.Position.Z);

            // five facing the player across the plaza
            Vector3 forward = new Vector3(player.forward.x, 0, player.forward.z).normalized, right = Vector3.Cross(Vector3.up, forward);
            Vector3 feet = player.position;
            var members = new (EnemyArchetype, Vector3)[5];
            for (int m = 0; m < 5; m++) members[m] = (m == 0 ? EnemyTable.Heavy : EnemyTable.Gunner, feet + forward * (m == 0 ? 11f : 14f) + right * ((m - 2) * 3f));
            EnemySquad squad = fight.Spawn("shots", members, yaw: rpg.Player.Look.Yaw + Math.PI, hostile: true);
            Enemy heavy = squad.All[0];
            for (float t = 0; t < 2.5f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                yield return null;
            }
            yield return ShotNow("11u_fight_squad");

            bool hit = false;
            Action<Vector3, double> onHit = (from, damage) => hit = true;
            fight.PlayerHit += onHit;
            yield return WaitFor(() =>
            {
                KeepStanding();
                return hit;
            }, "a shot to hit the player");
            fight.PlayerHit -= onHit;
            yield return ShotNow("11v_fight_incoming");

            Enemy aimed = squad.All[1];
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                LookAt(Chest(aimed));
                yield return null;
            }
            yield return ShotNow("11w_fight_health_bar");

            for (int i = 0; i < core.Inventory.DeckSize; i++) core.Inventory.UnequipHack(i);
            string[] hacks = { "qh_ping", "qh_short_circuit" };
            for (int i = 0; i < hacks.Length; i++)
            {
                RpgResult given = core.GiveItem(hacks[i]);
                if (!given.Ok) throw new InvalidOperationException($"the shots could not give {hacks[i]}: {given.Reason}");
                Item item = given.Item ?? core.Inventory.Items.Last(it => it.TemplateId == hacks[i]);
                if (!core.Inventory.EquipHack(item.Uid, i).Ok) throw new InvalidOperationException($"the shots could not slot {hacks[i]}");
            }
            core.Deck.SetMaxRam(40);
            core.Deck.Fill();
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.F));
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                LookAt(Chest(heavy));
                yield return null;
            }
            yield return ShotNow("11x_fight_deck");
            RpgResult ping = fight.UploadHack();
            if (!ping.Ok) throw new InvalidOperationException($"the shots' Ping did not go: {ping.Reason}");
            fight.SelectHack(1);
            for (float t = 0; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                LookAt(Chest(heavy));
                yield return null;
            }
            RpgResult shock = fight.UploadHack();
            if (!shock.Ok) throw new InvalidOperationException($"the shots' Short Circuit did not go: {shock.Reason}");
            yield return null;
            yield return ShotNow("11y_fight_short_circuit");
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());

            rpg.Player.Look.Set(rpg.Player.Look.Yaw + 0.6, 0);
            for (float t = 0; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                yield return null;
            }
            yield return ShotNow("11z_fight_pinged");

            Enemy fallen = squad.All[2];
            fight.Sim.Harm(fallen, 1e6);
            rpg.Kill(fallen.Archetype, fallen.Squad.Tag, new Vector3((float)fallen.Position.X, (float)fallen.Position.Y, (float)fallen.Position.Z));
            for (float t = 0; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                KeepStanding();
                LookAt(new Vector3((float)fallen.Position.X, (float)fallen.Position.Y + 0.3f, (float)fallen.Position.Z));
                yield return null;
            }
            yield return ShotNow("11z1_fight_body_down");

            rpg.HurtPlayer(1e6, "shots");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return ShotNow("11z2_flatline");
            yield return WaitFor(() => !fight.Downed, "the Trauma Team");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("11z3_trauma_team");
        }

        /// <summary>A picture this frame, without waiting for things to settle (a muzzle flash lasts a few frames).</summary>
        IEnumerator ShotNow(string name)
        {
            string path = Path.Combine(_options.Directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            _taken.Append(name).Append(".png  ").Append(ui.Top).Append("  ").Append(Screen.width).Append('x').Append(Screen.height).AppendLine();
        }

        IEnumerator Settle() => new WaitForSecondsRealtime(SettleSeconds);

        IEnumerator Shot(string name)
        {
            yield return Settle();
            string path = Path.Combine(_options.Directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            _taken.Append(name).Append(".png  ").Append(ui.Top).Append("  ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
        }

        IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) throw new TimeoutException($"Waited 30 s for {what}.");
                yield return null;
            }
            yield return Settle();
        }

        void PressVirtual(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
            StartCoroutine(ReleaseSoon());
        }

        IEnumerator ReleaseSoon()
        {
            yield return new WaitForSecondsRealtime(0.15f);
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception && !_finished) Finish(2);
        }

        void Finish(int code)
        {
            if (_finished) return;
            _finished = true;
            Application.Quit(code);
        }
    }
}
