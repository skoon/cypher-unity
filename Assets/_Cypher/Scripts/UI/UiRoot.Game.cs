using System;
using System.Collections.Generic;
using Cypher.Actors;
using Cypher.Core;
using Cypher.Game;
using UnityEngine;

namespace Cypher.UI
{
    /// <summary>
    /// The game's own screens over play: the conversation, the breach, the vendor and the menu (inventory, character, journal). The RPG's events open and close the
    /// first three; the menu keys open the menu. While one is open the flow holds the world (<see cref="FlowModal"/>): a menu or a vendor stops time, a conversation
    /// or a breach only stops the player. Esc leaves the screen, telling the RPG (ending the conversation, cancelling the breach, closing the shop). A pause keeps the
    /// screen; resuming brings it back.
    /// </summary>
    public sealed partial class UiRoot
    {
        ConversationScreen _conversation;
        BreachScreen _breach;
        GameMenuScreen _menu;
        VendorScreen _vendor;
        ScannerOverlay _scanner;
        List<ScanContact> _places;
        RpgCore _placesOf;

        /// <summary>The game screen open now, or None; kept across a pause.</summary>
        UiScreen _gameScreen = UiScreen.None;

        /// <summary>A screen the RPG asked for while another was open (a vendor opened by an answer, just before the conversation ends).</summary>
        UiScreen _pendingScreen = UiScreen.None;

        public ConversationScreen Conversation => _conversation;

        public BreachScreen Breach => _breach;

        public GameMenuScreen Menu => _menu;

        public VendorScreen Vendor => _vendor;

        public ScannerOverlay Scanner => _scanner;

        /// <summary>The guns, for the HUD's weapon panel, crosshair and hit marker; optional.</summary>
        public void ConfigureGuns(Cypher.Combat.WeaponSystem weaponSystem) => guns = weaponSystem;

        /// <summary>The fights, for the HUD's hit arcs, enemy health bar and flatline screen; optional.</summary>
        public void ConfigureFight(Cypher.Fight.EnemyController enemies) => fight = enemies;

        /// <summary>The people and the traffic, for the scanner's contacts; optional (a scene without them scans the places only).</summary>
        public void ConfigureScanner(CrowdController crowdController, TrafficController trafficController)
        {
            crowd = crowdController;
            traffic = trafficController;
        }

        /// <summary>The game screen open now, or None.</summary>
        public UiScreen GameScreen => _gameScreen;

        static bool IsGameScreen(UiScreen s) => s == UiScreen.GameMenu || s == UiScreen.Vendor || s == UiScreen.Dialogue || s == UiScreen.Breach;

        static FlowModal ModalFor(UiScreen s) => s == UiScreen.Dialogue || s == UiScreen.Breach ? FlowModal.Interaction : FlowModal.Menu;

        void BuildGameScreens()
        {
            _conversation = new ConversationScreen();
            _conversation.AnswerChosen += id =>
            {
                RpgResult r = rpg.Core.ChooseDialogueOption(id);
                if (!r.Ok) throw new InvalidOperationException($"the conversation refused answer '{id}': {r.Reason}");
            };
            _conversation.LeaveChosen += () => CloseGameScreen(UiScreen.Dialogue);
            _conversation.SetShown(false);
            _root.Add(_conversation.Root);

            _breach = new BreachScreen();
            _breach.CellPicked += (r, c) =>
            {
                RpgResult res = rpg.Core.BreachSelect(r, c);
                if (!res.Ok) throw new InvalidOperationException($"the breach refused the live cell {r},{c}: {res.Reason}");
            };
            _breach.Finished += () => CloseGameScreen(UiScreen.Breach);
            _breach.SetShown(false);
            _root.Add(_breach.Root);

            var inventory = new InventoryPage();
            var character = new CharacterPage();
            inventory.Refused += OnNotice;
            character.Refused += OnNotice;
            _menu = new GameMenuScreen(inventory, character, new JournalPage());
            _menu.TabChosen += ShowMenuTab;
            _menu.SetShown(false);
            _root.Add(_menu.Root);

            _scanner = new ScannerOverlay();
            _root.Add(_scanner.Root);   // over the HUD, under the game's screens

            _vendor = new VendorScreen();
            _vendor.Refused += OnNotice;
            _vendor.SetShown(false);
            _root.Add(_vendor.Root);
            rpg.Event += OnRpgEvent;
        }

