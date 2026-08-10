using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// Unity FBX Exporter (com.unity.formats.fbx) へのリフレクション橋渡し。
    ///
    /// asmdef から直接参照するとパッケージ未導入のプロジェクトでコンパイルが通らなくなるため、
    /// アセンブリを実行時に探して呼び出す。エクスポーターのバージョン差でオプション型の
    /// プロパティ名が異なることがあるので、設定は「あれば設定する」方式にしている。
    /// </summary>
    internal static class FbxExporterBridge
    {
        private const string ModelExporterTypeName = "UnityEditor.Formats.Fbx.Exporter.ModelExporter";
        private const string ExportOptionsTypeName = "UnityEditor.Formats.Fbx.Exporter.ExportModelOptions";

        private static bool resolved;
        private static Type modelExporterType;
        private static Type exportOptionsType;
        private static MethodInfo exportWithOptions;
        private static MethodInfo exportSimple;

        /// <summary>FBX Exporter が利用可能か。GUI の警告表示にも使う。</summary>
        public static bool IsAvailable
        {
            get
            {
                Resolve();
                return exportWithOptions != null || exportSimple != null;
            }
        }

        /// <summary>ExportModelOptions が使えるか(使えない場合はエクスポーター既定設定での書き出しになる)。</summary>
        public static bool SupportsExportOptions
        {
            get
            {
                Resolve();
                return exportWithOptions != null && exportOptionsType != null;
            }
        }

        public static void ClearCache()
        {
            resolved = false;
            modelExporterType = null;
            exportOptionsType = null;
            exportWithOptions = null;
            exportSimple = null;
        }

        /// <summary>
        /// 指定 GameObject を FBX として書き出す。
        /// </summary>
        /// <param name="absoluteFilePath">出力先の絶対パス(.fbx)</param>
        /// <param name="target">エクスポート対象のヒエラルキールート</param>
        /// <param name="ascii">ASCII FBX で書き出すか</param>
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
                if (exportWithOptions != null && exportOptionsType != null)
                {
                    object options = BuildExportOptions(ascii, animateSkinnedMesh);
                    result = exportWithOptions.Invoke(null, new object[] { absoluteFilePath, target, options });
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

        private static object BuildExportOptions(bool ascii, bool animateSkinnedMesh)
        {
            object options = Activator.CreateInstance(exportOptionsType);

            // モデルとアニメーションの両方を含める。プロパティ名はバージョンによって異なる。
            if (!TrySetEnum(options, "ModelAnimIncludeOption", "ModelAndAnim"))
            {
                TrySetEnum(options, "ExportModel", "ModelAndAnim");
            }

            TrySetEnum(options, "ExportFormat", ascii ? "ASCII" : "Binary");

            // ルートの位置をそのまま保つ(LocalCentered だとルートモーションの原点がずれる)。
            TrySetEnum(options, "ObjectPosition", "WorldAbsolute");

            TrySetBool(options, "AnimateSkinnedMesh", animateSkinnedMesh);
            TrySetBool(options, "ExportUnrendered", true);
            TrySetBool(options, "PreserveImportSettings", true);

            return options;
        }

        private static bool TrySetEnum(object target, string propertyName, string valueName)
        {
            PropertyInfo property = exportOptionsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
            {
                return false;
            }

            if (!Enum.GetNames(property.PropertyType).Contains(valueName))
            {
                return false;
            }

            property.SetValue(target, Enum.Parse(property.PropertyType, valueName));
            return true;
        }

        private static bool TrySetBool(object target, string propertyName, bool value)
        {
            PropertyInfo property = exportOptionsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
            {
                return false;
            }

            property.SetValue(target, value);
            return true;
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

            foreach (MethodInfo method in modelExporterType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "ExportObject")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }
                if (!parameters[1].ParameterType.IsAssignableFrom(typeof(GameObject)))
                {
                    continue;
                }

                if (parameters.Length == 2)
                {
                    exportSimple = method;
                }
                else if (parameters.Length == 3
                    && exportOptionsType != null
                    && parameters[2].ParameterType.IsAssignableFrom(exportOptionsType))
                {
                    exportWithOptions = method;
                }
            }
        }
    }
}
