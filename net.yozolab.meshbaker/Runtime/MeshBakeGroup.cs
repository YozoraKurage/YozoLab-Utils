using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// ベイク出力1グループ分のRendererを管理するコンポーネント。
    /// MeshBakeAssemblyの子に置くと出力グループとして解決され、
    /// グループごとに別の静的Mesh/プレハブが出力される
    /// （MeshBakeAssembly側の配列ベースのRenderer Groupsとも併用できる）。
    ///
    /// Rendererは明示リストと、このGameObject配下からの自動収集の両方で集められる。
    /// グループ名（未設定ならGameObject名）が成果物アセット名のサフィックスになる。
    /// </summary>
    [AddComponentMenu("YozoLab/Mesh Bake Group")]
    [DisallowMultipleComponent]
    public class MeshBakeGroup : MonoBehaviour
    {
        [Tooltip("グループ名。空ならこのGameObjectの名前を使います（成果物アセット名のサフィックス）。")]
        public string groupName = "";

        [Tooltip("このグループとしてベイクするRenderer（明示指定。階層外のRendererも指定可能）。")]
        public List<Renderer> renderers = new List<Renderer>();

        [Tooltip("このGameObject配下のRendererを自動的にグループへ含めます。" +
                 "より深い階層に別のMesh Bake Groupがある場合は、近い方のグループが優先されます。")]
        public bool includeChildRenderers = true;

        public string EffectiveName => string.IsNullOrEmpty(groupName) ? gameObject.name : groupName;
    }
}