        void DetachGameScreens()
        {
            if (rpg != null) rpg.Event -= OnRpgEvent;
        }

        void OnRpgEvent(string type, JsonObject payload)
        {
            switch (type)
            {
                case "rpg:dialogueStart":
                    OpenGameScreen(UiScreen.Dialogue);
                    break;
                case "rpg:dialogueNode":
                    _conversation.Show(payload);
                    break;
                case "rpg:hackStart":
                    OpenGameScreen(UiScreen.Breach);
                    _breach.Begin(payload);
                    break;
                case "rpg:hackSelect":
                case "rpg:hackEnd":
                    // on the last pick the end comes before the pick: both redraw from the core
                    if (_gameScreen == UiScreen.Breach && rpg.Core.GetBreach() is JsonObject session) _breach.Sync(session);
                    break;
                case "rpg:vendorOpened":
                    _vendor.Open(rpg.Core, (string)payload["id"]);
                    OpenGameScreen(UiScreen.Vendor);
                    break;
                case "rpg:vendorClosed":
                    CloseGameScreen(UiScreen.Vendor);
                    break;
                case "rpg:dialogueEnd":
                    if (_gameScreen != UiScreen.Dialogue) break;
                    // keep the last line to read, unless the conversation handed over to another screen
                    if (_pendingScreen != UiScreen.None || !_conversation.End()) CloseGameScreen(UiScreen.Dialogue);
                    break;
            }
        }

        /// <summary>Open a game screen over play; one asked for while another is open waits for that one to close.</summary>
        void OpenGameScreen(UiScreen screen)
        {
            if (_gameScreen == screen) return;
            if (_gameScreen != UiScreen.None)
            {
                _pendingScreen = screen;
                return;
            }
            if (!_stack.TryPush(screen)) throw new InvalidOperationException($"the {screen} screen cannot open over {_stack.Top}");
            _gameScreen = screen;
            if (!flow.SetModal(ModalFor(screen))) throw new InvalidOperationException($"the flow refused the {screen} screen in {flow.State}");
            UiSound.Raise(UiSound.Open);
            Refresh();
        }

        /// <summary>Close a game screen (only if it is the one open) and open whatever was waiting.</summary>
        void CloseGameScreen(UiScreen screen)
        {
            if (_gameScreen != screen) return;
            _gameScreen = UiScreen.None;
            if (_stack.Top == screen) _stack.Pop();
            flow.SetModal(FlowModal.None);
            UiSound.Raise(UiSound.Close);
            UiScreen next = _pendingScreen;
            _pendingScreen = UiScreen.None;
            if (next != UiScreen.None) OpenGameScreen(next);
            else Refresh();
        }

        /// <summary>Esc on a game screen: leave it, and tell the RPG.</summary>
        void LeaveGameScreen()
        {
            switch (_gameScreen)
            {
                case UiScreen.Dialogue:
                    // the RPG answers with the end of the conversation, which closes the screen; a conversation already over just closes
                    if (rpg.Core != null && rpg.Core.Dialogue.Active) rpg.Core.EndDialogue();
                    CloseGameScreen(UiScreen.Dialogue);
                    break;
                case UiScreen.Vendor:
                    // the core closes the shop and answers with vendorClosed, which closes the screen
                    if (rpg.Core != null && rpg.Core.Economy.OpenVendorId != null) rpg.Core.CloseVendor();
                    CloseGameScreen(UiScreen.Vendor);
                    break;
                case UiScreen.Breach:
                    // aborting: the core ends the breach (keeping any daemon already in), and the screen goes at once
                    if (rpg.Core != null && rpg.Core.Breach != null && !rpg.Core.Breach.Done) rpg.Core.CancelBreach();
                    CloseGameScreen(UiScreen.Breach);
                    break;
                default:
                    CloseGameScreen(_gameScreen);
                    break;
            }
        }

        /// <summary>The flow changed: a pause hid the game screen, resuming puts it back; the title or a new game ends it.</summary>
        void FollowFlowForGameScreens(FlowTransition t)
        {
            if (_gameScreen == UiScreen.None) return;
            if (t.To == FlowState.Playing && !t.NewSession && flow.Modal != FlowModal.None)
            {
                _stack.TryPush(_gameScreen);
            }
            else if (t.To != FlowState.Paused)
            {
                _gameScreen = UiScreen.None;
                _pendingScreen = UiScreen.None;
            }
        }

