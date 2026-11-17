using System;
using System.Collections.Generic;

namespace Spellcaster.Grimoire
{
    /// <summary>The Grimoire's save section: each pile's card ids in order, the debug preset, shops with a Scroll of Erasure.</summary>
    [Serializable]
    public class GrimoireSave
    {
        public List<string> draw = new List<string>();
        public List<string> hand = new List<string>();
        public List<string> discard = new List<string>();
        public int preset;
        /// <summary>Shop room ids that already got their scroll.</summary>
        public List<string> shops = new List<string>();
    }
}
