using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// ベイクで生成した FBX のインポート設定を、初回インポートの「前」に適用するためのポストプロセッサ。
    ///
    /// 生成後に ModelImporter を書き換えて SaveAndReimport() すると、同じ FBX を 2 回
    /// インポートすることになり待ち時間が倍になる。生成前にこのクラスへ設定を登録しておき、
    /// OnPreprocessModel で当てることで 1 回のインポートで済ませる。
    /// </summary>
    internal class FBXAnimationBakerPostprocessor : AssetPostprocessor
    {
        /// <summary>これから生成する FBX に適用したいインポート設定。</summary>
        internal struct PendingImport
        {
            public BakedFbxAnimationType animationType;

            /// <summary>不要なインポート処理(マテリアル/カメラ/ライト等)を切って速くするか。</summary>
            public bool leanImport;

            /// <summary>ブレンドシェイプを読み込むか。除外して書き出した場合は false。</summary>
            public bool importBlendShapes;

            /// <summary>タンジェントを計算するか。スケルトンのみの FBX では不要。</summary>
            public bool importTangents;
        }

        private static readonly Dictionary<string, PendingImport> pendingImports =
            new Dictionary<string, PendingImport>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string assetPath, PendingImport settings)
        {
            pendingImports[assetPath] = settings;
        }

        public static void Unregister(string assetPath)
        {
            pendingImports.Remove(assetPath);
        }

        private void OnPreprocessModel()
        {
            if (!pendingImports.TryGetValue(assetPath, out PendingImport settings))
            {
                return;
            }

            var importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return;
            }

            switch (settings.animationType)
            {
                case BakedFbxAnimationType.None:
                    break;
                case BakedFbxAnimationType.Legacy:
                    importer.animationType = ModelImporterAnimationType.Legacy;
                    break;
                case BakedFbxAnimationType.Humanoid:
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    break;
                default:
                    importer.animationType = ModelImporterAnimationType.Generic;
                    break;
            }

            if (settings.animationType != BakedFbxAnimationType.None)
            {
                importer.importAnimation = true;
            }

            if (!settings.leanImport)
            {
                return;
            }

            // 既にベイク済みのカーブなので、インポート時の再サンプリング/圧縮は
            // 時間がかかるだけで得がない(キーもずれる)。
            importer.resampleCurves = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;

            importer.importBlendShapes = settings.importBlendShapes;
            importer.importTangents = settings.importTangents
                ? ModelImporterTangents.CalculateMikk
                : ModelImporterTangents.None;

            // アニメーション用の FBX では使わないものを切る。
            // マテリアル生成はテクスチャ探索を伴うため、体感で一番効く。
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importConstraints = false;
            importer.isReadable = false;
        }

        private void OnPostprocessModel(GameObject model)
        {
            // 設定は .meta に永続化されるので、以後の再インポートでは登録不要
            pendingImports.Remove(assetPath);
        }
    }
}
