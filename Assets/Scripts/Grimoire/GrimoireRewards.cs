using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// Room-clear reward panel (the game is paused while it is open): take one of 3 random cards
    /// (1-3 or click), Remove a card for gold (4), or Skip (5). Removing shows the whole deck grouped by
    /// card: pick one with its number or a click; 0 / Esc goes back.
    /// </summary>
    public class GrimoireRewards
    {
        public bool IsOpen { get; private set; }
        public bool Removing { get; private set; }
        public bool RemovedThisPanel { get; private set; }
        /// <summary>Opened by a Scroll of Erasure: removal only, no gold cost.</summary>
        public bool IsFree { get; private set; }
        System.Action onFreeRemoved, onFreeCancelled;
        public readonly List<CardDef> Choices = new List<CardDef>();
        public string Message { get; private set; }
        public float MessageTime { get; private set; } = -10f;
        public int ClosedFrame { get; private set; } = -10;

        // Clickable areas, laid out by GrimoireHUD and hit-tested here.
        public readonly List<Rect> ChoiceRects = new List<Rect>();
        public readonly List<Rect> RemoveRects = new List<Rect>();
        public Rect RemoveButton, SkipButton, BackButton;

        GrimoireRun owner;

        public void Open(GrimoireRun g)
        {
            owner = g;
            Choices.Clear();
            var pool = Cards.RewardPool;
            for (int i = 0; i < C.RewardChoices && pool.Count > 0; i++)
            {
                int k = Random.Range(0, pool.Count);
                Choices.Add(pool[k]);
                pool.RemoveAt(k);
            }
            IsOpen = true;
            Removing = false;
            IsFree = false;
            RemovedThisPanel = false;
            Message = null;
            ChoiceRects.Clear();
            RemoveRects.Clear();
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
            Sfx.Play("item", 0.6f, 1.1f);
            g.AddLog("Reward: " + string.Join(", ", Choices.Select(c => c.Name)));
        }

        /// <summary>Multiplayer client: the host offers these cards (the pick goes back to the host).</summary>
        public void OpenOffer(GrimoireRun g, List<CardDef> choices)
        {
            owner = g;
            Choices.Clear();
            Choices.AddRange(choices);
            IsOpen = true;
            Removing = false;
            IsFree = false;
            RemovedThisPanel = false;
            Message = null;
            ChoiceRects.Clear();
            RemoveRects.Clear();
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
            Sfx.Play("item", 0.6f, 1.1f);
        }

        /// <summary>Removal only, free (Scroll of Erasure). False if the deck is already as small as it can be.</summary>
        public bool OpenFreeRemoval(GrimoireRun g, System.Action onRemoved, System.Action onCancelled)
        {
            if (g == null || g.Deck == null || g.Deck.Count <= C.MinDeckSize) return false;
            owner = g;
            Choices.Clear();
            ChoiceRects.Clear();
            RemoveRects.Clear();
            IsOpen = true;
            Removing = true;
            IsFree = true;
            RemovedThisPanel = false;
            Message = null;
            onFreeRemoved = onRemoved;
            onFreeCancelled = onCancelled;
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
            Sfx.Play("item", 0.6f, 0.9f);
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            var cancelled = IsFree && !RemovedThisPanel ? onFreeCancelled : null; // backed out: the scroll is given back
            onFreeRemoved = onFreeCancelled = null;
            IsFree = false;
            IsOpen = false;
            Removing = false;
            cancelled?.Invoke();
            ClosedFrame = Time.frameCount;
            if (GameState.Instance) GameState.Instance.IgnoreInputThisFrame();
            bool forge = SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen;
            if (!AutoTest.Active && !forge && GameState.Instance && GameState.Instance.InRun)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void Update(GrimoireRun g)
        {
            owner = g;
            if (!IsOpen) return;
            bool forge = SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen;
            if (forge) return; // the Forge was opened on top: it has the input
            if (!AutoTest.Active && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

            if (Removing)
            {
                for (int i = 0; i < 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { RemoveGroup(i); return; }
                if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) Back();
            }
            else
            {
                for (int i = 0; i < Choices.Count; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Take(i); return; }
                if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) StartRemove();
                if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) { Skip(); return; }
            }
            if (Input.GetMouseButtonDown(0)) Click(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        /// <summary>Handles a click at a GUI position (also a test hook).</summary>
        public void Click(Vector2 p)
        {
            if (!IsOpen) return;
            if (Removing)
            {
                for (int i = 0; i < RemoveRects.Count; i++)
                    if (RemoveRects[i].Contains(p)) { RemoveGroup(i); return; }
                if (BackButton.Contains(p)) Back();
                return;
            }
            for (int i = 0; i < ChoiceRects.Count; i++)
                if (ChoiceRects[i].Contains(p)) { Take(i); return; }
            if (RemoveButton.Contains(p)) { StartRemove(); return; }
            if (SkipButton.Contains(p)) Skip();
        }

        public void Take(int i)
        {
            if (i < 0 || i >= Choices.Count || owner == null) return;
            var def = Choices[i];
            if (Net.NetGrimoire.ClientMirror) Net.NetGrimoire.SendPick(i, null, false);
            else owner.Deck.AddToDiscard(def);
            owner.AddLog("Took " + def.Name);
            Sfx.Play("pickup", 0.7f, 1.1f);
            Close();
        }

        public void Skip()
        {
            owner?.AddLog("Skipped the reward");
            if (Net.NetGrimoire.ClientMirror) Net.NetGrimoire.SendPick(-1, null, true);
            Sfx.Play("tick", 0.5f, 0.9f);
            Close();
        }

        /// <summary>Why removing isn't possible right now, or null.</summary>
        public string RemoveBlocked()
        {
            if (RemovedThisPanel) return "Already removed a card here";
            if (owner == null || owner.Deck == null) return "No deck";
            if (owner.Deck.Count <= C.MinDeckSize) return $"The deck can't go below {C.MinDeckSize} cards";
            if (IsFree) return null;
            if (!Run.Instance || Run.Instance.Gold < C.RemoveCost) return $"Need {C.RemoveCost} gold";
            return null;
        }

        public void StartRemove()
        {
            var why = RemoveBlocked();
            if (why != null) { Say(why); Sfx.Play("fizzle", 0.4f, 1.3f); return; }
            Removing = true;
            RemoveRects.Clear();
            Sfx.Play("tick", 0.5f, 1.2f);
        }

        public void Back()
        {
            if (IsFree) { Close(); return; }
            Removing = false;
            Sfx.Play("tick", 0.4f, 0.9f);
        }

        /// <summary>Removes one card of the i-th group of the deck (grouped by card).</summary>
        public bool RemoveGroup(int i)
        {
            if (!Removing || owner == null) return false;
            var groups = owner.Deck.Grouped();
            if (i < 0 || i >= groups.Count) return false;
            var def = groups[i].def;
            if (Net.NetGrimoire.ClientMirror)
            {
                // The host removes it (and takes the gold); the deck comes back from it.
                Net.NetGrimoire.SendPick(-1, def.Id, false);
                RemovedThisPanel = true;
                Removing = false;
                Sfx.Play("buy", 0.7f);
                Say($"Removing {def.Name}...");
                return true;
            }
            if (!owner.Deck.Remove(def)) return false;
            RemovedThisPanel = true;
            Removing = false;
            owner.AddLog("Removed " + def.Name);
            Sfx.Play("buy", 0.7f);
            if (IsFree)
            {
                onFreeRemoved?.Invoke();
                if (Run.Instance) Run.Instance.Toast("Erased", $"{def.Name} is gone from your Grimoire", def.Color);
                Close();
                return true;
            }
            Run.Instance.Gold -= C.RemoveCost;
            Say($"Removed {def.Name} (-{C.RemoveCost} gold)");
            return true;
        }

        void Say(string s)
        {
            Message = s;
            MessageTime = Time.unscaledTime;
        }
    }
}
