using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// Unity FBX Exporter (com.unity.formats.fbx) へのリフレクション橋渡し。
    ///
    /// asmdef から直接参照するとパッケージ未導入のプロジェクトでコンパイルが通らなくなるため、
    /// アセンブリを実行時に探して呼び出す。
    ///
    /// 注意: ExportModelOptions を受け取る ExportObject のオーバーロードは、バージョンによっては
    /// internal になっている。Public だけを探すとこのオーバーロードが見つからず、
    /// 既定設定(= ASCII FBX、オプション無効)での書き出しになってしまうため、
    /// NonPublic も含めて探索する。
    /// </summary>
    internal static class FbxExporterBridge
    {
        private const string ModelExporterTypeName = "UnityEditor.Formats.Fbx.Exporter.ModelExporter";
        private const string ExportOptionsTypeName = "UnityEditor.Formats.Fbx.Exporter.ExportModelOptions";

        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>バイナリ FBX のファイル先頭にあるマジック文字列。</summary>
        private const string BinaryFbxMagic = "Kaydara FBX Binary";

        private static bool resolved;
        private static Type modelExporterType;
        private static Type exportOptionsType;
        private static MethodInfo exportWithOptions;
        private static MethodInfo exportObjectsWithOptions;
        private static MethodInfo exportSimple;
        private static bool loggedOptionWarning;

        /// <summary>FBX Exporter が利用可能か。GUI の警告表示にも使う。</summary>
        public static bool IsAvailable
        {
            get
            {
                Resolve();
                return exportWithOptions != null || exportObjectsWithOptions != null || exportSimple != null;
            }
        }

        /// <summary>
        /// エクスポートオプション(バイナリ/ASCII、スキンメッシュアニメーションなど)を指定できるか。
        /// false の場合は FBX Exporter 既定設定での書き出しになる。
        /// </summary>
        public static bool SupportsExportOptions
        {
            get
            {
                Resolve();
                return exportOptionsType != null && (exportWithOptions != null || exportObjectsWithOptions != null);
            }
        }

        public static void ClearCache()
        {
            resolved = false;
            modelExporterType = null;
            exportOptionsType = null;
            exportWithOptions = null;
            exportObjectsWithOptions = null;
            exportSimple = null;
            loggedOptionWarning = false;
        }

        /// <summary>
        /// 指定 GameObject を FBX として書き出す。
        /// </summary>
        /// <param name="absoluteFilePath">出力先の絶対パス(.fbx)</param>
        /// <param name="target">エクスポート対象のヒエラルキールート</param>
        /// <param name="ascii">ASCII FBX で書き出すか(false ならバイナリ)</param>
        /// <param name="animateSkinnedMesh">ブレンドシェイプなどスキンメッシュのアニメーションを含めるか</param>
        public static bool Export(string absoluteFilePath, GameObject target, bool ascii, bool animateSkinnedMesh, out string error)
        {
            error = string.Empty;
            Resolve();

            if (!IsAvailable)
            {
                error = "Unity FBX Exporter (com.unity.formats.fbx) is not installed.";
                return false;
            }

            string directory = Path.GetDirectoryName(absoluteFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                object result;
                if (SupportsExportOptions)
                {
                    object options = BuildExportOptions(ascii, animateSkinnedMesh);
                    result = exportWithOptions != null
                        ? exportWithOptions.Invoke(null, new object[] { absoluteFilePath, target, options })
                        : exportObjectsWithOptions.Invoke(null, new object[] { absoluteFilePath, new UnityEngine.Object[] { target }, options });
                }
                else
                {
                    result = exportSimple.Invoke(null, new object[] { absoluteFilePath, target });
                }

                if (result as string == null && !File.Exists(absoluteFilePath))
                {
                    error = "ModelExporter returned no path and no file was written.";
                    return false;
                }

                WarnIfFormatMismatch(absoluteFilePath, ascii);
                return true;
            }
            catch (TargetInvocationException e)
            {
                error = e.InnerException != null ? e.InnerException.ToString() : e.ToString();
                return false;
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }
        }

        /// <summary>
        /// 書き出したファイルが要求した形式になっているか検証する。
        /// エクスポーターのバージョン差でオプション名が変わっていると黙って既定形式になるため、
        /// 気付けるようにログを出す。
        /// </summary>
        private static void WarnIfFormatMismatch(string absoluteFilePath, bool ascii)
        {
            bool? isBinary = IsBinaryFbx(absoluteFilePath);
            if (isBinary == null || isBinary.Value == !ascii)
            {
                return;
            }

            Debug.LogWarning($"[FBX Animation Baker] Requested {(ascii ? "ASCII" : "Binary")} FBX but the exporter wrote " +
                             $"{(isBinary.Value ? "Binary" : "ASCII")}: {absoluteFilePath}" +
                             (SupportsExportOptions
                                 ? " (the installed FBX Exporter may use different export option names)"
                                 : " (this FBX Exporter version does not expose export options, so its default format is used)"));
        }

        private static bool? IsBinaryFbx(string absoluteFilePath)
        {
            try
            {
                using (var stream = new FileStream(absoluteFilePath, FileMode.Open, FileAccess.Read))
                {
                    var header = new byte[BinaryFbxMagic.Length];
                    int read = stream.Read(header, 0, header.Length);
                    if (read < header.Length)
                    {
                        return null;
                    }
                    return Encoding.ASCII.GetString(header) == BinaryFbxMagic;
                }
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static object BuildExportOptions(bool ascii, bool animateSkinnedMesh)
        {
            object options = Activator.CreateInstance(exportOptionsType, true);
            var unapplied = new List<string>();

            // プロパティ名・フィールド名はエクスポーターのバージョンによって異なるため候補を順に試す。
            if (!TrySetEnum(options, new[] { "ModelAnimIncludeOption", "ExportModel", "modelAnimIncludeOption" }, "ModelAndAnim"))
            {
                unapplied.Add("ModelAnimIncludeOption");
            }

            if (!TrySetEnum(options, new[] { "ExportFormat", "exportFormat" }, ascii ? "ASCII" : "Binary"))
            {
                unapplied.Add("ExportFormat");
            }

            // ルートの位置をそのまま保つ(LocalCentered だとルートモーションの原点がずれる)。
            TrySetEnum(options, new[] { "ObjectPosition", "objectPosition" }, "WorldAbsolute");

            TrySetBool(options, new[] { "AnimateSkinnedMesh", "animateSkinnedMesh" }, animateSkinnedMesh);
            TrySetBool(options, new[] { "ExportUnrendered", "exportUnrendered" }, true);
            TrySetBool(options, new[] { "PreserveImportSettings", "preserveImportSettings" }, true);
            TrySetBool(options, new[] { "EmbedTextures", "embedTextures" }, false);

            if (unapplied.Count > 0 && !loggedOptionWarning)
            {
                loggedOptionWarning = true;
                Debug.LogWarning($"[FBX Animation Baker] These export options could not be applied to {exportOptionsType.FullName}: " +
                                 $"{string.Join(", ", unapplied)}. The exporter default will be used instead.");
            }

            return options;
        }

        private static bool TrySetEnum(object target, string[] memberNames, string valueName)
        {
            foreach (string memberName in memberNames)
            {
                PropertyInfo property = exportOptionsType.GetProperty(memberName, MemberFlags);
                if (property != null && property.CanWrite && property.PropertyType.IsEnum
                    && Enum.GetNames(property.PropertyType).Contains(valueName))
                {
                    property.SetValue(target, Enum.Parse(property.PropertyType, valueName));
                    return true;
                }

                FieldInfo field = exportOptionsType.GetField(memberName, MemberFlags);
                if (field != null && field.FieldType.IsEnum
                    && Enum.GetNames(field.FieldType).Contains(valueName))
                {
                    field.SetValue(target, Enum.Parse(field.FieldType, valueName));
                    return true;
                }
            }
            return false;
        }

        private static bool TrySetBool(object target, string[] memberNames, bool value)
        {
            foreach (string memberName in memberNames)
            {
                PropertyInfo property = exportOptionsType.GetProperty(memberName, MemberFlags);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    property.SetValue(target, value);
                    return true;
                }

                FieldInfo field = exportOptionsType.GetField(memberName, MemberFlags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            return false;
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    modelExporterType = assembly.GetType(ModelExporterTypeName, false);
                    if (modelExporterType != null)
                    {
                        exportOptionsType = assembly.GetType(ExportOptionsTypeName, false);
                        break;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // 読み込めないアセンブリは無視して探索を続ける
                }
            }

            if (modelExporterType == null)
            {
                return;
            }

            foreach (MethodInfo method in modelExporterType.GetMethods(StaticFlags))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                bool takesSingleObject = parameters[1].ParameterType.IsAssignableFrom(typeof(GameObject));
                bool takesObjectArray = parameters[1].ParameterType == typeof(UnityEngine.Object[]);
                bool takesOptions = parameters.Length == 3
                    && exportOptionsType != null
                    && parameters[2].ParameterType.IsAssignableFrom(exportOptionsType);

                if (method.Name == "ExportObject" && takesSingleObject)
                {
                    if (parameters.Length == 2)
                    {
                        exportSimple = method;
                    }
                    else if (takesOptions)
                    {
                        exportWithOptions = method;
                    }
                }
                else if (method.Name == "ExportObjects" && takesObjectArray && takesOptions)
                {
                    exportObjectsWithOptions = method;
                }
            }
        }
    }
}
