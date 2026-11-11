using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// 12x12 pixel-art item icons drawn from ASCII. 'C' is the item colour (L = lighter, D = darker);
    /// the other letters are fixed colours. Unknown shapes fall back to the spell rune generator.
    /// </summary>
    public static class ItemIcons
    {
        static readonly Dictionary<string, string[]> Shapes = new Dictionary<string, string[]>
        {
            ["potion"] = new[]
            {
                "....BBBB....",
                ".....GG.....",
                ".....GG.....",
                "....GLLG....",
                "...GCLCCG...",
                "..GCCCCCCG..",
                "..GCLCCCCG..",
                "..GCCCCCDG..",
                "..GDCCCCDG..",
                "...GDDDDG...",
                "....GGGG....",
                "............",
            },
            ["bomb"] = new[]
            {
                "........Y.W.",
                ".......YCY..",
                "......BB....",
                ".....KGGK...",
                "...KKKKKKK..",
                "..KKGKKKKKK.",
                "..KGKKKKKKK.",
                "..KKKKKKKKK.",
                "..KKKKKKKKK.",
                "...KKKKKKK..",
                "....KKKKK...",
                "............",
            },
            ["scroll"] = new[]
            {
                "............",
                "..BBBBBBBB..",
                ".BPPPPPPPPB.",
                "..PPCCCCPP..",
                "..PPPPPPPP..",
                "..PPCCCPPP..",
                "..PPPPPPPP..",
                "..PPCCCCPP..",
                "..PPPPPPPP..",
                ".BPPPPPPPPB.",
                "..BBBBBBBB..",
                "............",
            },
            ["coin"] = new[]
            {
                "............",
                "....YYYY....",
                "..YYWWYYYY..",
                "..YWYYYYDY..",
                ".YWYYOOYYDY.",
                ".YWYOYYYYDY.",
                ".YWYYOOYYDY.",
                ".YYYYYYOYDY.",
                "..YYOOOOYY..",
                "..YYDDDDYY..",
                "....YYYY....",
                "............",
            },
            ["heart"] = new[]
            {
                "............",
                "..CCC..CCC..",
                ".CLLCCCCCCC.",
                ".CLCCCCCCCC.",
                ".CCCCCCCCCC.",
                ".CCCCCCCCCD.",
                "..CCCCCCCD..",
                "...CCCCCD...",
                "....CCCD....",
                ".....CD.....",
                "............",
                "............",
            },
            ["gem"] = new[]
            {
                "............",
                "...LLLLLL...",
                "..LWLCCCCD..",
                ".LWLCCCCCCD.",
                ".CCCCCCCCCD.",
                "..CCCCCCCD..",
                "...CCCCCD...",
                "....CCCD....",
                ".....CD.....",
                "............",
                "............",
                "............",
            },
            ["orb"] = new[]
            {
                "............",
                "....CCCC....",
                "...CLLCCC...",
                "..CLWCCCCC..",
                "..CLCCCCCD..",
                "..CCCCCCCD..",
                "..CCCCCCDD..",
                "...CCCCDD...",
                "....DDDD....",
                "...YYYYYY...",
                "..YOOOOOOY..",
                "............",
            },
            ["ring"] = new[]
            {
                "............",
                ".....LW.....",
                "....CLCC....",
                ".....CD.....",
                "...YYYYYY...",
                "..YY....YY..",
                "..Y......Y..",
                "..Y......O..",
                "..YY....OO..",
                "...YOOOOO...",
                "............",
                "............",
            },
            ["boots"] = new[]
            {
                "............",
                "...BBBB.....",
                "...BCCB.....",
                "...BCLB.....",
                "...BCLB.....",
                "...BCCB.....",
                "...BCCBBB...",
                "...BCCCCCB..",
                "...BCCCCCCB.",
                "...BDDDDDDB.",
                "...KKKKKKKK.",
                "............",
            },
            ["tome"] = new[]
            {
                "............",
                "..BBBBBBBB..",
                "..BCCCCCCPP.",
                "..BCCYYCCPP.",
                "..BCYCCYCPP.",
                "..BCCYYCCPP.",
                "..BCCCCCCPP.",
                "..BCCCCCCPP.",
                "..BCCCCCCPP.",
                "..BDDDDDDDP.",
                "..BBBBBBBB..",
                "............",
            },
            ["hourglass"] = new[]
            {
                "............",
                "..BBBBBBBB..",
                "...GCCCCG...",
                "...GCCCCG...",
                "....GCCG....",
                ".....GG.....",
                ".....GG.....",
                "....GLLG....",
                "...GLCCLG...",
                "...GCCCCG...",
                "..BBBBBBBB..",
                "............",
            },
            ["wand"] = new[]
            {
                "............",
                ".........WL.",
                "........LCL.",
                ".........L..",
                "........B...",
                ".......BB...",
                "......BB....",
                ".....BB.....",
                "....BB......",
                "...BB.......",
                "..BB........",
                "............",
            },
            ["shield"] = new[]
            {
                "............",
                "..GGGGGGGG..",
                "..GCCCCCCG..",
                "..GCLCCCCG..",
                "..GCLCCCCG..",
                "..GCCCCCCG..",
                "..GCCCCCCG..",
                "...GCCCCG...",
                "...GCCCDG...",
                "....GCDG....",
                ".....GG.....",
                "............",
            },
            ["clover"] = new[]
            {
                "............",
                "....CC.CC...",
                "...CLCCCCC..",
                "...CCCCCCC..",
                "....CCCCC...",
                "..CCCCDCCCC.",
                ".CLCCDCDCCC.",
                ".CCCCCDCCCC.",
                "..CCC.B.CC..",
                "......B.....",
                ".....B......",
                "............",
            },
            ["fang"] = new[]
            {
                "............",
                "..WWWWWWWW..",
                "..WPPPPPPW..",
                "...WPPPPW...",
                "...WPPPPW...",
                "....WPPW....",
                "....WPPW....",
                ".....WC.....",
                ".....CC.....",
                "......C.....",
                "............",
                "............",
            },
        };

        public static Texture2D Get(string shape, Color color)
        {
            if (shape == null || !Shapes.TryGetValue(shape, out var rows)) return SpellIcons.Get("item_" + shape, color);
            string key = $"item_{shape}_{ColorUtility.ToHtmlStringRGB(color)}";
            return PixelArt.Cached(key, () =>
            {
                Color32 c = color;
                var palette = new Dictionary<char, Color32>
                {
                    ['C'] = c,
                    ['L'] = Color.Lerp(color, Color.white, 0.45f),
                    ['D'] = Color.Lerp(color, Color.black, 0.4f),
                    ['W'] = new Color32(250, 250, 245, 255),
                    ['K'] = new Color32(48, 44, 56, 255),
                    ['G'] = new Color32(150, 160, 175, 255),
                    ['B'] = new Color32(110, 70, 40, 255),
                    ['Y'] = new Color32(250, 205, 70, 255),
                    ['O'] = new Color32(190, 130, 30, 255),
                    ['P'] = new Color32(235, 215, 170, 255),
                };
                return PixelArt.FromAscii(rows, palette);
            });
        }
    }
}
