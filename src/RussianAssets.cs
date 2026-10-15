using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianAssets
    {
        internal static readonly string DirectoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        internal static string TranslationPath => Path.Combine(DirectoryPath, "RussianData.json");
        private static AssetBundle _bundle;
        private static readonly Dictionary<string, byte[]> FontBytes = new Dictionary<string, byte[]>();

        private static AssetBundle Bundle
        {
            get
            {
                if (_bundle != null) return _bundle;
                string path = Path.Combine(DirectoryPath, "rtlc.bundle");
                if (!File.Exists(path)) throw new FileNotFoundException("Не найден rtlc.bundle", path);
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null) throw new InvalidDataException("Unity не смогла загрузить " + path);
                BoRussianPlugin.ModLog.LogInfo("Ресурсы загружены из " + path);
                return _bundle;
            }
        }

        private static TextAsset Text(string name)
        {
            TextAsset asset = Bundle.LoadAsset<TextAsset>(name);
            if (asset == null) throw new InvalidDataException("В rtlc.bundle отсутствует " + name);
            return asset;
        }

        internal static string ReadTranslation(bool debug)
        {
            if (!debug) return Encoding.UTF8.GetString(Text("translation").bytes).TrimStart('\ufeff');
            if (!File.Exists(TranslationPath))
            {
                // Never overwrite the editor's existing copy.
                using (var stream = new FileStream(TranslationPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    byte[] data = Text("translation").bytes;
                    stream.Write(data, 0, data.Length);
                }
                BoRussianPlugin.ModLog.LogInfo("Debug: создан редактируемый " + TranslationPath);
            }
            return File.ReadAllText(TranslationPath, new UTF8Encoding(false)).TrimStart('\ufeff');
        }

        internal static byte[] ReadFont(string name)
        {
            byte[] bytes;
            if (!FontBytes.TryGetValue(name, out bytes))
                FontBytes[name] = bytes = Text("fonts/" + name.ToLowerInvariant()).bytes;
            return bytes;
        }

        internal static Texture2D Texture(string name)
        {
            Texture2D texture = Bundle.LoadAsset<Texture2D>("textures/" + name);
            if (texture == null) throw new InvalidDataException("В rtlc.bundle отсутствует текстура " + name);
            return texture;
        }
    }
}