        /// <summary>Open the game menu on a tab (Tab, K and J while playing), switch tabs while it is open, or close it with the key of the tab showing.</summary>
        public void ToggleMenu(MenuTab tab)
        {
            if (_gameScreen == UiScreen.GameMenu)
            {
                if (_menu.Tab == tab) CloseGameScreen(UiScreen.GameMenu);
                else ShowMenuTab(tab);
                return;
            }
            if (_gameScreen != UiScreen.None || _stack.Top != UiScreen.None || rpg.Core == null) return;
            _menu.ShowTab(tab);
            RefreshMenu();
            OpenGameScreen(UiScreen.GameMenu);
        }

        void ShowMenuTab(MenuTab tab)
        {
            if (_menu.Tab == tab) return;
            _menu.ShowTab(tab);
            RefreshMenu();
            UiSound.Raise(UiSound.Select);
            FocusSoon();
        }

        void RefreshMenu()
        {
            Vector3 p = rpg.Player.transform.position;
            _menu.Refresh(rpg.Core, p.x, p.z);
        }

        void ReadMenuKeys()
        {
            if (rpg?.Input == null || flow.State != FlowState.Playing || IsCapturing) return;
            if (rpg.Input.WasPressed(GameAction.Inventory)) ToggleMenu(MenuTab.Inventory);
            else if (rpg.Input.WasPressed(GameAction.Character)) ToggleMenu(MenuTab.Character);
            else if (rpg.Input.WasPressed(GameAction.Journal)) ToggleMenu(MenuTab.Journal);
        }

        /// <summary>Everything the scanner can tag now: the places, the people walking and the vehicles driving.</summary>
        IEnumerable<ScanContact> ScanContacts()
        {
            if (_placesOf != rpg.Core)
            {
                _placesOf = rpg.Core;
                _places = ScanModel.Places(rpg.Core);
            }
            foreach (ScanContact p in _places) yield return p;
            if (crowd != null && crowd.Sim != null)
            {
                foreach (var person in crowd.Sim.Active) yield return ScanModel.Person(person.X, person.Y, person.Z);
            }
            if (traffic != null && traffic.Sim != null)
            {
                foreach (var v in traffic.Sim.Vehicles) yield return ScanModel.Vehicle(v.Type?.Key, v.X, v.Y, v.Z);
            }
            if (fight != null && fight.Sim != null)
            {
                Item hack = fight.HackSlot >= 0 ? rpg.Core.Inventory.DeckItems()[fight.HackSlot] : null;
                foreach (Enemy e in fight.Sim.Enemies)
                {
                    if (e.State == EnemyState.Down) continue;
                    bool target = e == fight.HackTarget && hack != null;
                    yield return ScanModel.Enemy(e, target ? hack.Name : null, target ? rpg.Core.Deck.CostOf(hack.Hack, rpg.Core.HackDiscount) : 0);
                }
            }
        }

        void UpdateScanner(double dt)
        {
            bool held = flow.State == FlowState.Playing && _gameScreen == UiScreen.None && rpg.Core != null && rpg.Input != null && rpg.Input.IsPressed(GameAction.Scan);
            _scanner.Update(held, dt, gameCamera, ScanContacts);
        }

        void UpdateGameScreens(double dt)
        {
            ReadMenuKeys();
            UpdateScanner(dt);
            if (_conversation != null && _stack.Top == UiScreen.Dialogue) _conversation.Update(dt);
            if (_breach != null && _stack.Top == UiScreen.Breach && rpg.Core?.Breach != null) _breach.Update(rpg.Core.Breach.S.TimeLeft, dt);
        }

        void ShowGameScreens()
        {
            if (_conversation == null) return;
            _conversation.SetShown(_stack.Top == UiScreen.Dialogue);
            _breach.SetShown(_stack.Top == UiScreen.Breach);
            _menu.SetShown(_stack.Top == UiScreen.GameMenu);
            _vendor.SetShown(_stack.Top == UiScreen.Vendor);
        }
    }
}
