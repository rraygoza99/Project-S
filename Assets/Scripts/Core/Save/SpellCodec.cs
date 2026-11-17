using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>A spell in the save: base spells by id, forged spells in full.</summary>
    [Serializable]
    public class SpellData
    {
        /// <summary>Set for a base spell (everything else is then ignored).</summary>
        public string baseId;
        public string id, name, noun, adjective, sequence, iconKey, secondaryIconKey;
        public float cooldown;
        public Color color;
        public int tier;
        public List<string> components = new List<string>();
        public List<string> synergies = new List<string>();
        public ObjData delivery = new ObjData();
        public List<ObjData> payload = new List<ObjData>();
    }

    /// <summary>An effect or delivery: its type name and every number / flag / text field.</summary>
    [Serializable]
    public class ObjData
    {
        public string type;
        public List<FieldData> fields = new List<FieldData>();
    }

    [Serializable]
    public class FieldData
    {
        public string name, value;
    }

    /// <summary>
    /// Saves and rebuilds spells. Forged spells are written out completely (delivery and every effect,
    /// by type name and fields), so new effect types need no extra code as long as their state is plain
    /// fields (numbers, flags, enums, text).
    /// </summary>
    public static class SpellCodec
    {
        public static bool IsBase(SpellDefinition s) =>
            s.Tier <= 1 && s.Components.Count == 1 && s.Components[0] == s.Id && SpellBook.BaseIds.Contains(s.Id);

        public static SpellData ToData(SpellDefinition s)
        {
            if (IsBase(s)) return new SpellData { baseId = s.Id };
            return new SpellData
            {
                id = s.Id, name = s.Name, noun = s.Noun, adjective = s.Adjective,
                sequence = Arrows.ToText(s.Sequence), iconKey = s.IconKey, secondaryIconKey = s.SecondaryIconKey,
                cooldown = s.Cooldown, color = s.Color, tier = s.Tier,
                components = new List<string>(s.Components), synergies = new List<string>(s.Synergies),
                delivery = Obj(s.Delivery),
                payload = s.Payload.Select(Obj).ToList(),
            };
        }

        public static SpellDefinition FromData(SpellData d)
        {
            if (!string.IsNullOrEmpty(d.baseId))
                return SpellBook.CreateBase(d.baseId) ?? throw new InvalidOperationException("unknown spell " + d.baseId);
            var s = new SpellDefinition
            {
                Id = d.id, Name = d.name, Noun = d.noun, Adjective = d.adjective,
                Sequence = Arrows.Parse(d.sequence), IconKey = d.iconKey, SecondaryIconKey = string.IsNullOrEmpty(d.secondaryIconKey) ? null : d.secondaryIconKey,
                Cooldown = d.cooldown, Color = d.color, Tier = d.tier,
                Delivery = (SpellDelivery)FromObj(d.delivery),
            };
            s.Components.AddRange(d.components);
            s.Synergies.AddRange(d.synergies);
            foreach (var e in d.payload) s.Payload.Add((SpellEffect)FromObj(e));
            return s;
        }

        public static string ToJson(SpellDefinition s) => JsonUtility.ToJson(ToData(s));
        public static SpellDefinition FromJson(string json) => FromData(JsonUtility.FromJson<SpellData>(json));

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (var x = t; x != null && x != typeof(object); x = x.BaseType)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)) yield return f;
        }

        static ObjData Obj(object o)
        {
            var d = new ObjData { type = o.GetType().FullName };
            foreach (var f in Fields(o.GetType()).OrderBy(f => f.DeclaringType.Name).ThenBy(f => f.Name))
                d.fields.Add(new FieldData { name = f.Name, value = Convert.ToString(f.GetValue(o), CultureInfo.InvariantCulture) });
            return d;
        }

        static object FromObj(ObjData d)
        {
            var t = Type.GetType(d.type) ?? throw new InvalidOperationException("unknown spell part " + d.type);
            var o = FormatterServices.GetUninitializedObject(t);
            var fields = Fields(t).ToList();
            foreach (var fd in d.fields)
            {
                var f = fields.FirstOrDefault(x => x.Name == fd.name);
                if (f == null) continue; // a field that no longer exists
                object v = f.FieldType.IsEnum ? Enum.Parse(f.FieldType, fd.value)
                    : f.FieldType == typeof(string) ? fd.value
                    : Convert.ChangeType(fd.value, f.FieldType, CultureInfo.InvariantCulture);
                f.SetValue(o, v);
            }
            return o;
        }
    }
}
