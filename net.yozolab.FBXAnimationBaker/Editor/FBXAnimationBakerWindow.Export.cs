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

        // 継承メンバも拾えるよう FlattenHierarchy を含める。
        // ExportSettings.instance は基底の ScriptableSingleton<T> 側にあるため、これが無いと見つからない。
        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>バイナリ FBX のファイル先頭にあるマジック文字列。</summary>
        private const string BinaryFbxMagic = "Kaydara FBX Binary";

        private static bool resolved;
        private static Type modelExporterType;
        private static Type exportOptionsType;
        private static MethodInfo exportWithOptions;
        private static MethodInfo exportObjectsWithOptions;
        private static MethodInfo exportSimple;
        private static bool loggedOptionWarning;
        private static bool loggedFormatDiagnostics;

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
            loggedFormatDiagnostics = false;
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

            // 呼び出し時オプションだけでは形式が変わらないエクスポーターがあるため、
            // Project Settings 側の形式も一時的に上書きし、終わったら必ず元へ戻す。
            List<Action> restoreGlobalFormat = OverrideGlobalExportFormat(ascii);

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
            finally
            {
                foreach (Action restore in restoreGlobalFormat)
                {
                    restore();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Project Settings 側のエクスポート形式の一時上書き
        // ═══════════════════════════════════════════════════════════════

        private const string ExportSettingsTypeName = "UnityEditor.Formats.Fbx.Exporter.ExportSettings";

        /// <summary>
        /// FBX Exporter の設定オブジェクトを辿り、ASCII/Binary を表す enum メンバを
        /// 目的の値に設定する。元へ戻すための復元処理のリストを返す。
        ///
        /// エクスポーターのバージョンによって、形式が
        /// 「呼び出し時オプション」ではなく「Project Settings の値」で決まることがあるため。
        /// </summary>
        private static List<Action> OverrideGlobalExportFormat(bool ascii)
        {
            var restores = new List<Action>();

            object settings = GetExportSettingsInstance();
            if (settings == null)
            {
                return restores;
            }

            OverrideFormatMembers(settings, ascii ? "ASCII" : "Binary", restores, new List<object>(), 0);
            return restores;
        }

        private static object GetExportSettingsInstance()
        {
            Type settingsType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                settingsType = assembly.GetType(ExportSettingsTypeName, false);
                if (settingsType != null)
                {
                    break;
                }
            }

            if (settingsType == null)
            {
                return null;
            }

            try
            {
                // instance は基底の ScriptableSingleton<T> が持つ静的メンバ。
                // 継承された静的メンバは FlattenHierarchy を付けないと GetProperty で拾えない。
                PropertyInfo instanceProperty = settingsType.GetProperty("instance", StaticFlags)
                    ?? typeof(ScriptableSingleton<>).MakeGenericType(settingsType)
                        .GetProperty("instance", StaticFlags);
                if (instanceProperty != null)
                {
                    return instanceProperty.GetValue(null);
                }

                FieldInfo instanceField = settingsType.GetField("instance", StaticFlags);
                return instanceField?.GetValue(null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 設定オブジェクトのフィールドを再帰的に辿り、ASCII/Binary を持つ enum を書き換える。
        /// メンバ名がバージョンによって違うため、名前ではなく「enum の中身」で見分ける。
        /// </summary>
        private static void OverrideFormatMembers(object target, string valueName, List<Action> restores, List<object> visited, int depth)
        {
            if (target == null || depth > 3)
            {
                return;
            }

            foreach (object seen in visited)
            {
                if (ReferenceEquals(seen, target))
                {
                    return;
                }
            }
            visited.Add(target);

            foreach (FieldInfo field in GetAllInstanceFields(target.GetType()))
            {
                Type fieldType = field.FieldType;

                if (fieldType.IsEnum)
                {
                    string[] names = Enum.GetNames(fieldType);
                    if (!names.Contains("ASCII") || !names.Contains("Binary"))
                    {
                        continue;
                    }

                    object original = field.GetValue(target);
                    object desired = Enum.Parse(fieldType, valueName);
                    if (Equals(original, desired))
                    {
                        continue;
                    }

                    field.SetValue(target, desired);
                    object capturedTarget = target;
                    FieldInfo capturedField = field;
                    restores.Add(() => capturedField.SetValue(capturedTarget, original));
                    continue;
                }

                // エクスポーター自身が定義する入れ子の設定クラスだけ辿る
                if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType.IsArray)
                {
                    continue;
                }
                if (fieldType.Assembly != target.GetType().Assembly)
                {
                    continue;
                }

                object child = null;
                try
                {
                    child = field.GetValue(target);
                }
                catch (Exception)
                {
                    continue;
                }

                OverrideFormatMembers(child, valueName, restores, visited, depth + 1);
            }
        }

        /// <summary>
        /// 見つかったエクスポーター API の状態を Console に出す。
        /// 形式やオプションが効かないときの原因切り分け用。
        /// </summary>
        public static void LogDiagnostics()
        {
            Resolve();

            var sb = new StringBuilder();
            sb.AppendLine("[FBX Animation Baker] FBX Exporter diagnostics");
            sb.AppendLine($"  ModelExporter type : {modelExporterType?.FullName ?? "(not found)"}");
            sb.AppendLine($"  Assembly           : {modelExporterType?.Assembly.GetName().Name} {modelExporterType?.Assembly.GetName().Version}");
            sb.AppendLine($"  ExportModelOptions : {exportOptionsType?.FullName ?? "(not found)"}");
            sb.AppendLine($"  ExportObject(2 args)          : {(exportSimple != null ? "found" : "not found")}");
            sb.AppendLine($"  ExportObject(with options)    : {(exportWithOptions != null ? "found" : "not found")}");
            sb.AppendLine($"  ExportObjects(with options)   : {(exportObjectsWithOptions != null ? "found" : "not found")}");

            if (exportOptionsType != null)
            {
                sb.AppendLine("  ExportModelOptions members:");
                foreach (PropertyInfo property in exportOptionsType.GetProperties(MemberFlags))
                {
                    sb.AppendLine($"    (prop) {property.Name} : {property.PropertyType.Name}{(property.CanWrite ? string.Empty : " (read-only)")}");
                }
                foreach (FieldInfo field in exportOptionsType.GetFields(MemberFlags))
                {
                    sb.AppendLine($"    (field) {field.Name} : {field.FieldType.Name}");
                }
            }

            object settings = GetExportSettingsInstance();
            sb.AppendLine($"  ExportSettings instance : {(settings != null ? settings.GetType().FullName : "(not found)")}");
            if (settings != null)
            {
                var found = new List<string>();
                CollectFormatMembers(settings, found, new List<object>(), 0);
                sb.AppendLine($"  ASCII/Binary members found : {(found.Count > 0 ? string.Join(", ", found) : "(none)")}");
            }

            Debug.Log(sb.ToString());
        }

        private static void CollectFormatMembers(object target, List<string> found, List<object> visited, int depth)
        {
            if (target == null || depth > 3)
            {
                return;
            }

            foreach (object seen in visited)
            {
                if (ReferenceEquals(seen, target))
                {
                    return;
                }
            }
            visited.Add(target);

            foreach (FieldInfo field in GetAllInstanceFields(target.GetType()))
            {
                Type fieldType = field.FieldType;

                if (fieldType.IsEnum)
                {
                    string[] names = Enum.GetNames(fieldType);
                    if (names.Contains("ASCII") && names.Contains("Binary"))
                    {
                        object value = null;
                        try
                        {
                            value = field.GetValue(target);
                        }
                        catch (Exception)
                        {
                            // 取得できないメンバは名前だけ拾う
                        }
                        found.Add($"{target.GetType().Name}.{field.Name}={value}");
                    }
                    continue;
                }

                if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType.IsArray)
                {
                    continue;
                }
                if (fieldType.Assembly != target.GetType().Assembly)
                {
                    continue;
                }

                try
                {
                    CollectFormatMembers(field.GetValue(target), found, visited, depth + 1);
                }
                catch (Exception)
                {
                    // 辿れないメンバは無視
                }
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

            // 原因の切り分けに必要な情報を、こちらから聞かなくても残るようにする(1 セッション 1 回)
            if (!loggedFormatDiagnostics)
            {
                loggedFormatDiagnostics = true;
                LogDiagnostics();
            }
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

        /// <summary>
        /// 基底クラスの private フィールドも含めた全インスタンスフィールド。
        /// FlattenHierarchy は基底の private メンバを返さないため、自前で辿る。
        /// </summary>
        private static IEnumerable<FieldInfo> GetAllInstanceFields(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    yield return field;
                }
            }
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
