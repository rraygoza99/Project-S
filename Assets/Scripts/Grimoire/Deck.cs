using System;
using System.Collections.Generic;
using System.Linq;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// Draw pile, hand and discard pile. Every cast plays the leftmost hand card (Next): it goes to the
    /// discard pile, the hand shifts left and one card is drawn. An empty draw pile is refilled by
    /// shuffling the discard pile into it.
    /// </summary>
    public class Deck
    {
        public readonly List<Card> DrawPile = new List<Card>();
        public readonly List<Card> Hand = new List<Card>();
        public readonly List<Card> DiscardPile = new List<Card>();

        public enum Change { Draw, Play, Cycle, Reshuffle, Added, Removed, Cleared }
        /// <summary>Raised for each change (the HUD animates them). The card is null for whole-deck changes.</summary>
        public event Action<Change, Card> Changed;

        public int Reshuffles { get; private set; }
        public Card Next => Hand.Count > 0 ? Hand[0] : null;
        public int Count => DrawPile.Count + Hand.Count + DiscardPile.Count;
        public IEnumerable<Card> AllCards => Hand.Concat(DrawPile).Concat(DiscardPile);

        static int HandSize => Math.Max(1, GrimoireConfig.HandSize);

        /// <summary>Starts a fresh deck: these cards, shuffled, then a full hand drawn.</summary>
        public void Build(IEnumerable<string> ids)
        {
            DrawPile.Clear();
            Hand.Clear();
            DiscardPile.Clear();
            foreach (var id in ids)
            {
                var def = Cards.Get(id);
                if (def != null) DrawPile.Add(new Card(def));
            }
            Shuffle(DrawPile);
            Fill();
        }

        /// <summary>Loading a save: the three piles exactly as they were (no shuffle, no draw).</summary>
        public void Restore(IEnumerable<string> draw, IEnumerable<string> hand, IEnumerable<string> discard)
        {
            DrawPile.Clear();
            Hand.Clear();
            DiscardPile.Clear();
            void Fill(List<Card> pile, IEnumerable<string> ids)
            {
                foreach (var id in ids) { var def = Cards.Get(id); if (def != null) pile.Add(new Card(def)); }
            }
            Fill(DrawPile, draw);
            Fill(Hand, hand);
            Fill(DiscardPile, discard);
        }

        /// <summary>Draws until the hand is full or no cards are left anywhere.</summary>
        public void Fill()
        {
            while (Hand.Count < HandSize && DrawOne()) { }
        }

        bool DrawOne()
        {
            if (DrawPile.Count == 0)
            {
                if (DiscardPile.Count == 0) return false;
                Reshuffle();
            }
            var c = DrawPile[DrawPile.Count - 1];
            DrawPile.RemoveAt(DrawPile.Count - 1);
            Hand.Add(c);
            Changed?.Invoke(Change.Draw, c);
            return true;
        }

        /// <summary>Shuffles the discard pile into the draw pile.</summary>
        public void Reshuffle()
        {
            DrawPile.AddRange(DiscardPile);
            DiscardPile.Clear();
            Shuffle(DrawPile);
            Reshuffles++;
            Changed?.Invoke(Change.Reshuffle, null);
        }

        /// <summary>Debug: reshuffle now (the draw pile too, if the discard pile is empty).</summary>
        public void ForceReshuffle()
        {
            if (DiscardPile.Count == 0) { Shuffle(DrawPile); Reshuffles++; Changed?.Invoke(Change.Reshuffle, null); }
            else Reshuffle();
        }

        /// <summary>Plays the Next card (a cast used it): to the discard pile, then draw one.</summary>
        public Card PlayNext() => TakeNext(Change.Play);

        /// <summary>Discards the Next card unused (cycle key), then draws one.</summary>
        public Card CycleNext() => TakeNext(Change.Cycle);

        Card TakeNext(Change how)
        {
            var c = Next;
            if (c == null) return null;
            Hand.RemoveAt(0);
            DiscardPile.Add(c);
            Changed?.Invoke(how, c);
            Fill();
            return c;
        }

        /// <summary>A new card goes into the discard pile (rewards, curses).</summary>
        public Card AddToDiscard(CardDef def)
        {
            var c = new Card(def);
            DiscardPile.Add(c);
            Changed?.Invoke(Change.Added, c);
            Fill();
            return c;
        }

        /// <summary>Debug: a new card becomes the Next card (the last hand card goes back on the draw pile).</summary>
        public Card AddToHand(CardDef def)
        {
            var c = new Card(def);
            Hand.Insert(0, c);
            while (Hand.Count > HandSize)
            {
                DrawPile.Add(Hand[Hand.Count - 1]);
                Hand.RemoveAt(Hand.Count - 1);
            }
            Changed?.Invoke(Change.Added, c);
            return c;
        }

        /// <summary>Removes one copy of a card from the deck for good (discard pile first, then draw pile, then hand).</summary>
        public bool Remove(CardDef def)
        {
            foreach (var pile in new[] { DiscardPile, DrawPile, Hand })
            {
                int i = pile.FindIndex(c => c.Def == def);
                if (i < 0) continue;
                var c = pile[i];
                pile.RemoveAt(i);
                Changed?.Invoke(Change.Removed, c);
                Fill();
                return true;
            }
            return false;
        }

        /// <summary>Debug: an empty deck.</summary>
        public void Clear()
        {
            DrawPile.Clear();
            Hand.Clear();
            DiscardPile.Clear();
            Changed?.Invoke(Change.Cleared, null);
        }

        /// <summary>The whole deck grouped by card, in card-list order: (card, how many).</summary>
        public List<(CardDef def, int count)> Grouped() =>
            Cards.All.Select(d => (d, AllCards.Count(c => c.Def == d))).Where(g => g.Item2 > 0).ToList();

        static void Shuffle(List<Card> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
