using UnityEditor;
using UnityEngine;

namespace Spellcaster.EditorTools
{
    /// <summary>Import rules for the Asset Store files copied by tools/import-assets.ps1.</summary>
    public class ThirdPartyImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/ThirdParty/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            bool pixelArt = assetPath.Contains("/Monsters/");
            ti.filterMode = pixelArt ? FilterMode.Point : FilterMode.Bilinear;
            ti.maxTextureSize = 4096;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/ThirdParty/")) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            ai.defaultSampleSettings = s;
        }
    }
}
